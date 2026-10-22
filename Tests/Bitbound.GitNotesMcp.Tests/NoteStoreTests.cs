using Bitbound.GitNotesMcp.Notes;
using ModelContextProtocol;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class NoteStoreTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Create_read_edit_and_append()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);

    var created = store.Create("notes/first.md", "# First\n\nHello.\n", overwrite: false);
    Assert.EndsWith("notes/first.md", created.Path);

    var read = store.Read("notes/first.md", heading: null, offset: null, limit: null);
    Assert.Contains("Hello.", read.Content);
    Assert.Equal(4, read.TotalLines);

    var edited = store.Edit("notes/first.md", "Hello.", "Hi.", replaceAll: false, expectedHash: read.Hash);
    Assert.Equal(1, edited.Replacements);

    store.Append("notes/first.md", "More.");
    var updated = store.Read("notes/first.md", heading: null, offset: null, limit: null);
    Assert.Equal("# First\n\nHi.\nMore.\n", updated.Content);
  }

  [Fact]
  public async Task Edit_requires_a_unique_match()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("dup.md", "same\nsame\n", overwrite: false);

    var error = Assert.Throws<McpException>(() => store.Edit("dup.md", "same", "x", replaceAll: false, expectedHash: null));
    Assert.Contains("2 times", error.Message);

    var replaced = store.Edit("dup.md", "same", "x", replaceAll: true, expectedHash: null);
    Assert.Equal(2, replaced.Replacements);
  }

  [Fact]
  public async Task Edit_rejects_a_stale_hash()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("stale.md", "one\n", overwrite: false);
    var read = store.Read("stale.md", heading: null, offset: null, limit: null);
    store.Append("stale.md", "two");

    Assert.Throws<McpException>(() =>
      store.Edit("stale.md", "one", "x", replaceAll: false, expectedHash: read.Hash));
  }

  [Fact]
  public async Task Read_returns_a_heading_section()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create(
      "outline.md",
      "# Title\n\nIntro.\n\n## Details\n\nBody.\n\n### Deeper\n\nMore.\n\n## Other\n\nEnd.\n",
      overwrite: false);

    var section = store.Read("outline.md", "Details", offset: null, limit: null);

    Assert.StartsWith("## Details", section.Content);
    Assert.Contains("More.", section.Content);
    Assert.DoesNotContain("End.", section.Content);
    Assert.Equal(5, section.StartLine);
  }

  [Fact]
  public async Task Read_returns_a_line_range()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("range.md", "one\ntwo\nthree\nfour\n", overwrite: false);

    var read = store.Read("range.md", heading: null, offset: 2, limit: 2);

    Assert.Equal("two\nthree", read.Content);
    Assert.Equal(2, read.StartLine);
    Assert.Equal(3, read.EndLine);
  }

  [Fact]
  public async Task Search_returns_line_column_and_context()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("search.md", "# Search\n\nAlpha beta\nGamma alpha\n", overwrite: false);

    var result = store.Search("alpha", folder: null, regex: false, caseSensitive: false, contextLines: 1, limit: 20);

    Assert.Equal(2, result.Matches.Count);
    Assert.Equal(3, result.Matches[0].Line);
    Assert.Equal(1, result.Matches[0].Column);
    Assert.Equal("Alpha beta", result.Matches[0].Match);
    Assert.Equal(2, result.Matches[0].ContextBefore[0].Line);
    Assert.Equal("", result.Matches[0].ContextBefore[0].Text);
    Assert.Equal(4, result.Matches[0].ContextAfter[0].Line);
    Assert.Equal("Gamma alpha", result.Matches[0].ContextAfter[0].Text);
    Assert.Equal(store.Read("search.md", null, null, null).Hash, result.Matches[0].Hash);
  }

  [Fact]
  public async Task Search_clamps_context_to_ten_lines_with_sequential_numbers()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    var body = string.Join('\n', Enumerable.Range(1, 21).Select(n => n == 11 ? "needle" : $"line {n}"));
    store.Create("deep.md", body + "\n", overwrite: false);

    var hit = Assert.Single(store.Search("needle", folder: null, regex: false, caseSensitive: false, contextLines: 50, limit: 5).Matches);

    Assert.Equal(11, hit.Line);
    Assert.Equal(10, hit.ContextBefore.Count);
    Assert.Equal(10, hit.ContextAfter.Count);
    Assert.Equal(1, hit.ContextBefore[0].Line);
    Assert.Equal(10, hit.ContextBefore[^1].Line);
    Assert.Equal(12, hit.ContextAfter[0].Line);
    Assert.Equal(21, hit.ContextAfter[^1].Line);
  }

  [Fact]
  public async Task Search_supports_regex_and_limits_results()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("regex.md", "item-123\nitem-abc\n", overwrite: false);
    store.Create("many.md", "hit\nhit\nhit\n", overwrite: false);

    var regex = store.Search(@"item-\d+", folder: null, regex: true, caseSensitive: false, contextLines: 0, limit: 10);
    Assert.Single(regex.Matches);

    var limited = store.Search("hit", folder: null, regex: false, caseSensitive: false, contextLines: 0, limit: 2);
    Assert.Equal(2, limited.Matches.Count);
    Assert.True(limited.Truncated);
  }

  [Fact]
  public async Task List_skips_the_git_directory()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    File.WriteAllText(Path.Combine(vault.Root, ".git", "hidden.md"), "# Hidden");
    store.Create("visible.md", "# Visible\n", overwrite: false);

    var notes = store.List(folder: null, recursive: true, limit: 100);

    Assert.Contains(notes, note => note.Path == "visible.md");
    Assert.DoesNotContain(notes, note => note.Path.Contains("hidden", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task Create_rejects_traversal_and_refuses_overwrite()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);

    Assert.Throws<McpException>(() => store.Create("../escape.md", "x", overwrite: false));

    store.Create("a.md", "a", overwrite: false);
    Assert.Throws<McpException>(() => store.Create("a.md", "b", overwrite: false));

    store.Create("a.md", "b", overwrite: true);
    Assert.Equal("b", store.Read("a.md", heading: null, offset: null, limit: null).Content);
  }

  [Fact]
  public async Task Move_and_delete_a_note()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("a.md", "a\n", overwrite: false);

    var moved = store.Move("a.md", "folder/b.md", overwrite: false);
    Assert.Equal("folder/b.md", moved.To);
    Assert.False(File.Exists(Path.Combine(vault.Root, "a.md")));

    store.Delete("folder/b.md");
    Assert.False(File.Exists(Path.Combine(vault.Root, "folder", "b.md")));
  }

  [Fact]
  public async Task Mutation_triggers_an_auto_checkpoint()
  {
    using var vault = new TempVault(autoCommit: true, threshold: 1);
    var (git, store) = await TestServices.CreateAsync(vault, Ct);
    var service = new NoteService(store, git);

    var result = await service.CreateAsync("note.md", "one\n", overwrite: false, Ct);

    Assert.True(result.Checkpoint.Committed);
    Assert.NotNull(result.Checkpoint.Commit);
  }
}
