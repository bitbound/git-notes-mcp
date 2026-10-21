using System.ComponentModel;
using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Bitbound.GitNotesMcp.Tools;

[McpServerToolType]
public sealed class GitReadTools(NotesOptions options, GitRepository git)
{
  [McpServerTool(Name = "git_status", ReadOnly = true, OpenWorld = false, Title = "Git status")]
  [Description("Show the current branch, last commit, dirty file count, ahead and behind counts, and uncommitted line changes.")]
  public async Task<string> GitStatus(CancellationToken cancellationToken = default)
  {
    try
    {
      return ToolJson.Serialize(new
      {
        Repository = await git.GetStatusAsync(cancellationToken),
        AutoCommit = options.AutoCommit,
        AutoCommitThreshold = options.AutoCommitThreshold,
        AutoPush = options.AutoPush,
        ReadOnly = options.ReadOnly
      });
    }
    catch (GitCommandException ex)
    {
      throw new McpException(ex.Message);
    }
  }
}
