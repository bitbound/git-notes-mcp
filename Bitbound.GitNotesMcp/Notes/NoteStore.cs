using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bitbound.GitNotesMcp.Configuration;
using ModelContextProtocol;

namespace Bitbound.GitNotesMcp.Notes;

public sealed class NoteStore(NotesOptions options, NotePathResolver resolver)
{
  private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Compiled);

  public NoteWriteResult Append(string path, string content)
  {
    var full = resolver.Resolve(path);
    var existing = File.Exists(full) ? File.ReadAllText(full, Encoding.UTF8) : string.Empty;
    var updated = existing;

    if (updated.Length > 0 && !updated.EndsWith('\n'))
    {
      updated += "\n";
    }

    updated += content;
    if (updated.Length > 0 && !updated.EndsWith('\n'))
    {
      updated += "\n";
    }

    WriteAtomic(full, updated);
    return new NoteWriteResult(Relative(full), ComputeHash(updated), Encoding.UTF8.GetByteCount(updated));
  }

  public NoteWriteResult Create(string path, string content, bool overwrite)
  {
    var full = resolver.Resolve(path);
    if (File.Exists(full) && !overwrite)
    {
      throw new McpException($"Note '{path}' already exists. Pass overwrite=true to replace it.");
    }

    WriteAtomic(full, content);
    return new NoteWriteResult(Relative(full), ComputeHash(content), Encoding.UTF8.GetByteCount(content));
  }

  public NoteDeleteResult Delete(string path)
  {
    var full = resolver.Resolve(path, mustExist: true);
    File.Delete(full);
    return new NoteDeleteResult(Relative(full));
  }

  public NoteEditResult Edit(string path, string oldText, string newText, bool replaceAll, string? expectedHash)
  {
    if (string.IsNullOrEmpty(oldText))
    {
      throw new McpException("oldText is required.");
    }

    var full = resolver.Resolve(path, mustExist: true);
    var original = File.ReadAllText(full, Encoding.UTF8);

    if (!string.IsNullOrWhiteSpace(expectedHash)
      && !string.Equals(expectedHash, ComputeHash(original), StringComparison.OrdinalIgnoreCase))
    {
      throw new McpException("The note changed since it was read. Read it again and retry.");
    }

    var occurrences = CountOccurrences(original, oldText);
    if (occurrences == 0)
    {
      throw new McpException("oldText was not found in the note.");
    }

    if (occurrences > 1 && !replaceAll)
    {
      throw new McpException($"oldText occurs {occurrences} times. Provide a longer unique string or set replaceAll=true.");
    }

    var updated = original.Replace(oldText, newText, StringComparison.Ordinal);
    WriteAtomic(full, updated);
    return new NoteEditResult(Relative(full), occurrences, ComputeHash(updated));
  }

  public IReadOnlyList<NoteSummary> List(string? folder, bool recursive, int limit)
  {
    var start = folder is null ? resolver.Root : resolver.ResolveDirectory(folder);
    var max = Math.Clamp(limit, 1, 1000);

    var files = recursive
      ? EnumerateNoteFiles(start)
      : Directory.EnumerateFiles(start).Where(NotePathResolver.IsMarkdown);

    return files
      .Select(ToSummary)
      .OrderBy(summary => summary.Path, StringComparer.Ordinal)
      .Take(max)
      .ToArray();
  }

  public NoteMoveResult Move(string from, string to, bool overwrite)
  {
    var source = resolver.Resolve(from, mustExist: true);
    var destination = resolver.Resolve(to);

    if (File.Exists(destination) && !overwrite)
    {
      throw new McpException($"Note '{to}' already exists. Pass overwrite=true to replace it.");
    }

    var directory = Path.GetDirectoryName(destination);
    if (directory is not null)
    {
      Directory.CreateDirectory(directory);
    }

    File.Move(source, destination, overwrite);
    return new NoteMoveResult(Relative(source), Relative(destination));
  }

  public NoteContent Read(string path, string? heading, int? offset, int? limit)
  {
    var full = resolver.Resolve(path, mustExist: true);
    var info = new FileInfo(full);
    if (info.Length > options.MaxFileBytes)
    {
      throw new McpException($"Note '{path}' is larger than {options.MaxFileBytes} bytes. Read it in ranges with offset and limit.");
    }

    var text = File.ReadAllText(full, Encoding.UTF8);
    var hash = ComputeHash(text);
    var lines = text
      .Split('\n')
      .Select(line => line.TrimEnd('\r'))
      .ToArray();

    if (heading is not null)
    {
      if (offset is not null || limit is not null)
      {
        throw new McpException("Use either heading or offset and limit, not both.");
      }

      var (start, end) = FindSection(lines, heading);
      return new NoteContent(
        Relative(full),
        string.Join('\n', lines[start..end]).TrimEnd('\n'),
        start + 1,
        end,
        lines.Length,
        hash,
        info.Length);
    }

    var from = Math.Clamp((offset ?? 1) - 1, 0, lines.Length);
    var take = Math.Max(1, limit ?? (lines.Length - from));
    var to = Math.Min(lines.Length, from + take);

    return new NoteContent(
      Relative(full),
      string.Join('\n', lines[from..to]),
      from + 1,
      to,
      lines.Length,
      hash,
      info.Length);
  }

  public SearchResponse Search(string query, string? folder, bool regex, bool caseSensitive, int contextLines, int limit)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      throw new McpException("query is required.");
    }

    var start = folder is null ? resolver.Root : resolver.ResolveDirectory(folder);
    var maxHits = Math.Clamp(limit, 1, 100);
    var context = Math.Clamp(contextLines, 0, 10);

    var regexOptions = RegexOptions.CultureInvariant
      | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

    Regex pattern;
    try
    {
      pattern = new Regex(regex ? query : Regex.Escape(query), regexOptions, TimeSpan.FromSeconds(2));
    }
    catch (ArgumentException ex)
    {
      throw new McpException($"Invalid regular expression. {ex.Message}");
    }

    var hits = new List<SearchHit>();
    foreach (var file in EnumerateNoteFiles(start).OrderBy(path => path, StringComparer.Ordinal))
    {
      var info = new FileInfo(file);
      if (!info.Exists || info.Length > options.MaxFileBytes)
      {
        continue;
      }

      string text;
      try
      {
        text = File.ReadAllText(file, Encoding.UTF8);
      }
      catch (IOException)
      {
        continue;
      }

      var hash = ComputeHash(text);
      var lines = text
        .Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .ToArray();

      for (var index = 0; index < lines.Length; index++)
      {
        Match match;
        try
        {
          match = pattern.Match(lines[index]);
        }
        catch (RegexMatchTimeoutException)
        {
          throw new McpException("The search took too long to evaluate. Narrow the pattern and try again.");
        }

        if (!match.Success)
        {
          continue;
        }

        var before = Enumerable
          .Range(Math.Max(0, index - context), Math.Min(context, index))
          .Select(i => new ContextLine(i + 1, lines[i]))
          .ToArray();
        var after = Enumerable
          .Range(index + 1, Math.Min(context, lines.Length - index - 1))
          .Select(i => new ContextLine(i + 1, lines[i]))
          .ToArray();

        hits.Add(new SearchHit(
          Relative(file),
          index + 1,
          match.Index + 1,
          lines[index],
          before,
          after,
          hash));

        if (hits.Count >= maxHits)
        {
          return new SearchResponse(hits, true);
        }
      }
    }

    return new SearchResponse(hits, false);
  }

  private static string ComputeHash(string content) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

  private static int CountOccurrences(string text, string value)
  {
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
      count++;
      index += value.Length;
    }

    return count;
  }

  private static (int Start, int End) FindSection(string[] lines, string heading)
  {
    var target = NormalizeHeading(heading);
    var start = -1;
    var level = 0;

    for (var index = 0; index < lines.Length; index++)
    {
      var match = HeadingPattern.Match(lines[index]);
      if (match.Success
        && string.Equals(NormalizeHeading(match.Groups[2].Value), target, StringComparison.OrdinalIgnoreCase))
      {
        start = index;
        level = match.Groups[1].Length;
        break;
      }
    }

    if (start < 0)
    {
      var headings = lines
        .Select(line => HeadingPattern.Match(line))
        .Where(match => match.Success)
        .Select(match => match.Groups[2].Value)
        .Take(20)
        .ToArray();

      throw new McpException(headings.Length == 0
        ? $"Heading '{heading}' was not found. The note has no headings."
        : $"Heading '{heading}' was not found. Available headings: {string.Join("; ", headings)}.");
    }

    var end = lines.Length;
    for (var index = start + 1; index < lines.Length; index++)
    {
      var match = HeadingPattern.Match(lines[index]);
      if (match.Success && match.Groups[1].Length <= level)
      {
        end = index;
        break;
      }
    }

    return (start, end);
  }

  private static string NormalizeHeading(string heading) =>
    string.Join(' ', heading.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

  private static void WriteAtomic(string path, string content)
  {
    var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The note path has no directory.");
    Directory.CreateDirectory(directory);

    var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    try
    {
      using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
      {
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
      }

      File.Move(temp, path, overwrite: true);
    }
    finally
    {
      if (File.Exists(temp))
      {
        File.Delete(temp);
      }
    }
  }

  private IEnumerable<string> EnumerateNoteFiles(string start)
  {
    var stack = new Stack<string>();
    stack.Push(start);

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
        if (NotePathResolver.IsMarkdown(file))
        {
          yield return file;
        }
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

  private string Relative(string fullPath) =>
    Path.GetRelativePath(resolver.Root, fullPath).Replace('\\', '/');

  private NoteSummary ToSummary(string fullPath)
  {
    var info = new FileInfo(fullPath);
    return new NoteSummary(
      Path.GetRelativePath(resolver.Root, fullPath).Replace('\\', '/'),
      info.Length,
      info.LastWriteTimeUtc);
  }
}
