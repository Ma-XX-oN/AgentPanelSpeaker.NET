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
  -> SpeechService playback policy
  -> SpeechSapiXmlBuilder
  -> SapiSpeechEngine STA renderer
  -> native SAPI SpVoice or System.Speech rendered PCM
  -> PcmWaveData tone/silence/speech composition
  -> WaveOutPlayer single contiguous output stream
```

The monitor indexes all conversational roles and fenced-code blocks.  Current
settings decide whether and how a fragment is spoken only when playback reaches
it.  Live settings therefore affect new and replayed history without rescanning
the JSONL.

## Components

| Component | Responsibility |
| --- | --- |
| `SessionLocator` | Finds latest or explicitly selected Claude/Codex JSONL. |
| `JsonlTailReader` | Reads only complete newly appended JSONL lines. |
| `JsonlRecordExtractor` | Classifies user, assistant, reasoning, and Plans. |
| `TextCleaner` | Cleans prose and preserves typed fenced-code lines. |
| `SentenceSegmenter` | Splits cleaned prose into navigable sentences. |
| `JsonlSessionMonitor` | Builds history and emits deduplicated fragments. |
| `SpeechService` | Owns history, navigation, eligibility, and speech state. |
| `SpeechSapiXmlBuilder` | Builds equivalent SAPI XML and SSML markup. |
| `SapiSpeechEngine` | Renders providers and serializes complete buffers. |
| `PcmWaveData` | Parses, converts, joins, and generates PCM audio. |
| `WaveOutPlayer` | Plays one PCM buffer through one WinMM output stream. |
| `PronunciationRuleSet` | Parses exact and `/i` whole-token IPA rules. |
| `PronunciationDialog` | Edits spelling/IPA and hosts the IPA toolbar. |
| `IpaSymbolCatalog` | Describes grouped symbols and preview examples. |
| `SpelledWordSet` | Normalizes the one-entry-per-line spelling list. |
| `AudioWakeSettings` | Stores normalized Bluetooth wake policy. |
| `AudioWakeSettingsDialog` | Explains, edits, and tests wake settings. |
| `ThemeManager` | Resolves System/Light/Dark and applies the palette. |
| `UserSettingsStore` | Atomically loads/saves immutable settings snapshots. |
| `MainForm` | Presents session, speech, transport, and diagnostics controls. |
| `DiagnosticLog` | Writes structured execution diagnostics. |

`GlyphButton` normally uses WinForms text rendering for IPA controls.  Main
transport buttons enable ink-bound centring, which builds the glyph outline and
translates its actual painted bounds to the centre of the standard-height
button.  This avoids the vertical bias caused by centring the font's ascent and
descent box.

## JSONL classification

### Codex

Accepted `event_msg` payloads:

| Payload type | Category |
| --- | --- |
| `user_message` | User |
| `agent_message` | Assistant |
| `agent_reasoning` | Reasoning |
| `item_completed` with `item.type=Plan` | Assistant |

Other `item_completed` item types and all `response_item` records are
rejected.  This excludes command calls, command output, tool calls, patches,
diffs, and file-edit details.

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
entry.  Node navigation groups fragments carrying the same `NodeId`.

## Playback policy

`SpeechService` resolves current policy immediately before every fragment.  A
profile contains:

- voice name or `Not Spoken`;
- rate `-10..10`;
- pitch `-10..10`;
- volume `0..100` percent.

Voice discovery is the case-insensitive union of enabled `System.Speech`
voices and native `SAPI.SpVoice` tokens.  A duplicate name uses native SAPI,
preserving the absolute-middle pitch element:

```xml
<pitch absmiddle="5">text</pitch>
```

A voice available only through `System.Speech` uses a complete SSML document
with an equivalent relative-pitch value.  Each provider renders synchronously
to a WAVE stream on the STA worker.  The result is converted to mono 48 kHz
16-bit PCM before any wake or inter-segment audio is added.

The complete PCM sequence is submitted to one `waveOut` buffer.  Normal speech,
voice tests, IPA previews, wake-tone tests, and wake-plus-phrase tests share
that serialized path.  `SpeechService` retains active and paused state; pause,
resume, and cancellation operate on the active `waveOut` stream.

A fenced-code line is eligible only when:

1. its content category has a spoken voice; and
2. its normalized fence type is present in the active allow-list, or `*` is
   present.

Disabled fragments remain in history.  Rewind/forward skips fragments that are
currently disabled.

## Date, time, spelling, and pronunciation markup

`SpeechSapiXmlBuilder` transforms text at playback time, after history and
sentence boundaries have already been established.  `TextCleaner` retains an
invisible heading-boundary marker, which the markup builder converts to a
250 ms native SAPI silence element or equivalent SSML break.  The heading and
following prose therefore remain in one contiguous utterance.

Recognized date/time forms include:

- ISO dates such as `2026-07-15`;
- slash- or dash-separated numeric dates;
- ISO date-times such as `2026-07-15T14:30:00Z`;
- 12-hour and 24-hour clock values such as `2:30 PM` and `14:30`.

Dates are expanded to a long date and times to a 12-hour clock form before SAPI
receives them.  A trailing `Z` is spoken as UTC.

The spelling list is normalized by trimming each line, removing empty lines,
and de-duplicating case-insensitively.  Whole-token matching is
case-insensitive.  Native SAPI matches use `spell`; System.Speech matches use
the equivalent SSML `say-as` element:

```xml
<spell>IDE</spell>
```

Pronunciation rules have these normalized forms:

```text
git=ipa:ɡɪt
git/i=ipa:ɡɪt
```

The first form is case-sensitive.  `/i` ignores case.  Both are whole-token
matches.  On a tie, an exact-case rule wins.  Matching precedence is:

1. IPA pronunciation rule;
2. spell-out rule;
3. date/time expansion;
4. ordinary escaped text.

An IPA match is emitted as a phoneme element inside the active pitch wrapper.
All policy is read from the current settings for every utterance, so edits also
affect later replay without rebuilding history.

## IPA editor and toolbar

The pronunciation dialog contains separate spell-out and IPA-rule tabs.  The
IPA toolbar is manually toggled and never closes itself.  Symbol buttons are
grouped using IPA chart sections.  Each definition supplies:

- the insertion symbol;
- a description and Unicode code point tooltip;
- an independent IPA pronunciation when one can be constructed;
- an example word or explicit carrier-word frame;
- the example IPA;
- the phone position inside the example.

The editor saves its selection before a toolbar button takes focus.  The
Pronounce button reads only the caret's current line.  It previews explicit
`ipa:` content through the IPA path and otherwise speaks the line's token with
ordinary voice pronunciation, including an empty value such as `word=`.  It is
hosted only by the Pronunciations tab.  Insertion is permitted only when:

1. the current line contains `=`;
2. the selection starts strictly to its right;
3. the selection does not cross the line boundary;
4. when the value starts with `ipa:`, insertion starts after the colon.

If the value does not start with `ipa:`, clicking a symbol inserts that prefix
at the value start and adjusts the saved selection before inserting the symbol.

A one-second hover timer starts a preview.  Holding Shift when entering a
symbol, or pressing Shift while it remains hovered, starts immediately.  The
editor and toolbar are separated by a user-movable horizontal splitter.  The
footer displays both hover instructions when they fit, otherwise alternates
between them until an enabled symbol is hovered.  The footer is a read-only
`RichTextBox`.  During hover, it uses a fixed layout: the displayed symbol, an
isolated pronunciation when available, the example
word, the example IPA, and the phone position.  It applies bold formatting to
the displayed symbol and every occurrence in both IPA transcriptions.
Combining marks include their carrier phone in the bold range, and tie bars
include both tied phones.  Square brackets are never inserted as a visual
highlight.

The same independent pronunciation shown in the footer is played first,
followed by the example after the saved IPA delay.  A mark with no meaningful
independent sound plays only its example.  Because
an installed provider can expose a smaller phoneme inventory than the complete
IPA chart, preview segments carry an explicit recovery policy.  A rejected
isolated-phone segment is omitted.  A rejected example-IPA segment is rendered
again as the ordinary example word.  Only `FormatException` and `COMException`
select this recovery path; unrelated synthesis and playback failures remain
fatal and visible.  Hover preview is ignored while any ordinary or paused
speech is active.

## Bluetooth wake sequence

`AudioWakeSettings` stores:

- enabled state;
- quiet threshold in milliseconds;
- frequency in hertz;
- tone volume;
- tone duration;
- settle duration;
- IPA phone/example delay.

`SapiSpeechEngine` owns the last-audio-end timestamp and measures quiet time
with `Stopwatch`, so wall-clock changes cannot alter the threshold.  For one
playback request it:

1. renders every speech segment through its selected SAPI or System.Speech
   provider into a WAVE stream;
2. parses integer PCM and converts it to mono 48 kHz 16-bit samples;
3. inserts IPA inter-segment delay as PCM silence when required;
4. checks the wake threshold after rendering;
5. when wake applies, prepends a faded sine tone and the configured settling
   silence to the same sample array;
6. submits the entire tone → silence → speech array as one WinMM `waveOut`
   buffer.

The audio device remains open from the first tone sample through the final
speech sample.  No `SoundPlayer` or provider-device handoff occurs between the
prefix and sentence.  The first output after launch is treated as having an
indefinite quiet period.  Wake tests force the prefix.  Explicit IPA hover and
Pronounce previews also force it whenever wake is enabled, so short sounds get
the configured tone-plus-settle lead-in even when the ordinary quiet threshold
has not elapsed.  **Test wake + phrase** uses the content profile selected in
the dialog and the phrase `Yes, that makes sense.`

All provider rendering and playback ownership remain on the same STA worker.
The worker polls commands while `waveOut` is active, so pause, resume, cancel,
and dispose remain responsive.

## Theme

`AppTheme` has `System`, `Light`, and `Dark`.  `System` reads
`AppsUseLightTheme` from the current user's Windows personalization settings.
`ThemeManager` recursively applies window, input, control, and text colours and
requests a matching DWM title bar.

The main form subscribes to Windows preference changes.  When `System` is
selected, it reapplies the effective theme on the UI thread.  Modal dialogs use
the effective theme supplied when they are opened.

## Fenced-code allow-list

The edit box contains CSV values.  Parsing:

1. splits on commas;
2. trims leading/trailing whitespace;
3. removes empty entries;
4. compares case-insensitively;
5. removes duplicates while retaining first-occurrence order;
6. uses `untyped` for a fence without an info-string token;
7. uses `*` to enable every type.

Edits apply one second after the last keystroke.  Closing or explicitly saving
forces pending text to apply.

Activity reports one outcome per block encountered during playback:

```text
Spoken fenced block: type=cpp; non-empty lines=12.
Skipped fenced block: type=cpp; reason=type is not enabled.
```

## History and navigation

History owns all indexed fragments, including currently disabled categories and
fence types.  The cursor tracks the active, pending, or next fragment.

- sentence controls move one currently eligible fragment;
- node controls move to a node containing an eligible fragment;
- replay continues forward after navigation;
- forwarding beyond the last eligible sentence/code line or node cancels
  replay and moves the cursor to the live end;
- `Silence` cancels speech and returns to the live end;
- the play/stop toggle stops both speech and JSONL monitoring when active;
- pause/resume affects only the active utterance and does not stop monitoring.

Application-local hotkeys are processed by `MainForm.ProcessCmdKey`:

| Shortcut | Action |
| --- | --- |
| `H` / `Alt+H` | Previous node |
| `J` / `Alt+J` | Previous sentence/code line |
| `I` / `Alt+I` | Toggle pause/resume |
| `K` / `Alt+K` | Toggle start/stop |
| `L` / `Alt+L` | Next sentence/code line |
| `;` / `Alt+;` | Next node |
| `'` / `Alt+'` | Silence |

They operate only while the Agent Panel Speaker window has focus.  The invoked
button receives focus before `PerformClick` runs.  Bare keys work in main-form
text boxes, numeric controls, and voice dropdowns.  The fenced-code CSV box is
exempt so it can accept normal typing; Alt variants still work there.

## Settings

Settings are stored at:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\settings.json
```

Saved values include source, follow-newest state, pinned session path, every
role's voice/rate/pitch/volume, fenced-code CSV, spelling rules, IPA rules,
Bluetooth wake settings, theme, startup playback, polling interval, and normal
window bounds.

Settings version 4 adds `Pronunciations`, `AudioWake`, and `Theme`.  Creating or
upgrading a settings file preserves the spelling-list migration that adds `IDE`
when no prior list exists.  Normal controls save immediately.  The fenced-code
CSV uses a one-second debounce.  Writes use a temporary file followed by atomic
replacement.  Missing saved voices become `Not Spoken` and are logged.

## Invariants

- Tool calls, tool results, command output, patches, and diffs never enter
  history.
- Every history fragment retains role and JSONL-node identity.
- Playback settings, spelling, pronunciation, and wake policy are resolved at
  playback time.
- `Not Spoken` changes eligibility, not history retention.
- Fenced-code aliases are explicit; `cpp` does not imply `c++`.
- Only the SAPI worker thread accesses the late-bound COM voice.
- Every sound-producing path uses the serialized worker and PCM composer.
- A wake tone, settling silence, and its speech use one `waveOut` buffer.
- The play/stop toggle cancels speech before stopping monitoring.
- The IPA toolbar never inserts into a token or into its `ipa:` prefix.
- Settings writes never overwrite the live file partially.

## Diagnostics

Logs record session selection/switching, JSONL classification, accepted nodes,
duplicate suppression, emitted fragments, playback/navigation, fence outcomes,
settings saves, missing voices, wake-tone output/failures, SAPI failures, and
form/screen geometry.  Accepted conversation text is present in logs and should
be reviewed before sharing.

## IPA toolbar examples

IPA modifier definitions carry a named example word and a representative IPA
transcription.  Combining marks are displayed with a dotted-circle carrier in
the information line, but the raw mark is inserted into the pronunciation
editor.  Unsupported IPA markup still falls back to the example word's normal
voice pronunciation.
