using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;
using Bitbound.GitNotesMcp.Notes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitbound.GitNotesMcp.Tests;

internal static class TestServices
{
  public static GitRepository Git(NotesOptions options) =>
    new(options, new GitCommandRunner(options), NullLogger<GitRepository>.Instance);

  public static NoteStore Store(NotesOptions options) =>
    new(options, new NotePathResolver(options));

  public static async Task<(GitRepository Git, NoteStore Store)> CreateAsync(
    TempVault vault,
    CancellationToken cancellationToken = default)
  {
    var git = Git(vault.Options);
    await git.InitializeAsync(cancellationToken);
    return (git, Store(vault.Options));
  }
}
