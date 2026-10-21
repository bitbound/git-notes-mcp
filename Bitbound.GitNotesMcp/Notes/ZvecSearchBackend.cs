using Bitbound.GitNotesMcp.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Bitbound.GitNotesMcp.Notes;

public sealed class ZvecSearchBackend(
  NotesOptions options,
  NotePathResolver resolver,
  ILogger<ZvecSearchBackend> logger) : IAsyncDisposable
{
  private readonly SemaphoreSlim _gate = new(1, 1);

  private McpClient? _client;

  public bool IsEnabled => options.ZvecUrl is not null;

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

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.ZvecTimeoutSeconds));

    try
    {
      var result = await client.CallToolAsync("zvec_grep_search", arguments, cancellationToken: timeout.Token);
      var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

      if (result.IsError == true)
      {
        throw new McpException(text.Length > 0 ? text : "zvec-grep returned an error.");
      }

      return text;
    }
    catch
    {
      await InvalidateAsync();
      throw;
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

      var headers = new Dictionary<string, string>();
      if (!string.IsNullOrWhiteSpace(options.ZvecToken))
      {
        headers["Authorization"] = $"Bearer {options.ZvecToken}";
      }

      var transport = new HttpClientTransport(new HttpClientTransportOptions
      {
        Endpoint = new Uri(options.ZvecUrl!),
        TransportMode = HttpTransportMode.StreamableHttp,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        AdditionalHeaders = headers
      });

      using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      connectTimeout.CancelAfter(TimeSpan.FromSeconds(2));

      try
      {
        _client = await McpClient.CreateAsync(transport, cancellationToken: connectTimeout.Token);
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        await transport.DisposeAsync();
        throw new McpException($"Timed out connecting to zvec-grep at {options.ZvecUrl}.");
      }
      catch
      {
        await transport.DisposeAsync();
        throw;
      }

      logger.LogInformation("Connected to zvec-grep at {Endpoint}.", options.ZvecUrl);
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
