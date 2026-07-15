# Agent Panel Speaker internal design

## Purpose

Agent Panel Speaker tails Claude and Codex conversation JSONL files and speaks
conversation text without reading tool calls, command output, patches, diffs,
or tool results.

## Data flow

```text
SessionLocator
  -> JsonlTailReader
  -> JsonlRecordExtractor
  -> TextCleaner
  -> SentenceSegmenter
  -> SpeechFragment history
  -> SpeechService policy check
  -> System.Speech SpeechSynthesizer
```

The monitor always indexes all conversational roles and all fenced-code blocks.
Current settings decide whether a fragment is eligible only when playback
reaches that fragment.  This permits live voice/fence changes and makes old
content available after rewind without rescanning the JSONL.

## Components

| Component | Responsibility |
| --- | --- |
| `SessionLocator` | Finds latest or explicitly selected Claude/Codex JSONL. |
| `JsonlTailReader` | Reads only complete newly appended JSONL lines. |
| `JsonlRecordExtractor` | Classifies user, assistant, and reasoning text. |
| `TextCleaner` | Cleans prose and preserves typed fenced-code lines. |
| `SentenceSegmenter` | Splits cleaned prose into navigable sentences. |
| `JsonlSessionMonitor` | Builds history and emits deduplicated fragments. |
| `SpeechService` | Owns playback history, navigation, policy, and synthesis. |
| `UserSettingsStore` | Atomically loads/saves immutable settings snapshots. |
| `MainForm` | Presents session, speech, transport, and diagnostics controls. |
| `DiagnosticLog` | Writes structured execution diagnostics. |

## JSONL classification

### Codex

Accepted `event_msg` payloads:

| Payload type | Category |
| --- | --- |
| `user_message` | User |
| `agent_message` | Assistant |
| `agent_reasoning` | Reasoning |

All `response_item` records are rejected.  This excludes command calls,
command output, tool calls, patches, diffs, and file-edit details.

### Claude

Accepted records:

| Record/block | Category |
| --- | --- |
| `user` / `text` | User |
| `assistant` / `text` | Assistant |
| `assistant` / `thinking` | Reasoning |

Rejected data includes `tool_use`, `tool_result`, sidechain,
`queue-operation`, image, and synthetic-assistant records.  System/IDE XML
context is stripped from user text.

## Speech fragments

Each `SpeechFragment` retains:

- JSONL node identity;
- content category;
- fragment kind (`Prose` or `FencedCodeLine`);
- text;
- normalized fence type;
- fenced-block identity, line index, and non-empty line count.

Prose is sentence-split.  Every non-empty fenced-code line is one navigation
entry.  Node navigation groups all fragments carrying the same `NodeId`.

## Playback policy

`SpeechService` asks `UserSettingsStore` for the current profile immediately
before each fragment starts.  A profile contains:

- voice name or `Not Spoken`;
- SAPI rate `-10..10`;
- pitch setting `-10..10`, mapped to relative SSML percentages;
- volume `0..100` percent;

Voice, rate, pitch, and volume edits therefore affect the next fragment
without restarting the active fragment.  Pitch is applied through SSML
prosody using five percentage points per UI step; rate and volume use
`SpeechSynthesizer` properties.  `SpeechService` emits speaking-state
transitions so every row test button is disabled during active playback.

A fenced-code line is eligible only when:

1. its content category has a spoken voice; and
2. its normalized fence type is present in the active allow-list, or `*` is
   present.

Disabled fragments remain in history.  Rewind/forward skips fragments that are
currently disabled.

## Fenced-code allow-list

The edit box contains CSV values.  Parsing:

1. splits on commas;
2. trims leading/trailing whitespace;
3. removes empty entries;
4. compares case-insensitively;
5. removes duplicates while retaining first-occurrence order;
6. uses `untyped` for a fence without an info-string token;
7. uses `*` to enable every type.

Edits are applied one second after the last keystroke.  No Enter or Apply action
is needed.  Closing or explicitly saving forces pending text to apply.

Activity reports one outcome per block encountered during playback:

```text
Spoken fenced block: type=cpp; non-empty lines=12.
Skipped fenced block: type=cpp; reason=type is not enabled.
```

## History and navigation

History owns all indexed fragments, including currently disabled categories and
fence types.  The cursor tracks the active, pending, or next fragment.

- sentence controls move one currently eligible fragment;
- node controls move to a node containing at least one currently eligible
  fragment;
- replay continues forward after navigation;
- forwarding beyond the last eligible sentence/code line or node cancels
  replay and moves the cursor to the live end;
- `Silence` cancels current/queued speech and returns to the live end;
- the play/stop toggle stops both speech and JSONL monitoring when active.

Application-local hotkeys are processed by `MainForm.ProcessCmdKey`:

| Shortcut | Action |
| --- | --- |
| `H` / `Alt+H` | Previous node |
| `J` / `Alt+J` | Previous sentence/code line |
| `K` / `Alt+K` | Toggle start/stop |
| `L` / `Alt+L` | Next sentence/code line |
| `;` / `Alt+;` | Next node |
| `'` / `Alt+'` | Silence |

They operate only while the Agent Panel Speaker window has focus.  Bare keys
are disabled while focus is in a text box, numeric field, or voice dropdown;
Alt variants remain active there.

## Settings

Settings are stored at:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\settings.json
```

Saved values include source, follow-newest state, pinned session path, every
role's voice/rate/pitch/volume, fenced-code CSV, startup playback
option, polling interval, and normal window bounds.

Normal controls save immediately.  The fenced-code CSV uses its one-second
debounce.  Window placement saves after resize and on shutdown.  Writes use a
temporary file followed by atomic replacement.  Missing saved voices become
`Not Spoken` and are logged.

## Invariants

- Tool calls, tool results, command output, patches, and diffs never enter
  history.
- Every history fragment retains role and JSONL-node identity.
- Playback settings are resolved when playback begins, not when indexed.
- `Not Spoken` changes eligibility, not history retention.
- Fenced-code aliases are explicit; `cpp` does not imply `c++`.
- The play/stop toggle cancels speech before stopping monitoring.
- Settings writes never overwrite the live file partially.

## Diagnostics

Logs record session selection/switching, JSONL classification, accepted nodes,
duplicate suppression, emitted fragments, playback/navigation, fence outcomes,
settings saves, missing voices, and form/screen geometry.  Accepted
conversation text is present in logs and should be reviewed before sharing.
