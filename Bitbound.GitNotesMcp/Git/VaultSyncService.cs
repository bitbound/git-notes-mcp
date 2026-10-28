using Bitbound.GitNotesMcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Bitbound.GitNotesMcp.Git;

/// <summary>
/// Keeps the working copy fresh for read operations by fast-forwarding from the remote,
/// rate limited so a burst of reads performs at most one fetch per interval.
/// </summary>
public sealed class VaultSyncService(
  NotesOptions options,
  GitRepository git,
  ILogger<VaultSyncService> logger,
  TimeProvider? timeProvider = null)
{
  private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private DateTimeOffset _lastSyncUtc = DateTimeOffset.MinValue;

  public async Task SyncBeforeReadAsync(CancellationToken cancellationToken = default)
  {
    if (!options.SyncBeforeRead || options.ReadOnly)
    {
      return;
    }

    if (IsThrottled())
    {
      return;
    }

    // A read should never queue behind an in-flight fetch. Serve the local copy instead.
    if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
    {
      return;
    }

    try
    {
      if (IsThrottled())
      {
        return;
      }

      _lastSyncUtc = _clock.GetUtcNow();
      await git.SyncAsync(cancellationToken);
    }
    catch (GitCommandException ex)
    {
      logger.LogWarning(ex, "The pre-read sync failed. Serving the local copy.");
    }
    finally
    {
      _gate.Release();
    }
  }

  private bool IsThrottled() =>
    _clock.GetUtcNow() - _lastSyncUtc < TimeSpan.FromSeconds(options.SyncMinSeconds);
}
