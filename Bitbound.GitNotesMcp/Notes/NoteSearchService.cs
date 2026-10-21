using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Bitbound.GitNotesMcp.Notes;

public sealed class NoteSearchService(
  NoteStore store,
  NotePathResolver resolver,
  ZvecSearchBackend zvec,
  ILogger<NoteSearchService> logger)
{
  public async Task<SearchPayload> SearchAsync(
    string query,
    string? folder,
    bool regex,
    bool caseSensitive,
    int contextLines,
    int limit,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      throw new McpException("query is required.");
    }

    string? glob = null;
    if (folder is not null)
    {
      var full = resolver.ResolveDirectory(folder);
      var relative = Path.GetRelativePath(resolver.Root, full).Replace('\\', '/');
      glob = relative == "." ? "**/*" : $"{relative.TrimEnd('/')}/**";
    }

    string? fallbackReason = null;
    if (zvec.IsEnabled && !regex && !caseSensitive)
    {
      try
      {
        var text = await zvec.SearchAsync(query, glob, limit, cancellationToken);
        return new SearchPayload("zvec", Text: text);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        fallbackReason = ex.Message;
        logger.LogWarning(ex, "zvec-grep search failed. Falling back to keyword search.");
      }
    }

    return new SearchPayload(
      "keyword",
      Matches: store.Search(query, folder, regex, caseSensitive, contextLines, limit),
      FallbackReason: fallbackReason);
  }
}
