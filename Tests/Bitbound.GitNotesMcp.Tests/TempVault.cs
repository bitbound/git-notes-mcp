using System.Diagnostics;
using Bitbound.GitNotesMcp.Configuration;

namespace Bitbound.GitNotesMcp.Tests;

internal sealed class TempVault : IDisposable
{
  private readonly string _basePath;

  public TempVault(bool autoCommit = false, int threshold = 100, bool autoPush = false, bool readOnly = false)
  {
    _basePath = Path.Combine(Path.GetTempPath(), "git-notes-mcp-tests", Guid.NewGuid().ToString("N"));
    Root = Path.Combine(_basePath, "vault");
    RemotePath = Path.Combine(_basePath, "remote.git");
    Directory.CreateDirectory(Root);

    var init = Git(Path.GetTempPath(), "init", "--bare", "-b", "main", RemotePath);
    if (init.ExitCode != 0)
    {
      throw new InvalidOperationException(init.Error);
    }

    Options = new NotesOptions
    {
      VaultPath = Root,
      RemoteUrl = RemotePath,
      UserName = "Test",
      UserEmail = "test@example.com",
      AutoCommit = autoCommit || autoPush,
      AutoCommitThreshold = threshold,
      AutoPush = autoPush,
      ReadOnly = readOnly
    };
  }

  public string Root { get; }

  public string RemotePath { get; }

  public NotesOptions Options { get; }

  public (int ExitCode, string Output, string Error) Run(params string[] args) => Git(Root, args);

  public (int ExitCode, string Output, string Error) RunRemote(params string[] args) =>
    Git(Root, new[] { $"--git-dir={RemotePath}" }.Concat(args).ToArray());

  public static (int ExitCode, string Output, string Error) Git(string workingDirectory, params string[] args)
  {
    var startInfo = new ProcessStartInfo("git")
    {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false
    };

    foreach (var argument in args)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output, error);
  }

  public void Dispose()
  {
    try
    {
      Directory.Delete(_basePath, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}
