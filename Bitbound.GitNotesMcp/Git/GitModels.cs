namespace Bitbound.GitNotesMcp.Git;

public sealed record RepositoryStatus(
  string Branch,
  string? LastCommitHash,
  string? LastCommitSubject,
  string? LastCommitAuthor,
  DateTimeOffset? LastCommitAt,
  bool Dirty,
  int ChangedFiles,
  bool HasUpstream,
  int Ahead,
  int Behind,
  int ChangedLines);

public sealed record PushResult(string Remote, string Branch, int Ahead, int Behind, bool Pushed);

public sealed record TagResult(string Name, string Commit, bool Pushed);

public sealed record CheckpointResult(
  bool Attempted,
  bool Committed,
  bool Pushed,
  string? Commit,
  string? Error,
  int? ChangedLines)
{
  public static CheckpointResult None { get; } = new(false, false, false, null, null, null);
}
