using System.Text;
using Bitbound.GitNotesMcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Bitbound.GitNotesMcp.Git;

public sealed class GitRepository(NotesOptions options, GitCommandRunner git, ILogger<GitRepository> logger)
{
  private readonly SemaphoreSlim _writeLock = new(1, 1);

  public async Task<string?> CommitAsync(string message, CancellationToken cancellationToken = default)
  {
    await _writeLock.WaitAsync(cancellationToken);
    try
    {
      return await CommitCoreAsync(message, cancellationToken);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  public async Task<int> CountChangedLinesAsync(CancellationToken cancellationToken = default)
  {
    var total = 0;
    var head = await GitAsync(["rev-parse", "--verify", "HEAD"], cancellationToken, throwOnFailure: false);

    if (!head.Success)
    {
      foreach (var file in EnumerateFiles(options.VaultPath))
      {
        total += CountLines(file);
      }

      return total;
    }

    var numstat = await GitAsync(["diff", "--numstat", "HEAD"], cancellationToken, throwOnFailure: false);
    if (numstat.Success)
    {
      foreach (var line in numstat.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        var parts = line.Split('\t');
        if (parts.Length >= 2
          && int.TryParse(parts[0], out var added)
          && int.TryParse(parts[1], out var deleted))
        {
          total += added + deleted;
        }
      }
    }

    var status = await GitAsync(["status", "--porcelain=v1"], cancellationToken, throwOnFailure: false);
    foreach (var line in status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      if (!line.StartsWith("?? ", StringComparison.Ordinal))
      {
        continue;
      }

      var relative = line[3..].Trim().Trim('"').Replace('\\', '/');
      var full = Path.Combine(options.VaultPath, relative);
      if (Directory.Exists(full))
      {
        foreach (var file in EnumerateFiles(full))
        {
          total += CountLines(file);
        }
      }
      else
      {
        total += CountLines(full);
      }
    }

    return total;
  }

  public async Task<TagResult> CreateTagAsync(string name, string message, bool push, CancellationToken cancellationToken = default)
  {
    await _writeLock.WaitAsync(cancellationToken);
    try
    {
      var check = await GitAsync(["check-ref-format", $"refs/tags/{name}"], cancellationToken, throwOnFailure: false);
      if (!check.Success)
      {
        throw new GitCommandException($"'{name}' is not a valid tag name.");
      }

      var exists = await GitAsync(["rev-parse", "-q", "--verify", $"refs/tags/{name}"], cancellationToken, throwOnFailure: false);
      if (exists.Success)
      {
        throw new GitCommandException($"Tag '{name}' already exists.");
      }

      var status = await GitAsync(["status", "--porcelain=v1"], cancellationToken, throwOnFailure: false);
      var checkpoint = string.IsNullOrWhiteSpace(status.StandardOutput)
        ? null
        : await CommitCoreAsync($"Checkpoint before tag '{name}'.", cancellationToken);

      await GitAsync(["tag", "-a", name, "-m", message], cancellationToken);
      var commit = (await GitAsync(["rev-parse", "HEAD"], cancellationToken)).StandardOutput.Trim();

      var pushed = false;
      if (push || options.AutoPush)
      {
        var result = await GitAsync(
          ["push", options.RemoteName, $"refs/tags/{name}"],
          cancellationToken,
          throwOnFailure: false,
          timeoutSeconds: options.GitTimeoutSeconds * 2);

        if (!result.Success)
        {
          throw new GitCommandException($"Tag '{name}' was created locally but could not be pushed. {GitCommandRunner.Redact(result.StandardError).Trim()}");
        }

        pushed = true;
      }

      return new TagResult(name, commit, pushed, checkpoint);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  public async Task<RepositoryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
  {
    var branch = await GitAsync(["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken, throwOnFailure: false);
    var branchName = branch.Success ? branch.StandardOutput.Trim() : options.Branch;

    var head = await GitAsync(["log", "-1", "--format=%H%x00%an%x00%aI%x00%s"], cancellationToken, throwOnFailure: false);
    string? hash = null;
    string? subject = null;
    string? author = null;
    DateTimeOffset? committedAt = null;

    if (head.Success && !string.IsNullOrWhiteSpace(head.StandardOutput))
    {
      var parts = head.StandardOutput.TrimEnd('\n').Split('\0');
      if (parts.Length >= 4)
      {
        hash = parts[0];
        author = parts[1];
        subject = parts[3];
        if (DateTimeOffset.TryParse(parts[2], out var parsed))
        {
          committedAt = parsed;
        }
      }
    }

    var status = await GitAsync(["status", "--porcelain=v1"], cancellationToken, throwOnFailure: false);
    var changedFiles = string.IsNullOrWhiteSpace(status.StandardOutput)
      ? 0
      : status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    var ahead = 0;
    var behind = 0;
    var counts = await GitAsync(["rev-list", "--left-right", "--count", "HEAD...@{upstream}"], cancellationToken, throwOnFailure: false);
    var hasUpstream = counts.Success;
    if (hasUpstream)
    {
      var parts = counts.StandardOutput.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length >= 2)
      {
        _ = int.TryParse(parts[0], out ahead);
        _ = int.TryParse(parts[1], out behind);
      }
    }

    var changedLines = await CountChangedLinesAsync(cancellationToken);

    return new RepositoryStatus(
      branchName,
      hash,
      subject,
      author,
      committedAt,
      changedFiles > 0,
      changedFiles,
      hasUpstream,
      ahead,
      behind,
      changedLines);
  }

  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    if (options.ReadOnly)
    {
      if (!Directory.Exists(options.VaultPath))
      {
        throw new NotesConfigurationException("Read-only mode requires an existing directory at GIT_NOTES_VAULT_PATH.");
      }

      var existingRoot = await GetRepositoryRootAsync(cancellationToken);
      if (existingRoot is null || !PathsEqual(existingRoot, options.VaultPath))
      {
        throw new NotesConfigurationException("Read-only mode requires GIT_NOTES_VAULT_PATH to be the root of an existing git repository.");
      }

      return;
    }

    Directory.CreateDirectory(options.VaultPath);
    var repositoryRoot = await GetRepositoryRootAsync(cancellationToken);
    if (repositoryRoot is not null && !PathsEqual(repositoryRoot, options.VaultPath))
    {
      throw new NotesConfigurationException(
        $"GIT_NOTES_VAULT_PATH is inside the git repository at {repositoryRoot}. "
        + "Point it at that root or choose a directory outside a repository.");
    }

    var created = repositoryRoot is null;
    if (created)
    {
      await GitAsync(["init", "-b", options.Branch], cancellationToken);
      logger.LogInformation("Initialized a notes repository at {Path}.", options.VaultPath);
    }

    await EnsureBranchAsync(cancellationToken);

    if (options.UserName is not null)
    {
      await GitAsync(["config", "user.name", options.UserName], cancellationToken);
    }

    if (options.UserEmail is not null)
    {
      await GitAsync(["config", "user.email", options.UserEmail], cancellationToken);
    }

    await EnsureIdentityAsync(cancellationToken);
    var setupChanged = EnsureSetupFiles();
    await EnsureRemoteAsync(cancellationToken);

    var committed = false;
    if (created || setupChanged)
    {
      await GitAsync(["add", "--", ".gitignore", ".gitattributes"], cancellationToken);
      var staged = await GitAsync(["diff", "--cached", "--quiet"], cancellationToken, throwOnFailure: false);
      if (staged.ExitCode == 1)
      {
        var message = created ? "Initialize notes vault." : "Update notes vault setup files.";
        await GitAsync(["-c", "commit.gpgsign=false", "commit", "-m", message, "--", ".gitignore", ".gitattributes"], cancellationToken);
        committed = true;
      }
    }

    if (committed && options.AutoPush)
    {
      try
      {
        await PushAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "The initial push failed. The commit stays local.");
      }
    }
  }

  public async Task<PushResult> PushAsync(CancellationToken cancellationToken = default)
  {
    await _writeLock.WaitAsync(cancellationToken);
    try
    {
      return await PushCoreAsync(cancellationToken);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  public async Task<CheckpointResult> TryAutoCheckpointAsync(CancellationToken cancellationToken = default)
  {
    if (!options.AutoCommit)
    {
      return CheckpointResult.None;
    }

    try
    {
      int? changedLines = null;
      if (options.AutoCommitThreshold > 0)
      {
        changedLines = await CountChangedLinesAsync(cancellationToken);
        if (changedLines < options.AutoCommitThreshold)
        {
          return new CheckpointResult(false, false, false, null, null, changedLines);
        }
      }

      var commit = await CommitAsync(options.AutoCommitMessage, cancellationToken);
      var pushed = false;
      if (commit is not null && options.AutoPush)
      {
        await PushAsync(cancellationToken);
        pushed = true;
      }

      return new CheckpointResult(true, commit is not null, pushed, commit, null, changedLines);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "The automatic checkpoint failed.");
      return new CheckpointResult(true, false, false, null, ex.Message, null);
    }
  }

  private static bool AppendMissingLines(string path, string[] required)
  {
    var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
    var existingLines = existing
      .Split('\n')
      .Select(line => line.TrimEnd('\r'))
      .ToHashSet(StringComparer.Ordinal);

    var missing = required.Where(line => !existingLines.Contains(line)).ToArray();
    if (missing.Length == 0)
    {
      return false;
    }

    var builder = new StringBuilder(existing);
    if (existing.Length > 0 && !existing.EndsWith('\n'))
    {
      builder.Append('\n');
    }

    foreach (var line in missing)
    {
      builder.Append(line).Append('\n');
    }

    File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    return true;
  }

  private static IEnumerable<string> EnumerateFiles(string root)
  {
    var stack = new Stack<string>();
    stack.Push(root);

    while (stack.Count > 0)
    {
      var directory = stack.Pop();

      string[] files;
      try
      {
        files = Directory.GetFiles(directory);
      }
      catch (IOException)
      {
        continue;
      }
      catch (UnauthorizedAccessException)
      {
        continue;
      }

      foreach (var file in files)
      {
        yield return file;
      }

      string[] directories;
      try
      {
        directories = Directory.GetDirectories(
          directory,
          "*",
          new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true });
      }
      catch (IOException)
      {
        continue;
      }
      catch (UnauthorizedAccessException)
      {
        continue;
      }

      foreach (var child in directories)
      {
        var name = Path.GetFileName(child);
        if (name is ".git" or ".zvec-grep")
        {
          continue;
        }

        stack.Push(child);
      }
    }
  }

  private static string NormalizeUrl(string url)
  {
    var normalized = url.Trim().TrimEnd('/', '\\').Replace('\\', '/');
    if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
      && (normalized.Contains("://", StringComparison.Ordinal) || normalized.StartsWith("git@", StringComparison.Ordinal)))
    {
      normalized = normalized[..^4];
    }

    return normalized;
  }

  private static bool PathsEqual(string left, string right) =>
    string.Equals(
      ResolveDirectory(left),
      ResolveDirectory(right),
      OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal);

  private static string ResolveDirectory(string path)
  {
    var full = Path.GetFullPath(path);
    if (!Directory.Exists(full))
    {
      return full;
    }

    return new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
  }

  private static bool UrlsEqual(string left, string right) =>
    string.Equals(NormalizeUrl(left), NormalizeUrl(right), StringComparison.OrdinalIgnoreCase);

  private async Task<string?> CommitCoreAsync(string message, CancellationToken cancellationToken)
  {
    await GitAsync(["add", "-A", "--", "."], cancellationToken);
    var staged = await GitAsync(["diff", "--cached", "--quiet"], cancellationToken, throwOnFailure: false);
    if (staged.ExitCode == 0)
    {
      return null;
    }

    if (staged.ExitCode != 1)
    {
      throw new GitCommandException($"Could not inspect the git index. {staged.StandardError.Trim()}");
    }

    await GitAsync(["-c", "commit.gpgsign=false", "commit", "-m", message], cancellationToken);
    var hash = await GitAsync(["rev-parse", "HEAD"], cancellationToken);
    return hash.StandardOutput.Trim();
  }

  private int CountLines(string path)
  {
    try
    {
      var info = new FileInfo(path);
      if (!info.Exists || info.Length > options.MaxFileBytes)
      {
        return 0;
      }

      return File.ReadLines(path).Count();
    }
    catch (IOException)
    {
      return 0;
    }
    catch (UnauthorizedAccessException)
    {
      return 0;
    }
  }

  private async Task EnsureBranchAsync(CancellationToken cancellationToken)
  {
    var result = await GitAsync(["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken, throwOnFailure: false);
    if (!result.Success)
    {
      return;
    }

    var current = result.StandardOutput.Trim();
    if (current == "HEAD")
    {
      throw new NotesConfigurationException("The vault is in a detached HEAD state. Check out a branch before starting the server.");
    }

    if (!string.Equals(current, options.Branch, StringComparison.Ordinal))
    {
      throw new NotesConfigurationException(
        $"The vault is on branch '{current}' but GIT_NOTES_BRANCH is '{options.Branch}'. "
        + "Check out that branch or change the setting.");
    }
  }

  private async Task EnsureIdentityAsync(CancellationToken cancellationToken)
  {
    var name = await GitAsync(["config", "user.name"], cancellationToken, throwOnFailure: false);
    var email = await GitAsync(["config", "user.email"], cancellationToken, throwOnFailure: false);

    if (!name.Success
      || string.IsNullOrWhiteSpace(name.StandardOutput)
      || !email.Success
      || string.IsNullOrWhiteSpace(email.StandardOutput))
    {
      throw new NotesConfigurationException(
        "Git identity is not configured. Set GIT_NOTES_USER_NAME and GIT_NOTES_USER_EMAIL, or configure user.name and user.email globally.");
    }
  }

  private async Task EnsureRemoteAsync(CancellationToken cancellationToken)
  {
    var current = await GitAsync(["remote", "get-url", options.RemoteName], cancellationToken, throwOnFailure: false);
    if (!current.Success)
    {
      await GitAsync(["remote", "add", options.RemoteName, options.RemoteUrl], cancellationToken);
    }
    else if (!UrlsEqual(current.StandardOutput.Trim(), options.RemoteUrl))
    {
      if (!options.AllowRemoteMismatch)
      {
        throw new NotesConfigurationException(
          $"Remote '{options.RemoteName}' points at {GitCommandRunner.Redact(current.StandardOutput.Trim())} instead of {GitCommandRunner.Redact(options.RemoteUrl)}. "
          + "Set GIT_NOTES_ALLOW_REMOTE_MISMATCH=true to replace it.");
      }

      await GitAsync(["remote", "set-url", options.RemoteName, options.RemoteUrl], cancellationToken);
    }

    // git init plus remote add leaves the fetch refspec unset, so remote-tracking refs never appear.
    var refspecKey = $"remote.{options.RemoteName}.fetch";
    var refspec = await GitAsync(["config", "--get", refspecKey], cancellationToken, throwOnFailure: false);
    if (!refspec.Success || string.IsNullOrWhiteSpace(refspec.StandardOutput))
    {
      await GitAsync(
        ["config", refspecKey, $"+refs/heads/*:refs/remotes/{options.RemoteName}/*"],
        cancellationToken);
    }
  }

  private bool EnsureSetupFiles()
  {
    var ignoreChanged = AppendMissingLines(
      Path.Combine(options.VaultPath, ".gitignore"),
      [".DS_Store", "Thumbs.db", "*.tmp", ".zvec-grep/"]);

    var attributesChanged = AppendMissingLines(
      Path.Combine(options.VaultPath, ".gitattributes"),
      ["* text=auto eol=lf", "*.md text eol=lf"]);

    return ignoreChanged || attributesChanged;
  }

  private async Task<string?> GetRepositoryRootAsync(CancellationToken cancellationToken)
  {
    var result = await GitAsync(["rev-parse", "--show-toplevel"], cancellationToken, throwOnFailure: false);
    return result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput)
      ? Path.GetFullPath(result.StandardOutput.Trim())
      : null;
  }

  private Task<GitResult> GitAsync(
    IReadOnlyList<string> arguments,
    CancellationToken cancellationToken,
    bool throwOnFailure = true,
    int? timeoutSeconds = null) =>
    git.RunAsync(arguments, throwOnFailure, timeoutSeconds, cancellationToken);

  private async Task<PushResult> PushCoreAsync(CancellationToken cancellationToken)
  {
    var remote = options.RemoteName;
    var branch = options.Branch;
    var remoteRef = $"refs/remotes/{remote}/{branch}";
    var timeoutSeconds = options.GitTimeoutSeconds * 2;

    var status = await GitAsync(["status", "--porcelain=v1"], cancellationToken, throwOnFailure: false);
    var checkpoint = string.IsNullOrWhiteSpace(status.StandardOutput)
      ? null
      : await CommitCoreAsync("Checkpoint before push.", cancellationToken);

    var fetch = await GitAsync(["fetch", remote], cancellationToken, throwOnFailure: false, timeoutSeconds: timeoutSeconds);
    if (!fetch.Success && !fetch.StandardError.Contains("couldn't find remote ref", StringComparison.OrdinalIgnoreCase))
    {
      throw new GitCommandException($"Could not fetch {remote}. {GitCommandRunner.Redact(fetch.StandardError).Trim()}");
    }

    var remoteExists = (await GitAsync(["rev-parse", "--verify", remoteRef], cancellationToken, throwOnFailure: false)).Success;
    var ahead = 0;
    var behind = 0;

    if (remoteExists)
    {
      var counts = await GitAsync(
        ["rev-list", "--left-right", "--count", $"HEAD...{remote}/{branch}"],
        cancellationToken,
        throwOnFailure: false);

      if (counts.Success)
      {
        var parts = counts.StandardOutput.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
          _ = int.TryParse(parts[0], out ahead);
          _ = int.TryParse(parts[1], out behind);
        }
      }

      if (behind > 0)
      {
        var merge = await GitAsync(["merge", "--no-edit", $"{remote}/{branch}"], cancellationToken, throwOnFailure: false, timeoutSeconds: timeoutSeconds);
        if (!merge.Success)
        {
          await GitAsync(["merge", "--abort"], cancellationToken, throwOnFailure: false);
          throw new GitCommandException($"Merging {remote}/{branch} failed. Resolve the conflict manually. {GitCommandRunner.Redact(merge.StandardError).Trim()}");
        }
      }
    }

    var push = await GitAsync(["push", "-u", remote, $"HEAD:{branch}"], cancellationToken, throwOnFailure: false, timeoutSeconds: timeoutSeconds);
    if (!push.Success)
    {
      throw new GitCommandException($"Push to {remote}/{branch} failed. {GitCommandRunner.Redact(push.StandardError).Trim()}");
    }

    return new PushResult(remote, branch, ahead, behind, true, checkpoint);
  }
}
