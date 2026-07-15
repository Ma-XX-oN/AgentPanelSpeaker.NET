# Agent Panel Speaker v19

Agent Panel Speaker reads Claude and Codex session JSONL files directly.  It
speaks assistant text and optional reasoning while excluding tool calls, command
output, diffs, patches, and tool results.

## What it speaks

### Codex

The reader accepts only these `event_msg` payloads:

- `agent_message` when **Speak assistant messages** is enabled.
- `agent_reasoning` when **Speak reasoning/thinking** is enabled.

It discards `response_item` records, including function calls, command details,
command output, patches, diffs, and file-edit data.

### Claude

The reader accepts only `assistant` records and these content blocks:

- `text` when **Speak assistant messages** is enabled.
- `thinking` when **Speak reasoning/thinking** is enabled.

It discards `tool_use`, user `tool_result`, sidechain, queue-operation, and
synthetic assistant records.

## Session selection

The reader uses the same storage conventions as `AI-transcript.py`:

- Claude: `%CLAUDE_CONFIG_DIR%\projects\**\*.jsonl`, or
  `%USERPROFILE%\.claude\projects\**\*.jsonl`.
- Codex: `%CODEX_HOME%\sessions\**\*.jsonl`, or
  `%USERPROFILE%\.codex\sessions\**\*.jsonl`.

**Detect latest** selects the newest matching session.

**Browse JSONL** starts in the selected source's session directory:

- **Claude** opens the Claude projects directory.
- **Codex** opens the Codex sessions directory.
- **Auto** opens the directory containing the newest detected session.

After selection, the window shows both:

- **Session** — the Codex `thread_name`, or the first real Claude user message.
- **Path** — the complete JSONL path in a separate full-width field.

Selecting a file manually disables **Follow newest session**.

## Playback controls

The transport controls are ordered from backward navigation to forward
navigation:

| Control | Action |
| --- | --- |
| `⏮` | Previous JSONL assistant node |
| `⏪` | Previous sentence |
| `▶` | Start monitoring and playback |
| `⏹` | Stop monitoring and speech immediately |
| `⏩` | Next sentence |
| `⏭` | Next JSONL assistant node |

Hover over a control to see its description.

**Silence** stops current and queued speech without stopping JSONL monitoring.

## Existing conversation history

When monitoring starts, the application indexes eligible assistant text already
present in the selected JSONL.  It does not speak that history by default, but
sentence and node rewind are available as soon as indexing completes.

Enable **Speak last existing assistant message on start** to begin playback at
the last existing assistant node instead of waiting at the live end.

New assistant records are appended to the same navigation history while the
reader is running.

## Multi-monitor behaviour

Version 19 uses Windows Forms `SystemAware` DPI mode.  The form is laid out once
using the primary monitor's DPI and is not rebuilt each time it crosses between
monitors with different scale settings.  This prevents the cumulative control
resizing seen with per-monitor rescaling.

Windows can bitmap-scale the whole window on a monitor whose scale differs from
the primary monitor, so that copy can look slightly softer.  The layout and
control proportions remain stable when moving away from and back to the primary
monitor.

## Voices

The current speech provider is `System.Speech`, so the voice list contains the
legacy SAPI voices installed for desktop applications.  Installing a Windows
Narrator natural voice does not guarantee that it appears in this list.

A substantially more natural programmable voice requires a separate provider,
such as Azure neural text to speech.  That provider requires network access,
Azure credentials, and usage-based billing, so it is not enabled by this
version.

## Text cleanup

The reader:

- removes Markdown link destinations while preserving their labels;
- removes raw URLs and image markup;
- removes Markdown decoration;
- optionally drops fenced code blocks;
- splits each accepted JSONL node into sentence-history entries;
- suppresses recent exact duplicates after whitespace normalization.

## Requirements

- Windows 11
- .NET 10 SDK to build from source
- an installed Windows SAPI voice

Visual Studio is not required.

## Build

From PowerShell or Command Prompt:

```text
.\build.cmd
```

## Run

```text
.\run.cmd
```

## Publish a standalone build

```text
.\publish-win-x64.cmd
```

The output is written under `publish\win-x64`.

## Diagnostic logging

Logs are written to:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\Logs
```

The log records:

- selected and switched session files and display titles;
- byte offsets and JSON record classifications;
- why each record was accepted or discarded;
- existing-history indexing and playback position;
- cleaned text accepted as a speech node;
- duplicate suppression;
- sentence emission, speech queueing, and navigation;
- high-DPI mode, monitor bounds, and form/control layout;
- stop and silence actions.

Accepted assistant text is included in the log.  Review it before sharing a log
from a sensitive session.
