using System.Diagnostics;
using System.Text;
using Bitbound.GitNotesMcp.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Bitbound.GitNotesMcp.Notes;

/// <summary>
/// Runs zvec-grep as a local stdio MCP server. No endpoint or credential is involved.
/// </summary>
public sealed class ZvecSearchBackend(
  NotesOptions options,
  NotePathResolver resolver,
  ILogger<ZvecSearchBackend> logger) : IAsyncDisposable
{
  private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan IndexTimeout = TimeSpan.FromMinutes(5);
  private readonly SemaphoreSlim _gate = new(1, 1);

  private McpClient? _client;
  private bool _indexAttempted;

  public bool IsEnabled => options.ZvecEnabled;

  public async ValueTask DisposeAsync()
  {
    await InvalidateAsync();
    _gate.Dispose();
  }

  public async Task<string> SearchAsync(
    string query,
    string? glob,
    int limit,
    CancellationToken cancellationToken)
  {
    var client = await GetClientAsync(cancellationToken);
    var arguments = new Dictionary<string, object?>
    {
      ["root"] = resolver.Root,
      ["query"] = query,
      ["limit"] = Math.Clamp(limit, 1, 50)
    };

    if (!string.IsNullOrEmpty(glob))
    {
      arguments["globs"] = new[] { glob };
    }

    try
    {
      var result = await client.CallToolAsync("zvec_grep_search", arguments, cancellationToken: cancellationToken);
      var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

      if (result.IsError == true)
      {
        throw new McpException(text.Length > 0 ? text : "zvec-grep returned an error.");
      }

      return text;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch
    {
      await InvalidateAsync();
      throw;
    }
  }

  private static void TryKill(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
    }
  }

  private async Task EnsureIndexedAsync(CancellationToken cancellationToken)
  {
    if (_indexAttempted)
    {
      return;
    }

    _indexAttempted = true;

    var startInfo = new ProcessStartInfo(options.ZvecCommand)
    {
      WorkingDirectory = resolver.Root,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    startInfo.ArgumentList.Add("index");
    startInfo.ArgumentList.Add(resolver.Root);

    using var process = new Process { StartInfo = startInfo };
    try
    {
      process.Start();
      // Close stdin so a child can never block reading this server's inherited stdio pipe.
      process.StandardInput.Close();
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
      throw new McpException($"'{options.ZvecCommand}' was not found on PATH. Install zvec-grep or set GIT_NOTES_ZVEC_ENABLED=false.");
    }

    var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutSource.CancelAfter(IndexTimeout);

    try
    {
      await process.WaitForExitAsync(timeoutSource.Token);
    }
    catch (OperationCanceledException)
    {
      TryKill(process);
      cancellationToken.ThrowIfCancellationRequested();
      throw new McpException($"'{options.ZvecCommand} index' timed out after {IndexTimeout.TotalMinutes:0} minutes.");
    }

    var error = (await standardError).Trim();
    var output = (await standardOutput).Trim();
    var detail = error.Length > 0 ? error : output;
    logger.LogDebug("zvec-grep index exited with {ExitCode}: {Detail}", process.ExitCode, detail);

    if (process.ExitCode != 0)
    {
      throw new McpException($"'{options.ZvecCommand} index' failed with exit code {process.ExitCode}. {detail}");
    }
  }

  private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
  {
    if (_client is not null)
    {
      return _client;
    }

    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (_client is not null)
      {
        return _client;
      }

      await EnsureIndexedAsync(cancellationToken);

      var transport = new StdioClientTransport(new StdioClientTransportOptions
      {
        Name = "zvec-grep",
        Command = options.ZvecCommand,
        Arguments = ["server", "--stdio"],
        WorkingDirectory = resolver.Root,
        StandardErrorLines = line => logger.LogDebug("zvec-grep: {Line}", line)
      });

      using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      connectTimeout.CancelAfter(ConnectTimeout);

      try
      {
        _client = await McpClient.CreateAsync(transport, cancellationToken: connectTimeout.Token);
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        throw new McpException($"Timed out starting '{options.ZvecCommand} server --stdio'.");
      }

      logger.LogInformation("Started zvec-grep from {Command}.", options.ZvecCommand);
      return _client;
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task InvalidateAsync()
  {
    McpClient? client;
    await _gate.WaitAsync();
    try
    {
      client = _client;
      _client = null;
    }
    finally
    {
      _gate.Release();
    }

    if (client is not null)
    {
      try
      {
        await client.DisposeAsync();
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Disposing the zvec-grep client failed.");
      }
    }
  }
}
