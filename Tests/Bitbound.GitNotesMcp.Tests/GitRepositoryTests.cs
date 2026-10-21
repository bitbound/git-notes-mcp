using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class GitRepositoryTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Initialize_creates_repository_and_setup_files()
  {
    using var vault = new TempVault();
    var git = TestServices.Git(vault.Options);

    await git.InitializeAsync(Ct);

    Assert.True(Directory.Exists(Path.Combine(vault.Root, ".git")));
    Assert.Contains("*.tmp", File.ReadAllText(Path.Combine(vault.Root, ".gitignore")));
    Assert.Contains("*.md text eol=lf", File.ReadAllText(Path.Combine(vault.Root, ".gitattributes")));

    var remote = vault.Run("remote", "get-url", "origin");
    Assert.Equal(0, remote.ExitCode);
    Assert.Equal(vault.RemotePath, remote.Output.Trim());

    var log = vault.Run("log", "--oneline");
    Assert.Contains("Initialize notes vault.", log.Output);
  }

  [Fact]
  public async Task Initialize_rejects_a_different_remote()
  {
    using var vault = new TempVault();
    await TestServices.Git(vault.Options).InitializeAsync(Ct);

    var other = vault.Options with { RemoteUrl = vault.RemotePath + "-other" };

    var error = await Assert.ThrowsAsync<NotesConfigurationException>(() => TestServices.Git(other).InitializeAsync(Ct));
    Assert.Contains("GIT_NOTES_ALLOW_REMOTE_MISMATCH", error.Message);
  }

  [Fact]
  public async Task Initialize_rejects_a_vault_inside_another_repository()
  {
    using var vault = new TempVault();
    var parent = Path.Combine(vault.Root, "parent");
    Directory.CreateDirectory(parent);
    Assert.Equal(0, TempVault.Git(parent, "init", "-b", "main").ExitCode);

    var nested = Path.Combine(parent, "notes");
    Directory.CreateDirectory(nested);
    var options = vault.Options with { VaultPath = nested };

    var error = await Assert.ThrowsAsync<NotesConfigurationException>(() => TestServices.Git(options).InitializeAsync(Ct));
    Assert.Contains("inside the git repository", error.Message);
  }

  [Fact]
  public async Task Initialize_rejects_a_different_branch()
  {
    using var vault = new TempVault();
    await TestServices.Git(vault.Options).InitializeAsync(Ct);
    Assert.Equal(0, vault.Run("checkout", "-b", "other").ExitCode);

    var error = await Assert.ThrowsAsync<NotesConfigurationException>(() => TestServices.Git(vault.Options).InitializeAsync(Ct));
    Assert.Contains("GIT_NOTES_BRANCH", error.Message);
  }

  [Fact]
  public async Task Commit_returns_null_when_there_is_nothing_to_commit()
  {
    using var vault = new TempVault();
    var (git, _) = await TestServices.CreateAsync(vault, Ct);

    Assert.Null(await git.CommitAsync("No changes.", Ct));
  }

  [Fact]
  public async Task Counts_untracked_lines()
  {
    using var vault = new TempVault();
    var (git, _) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, "note.md"), "one\ntwo\nthree\n");

    Assert.Equal(3, await git.CountChangedLinesAsync(Ct));
  }

  [Fact]
  public async Task Auto_checkpoint_waits_for_the_threshold()
  {
    using var vault = new TempVault(autoCommit: true, threshold: 5);
    var (git, _) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, "note.md"), "one\ntwo\n");

    var result = await git.TryAutoCheckpointAsync(Ct);

    Assert.False(result.Committed);
    Assert.Equal(2, result.ChangedLines);
  }

  [Fact]
  public async Task Auto_checkpoint_commits_at_the_threshold()
  {
    using var vault = new TempVault(autoCommit: true, threshold: 3);
    var (git, _) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, "note.md"), "one\ntwo\nthree\n");

    var result = await git.TryAutoCheckpointAsync(Ct);

    Assert.True(result.Committed);
    Assert.NotNull(result.Commit);
    Assert.Equal(vault.Options.AutoCommitMessage, vault.Run("log", "-1", "--format=%s").Output.Trim());
  }

  [Fact]
  public async Task Commit_pushes_and_tags()
  {
    using var vault = new TempVault(autoPush: true);
    var (git, _) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, "note.md"), "one\n");

    Assert.NotNull(await git.CommitAsync("Add note.", Ct));
    var push = await git.PushAsync(Ct);
    Assert.True(push.Pushed);
    Assert.Contains("Add note.", vault.RunRemote("log", "--oneline", "main").Output);

    var tag = await git.CreateTagAsync("notes-1", "First notes tag", push: true, cancellationToken: Ct);
    Assert.True(tag.Pushed);
    Assert.Contains("notes-1", vault.RunRemote("tag", "--list").Output);
  }

  [Fact]
  public async Task Tag_requires_a_clean_tree_and_a_unique_name()
  {
    using var vault = new TempVault();
    var (git, _) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, "dirty.md"), "x");

    await Assert.ThrowsAsync<GitCommandException>(() =>
      git.CreateTagAsync("dirty", "x", push: false, cancellationToken: Ct));

    File.Delete(Path.Combine(vault.Root, "dirty.md"));

    var tag = await git.CreateTagAsync("clean", "x", push: false, cancellationToken: Ct);
    Assert.Equal("clean", tag.Name);
    await Assert.ThrowsAsync<GitCommandException>(() =>
      git.CreateTagAsync("clean", "x", push: false, cancellationToken: Ct));
  }
}
