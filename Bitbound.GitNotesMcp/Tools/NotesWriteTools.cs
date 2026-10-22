using System.ComponentModel;
using Bitbound.GitNotesMcp.Notes;
using ModelContextProtocol.Server;

namespace Bitbound.GitNotesMcp.Tools;

[McpServerToolType]
public sealed class NotesWriteTools(NoteService service)
{
  [McpServerTool(Name = "append_note", Destructive = false, OpenWorld = false, Title = "Append to note")]
  [Description("Append text to a note without reading it first. Creates the note when it does not exist.")]
  public async Task<string> AppendNote(
    [Description("Vault-relative path to the note. Parent folders are created when missing.")]
    string path,
    [Description("Text to append. A trailing newline is added when missing.")]
    string content,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await service.AppendAsync(path, content, cancellationToken));

  [McpServerTool(Name = "create_note", OpenWorld = false, Title = "Create note")]
  [Description("Create a markdown note. Fails when the note already exists unless overwrite is true.")]
  public async Task<string> CreateNote(
    [Description("Vault-relative path for the new note, ending in .md or .markdown. Parent folders are created when missing.")]
    string path,
    [Description("Full markdown content of the note.")]
    string content,
    [Description("Replace an existing note at the same path.")]
    bool overwrite = false,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await service.CreateAsync(path, content, overwrite, cancellationToken));

  [McpServerTool(Name = "delete_note", Destructive = true, OpenWorld = false, Title = "Delete note")]
  [Description("Permanently delete a note. Git history is the only way to recover it.")]
  public async Task<string> DeleteNote(
    [Description("Vault-relative path to the note.")]
    string path,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await service.DeleteAsync(path, cancellationToken));

  [McpServerTool(Name = "edit_note", OpenWorld = false, Title = "Edit note")]
  [Description("Replace an exact string in a note. By default oldText must occur exactly once.")]
  public async Task<string> EditNote(
    [Description("Vault-relative path to the note.")]
    string path,
    [Description("Exact text to replace.")]
    string oldText,
    [Description("Replacement text. Use an empty string to delete the matched text.")]
    string newText,
    [Description("Replace every occurrence instead of requiring a unique match.")]
    bool replaceAll = false,
    [Description("Optional hash returned by read_note or search_notes. When supplied, the edit fails if the note changed since it was read.")]
    string? expectedHash = null,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await service.EditAsync(path, oldText, newText, replaceAll, expectedHash, cancellationToken));

  [McpServerTool(Name = "move_note", OpenWorld = false, Title = "Move note")]
  [Description("Move or rename a note. Fails when the destination exists unless overwrite is true.")]
  public async Task<string> MoveNote(
    [Description("Current vault-relative path.")]
    string from,
    [Description("New vault-relative path. Parent folders are created when missing.")]
    string to,
    [Description("Replace an existing note at the destination.")]
    bool overwrite = false,
    CancellationToken cancellationToken = default) =>
    ToolJson.Serialize(await service.MoveAsync(from, to, overwrite, cancellationToken));
}
