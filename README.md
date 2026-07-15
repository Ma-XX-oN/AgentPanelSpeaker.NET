# Agent Panel Speaker v21

Agent Panel Speaker reads Claude and Codex conversation JSONL directly and
speaks user, assistant, and reasoning text with separate voices.  It excludes
tool calls, tool results, command output, patches, diffs, and status records.

See [DESIGN.md](DESIGN.md) for the internal architecture and invariants.

## Speech by content

Each content row has its own:

- voice (`Not Spoken` disables that category);
- rate (`-10..10`);
- pitch (`-10..10` relative semitones);
- volume slider (`0..100` percent);
- test button for that content voice.

Changes remain available while monitoring and apply to the next sentence or
code line.  The currently speaking fragment is not restarted.  Test buttons
are disabled while any text is being spoken, so a test cannot interrupt it.

## Fenced code

**Spoken fenced-code types** is a CSV allow-list.  Entries are trimmed,
case-insensitive, de-duplicated, and applied one second after editing stops.
No Enter key or Apply button is required.

- `untyped` enables fences without a language tag.
- `*` enables all fence types.
- An empty list skips all fenced blocks.
- Aliases are explicit: list both `cpp` and `c++` when both are wanted.

Every non-empty accepted code line is one rewind/forward entry.  Activity logs
both spoken and skipped blocks with the normalized fence type.

## Playback controls and local hotkeys

| Control | Hotkey | Action |
| --- | --- | --- |
| `⏮` | `Alt+G` | Previous JSONL node |
| `⏪` | `Alt+H` | Previous sentence/code line |
| `▶` | `Alt+J` | Start monitoring |
| `⏹` | `Alt+K` | Stop monitoring and speech |
| `⏩` | `Alt+L` | Next sentence/code line |
| `⏭` | `Alt+;` | Next JSONL node |
| Silence | `Alt+'` | Cancel speech; keep monitoring |

Hotkeys work only while Agent Panel Speaker is active.

## Session selection

- Claude: `%CLAUDE_CONFIG_DIR%\projects\**\*.jsonl`, or
  `%USERPROFILE%\.claude\projects\**\*.jsonl`.
- Codex: `%CODEX_HOME%\sessions\**\*.jsonl`, or
  `%USERPROFILE%\.codex\sessions\**\*.jsonl`.

**Detect latest** selects the newest matching session.  **Browse JSONL** opens
at the selected source's session directory.  The full session title and path
are displayed separately.

Existing conversation text is indexed at start, so rewind is immediately
available.  **Speak last existing enabled message on start** begins at the
last node that is currently eligible instead of waiting at the live end.

## Settings

Settings automatically persist at:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\settings.json
```

**Save settings** flushes pending fenced-code edits and saves immediately.
**Reset defaults** restores defaults.  Window position/size, session choices,
voice profiles, fence types, polling interval, and startup playback are
saved.

## Build and run

Requirements: Windows 11, .NET 10 SDK, and at least one installed SAPI voice.

```text
.\build.cmd
.\run.cmd
```

Publish a self-contained Windows build:

```text
.\publish-win-x64.cmd
```

The entire `publish\win-x64` directory must remain together.

## Diagnostics

Logs are written under:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\Logs
```

Conversation text is included in diagnostics.  Review logs before sharing.
