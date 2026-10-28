using Bitbound.GitNotesMcp.Configuration;

namespace Bitbound.GitNotesMcp.Tests;

public sealed class NotesOptionsTests
{
  [Fact]
  public void Missing_vault_path_is_rejected()
  {
    var error = Assert.Throws<NotesConfigurationException>(() =>
      NotesOptions.From(name => name == "GIT_NOTES_REMOTE_URL" ? "https://example.com/notes.git" : null));

    Assert.Contains("GIT_NOTES_VAULT_PATH", error.Message);
  }

  [Fact]
  public void Relative_vault_path_is_rejected()
  {
    var error = Assert.Throws<NotesConfigurationException>(() =>
      NotesOptions.From(name => name switch
      {
        "GIT_NOTES_VAULT_PATH" => "notes",
        "GIT_NOTES_REMOTE_URL" => "https://example.com/notes.git",
        _ => null
      }));

    Assert.Contains("absolute", error.Message);
  }

  [Fact]
  public void Auto_push_enables_auto_commit_and_applies_defaults()
  {
    var path = Path.Combine(Path.GetTempPath(), "notes");

    var options = NotesOptions.From(name => name switch
    {
      "GIT_NOTES_VAULT_PATH" => path,
      "GIT_NOTES_REMOTE_URL" => "https://example.com/notes.git",
      "GIT_NOTES_AUTO_PUSH" => "true",
      _ => null
    });

    Assert.True(options.AutoPush);
    Assert.True(options.AutoCommit);
    Assert.Equal(100, options.AutoCommitThreshold);
    Assert.Equal("main", options.Branch);
    Assert.Equal("origin", options.RemoteName);
    Assert.Equal("Automatic checkpoint.", options.AutoCommitMessage);
    Assert.False(options.ReadOnly);
    Assert.True(options.SyncOnStart);
    Assert.True(options.SyncBeforeRead);
    Assert.Equal(60, options.SyncMinSeconds);
  }

  [Fact]
  public void Invalid_threshold_is_rejected()
  {
    var path = Path.Combine(Path.GetTempPath(), "notes");

    var error = Assert.Throws<NotesConfigurationException>(() =>
      NotesOptions.From(name => name switch
      {
        "GIT_NOTES_VAULT_PATH" => path,
        "GIT_NOTES_REMOTE_URL" => "https://example.com/notes.git",
        "GIT_NOTES_AUTO_COMMIT_THRESHOLD" => "many",
        _ => null
      }));

    Assert.Contains("GIT_NOTES_AUTO_COMMIT_THRESHOLD", error.Message);
  }
}
