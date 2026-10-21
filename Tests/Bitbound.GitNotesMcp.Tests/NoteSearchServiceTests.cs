namespace Bitbound.GitNotesMcp.Tests;

public sealed class NoteSearchServiceTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Keyword_search_is_used_when_zvec_is_not_configured()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("a.md", "alpha\n", overwrite: false);
    var search = TestServices.Search(vault.Options, store);

    var result = await search.SearchAsync("alpha", folder: null, regex: false, caseSensitive: false, contextLines: 0, limit: 10, Ct);

    Assert.Equal("keyword", result.Backend);
    Assert.NotNull(result.Matches);
    Assert.Single(result.Matches.Matches);
    Assert.Null(result.FallbackReason);
  }

  [Fact]
  public async Task Regex_search_stays_on_keyword_when_zvec_is_configured()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("a.md", "alpha\n", overwrite: false);

    var options = vault.Options with { ZvecEnabled = true, ZvecCommand = "/nonexistent/zvec-grep" };
    var search = TestServices.Search(options, TestServices.Store(options));

    var result = await search.SearchAsync("al.ha", folder: null, regex: true, caseSensitive: false, contextLines: 0, limit: 10, Ct);

    Assert.Equal("keyword", result.Backend);
    Assert.Null(result.FallbackReason);
  }

  [Fact]
  public async Task Missing_zvec_command_falls_back_to_keyword_search()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("a.md", "alpha\n", overwrite: false);

    var options = vault.Options with { ZvecEnabled = true, ZvecCommand = "/nonexistent/zvec-grep" };
    var search = TestServices.Search(options, TestServices.Store(options));

    var result = await search.SearchAsync("alpha", folder: null, regex: false, caseSensitive: false, contextLines: 0, limit: 10, Ct);

    Assert.Equal("keyword", result.Backend);
    Assert.NotNull(result.FallbackReason);
    Assert.NotNull(result.Matches);
    Assert.Single(result.Matches.Matches);
  }
}
