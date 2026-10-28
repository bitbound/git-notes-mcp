using Microsoft.Extensions.Logging;

namespace Bitbound.GitNotesMcp.Configuration;

public sealed record NotesOptions
{
  public required string VaultPath { get; init; }

  public required string RemoteUrl { get; init; }

  public string Branch { get; init; } = "main";

  public string RemoteName { get; init; } = "origin";

  public string? UserName { get; init; }

  public string? UserEmail { get; init; }

  public bool ReadOnly { get; init; }

  public bool AutoCommit { get; init; }

  public string AutoCommitMessage { get; init; } = "Automatic checkpoint.";

  public int AutoCommitThreshold { get; init; } = 100;

  public bool AutoPush { get; init; }

  public bool AllowRemoteMismatch { get; init; }

  public bool SyncOnStart { get; init; } = true;

  public bool SyncBeforeRead { get; init; } = true;

  public int SyncMinSeconds { get; init; } = 60;

  public int GitTimeoutSeconds { get; init; } = 60;

  public long MaxFileBytes { get; init; } = 1024 * 1024;

  public bool ZvecEnabled { get; init; }

  internal string ZvecCommand { get; init; } = "zg";

  public LogLevel LogLevel { get; init; } = LogLevel.Warning;

  public static NotesOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

  internal static NotesOptions From(Func<string, string?> get)
  {
    var vaultPath = Required(get, "GIT_NOTES_VAULT_PATH");
    if (!Path.IsPathFullyQualified(vaultPath))
    {
      throw new NotesConfigurationException("GIT_NOTES_VAULT_PATH must be an absolute path.");
    }

    var remoteUrl = Required(get, "GIT_NOTES_REMOTE_URL");
    var autoPush = GetBool(get, "GIT_NOTES_AUTO_PUSH", false);
    var autoCommit = GetBool(get, "GIT_NOTES_AUTO_COMMIT", false) || autoPush;

    return new NotesOptions
    {
      VaultPath = Path.GetFullPath(vaultPath),
      RemoteUrl = remoteUrl,
      Branch = GetString(get, "GIT_NOTES_BRANCH", "main"),
      RemoteName = GetString(get, "GIT_NOTES_REMOTE_NAME", "origin"),
      UserName = GetOptional(get, "GIT_NOTES_USER_NAME"),
      UserEmail = GetOptional(get, "GIT_NOTES_USER_EMAIL"),
      ReadOnly = GetBool(get, "GIT_NOTES_READ_ONLY", false),
      AutoCommit = autoCommit,
      AutoCommitMessage = GetString(get, "GIT_NOTES_AUTO_COMMIT_MESSAGE", "Automatic checkpoint."),
      AutoCommitThreshold = GetInt(get, "GIT_NOTES_AUTO_COMMIT_THRESHOLD", 100, 0, 100_000),
      AutoPush = autoPush,
      AllowRemoteMismatch = GetBool(get, "GIT_NOTES_ALLOW_REMOTE_MISMATCH", false),
      SyncOnStart = GetBool(get, "GIT_NOTES_SYNC_ON_START", true),
      SyncBeforeRead = GetBool(get, "GIT_NOTES_SYNC_BEFORE_READ", true),
      SyncMinSeconds = GetInt(get, "GIT_NOTES_SYNC_MIN_SECONDS", 60, 0, 3600),
      GitTimeoutSeconds = GetInt(get, "GIT_NOTES_GIT_TIMEOUT_SECONDS", 60, 5, 600),
      MaxFileBytes = GetLong(get, "GIT_NOTES_MAX_FILE_BYTES", 1024 * 1024, 1024, 64 * 1024 * 1024),
      ZvecEnabled = GetBool(get, "GIT_NOTES_ZVEC_ENABLED", false),
      LogLevel = GetLogLevel(get, "GIT_NOTES_LOG_LEVEL", LogLevel.Warning)
    };
  }

  private static string Required(Func<string, string?> get, string name)
  {
    var value = get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new NotesConfigurationException($"{name} is required.");
    }

    return value.Trim();
  }

  private static string? GetOptional(Func<string, string?> get, string name)
  {
    var value = get(name);
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }

  private static string GetString(Func<string, string?> get, string name, string fallback)
  {
    var value = get(name);
    return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
  }

  private static bool GetBool(Func<string, string?> get, string name, bool fallback)
  {
    var value = get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      return fallback;
    }

    return value.Trim().ToLowerInvariant() switch
    {
      "true" or "1" or "yes" => true,
      "false" or "0" or "no" => false,
      _ => throw new NotesConfigurationException($"{name} must be true or false.")
    };
  }

  private static int GetInt(Func<string, string?> get, string name, int fallback, int min, int max)
  {
    var value = get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      return fallback;
    }

    if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
    {
      throw new NotesConfigurationException($"{name} must be an integer between {min} and {max}.");
    }

    return parsed;
  }

  private static long GetLong(Func<string, string?> get, string name, long fallback, long min, long max)
  {
    var value = get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      return fallback;
    }

    if (!long.TryParse(value, out var parsed) || parsed < min || parsed > max)
    {
      throw new NotesConfigurationException($"{name} must be an integer between {min} and {max}.");
    }

    return parsed;
  }

  private static LogLevel GetLogLevel(Func<string, string?> get, string name, LogLevel fallback)
  {
    var value = get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      return fallback;
    }

    if (!Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
    {
      throw new NotesConfigurationException($"{name} must be a log level such as Trace, Debug, Information, Warning, Error, or Critical.");
    }

    return parsed;
  }
}

public sealed class NotesConfigurationException(string message) : Exception(message);
