using System.ComponentModel;
using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Bitbound.GitNotesMcp.Tools;

[McpServerToolType]
public sealed class GitWriteTools(NotesOptions options, GitRepository git)
{
  [McpServerTool(Name = "commit_notes", Destructive = false, OpenWorld = false, Title = "Commit notes")]
  [Description("Commit every change in the vault. Returns committed=false when there is nothing to commit.")]
  public async Task<string> CommitNotes(
    [Description("Commit message. Defaults to the configured automatic checkpoint message.")]
    string? message = null,
    CancellationToken cancellationToken = default)
  {
    var effectiveMessage = string.IsNullOrWhiteSpace(message) ? options.AutoCommitMessage : message.Trim();

    try
    {
      var commit = await git.CommitAsync(effectiveMessage, cancellationToken);
      return ToolJson.Serialize(new
      {
        Committed = commit is not null,
        Commit = commit,
        Message = effectiveMessage
      });
    }
    catch (GitCommandException ex)
    {
      throw new McpException(ex.Message);
    }
  }

  [McpServerTool(Name = "create_tag", Destructive = false, OpenWorld = true, Title = "Create tag")]
  [Description("Commit pending changes, then create an annotated tag at the current commit. Optionally push the tag.")]
  public async Task<string> CreateTag(
    [Description("Tag name, for example notes-2026-10-21.")]
    string name,
    [Description("Annotation message. Defaults to the tag name.")]
    string? message = null,
    [Description("Push the tag to the remote. Auto-push also pushes tags.")]
    bool push = false,
    CancellationToken cancellationToken = default)
  {
    try
    {
      return ToolJson.Serialize(await git.CreateTagAsync(
        name,
        string.IsNullOrWhiteSpace(message) ? name : message.Trim(),
        push,
        cancellationToken));
    }
    catch (GitCommandException ex)
    {
      throw new McpException(ex.Message);
    }
  }

  [McpServerTool(Name = "push_notes", Destructive = false, OpenWorld = true, Title = "Push notes")]
  [Description("Commit pending changes, fetch the remote, merge when behind, then push the current branch.")]
  public async Task<string> PushNotes(CancellationToken cancellationToken = default)
  {
    try
    {
      return ToolJson.Serialize(await git.PushAsync(cancellationToken));
    }
    catch (GitCommandException ex)
    {
      throw new McpException(ex.Message);
    }
  }
}
