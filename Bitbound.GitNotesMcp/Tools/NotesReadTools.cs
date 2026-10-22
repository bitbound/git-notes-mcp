using System.ComponentModel;
using Bitbound.GitNotesMcp.Notes;
using ModelContextProtocol.Server;

namespace Bitbound.GitNotesMcp.Tools;

[McpServerToolType]
public sealed class NotesReadTools(NoteStore store, NoteSearchService search)
{
  [McpServerTool(Name = "list_notes", ReadOnly = true, OpenWorld = false, Title = "List notes")]
  [Description("List markdown notes with their vault-relative paths, sizes, and modified times.")]
  public string ListNotes(
    [Description("Optional folder to list, relative to the vault root.")]
    string? folder = null,
    [Description("Include notes in subfolders.")]
    bool recursive = true,
    [Description("Maximum number of notes to return, from 1 to 1000.")]
    int limit = 200) =>
    ToolJson.Serialize(store.List(folder, recursive, limit));

  [McpServerTool(Name = "read_note", ReadOnly = true, OpenWorld = false, Title = "Read note")]
  [Description("Read a markdown note by its vault-relative path. Optionally return a single heading section, or a line range.")]
  public string ReadNote(
    [Description("Vault-relative path to a .md or .markdown file.")]
    string path,
    [Description("Optional heading text. Returns that heading and its content up to the next heading of the same or higher level.")]
    string? heading = null,
    [Description("Optional 1-based first line to return when heading is not set.")]
    int? offset = null,
    [Description("Optional number of lines to return when heading is not set.")]
    int? limit = null) =>
    ToolJson.Serialize(store.Read(path, heading, offset, limit));

  [McpServerTool(Name = "search_notes", ReadOnly = true, OpenWorld = false, Title = "Search notes")]
  [Description("Search markdown notes by text or regular expression. Returns vault-relative paths with line numbers, the matching line, and optional context lines. When zvec-grep is enabled, natural-language queries use it and the result reports backend zvec.")]
  public async Task<string> SearchNotes(
    [Description("Text or regular expression to find.")]
    string query,
    [Description("Optional folder to search under, relative to the vault root.")]
    string? folder = null,
    [Description("Treat query as a regular expression. Regex searches always use keyword search.")]
    bool regex = false,
    [Description("Match case exactly. Case-sensitive searches always use keyword search.")]
    bool caseSensitive = false,
    [Description("Lines of context before and after each match, from 0 to 3.")]
    int contextLines = 1,
    [Description("Maximum number of matches, from 1 to 100.")]
    int limit = 20,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await search.SearchAsync(
      query,
      folder,
      regex,
      caseSensitive,
      contextLines,
      limit,
      cancellationToken));
}
