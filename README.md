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
| `GIT_NOTES_SYNC_ON_START` | no | `true` | Fast-forward the working copy from the remote at startup. |
| `GIT_NOTES_SYNC_BEFORE_READ` | no | `true` | Fast-forward before `list_notes`, `read_note`, and `search_notes`. |
| `GIT_NOTES_SYNC_MIN_SECONDS` | no | `60` | Minimum seconds between pre-read fetches. `0` fetches before every read. |
| `GIT_NOTES_GIT_TIMEOUT_SECONDS` | no | `60` | Timeout for local git commands. Network commands use twice this value. |
| `GIT_NOTES_MAX_FILE_BYTES` | no | `1048576` | Maximum note size for reads and searches. |
| `GIT_NOTES_ZVEC_ENABLED` | no | `false` | Use `zg` for natural-language search. Requires zvec-grep on PATH. |
| `GIT_NOTES_LOG_LEVEL` | no | `Warning` | Minimum log level. Logs go to stderr. |

Startup creates the directory and repository when needed, writes `.gitignore` and `.gitattributes`, configures the remote, disables commit and tag signing in the vault, and makes an initial commit. It then fast-forwards the working copy from the remote unless `GIT_NOTES_SYNC_ON_START=false`.

Read tools fast-forward the working copy first so a second machine sees notes pushed from elsewhere. The fetch is rate limited to once per `GIT_NOTES_SYNC_MIN_SECONDS`, fast-forward only, and skipped when offline, when local commits have diverged, or in read-only mode. Sync never creates merge commits and never fails a read.

The vault must be the root of its own repository. A path inside an existing repository is refused instead of touching the parent repository, and the configured branch must already be checked out.

Push commits pending changes, merges the remote branch when behind, then pushes. Tag creation commits pending changes before tagging.

## Tools

| Tool | Access | Purpose |
| --- | --- | --- |
| `search_notes` | read | Text or regex search with line numbers, numbered context lines, and a hash for `edit_note`. Uses zvec-grep when configured. |
| `read_note` | read | Read a note, a heading section, or a line range. |
| `list_notes` | read | List notes with sizes and modified times. |
| `create_note` | write | Create a note, optionally replacing an existing one. |
| `edit_note` | write | Replace an exact string, with a stale-write hash check. |
| `append_note` | write | Append text and create the note if needed. |
| `move_note` | write | Move or rename a note. |
| `delete_note` | destructive | Delete a note. |
| `git_status` | read | Branch, last commit, dirty files, ahead and behind counts, and changed lines. |
| `commit_notes` | write | Commit all changes with a message. |
| `push_notes` | write | Commit pending changes, fetch, merge when behind, then push. |
| `create_tag` | write | Commit pending changes, then create an annotated tag. |

Write tools are not registered when `GIT_NOTES_READ_ONLY=true`.

## Semantic search

Set `GIT_NOTES_ZVEC_ENABLED=true` to search with [zvec-grep](https://github.com/zvec-ai/zvec-grep). The server finds `zg` on PATH, starts it as a stdio MCP child with `zg server --stdio` on the first natural-language search, and stops it when the server exits. Nothing leaves the machine, and no endpoint or token is involved.

The server builds the index itself. Before the first natural-language search it runs `zg index <vault>` once per process, so a fresh vault needs no setup. Indexing uses the default local embedding model, which zvec-grep downloads to `~/.zvec-grep/models` on first use, and zvec-grep refreshes an existing index in the background as you search.

Regex and case-sensitive searches always use the built-in keyword search. If `zg` is missing, indexing fails, or the search fails, the call falls back to keyword search and reports the reason in `fallbackReason`.

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

Both scripts pack into `NUGET_LOCAL_SOURCE` when set, otherwise `~/.nuget/local-packages`, and bump the patch version from the highest package already there.

```powershell
./publish-local.ps1
```

```bash
./publish-local.sh
```

The local folder is registered as the `bitbound-local` NuGet source.
