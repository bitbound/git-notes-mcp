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

    startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
    startInfo.Environment["GIT_PAGER"] = "cat";
    startInfo.Environment["LC_ALL"] = "C";

    using var process = new Process { StartInfo = startInfo };
    try
    {
      process.Start();
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
