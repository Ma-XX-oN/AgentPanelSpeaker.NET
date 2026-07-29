# Agent Panel Speaker internal design

## v33 Main/Context speech styles

`ContentCategory.UserContext` identifies cleaned prose that came from an
explicit Markdown blockquote inside a genuine User record.  `TextCleaner`
retains `SpeechTextStyle.Context` on quote blocks after removing Markdown
prefixes.  `JsonlSessionMonitor` maps only quoted User parts to that
category.

The three shared voice rows expose Main and Context rate/pitch controls.  The
AI rows use Context for reasoning, while the User row uses Context for quoted
material.  Voice and volume remain shared within each row, so quote playback
changes tone without claiming a different speaker.

## v32 display-awake ownership

`MainForm.UpdateDisplayAwakeState()` is the single policy point for display
sleep prevention.  It enables `DisplayAwakeController` only when the checkbox
is selected, `SpeechService.IsSpeaking` is true, and
`SpeechService.IsPaused` is false.

`DisplayAwakeController` calls `SetThreadExecutionState()` on the UI thread
with `ES_CONTINUOUS | ES_DISPLAY_REQUIRED`.  It clears the request with
`ES_CONTINUOUS` when speech pauses or ends and during form disposal.  No
`ES_SYSTEM_REQUIRED` flag is used.

## v31 compiler correction

Version 31 keeps the version 30 voice-role and background-agent design and
corrects two C# flow-analysis failures in `SpeechService.cs`: nullable
dictionary lookup output and the processing-time endpoint's definite assignment.

## v30 voice roles and background agents

The speech matrix has three shared-voice roles: AI agent, AI subagent, and User.
All three roles now expose Main and Context rate/pitch controls.  AI Context
is reasoning; User Context is explicit Markdown blockquotes.  Typed prompts,
Codex selections, and Claude queued commands otherwise use User Main.

Claude `Agent` tool-use records create `SubagentAssistant` start announcements.
The matching top-level `tool_result` and completed `<task-notification>`
records create an `Assistant` completion announcement followed by one
`SubagentAssistant` result node.  A Not Spoken subagent voice therefore
suppresses the start and result while preserving the main-agent
completion/duration report.
Each result is appended before the next JSONL record is processed, preserving a
non-interleaved completion/result group.

`BackgroundWorkEvent` records stable IDs, descriptions, starts, and optional end
times.  `SpeechService` merges these events and includes active and completed
background work in processing-time announcements for the selected User turn.


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
  -> Windows.Media, native SAPI, or System.Speech rendered PCM
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
| `JsonlRecordExtractor` | Classifies conversation, questions, and answers. |
| `TextCleaner` | Cleans prose and preserves typed fenced-code lines. |
| `SentenceSegmenter` | Splits cleaned prose into navigable sentences. |
| `JsonlSessionMonitor` | Correlates input calls and builds speech history. |
| `SpeechService` | Owns history, navigation, eligibility, and speech state. |
| `SpeechSapiXmlBuilder` | Builds equivalent SAPI XML and SSML markup. |
| `SapiSpeechEngine` | Renders three providers and serializes buffers. |
| `InstalledSpeechVoice` | Stores provider identity and sortable metadata. |
| `GlyphButton` | Draws centred transport, speaker-turn, and clock icons. |
| `PcmWaveData` | Parses, converts, joins, and generates PCM audio. |
| `WaveOutPlayer` | Plays one PCM buffer through one WinMM output stream. |
| `PronunciationRuleSet` | Parses exact and `/i` whole-token IPA rules. |
| `PronunciationDialog` | Edits multi-line rules and hosts the IPA toolbar. |
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
button.  Custom controls use `GraphicsPath` for the two-bubble speaker arrows
and the processing-time clock.  The transport row uses an explicit taller
button height, and vector icons preserve extra vertical inset.

## JSONL classification

### Codex

Accepted `event_msg` payloads:

| Payload type | Category |
| --- | --- |
| `user_message` | User |
| `agent_message` (`commentary`/`analysis`/`reasoning`) | Reasoning |
| other `agent_message` phases, including `final_answer` | Assistant |
| `agent_reasoning` | Reasoning |
| `item_completed` with `item.type=Plan` | Assistant |

Other `item_completed` item types are rejected.  A `response_item`
`function_call` is accepted only when its name is `request_user_input`; each
question and its ordered options become Assistant speech.  The monitor retains
that call by `call_id`.  A later matching `function_call_output` contributes
only its structured selected answers, which become User-profile narration in
question order.  Secret answers are excluded.  Every unmatched or unrelated
`response_item` remains excluded, including command calls, command output,
tool calls, patches, diffs, and file-edit details.

### Claude

Accepted records:

| Record/block | Category |
| --- | --- |
| `user` / `text` | User |
| `attachment` / `queued_command` | User |
| `assistant` / `text` | Assistant |
| `assistant` / `thinking` | Reasoning |

Ordinary `tool_use`, `tool_result`, and `queue-operation` data remains
rejected.  Claude `Agent` tool calls, matching completed Agent results, and
completed `<task-notification>` records are the background-agent exceptions.
Sidechain, image, and synthetic-assistant records remain excluded.  System/IDE
XML context is stripped from user text.

## Speech fragments

Each `SpeechFragment` retains:

- JSONL node identity;
- content category;
- whether it starts an actual User turn;
- fragment kind (`Prose` or `FencedCodeLine`);
- text;
- normalized fence type;
- fenced-block identity, line index, and non-empty line count;
- the source-node timestamp normalized to UTC when available.

Prose is sentence-split.  Every non-empty line in a non-`md` fenced block is
one navigation entry, and punctuation inside that line is ignored.  An
explicit `md` fence is parsed recursively with normal Markdown block and
sentence segmentation while retaining fenced-block playback policy.  Node
navigation groups fragments carrying the same `NodeId`.  A selected Codex input
answer carries the User category so it uses the **User messages** voice, but its
`StartsUserTurn` flag remains false.  Processing-time and latest-turn logic use
that flag rather than the voice category, so an answer selection cannot create
a false turn boundary.

Inline Markdown code is protected before link, HTML, and decoration cleanup,
then restored as ordinary Prose.  It is therefore spoken under the containing
content profile and is never controlled by the fenced-code allow-list.  HTML
cleanup removes balanced common elements, comments, and void elements rather
than treating every angle-bracket range as a tag; comparisons such as `a < b`
and C++ template/cast text remain intact.

## Playback policy

`SpeechService` resolves current policy immediately before every fragment.  A
profile contains:

- voice name or `Not Spoken`;
- rate `-10..10`;
- pitch `-10..10`;
- volume `0..100` percent.

The UI formats each installed voice as structured location, language, name,
Natural quality, and maker fields.  The current field order is also the sort
order.  Clicking the Voice heading rotates that order without changing the
stable provider name stored in the profile.  Voice, rate, pitch, and volume
edits are debounced for 350 ms and then spoken as an untracked preview unless
monitored playback is active.

Voice discovery merges three catalogues:

1. `Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices`;
2. native `SAPI.SpVoice` tokens;
3. enabled `System.Speech` voices.

`InstalledSpeechVoice` retains a stable settings name, the provider-specific
voice identifier, provider kind, maker, voice name, Natural/Natural HD quality,
language, and location.  Provider-independent catalogue keys merge duplicate
entries.  The chosen backend priority is Windows.Media, then SAPI, then
System.Speech, while missing display fields are filled from matching lower
priority entries.  The existing stable name is retained when a higher-priority
backend replaces a duplicate, so saved selections continue to resolve.

Native SAPI preserves the absolute-middle pitch element:

```xml
<pitch absmiddle="5">text</pitch>
```

System.Speech and Windows.Media receive a complete SSML document with an
equivalent relative-pitch value.  Windows.Media additionally receives its
provider voice ID, speaking-rate ratio, and normalized volume through
`SpeechSynthesizerOptions`.  Each provider renders synchronously to a WAVE
stream on the STA worker.  The result is converted to mono 48 kHz 16-bit PCM
before any wake or inter-segment audio is added.  Failure to initialize the
modern provider is logged and leaves the two legacy providers available.

The complete PCM sequence is submitted to one `waveOut` buffer.  Normal speech,
automatic voice-setting previews, IPA previews, wake-tone tests, and
wake-plus-phrase tests share that serialized path.  `SpeechService` retains
active and paused state; pause, resume, and cancellation operate on the active
`waveOut` stream.

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
case-insensitive.  Native SAPI matches use `spell`; the System.Speech and
Windows.Media paths use the equivalent SSML `say-as` element:

```xml
<spell>IDE</spell>
```

Pronunciation rules have these normalized forms:

```text
TODO=to do
TODO/i=to do
git=ipa:ɡɪt
git/i=ipa:ɡɪt
```

A value without `ipa:` is escaped and emitted as replacement speech text.  A
value with `ipa:` is emitted as a phoneme element.  The plain form is
case-sensitive; `/i` ignores case.  Every form is a whole-token match.  On a
tie, an exact-case rule wins.  Matching precedence is:

1. spoken-text or IPA pronunciation rule;
2. spell-out rule;
3. date/time expansion;
4. ordinary escaped text.

All policy is read from the current settings for every utterance, so edits also
affect later replay without rebuilding history.  Replacement text is emitted
literally and is not recursively matched against other pronunciation rules.

## IPA editor and toolbar

The pronunciation dialog contains separate spell-out and rule tabs.  Both
editors intercept Enter and Shift+Enter as line insertion, preventing the
form's default OK button from limiting either editor to one entry.  The IPA
toolbar is manually toggled and never closes itself.  Symbol buttons are
grouped using IPA chart sections.  Each definition supplies:

- the insertion symbol;
- a description and Unicode code point tooltip;
- an independent IPA pronunciation when one can be constructed;
- an example word or explicit carrier-word frame;
- the example IPA;
- the phone position inside the example.

The editor saves its selection before a toolbar button takes focus.  The
Pronounce button reads only the caret's current line.  It previews explicit
`ipa:` content through the IPA path and speaks a non-IPA value as replacement
text.  An incomplete empty value previews the token's ordinary pronunciation.
The button is hosted only by the Pronunciations tab.  Insertion is permitted
only when:

1. the current line contains `=`;
2. the selection starts strictly to its right;
3. the selection does not cross the line boundary;
4. when the value starts with `ipa:`, insertion starts after the colon.

If the value does not start with `ipa:`, clicking a symbol inserts that prefix
at the value start and adjusts the saved selection before inserting the symbol.

Pointer hover and keyboard focus share one IPA-key interaction path.  Either
shows the symbol information and starts a one-second delayed preview.  Shift
shows the information and previews immediately.  A separate seven-second timer
returns the footer to rotating helper text without removing the active key used
by Shift.  The editor and toolbar are separated by a user-movable splitter.  On
first expansion the toolbar receives about 86% of the split area; later manual
heights are retained.  The caret line is moved to the top of the remaining
editor, and toolbar scroll position is restored after focus-changing actions.

The footer is a read-only `RichTextBox`.  It uses a fixed layout containing the
displayed symbol, an isolated pronunciation when available, a real word or an
explicitly labelled carrier, the example IPA, and the position.  It applies
semibold formatting to the displayed symbol and every matching phone cluster in
both IPA transcriptions.  Combining marks include their carrier phone, tie bars
include both tied phones, and square brackets are never used as highlights.

The same independent pronunciation shown in the footer is played first,
followed by the example after the saved IPA delay.  A mark with no meaningful
independent sound plays only its example.  Real-word examples retain ordinary
text fallback.  Carrier examples deliberately have no text fallback, so a
provider rejection skips the carrier instead of speaking an unrelated fake
word.  Only `FormatException` and `COMException` select this recovery path;
unrelated synthesis and playback failures remain fatal and visible.  The rule
editor remains available while ordinary or paused speech is active, but its
Pronounce action and pending IPA hover audio are disabled until speech is idle.
IPA symbol insertion remains available because it does not require audio.

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

1. renders every speech segment through its selected Windows.Media, SAPI,
   or System.Speech provider into a WAVE stream;
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
- speaker controls move between opposite-speaker runs;
- consecutive User fragments form one User run;
- consecutive Assistant and Reasoning fragments form one AI run, even when
  they span several JSONL nodes;
- from AI, speaker navigation targets the adjacent User run; from User, it
  targets the first eligible fragment of the adjacent AI run;
- replay continues forward after navigation;
- forwarding beyond the last eligible sentence/code line, node, or speaker
  run cancels replay and moves the cursor to the live end;
- the clock control queues one User-voice processing-time announcement after
  the current JSONL node finishes speaking;
- the play/stop toggle stops both speech and JSONL monitoring when active;
- pause/resume affects only the active utterance and does not stop monitoring.

Application-local hotkeys are processed by `MainForm.ProcessCmdKey`:

| Shortcut | Action |
| --- | --- |
| `U` / `Alt+U` | Previous opposite-speaker run |
| `H` / `Alt+H` | Previous node |
| `J` / `Alt+J` | Previous sentence/code line |
| `I` / `Alt+I` | Toggle pause/resume |
| `K` / `Alt+K` | Toggle start/stop |
| `L` / `Alt+L` | Next sentence/code line |
| `;` / `Alt+;` | Next node |
| `O` / `Alt+O` | Next opposite-speaker run |
| `'` / `Alt+'` | Speak AI processing time |

They operate only while the Agent Panel Speaker window has focus.  The invoked
button receives focus before `PerformClick` runs.  Bare keys are ignored while
an editable text, numeric, or drop-down control has focus; Alt variants remain
available there.

## Processing-time announcements

Every `SpeechFragment` retains its source-node timestamp.  The clock action
finds the User node that owns the playback cursor's response.  A Codex
`task_complete` timestamp is the preferred endpoint.  Otherwise, the response
is complete only when its final retained AI message is user-facing Assistant
text rather than Reasoning/thinking.  A latest response with no completed
endpoint is measured to the request time and uses present-progressive wording;
completed responses use past-tense wording.

`SpeechService` retains a pending announcement and the active node identifier.
Normal playback continues through the remaining fragments of that node.  At
the next node boundary, the announcement is inserted ahead of the next queued
node and spoken with the User profile.  The clock control remains disabled from
request through completion.

## Session identity and startup position

A path displayed after Browse or Detect latest is passed as `ExplicitPath` when
Auto-follow is off.  Stop and Play therefore reopen the same JSONL.  Only an
explicit Auto-follow session may periodically call `FindLatest` and switch to a
newer file.  `SessionChanged` advances a generation counter and clears active,
pending, and indexed speech before history or live events from the new file are
accepted, preventing queued output from the previous conversation.

Existing history is always indexed.  `PlaybackStartMode.LatestTurn` scans
backward for the final User node, starts at its first currently eligible
fragment, and leaves all following Reasoning/Assistant nodes in sequence.  If
there is no User node, it falls back to the first eligible fragment in the final
eligible node.  An automatic file switch uses `LiveEnd`, so it never replays the
new file's old conversation.

## Settings

Settings are stored at:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\settings.json
```

Saved values include source, follow-newest state, pinned session path, every
role's voice/rate/pitch/volume, fenced-code CSV, spelling rules, IPA rules,
Bluetooth wake settings, theme, startup playback, polling interval, and normal
window bounds.

Settings version 4 added `Pronunciations`, `AudioWake`, and `Theme`.
Settings version 5 changes follow-newest migration so existing installations
start pinned rather than inheriting automatic session switching.  Creating or
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
- Only the speech worker thread accesses SAPI COM, System.Speech, or WinRT
  synthesizer instances.
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

## Structural speech boundaries

Markdown block structure and sentence navigation share one segmentation model.
Headings, paragraphs, list items, quote blocks, and table rows end a navigation
unit and receive one 250 ms pause.  Non-`md` fenced blocks instead treat every
non-empty source line as exactly one unit and ignore punctuation within it.
Explicit `md` fences recurse through the normal Markdown rules, including
nested fences.  Fence delimiters may contain one or more matching backticks or
tildes.  A same-line closing marker identifies inline code and prevents that
line from opening a block fence, so following prose is never swallowed by a
false fence.  When a block already ends in sentence punctuation, the punctuation
and block ending are one coincident boundary and never create an empty
fragment.
