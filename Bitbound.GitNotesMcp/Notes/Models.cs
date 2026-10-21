using Bitbound.GitNotesMcp.Git;

namespace Bitbound.GitNotesMcp.Notes;

public sealed record NoteSummary(string Path, long SizeBytes, DateTimeOffset ModifiedUtc);

public sealed record NoteContent(
  string Path,
  string Content,
  int StartLine,
  int EndLine,
  int TotalLines,
  string Hash,
  long SizeBytes);

public sealed record NoteWriteResult(string Path, string Hash, long SizeBytes);

public sealed record NoteEditResult(string Path, int Replacements, string Hash);

public sealed record NoteDeleteResult(string Path);

public sealed record NoteMoveResult(string From, string To);

public sealed record SearchHit(
  string Path,
  int Line,
  int Column,
  string Match,
  IReadOnlyList<string> ContextBefore,
  IReadOnlyList<string> ContextAfter);

public sealed record SearchResponse(IReadOnlyList<SearchHit> Matches, bool Truncated);

public sealed record SearchPayload(
  string Backend,
  SearchResponse? Matches = null,
  string? Text = null,
  string? FallbackReason = null);

public sealed record MutationResult<T>(T Value, CheckpointResult Checkpoint);
