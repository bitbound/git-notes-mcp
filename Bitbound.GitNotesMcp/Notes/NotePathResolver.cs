using Bitbound.GitNotesMcp.Configuration;
using ModelContextProtocol;

namespace Bitbound.GitNotesMcp.Notes;

public sealed class NotePathResolver
{
  private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
    ? StringComparison.OrdinalIgnoreCase
    : StringComparison.Ordinal;

  private readonly string _root;

  private readonly string _rootWithSeparator;

  public NotePathResolver(NotesOptions options)
  {
    var root = Path.GetFullPath(options.VaultPath);
    if (Directory.Exists(root))
    {
      root = new DirectoryInfo(root).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root;
    }

    _root = root;
    _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
      ? _root
      : _root + Path.DirectorySeparatorChar;
  }

  public string Root => _root;

  public static bool IsMarkdown(string path) =>
    path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

  public string Resolve(string relativePath, bool mustExist = false, bool requireMarkdown = true)
  {
    if (string.IsNullOrWhiteSpace(relativePath))
    {
      throw new McpException("A note path is required.");
    }

    if (relativePath.Contains('\0'))
    {
      throw new McpException("The path contains an invalid character.");
    }

    if (Path.IsPathRooted(relativePath))
    {
      throw new McpException("The path must be relative to the notes root.");
    }

    var normalized = relativePath.Replace('\\', '/');
    foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
      if (segment == ".")
      {
        continue;
      }

      if (segment == "..")
      {
        throw new McpException("The path must not contain '..'.");
      }

      if (segment.Contains(':'))
      {
        throw new McpException("The path contains an invalid character.");
      }

      if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || segment.Equals(".zvec-grep", StringComparison.OrdinalIgnoreCase))
      {
        throw new McpException($"The path must not enter '{segment}'.");
      }
    }

    var combined = Path.GetFullPath(Path.Combine(_root, normalized));
    if (!IsUnderRoot(combined))
    {
      throw new McpException("The path escapes the notes root.");
    }

    var physical = ResolvePhysical(combined);
    if (!IsUnderRoot(physical))
    {
      throw new McpException("The path escapes the notes root through a symbolic link.");
    }

    if (requireMarkdown && !IsMarkdown(combined))
    {
      throw new McpException("Only .md and .markdown files are supported.");
    }

    if (mustExist && !File.Exists(combined))
    {
      throw new McpException($"Note '{relativePath}' does not exist.");
    }

    return combined;
  }

  public string ResolveDirectory(string relativePath)
  {
    var full = Resolve(relativePath, mustExist: false, requireMarkdown: false);
    if (!Directory.Exists(full))
    {
      throw new McpException($"Folder '{relativePath}' does not exist.");
    }

    return full;
  }

  private bool IsUnderRoot(string path) =>
    path.Equals(_root, PathComparison)
    || path.StartsWith(_rootWithSeparator, PathComparison);

  private string ResolvePhysical(string fullPath)
  {
    var relative = Path.GetRelativePath(_root, fullPath);
    var parts = relative.Split(
      new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
      StringSplitOptions.RemoveEmptyEntries);

    var current = _root;
    foreach (var part in parts)
    {
      var next = Path.Combine(current, part);
      string? target = null;
      if (Directory.Exists(next))
      {
        target = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
      }
      else if (File.Exists(next))
      {
        target = new FileInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
      }

      current = target ?? next;
    }

    return Path.GetFullPath(current);
  }
}
