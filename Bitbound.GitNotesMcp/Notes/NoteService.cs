using Bitbound.GitNotesMcp.Git;

namespace Bitbound.GitNotesMcp.Notes;

public sealed class NoteService(NoteStore store, GitRepository git)
{
  private readonly SemaphoreSlim _mutationLock = new(1, 1);

  public Task<MutationResult<NoteWriteResult>> AppendAsync(
    string path,
    string content,
    CancellationToken cancellationToken) =>
    MutateAsync(() => store.Append(path, content), cancellationToken);

  public Task<MutationResult<NoteWriteResult>> CreateAsync(
    string path,
    string content,
    bool overwrite,
    CancellationToken cancellationToken) =>
    MutateAsync(() => store.Create(path, content, overwrite), cancellationToken);

  public Task<MutationResult<NoteDeleteResult>> DeleteAsync(
    string path,
    CancellationToken cancellationToken) =>
    MutateAsync(() => store.Delete(path), cancellationToken);

  public Task<MutationResult<NoteEditResult>> EditAsync(
    string path,
    string oldText,
    string newText,
    bool replaceAll,
    string? expectedHash,
    CancellationToken cancellationToken) =>
    MutateAsync(() => store.Edit(path, oldText, newText, replaceAll, expectedHash), cancellationToken);

  public Task<MutationResult<NoteMoveResult>> MoveAsync(
    string from,
    string to,
    bool overwrite,
    CancellationToken cancellationToken) =>
    MutateAsync(() => store.Move(from, to, overwrite), cancellationToken);

  private async Task<MutationResult<T>> MutateAsync<T>(Func<T> action, CancellationToken cancellationToken)
  {
    await _mutationLock.WaitAsync(cancellationToken);
    try
    {
      var value = action();
      var checkpoint = await git.TryAutoCheckpointAsync(cancellationToken);
      return new MutationResult<T>(value, checkpoint);
    }
    finally
    {
      _mutationLock.Release();
    }
  }
}
