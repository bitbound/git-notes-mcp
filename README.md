# Bitbound.GitNotesMcp

An MCP server for creating, reading, editing, searching, and versioning markdown notes in a git repository. Notes stay as plain `.md` files on disk.

## Configuration

The server reads these environment variables at startup.

| Variable | Required | Default | Purpose |
| --- | --- | --- | --- |
| `GIT_NOTES_VAULT_PATH` | yes | | Absolute path to the notes repository. Created when missing. |
| `GIT_NOTES_REMOTE_URL` | yes | | Git remote used for fetch and push. |
| `GIT_NOTES_BRANCH` | no | `main` | Branch to initialize and push. |
| `GIT_NOTES_REMOTE_NAME` | no | `origin` | Remote name. |
| `GIT_NOTES_USER_NAME` | no | global git config | Local `user.name`. |
| `GIT_NOTES_USER_EMAIL` | no | global git config | Local `user.email`. |
| `GIT_NOTES_READ_ONLY` | no | `false` | Register read tools only. |
| `GIT_NOTES_AUTO_COMMIT` | no | `false` | Commit after a mutation once the threshold is reached. |
| `GIT_NOTES_AUTO_COMMIT_MESSAGE` | no | `Automatic checkpoint.` | Message for automatic commits. |
| `GIT_NOTES_AUTO_COMMIT_THRESHOLD` | no | `100` | Changed lines required before an automatic commit. `0` commits after every mutation. |
| `GIT_NOTES_AUTO_PUSH` | no | `false` | Push after automatic commits. Implies auto-commit. |
| `GIT_NOTES_ALLOW_REMOTE_MISMATCH` | no | `false` | Replace an existing remote that points elsewhere. |
| `GIT_NOTES_GIT_TIMEOUT_SECONDS` | no | `60` | Timeout for local git commands. Network commands use twice this value. |
| `GIT_NOTES_MAX_FILE_BYTES` | no | `1048576` | Maximum note size for reads and searches. |
| `GIT_NOTES_LOG_LEVEL` | no | `Warning` | Minimum log level. Logs go to stderr. |

Startup creates the directory and repository when needed, writes `.gitignore` and `.gitattributes`, configures the remote, and makes an initial commit. It never fetches at startup.

The vault must be the root of its own repository. A path inside an existing repository is refused instead of touching the parent repository, and the configured branch must already be checked out.

## Tools

| Tool | Access | Purpose |
| --- | --- | --- |
| `search_notes` | read | Text or regex search with line numbers and context. |
| `read_note` | read | Read a note, a heading section, or a line range. |
| `list_notes` | read | List notes with sizes and modified times. |
| `create_note` | write | Create a note, optionally replacing an existing one. |
| `edit_note` | write | Replace an exact string, with a stale-write hash check. |
| `append_note` | write | Append text and create the note if needed. |
| `move_note` | write | Move or rename a note. |
| `delete_note` | destructive | Delete a note. |
| `git_status` | read | Branch, last commit, dirty files, ahead and behind counts, and changed lines. |
| `commit_notes` | write | Commit all changes with a message. |
| `push_notes` | write | Fetch, rebase when clean and behind, then push. |
| `create_tag` | write | Create an annotated tag, optionally pushing it. |

Write tools are not registered when `GIT_NOTES_READ_ONLY=true`.

## Build and test

```dotnetcli
dotnet build Bitbound.GitNotesMcp.slnx
dotnet run --project Tests/Bitbound.GitNotesMcp.Tests/Bitbound.GitNotesMcp.Tests.csproj
```

## Run

```dotnetcli
dnx Bitbound.GitNotesMcp
```

MCP client entry:

```json
{
  "mcpServers": {
    "git-notes": {
      "command": "dnx",
      "args": ["Bitbound.GitNotesMcp"],
      "env": {
        "GIT_NOTES_VAULT_PATH": "/absolute/path/to/notes",
        "GIT_NOTES_REMOTE_URL": "git@github.com:you/notes.git"
      }
    }
  }
}
```

## Local publish

```powershell
./publish-local.ps1
```
