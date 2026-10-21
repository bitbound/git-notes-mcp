using Bitbound.GitNotesMcp.Notes;
using ModelContextProtocol;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class NotePathResolverTests
{
  [Fact]
  public void Resolves_a_nested_note()
  {
    using var vault = new TempVault();
    var resolver = new NotePathResolver(vault.Options);

    var path = resolver.Resolve("folder/note.md");

    Assert.EndsWith(Path.Combine("folder", "note.md"), path);
  }

  [Fact]
  public void Rejects_parent_traversal()
  {
    using var vault = new TempVault();
    var resolver = new NotePathResolver(vault.Options);

    Assert.Throws<McpException>(() => resolver.Resolve("../escape.md"));
  }

  [Fact]
  public void Rejects_absolute_paths()
  {
    using var vault = new TempVault();
    var resolver = new NotePathResolver(vault.Options);

    Assert.Throws<McpException>(() => resolver.Resolve(Path.Combine(vault.Root, "note.md")));
  }

  [Fact]
  public void Rejects_the_git_directory()
  {
    using var vault = new TempVault();
    var resolver = new NotePathResolver(vault.Options);

    Assert.Throws<McpException>(() => resolver.Resolve(".git/config.md"));
  }

  [Fact]
  public void Rejects_non_markdown_files()
  {
    using var vault = new TempVault();
    var resolver = new NotePathResolver(vault.Options);

    Assert.Throws<McpException>(() => resolver.Resolve("notes/script.sh"));
  }

  [Fact]
  public void Rejects_a_symlink_escape()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    using var vault = new TempVault();
    var outside = Path.Combine(Path.GetTempPath(), "git-notes-outside-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outside);

    try
    {
      File.WriteAllText(Path.Combine(outside, "secret.md"), "secret");
      Directory.CreateSymbolicLink(Path.Combine(vault.Root, "link"), outside);

      var resolver = new NotePathResolver(vault.Options);

      Assert.Throws<McpException>(() => resolver.Resolve("link/secret.md", mustExist: true));
    }
    finally
    {
      Directory.Delete(outside, recursive: true);
    }
  }
}
