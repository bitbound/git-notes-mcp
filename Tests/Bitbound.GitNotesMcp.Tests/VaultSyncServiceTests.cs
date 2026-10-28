using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class VaultSyncServiceTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Reads_fast_forward_once_then_wait_for_the_interval()
  {
    using var vault = new TempVault();
    var (git, _) = await TestServices.CreateAsync(vault, Ct);

    File.WriteAllText(Path.Combine(vault.Root, "a.md"), "a\n");
    Assert.NotNull(await git.CommitAsync("Add a.", Ct));
    await git.PushAsync(Ct);

    var clone = CloneWithIdentity(vault);
    try
    {
      var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
      var sync = new VaultSyncService(vault.Options, git, NullLogger<VaultSyncService>.Instance, clock);

      WriteAndPush(clone, "b.md", "b");
      await sync.SyncBeforeReadAsync(Ct);
      Assert.True(File.Exists(Path.Combine(vault.Root, "b.md")));

      WriteAndPush(clone, "c.md", "c");
      await sync.SyncBeforeReadAsync(Ct);
      Assert.False(File.Exists(Path.Combine(vault.Root, "c.md")));

      clock.UtcNow = clock.UtcNow.AddSeconds(vault.Options.SyncMinSeconds + 1);
      await sync.SyncBeforeReadAsync(Ct);
      Assert.True(File.Exists(Path.Combine(vault.Root, "c.md")));
    }
    finally
    {
      TempVault.DeleteTree(clone);
    }
  }

  [Fact]
  public async Task Disabled_sync_never_fetches()
  {
    using var vault = new TempVault();
    var options = vault.Options with { SyncBeforeRead = false };
    var (git, _) = await TestServices.CreateAsync(vault, Ct);

    File.WriteAllText(Path.Combine(vault.Root, "a.md"), "a\n");
    Assert.NotNull(await git.CommitAsync("Add a.", Ct));
    await git.PushAsync(Ct);

    var clone = CloneWithIdentity(vault);
    try
    {
      WriteAndPush(clone, "b.md", "b");
      var sync = new VaultSyncService(options, git, NullLogger<VaultSyncService>.Instance);

      await sync.SyncBeforeReadAsync(Ct);
      Assert.False(File.Exists(Path.Combine(vault.Root, "b.md")));
    }
    finally
    {
      TempVault.DeleteTree(clone);
    }
  }

  private static void WriteAndPush(string clone, string name, string content)
  {
    File.WriteAllText(Path.Combine(clone, name), content + "\n");
    Assert.Equal(0, TempVault.Git(clone, "add", "-A").ExitCode);
    Assert.Equal(0, TempVault.Git(clone, "commit", "-m", $"Add {name}.").ExitCode);
    Assert.Equal(0, TempVault.Git(clone, "push", "origin", "main").ExitCode);
  }

  private static string CloneWithIdentity(TempVault vault)
  {
    var clone = Path.Combine(Path.GetTempPath(), "git-notes-clone-" + Guid.NewGuid().ToString("N"));
    Assert.Equal(0, TempVault.Git(Path.GetTempPath(), "clone", vault.RemotePath, clone).ExitCode);
    Assert.Equal(0, TempVault.Git(clone, "config", "user.name", "Other").ExitCode);
    Assert.Equal(0, TempVault.Git(clone, "config", "user.email", "other@example.com").ExitCode);
    return clone;
  }

  private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
  }
}
