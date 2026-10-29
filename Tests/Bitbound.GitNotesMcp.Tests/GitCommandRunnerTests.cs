using Bitbound.GitNotesMcp.Git;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class GitCommandRunnerTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Child_reads_closed_stdin_instead_of_the_inherited_stdio_pipe()
  {
    using var vault = new TempVault();
    var runner = new GitCommandRunner(vault.Options);

    // A child left on the parent's stdin would block reading the inherited pipe until timeout.
    var result = await runner.RunAsync(["hash-object", "--stdin"], cancellationToken: Ct);

    Assert.True(result.Success);
    Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", result.StandardOutput.Trim());
  }

  [Fact]
  public void Children_get_a_clean_controlled_git_environment()
  {
    var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
      // Typical MCP host pollution pointing at a bundled git the server cannot rely on.
      ["GIT_EXEC_PATH"] = @"C:\somewhere\github-copilot-git-1.2.3\libexec\git-core",
      ["GIT_SHELL_PATH"] = @"C:\somewhere\github-copilot-git-1.2.3\usr\bin\sh.exe",
      ["GIT_CONFIG_COUNT"] = "3",
      ["GIT_CONFIG_KEY_0"] = "credential.helper",
      ["GIT_CONFIG_VALUE_0"] = "copilot",
      ["GIT_DIR"] = @"C:\not\the\vault",
      ["GIT_WORK_TREE"] = @"C:\not\the\vault",
      ["GIT_ASKPASS"] = string.Empty,
      // Server configuration must survive.
      ["GIT_NOTES_VAULT_PATH"] = @"C:\vault",
      ["PATH"] = @"C:\Windows\system32"
    };

    GitCommandRunner.SanitizeEnvironment(environment);

    Assert.False(environment.ContainsKey("GIT_EXEC_PATH"));
    Assert.False(environment.ContainsKey("GIT_SHELL_PATH"));
    Assert.False(environment.ContainsKey("GIT_CONFIG_COUNT"));
    Assert.False(environment.ContainsKey("GIT_CONFIG_KEY_0"));
    Assert.False(environment.ContainsKey("GIT_DIR"));
    Assert.False(environment.ContainsKey("GIT_WORK_TREE"));
    Assert.Equal(@"C:\vault", environment["GIT_NOTES_VAULT_PATH"]);
    Assert.Equal(@"C:\Windows\system32", environment["PATH"]);
    Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
    Assert.Equal("cat", environment["GIT_PAGER"]);
    Assert.Equal("true", environment["GIT_EDITOR"]);
    Assert.Equal("echo", environment["GIT_ASKPASS"]);
    Assert.Equal("never", environment["GCM_INTERACTIVE"]);
  }
}
