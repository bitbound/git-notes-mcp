using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Bitbound.GitNotesMcp.Git;

public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError)
{
  public bool Success => ExitCode == 0;
}

public sealed class GitCommandException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class GitCommandRunner(Configuration.NotesOptions options)
{
  private static readonly Regex CredentialPattern = new(@"://[^/@\s]+@", RegexOptions.Compiled);

  public static string Redact(string text) => CredentialPattern.Replace(text, "://***@");

  public async Task<GitResult> RunAsync(
    IReadOnlyList<string> arguments,
    bool throwOnFailure = true,
    int? timeoutSeconds = null,
    CancellationToken cancellationToken = default)
  {
    var startInfo = new ProcessStartInfo("git")
    {
      WorkingDirectory = options.VaultPath,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    SanitizeEnvironment(startInfo.Environment);

    using var process = new Process { StartInfo = startInfo };
    try
    {
      process.Start();
      // An unredirected stdin would be inherited as this server's stdio pipe. Any read by
      // git or its child processes (credential helpers, hooks, editors) would block forever.
      process.StandardInput.Close();
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
      throw new GitCommandException("Git was not found on PATH. Install Git and make sure it is available.", ex);
    }

    var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
    var timeout = TimeSpan.FromSeconds(timeoutSeconds ?? options.GitTimeoutSeconds);

    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutSource.CancelAfter(timeout);

    try
    {
      await process.WaitForExitAsync(timeoutSource.Token);
    }
    catch (OperationCanceledException)
    {
      TryKill(process);
      cancellationToken.ThrowIfCancellationRequested();
      throw new GitCommandException($"git {Describe(arguments)} timed out after {timeout.TotalSeconds:0} seconds.");
    }

    var output = await standardOutput;
    var error = await standardError;

    if (throwOnFailure && process.ExitCode != 0)
    {
      var message = string.IsNullOrWhiteSpace(error) ? output : error;
      throw new GitCommandException($"git {Describe(arguments)} failed with exit code {process.ExitCode}. {Redact(message).Trim()}");
    }

    return new GitResult(process.ExitCode, output, error);
  }

  /// <summary>
  /// MCP hosts launch servers with their own git environment (a bundled GIT_EXEC_PATH, injected
  /// credential helpers, askpass shims). Mixed with a different git on PATH, children can hang or
  /// fail at startup. Drop inherited GIT_* variables and control the git environment explicitly.
  /// </summary>
  internal static void SanitizeEnvironment(IDictionary<string, string?> environment)
  {
    var polluted = environment
      .Keys
      .Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
        && !key.StartsWith("GIT_NOTES_", StringComparison.OrdinalIgnoreCase))
      .ToList();

    foreach (var key in polluted)
    {
      environment.Remove(key);
    }

    environment["GIT_TERMINAL_PROMPT"] = "0";
    environment["GIT_PAGER"] = "cat";
    // Never leave a child able to block on a human. "true" is the no-op from Git's bundled shell.
    environment["GIT_EDITOR"] = "true";
    environment["GIT_SEQUENCE_EDITOR"] = "true";
    environment["GIT_ASKPASS"] = "echo";
    environment["GCM_INTERACTIVE"] = "never";
    environment["GIT_SSH_COMMAND"] = "ssh -oBatchMode=yes";
    environment["GIT_OPTIONAL_LOCKS"] = "0";
    environment["LC_ALL"] = "C";
  }

  private static string Describe(IReadOnlyList<string> arguments) =>
    string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));

  private static void TryKill(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
    }
  }
}
