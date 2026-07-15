# Agent Panel Speaker v18

Agent Panel Speaker now reads the Claude and Codex session JSONL files directly.
It no longer uses Windows UI Automation or inspects the VS Code window tree.

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

## Session locations

The reader follows the same default storage conventions as `AI-transcript.py`:

- Claude: `%CLAUDE_CONFIG_DIR%\projects\**\*.jsonl`, or
  `%USERPROFILE%\.claude\projects\**\*.jsonl`.
- Codex: `%CODEX_HOME%\sessions\**\*.jsonl`, or
  `%USERPROFILE%\.codex\sessions\**\*.jsonl`.

Select **Auto**, **Codex**, or **Claude**, then choose **Detect latest**. You can
instead use **Browse JSONL** to pin the reader to one file.

With **Follow newest session** enabled, the reader checks every second for
a newer session file. Selecting a file manually disables this behaviour.

## Startup behaviour

By default, monitoring begins at the current end of the selected JSONL file.
Existing history is not spoken. Enable **Speak last existing assistant message
on start** to speak only the final currently stored eligible message.

A newly created session discovered while monitoring emits only eligible assistant
records timestamped after monitoring began, then continues from the file tail.

## Text cleanup

The reader:

- removes Markdown link destinations while preserving their labels;
- removes raw URLs and image markup;
- removes Markdown decoration;
- optionally drops fenced code blocks;
- splits each accepted JSONL node into sentence-history entries;
- suppresses recent exact duplicates after whitespace normalization.

Rewind/forward by sentence and by JSONL node remains available.

## Requirements

- Windows 11
- .NET 10 SDK to build from source
- An installed Windows speech voice

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

- selected and switched session files;
- byte offsets and JSON record classifications;
- why each record was accepted or discarded;
- cleaned text accepted as a speech node;
- duplicate suppression;
- sentence emission and speech queueing;
- stop/cancel and navigation actions.

Accepted assistant text is included in the log. Review it before sharing a log
from a sensitive session.
