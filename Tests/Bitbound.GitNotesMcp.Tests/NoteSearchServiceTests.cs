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
  public async Task Unreachable_zvec_falls_back_to_keyword_search()
  {
    using var vault = new TempVault();
    var (_, store) = await TestServices.CreateAsync(vault, Ct);
    store.Create("a.md", "alpha\n", overwrite: false);

    var options = vault.Options with
    {
      ZvecUrl = "http://127.0.0.1:1/mcp",
      ZvecTimeoutSeconds = 2
    };

    var search = TestServices.Search(options, TestServices.Store(options));
    var result = await search.SearchAsync("alpha", folder: null, regex: false, caseSensitive: false, contextLines: 0, limit: 10, Ct);

    Assert.Equal("keyword", result.Backend);
    Assert.NotNull(result.FallbackReason);
    Assert.NotNull(result.Matches);
    Assert.Single(result.Matches.Matches);
  }
}
