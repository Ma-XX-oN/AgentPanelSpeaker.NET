# Agent Panel Speaker internal design

## v68 decimal-aware bookmark tokens

`SpeechTokenization` recognizes decimal values before general period and word
tokens.  This prevents token-level SSML marks from splitting the text that a
Windows.Media voice must see contiguously to pronounce a decimal as “point”.
The optional `f`, `F`, `l`, or `L` suffix is deliberately excluded from the
decimal token and becomes the next display token.

`SapiSpeechEngine.GetBookmarkedSynthesisText()` decouples display identity from
synthesis text.  A leading-decimal token such as `.5` remains one display range
but is rendered as `point 5`; an attached identifier period remains `.` in the
display model but is rendered as `dot`.  Bookmark count and token indexes
therefore continue to correspond to display tokens.



## System.Speech timing format

Desktop voices rendered through `System.Speech` use an explicit 16,000 Hz,
16-bit, mono PCM WAVE format.  `SpeakProgress.AudioPosition` must be measured
on the same sample-rate timeline as the generated WAVE data; relying on the
default 22.05 kHz WAVE format can produce proportional transcript-highlight
lag for voices whose event timeline is 16 kHz.

## v51 bounded playback mailbox

`SpeechService.PlaybackPositionChanged` publishes into
`TranscriptPlaybackMailbox` instead of calling `MainForm.PostToUi()` for every
word boundary.  The mailbox is a lock-protected circular buffer whose capacity
is `TranscriptSettings.HighlightQueueCapacity` (1--16, default 1).

`TranscriptPlaybackMailbox.Publish()` returns true only when it changes from no
pending UI wake-up to one pending wake-up.  `MainForm` then posts exactly one
`BeginInvoke`.  The UI callback captures the retained batch count, applies at
most that many positions, and posts one further callback only when a producer
published during the current drain.  A full mailbox discards its oldest value,
so capacity 1 is a latest-value mailbox rather than an event queue.

This change is isolated to the speech-boundary-to-UI handoff.  WebView messages,
control commands, visibility policy, fragment identity, diagnostics, and the
shared-buffer proposal are intentionally unchanged for v51.

## v50 low-latency marker channel and bounded colour updates

`TranscriptView` sends `playback` and `settings` messages with
`CoreWebView2.PostWebMessageAsJson()`.  Playback messages are one-way and carry a
monotonic sequence number; the page ignores stale values.  Settings live in one
C# mailbox and are sampled at 100 ms, so pointer motion cannot append an
unbounded series of WebView calls.

`TranscriptSettingsPopup` updates its native swatches immediately but publishes
colour changes at most once per 75 ms.  `MainForm` also updates the speech
worker's boundary-poll interval only when the tracking setting itself changes.

The page fades retired words with the Web Animations API.  Each word owns at
most one fade animation, and applying an active or paused marker cancels that
animation first.  `clearMarkers()` touches only the current range instead of
walking the complete transcript on every word boundary.  These changes avoid
both timer backlogs and an O(total rendered words) operation per spoken token.

`ParentAutoCloseRequested` implements the nested-overlay grace-period contract
directly.  Expiry or a revisit-and-leave closes the parent even when the pointer
is still over another part of the parent overlay.

## v49 render reuse and coalesced WebView updates

`TranscriptView.SelectSession()` treats a matching source and path as an
idempotent selection.  It checks for an actual file change but preserves the
existing DOM and identity map when `JsonlSessionMonitor` announces the session
that was already selected before monitoring started.

`TranscriptNodeIdentityMap.BuildSegments()` mirrors
`JsonlSessionMonitor.ProcessNode()`: prose passes through
`SentenceSegmenter.Split()`, while code lines remain individual segments.
Rendered segment keys therefore match `SpeechFragment.Text` exactly.

Transcript settings and playback markers each use a one-slot asynchronous
mailbox.  While `CoreWebView2.ExecuteScriptAsync()` is in flight, later changes
replace the pending value rather than appending more JavaScript work.  Seek,
pause, and colour changes consequently converge on the latest state.

## v47 cancellable rendering and maximized transport dispatch

`TranscriptView` assigns every render generation its own
`CancellationTokenSource`.  `SelectSession()`, `ClearSession()`, and disposal
cancel the previous generation; an obsolete task cannot hold the next selected
file behind `_refreshInProgress` or enqueue another refresh during shutdown.
`TranscriptMarkdownFormatter.Format()` and `TranscriptNodeIdentityMap.Build()`
check that token between JSONL records.

The WebView builds `displayWordsByRecord` and `lexicalWordsByRecord` once after
`wrapWords()`.  `assignNodeScopes()` searches those record-local arrays and
stores the resulting ranges in `segmentRangesByNode`.  `findFragmentRange()`
therefore performs an exact node-local segment lookup during speech instead of
repeating whole-document scans.  `transcript.render_completed` records
preparation, DOM, and total milliseconds.

`MainForm.ActivateTransportShortcut()` no longer calls
`Button.PerformClick()`.  `SetDiagnosticsMaximized()` hides the transport row,
and a hidden WinForms button is not selectable, so `PerformClick()` does not
raise its click handler.  The dispatcher now calls the corresponding navigation,
play/pause, or processing-time command directly after applying the same enabled-
state and focus rules.  `PreFilterMessage()`, `ProcessCmdKey()`, and the WebView
JavaScript bridge remain the three keyboard-input paths.

`UserSettings.CreateDefault()` enables `FollowNewestSession`.  Normalization also
enables it whenever no `ManualSessionPath` exists.  `FollowLatestChanged()`
releases or establishes `_pathIsManual`, and the checkbox remains available
while configuration is unlocked.

`TranscriptColourPopup` synchronizes a compact Cyotek `ColorWheel` with
`ColorEditor`, retaining alpha and numeric colour entry while preserving the
nested-overlay focus and dismissal contract.

## v46 WebView keyboard bridge correction

`TranscriptView` does not subscribe to a native accelerator event.  The
Microsoft.Web.WebView2 1.0.4078.44 WinForms wrapper used by the project exposes
neither `CoreWebView2Controller` nor `AcceleratorKeyPressed`.  The rendered page
handles transport keys during DOM capture, suppresses their browser action, and
posts a `transport` message through `chrome.webview`.  `WebMessageReceived` maps
that message to `TransportKeyPressed`; `MainForm` remains the single owner of the
transport command.

## v45 symbol-aware playback

The rendered token index now contains lexical words and individual visible
symbols.  `TranscriptNodeIdentityMap` segments are matched against the complete
token stream first; lexical-only matching remains the fallback for Markdown
constructs whose punctuation is not present in the rendered DOM.  Each matched
segment assigns its node ID to the entire displayed range, including operators
between words.

Playback resolves `SpeechWordBoundary.Text` inside the mapped fragment range
instead of treating the boundary ordinal as a direct DOM-word offset.  A single
speech boundary can activate a range of symbol spans, while unspoken punctuation
does not advance the marker.  `SpeechTokenization` supplies the matching C# rule
for approximate boundaries, initial markers, and restart positions.

## v44 nested colour editor, asynchronous rendering, and pause unlock

`TranscriptSettingsPopup` owns a compact `TranscriptColourPopup` child overlay.
The child is attached to the same `MainForm` rather than creating a top-level
window.  Boundary Tab traversal and Escape hide only the child and return focus
to the current-colour swatch.  Pointer dismissal arms a three-second parent
close suppression interval; revisiting and leaving the swatch ends that interval
early.  `TranscriptColourPopup.ApplyTheme()` supplies explicit dark and light
surfaces for the Cyotek `ColorWheel` and its swatches.

`MainForm.UpdateControlState()` treats paused monitored playback as a
reconfiguration state rather than an active-playback lock.  Source-changing
commands call `StopPausedMonitoringForReconfiguration()` before replacing the
session, preserving one monitor and one speech-history owner.

`TranscriptView` performs `TranscriptMarkdownFormatter.Format()` and
`TranscriptNodeIdentityMap.Build()` in one serialized background render task.
The UI displays a themed loading surface until `replaceTranscript()` completes.
Generation checks discard a render belonging to a previously selected file,
and one pending refresh is coalesced while a render is active.

`TextCleaner` recursively parses the contents of Markdown blockquotes after
removing one quote prefix.  Every nested heading, paragraph, list item, and
fenced-code line therefore produces its own `SpeechTextPart`; a source block
boundary is a navigation break without requiring terminal punctuation.

`MainForm.PreFilterMessage()` associates key messages with the form through
`GetAncestor(..., GA_ROOT)` instead of `Form.ActiveForm`.  This preserves bare
transport shortcuts when a native WebView2 child owns focus in maximized mode.
The controller accelerator and in-page JavaScript paths remain fallbacks.

`DiagnosticLog.OpenCurrentLogInExplorer()` runs an STA Shell Automation worker.
It selects the current JSONL log in an Explorer window already at the Logs
folder, otherwise navigates the first reusable Explorer window there and waits
for navigation before selecting the file.  A new Explorer window is the final
fallback.

## v43 queued-command rendering and record-scoped identity

`TranscriptMarkdownFormatter` now renders Claude `attachment` records whose
attachment type is `queued_command`.  This removes the previous split in which
`JsonlRecordExtractor` created speech history for the record while the rendered
Transcript omitted it.

Every rendered record begins with a hidden `record-anchor` carrying the JSONL
record number and source UUID.  `TranscriptNodeIdentityMap` carries the same
record number with each accepted node.  JavaScript first limits segment matching
to words owned by that source ID and record number, then assigns the monitor
node ID.  Text matching remains inside the record because Markdown cleanup can
alter punctuation and markup.  Mapping and playback failures are reported
once per unique failure.

`JsonlRecordExtractor` splits a queued-command attachment into a generated
User-context node and an actual User node.  Both retain the same source record
identity and both remain navigable.  `JsonlSessionMonitor` places
`StartsUserTurn` only on the first fragment of the actual User node rather than
on every fragment.  `SpeechService.FindLatestTurnStartLocked` starts at that
fragment directly instead of rewinding to the node boundary.

`MainForm` implements `IMessageFilter` so transport keys are captured from
focused child HWNDs, including WebView2 in maximized Transcript mode.  The
existing editable-control exclusion is applied before dispatch.

`TranscriptSettings.FadeMilliseconds` is normalized to 1/64-second increments
from 0--500 ms.  `tools/ColourPickerComparison` is a standalone picker test
harness and does not select a replacement for the production picker.

## v42 transcript identity, colour, and timing corrections

`TranscriptNodeIdentityMap` reconstructs the same accepted-node numbering and
recent-fingerprint suppression used by `JsonlSessionMonitor`.  Each node's
ordered cleaned speech segments are passed with the rendered HTML.  JavaScript
assigns those segments to word spans in document order and first resolves a
playback fragment inside spans carrying the exact node ID.  Text-only nearest
matching remains a fallback for source records that are intentionally not part
of the rendered transcript.

Live markers use `CoreWebView2.PostWebMessageAsJson`, avoiding a backlog of
`ExecuteScriptAsync` calls.  `TranscriptSettings.HighlightUpdateMilliseconds`
is persisted in settings version 11 and controls the `SapiSpeechEngine` polling
interval from 5--40 ms.  Intervals at or below 10 ms also request
`ThreadPriority.AboveNormal`; failure to change priority is non-fatal and
diagnostic-only.

`TranscriptSettingsPopup` uses the MIT-licensed Cyotek `ColorWheel` and
`ColorEditor` controls with previous/current swatches.  All changes are live.
Popup background clicks and anchor clicks move focus to the first enabled
control.  `GlyphButton` draws transport and transcript-toolbar symbols with
GDI+ vector paths so their size does not depend on Unicode font metrics.

## v41 transcript integration corrections

Version 41 removes the unused WebView2 WPF assembly reference, uses an in-app
themed RGB colour editor, fixes popup hover dismissal and fragment word mapping,
and reduces transcript/transport glyph sizes.  Fade is 0--500 ms in 1/16-second
increments.

## v40 rendered transcript and synchronized speech markers

`MainForm` places **Transcript** before Activity and Accepted Text in the bottom
`TabControl` and selects it initially.  `TranscriptView` owns one WebView2
control and a 250 ms file-refresh timer.  No selected session produces an empty
page.  A WebView2 initialization failure is isolated to that view and is
reported without changing monitor or speech ownership.

`TranscriptMarkdownFormatter` reads the selected Claude or Codex JSONL and
produces Markdown independently of the cleaned speech history.  Markdig converts
that Markdown to HTML.  The fixed WebView2 shell applies a content-security
policy, restrained light/dark CSS, word spans, `<details>` state restoration,
and scroll preservation.  Claude `Agent` tool calls and completed child-agent
queue notifications become top-level `## Claude Sub-agent <id>` sections with
descriptions and matching results.  The opaque ID remains visible but is not
part of any `SpeechFragment`.

The transcript toolbar is overlaid on the right of the tab strip.  The gear
opens `TranscriptSettingsPopup`, another child overlay rather than a top-level
window.  Follow state, separate light/dark ARGB highlight colours, and a
0--500 ms fade rounded to 1/16 second are persisted in settings version 10.  The
maximize button collapses rows 0--6 of `MainForm._mainLayout`; the tab strip,
gear, and restore button remain visible.

`SapiSpeechEngine` now associates each rendered PCM buffer with ordered
`SpeechWordBoundary` records.  System.Speech collects exact source-range `SpeakProgress` audio positions.
Windows.Media requests and consumes exact `SpeechWord` timed metadata.  Native
SAPI constructs duration-weighted word estimates.  `WaveOutPlayer.Position` queries `waveOutGetPosition`, and the
engine raises each boundary as the device cursor reaches it.

`SpeechService` maps those boundaries to the active history fragment and raises
`TranscriptPlaybackPosition`.  Speaking uses a filled background marker;
completed words fade in JavaScript.  Pause uses a blinking hollow outline.
When a pause exceeds one second, `SpeechService` cancels the remainder and
resynthesizes from the current word.  Pausing at the live end uses a separate
one-em marker after the document.  Navigation starts the selected fragment and
therefore moves the transcript marker through the same event path.

`I` and `Alt+I` are removed from the form, compact profile editor, transcript
settings overlay, WebView2 keyboard bridge, and current documentation.  `K` and
`Alt+K` remain the sole Play/Pause shortcuts.

The bundled `tools/AI-transcript.py` is the reference formatter updated to
render parent and child Claude sub-agents.  Its fixture verifies opaque IDs,
visible results, and removal of `agentId`, worktree, and usage metadata.

## v39 unified playback and diagnostic tabs

`MainForm` owns one Play/Pause button.  When monitoring is stopped, activating
it starts the selected JSONL session.  While monitoring, it calls
`SpeechService.TogglePause(allowIdlePause: true)`.  `SpeechService` therefore
permits a paused state even when no utterance is active at the current live
end.  `StartPendingOrNextLocked()` does no work while paused; live fragments
remain in history and begin when playback resumes.  Reaching the live end does
not stop monitoring or change the button to Play.

Both `I` and `K`, including their Alt forms, route to the same Play/Pause
button.  The separate Pause and visible Stop controls no longer exist.
Cancellation remains available internally for application disposal, session
replacement, navigation restart, and other ownership transitions.

`MainForm` places Activity and accepted-text diagnostics in one bottom
`TabControl`.  The accepted page uses the short **Accepted Text** title while
inactive and **Recent Accepted JSONL** while selected.  Accepted-text updates
capture whether the view was following the final display line and its first
visible line.  They either continue following the end or restore the previous
scroll position instead of resetting to the top.

Speech-profile popup names use title case.  User Context is presented as
**User Quoted Text Speech Profile** because its structural source is Markdown
blockquote text.  All four speech-table headers use the default bold UI font at
the same size and top alignment.

## v38 designer serialization metadata

`SpeechProfileCompactControl.Rate`, `Pitch`, and `Volume` are runtime-owned
properties.  Each is marked with `DesignerSerializationVisibility.Hidden`,
matching the other custom runtime-only WinForms properties in the project.
This prevents the .NET 10 WinForms analyzer from requiring generated-code
serialization semantics for controls that `MainForm` creates and configures
entirely in code.

## v37 compact Main and Context profiles

`MainForm` now presents four speech-table columns: Content, shared Voice, Main,
and Context.  Each Main or Context cell owns a
`SpeechProfileCompactControl`, which stores its own rate, pitch, and volume.
The shared Voice dropdown is the row-wide master switch.  A profile with volume
zero remains editable but is not eligible for playback; its compact rendering
is replaced by crossed-out lips.

The compact control uses the selected no-band visualization.  Signed rate and
pitch retain the horizontal `-10..10` scale, draw positive values only above
the centre axis and negative values only below it, and give small nonzero
values a minimum visible height.  Volume uses a right-triangle fill from zero
to 100.

`SpeechProfilePopup` is a child overlay of `MainForm`, not a top-level window.
It is centred over its compact control and therefore cannot change owner-window
activation or stacking.  The compact control treats its anchor and overlay as
one delayed-hover region.  Escape and Alt+F4 hide the overlay without disposing
it or closing the application.  Outside clicks close visible overlays.

The editor's Tab traversal is Rate, Pitch, Volume, then normal form tab order.
When volume is zero, Rate and Pitch are disabled and either Tab direction from
Volume exits the editor.  Profile sliders forward the existing bare transport
keys to `MainForm`; editable text controls retain their ordinary typing rules.

`SpeechProfileSettings.IsSpoken` now requires both a selected voice and volume
greater than zero.  `MainForm.ReadRoleProfile()` reads each compact profile's
volume independently, and settings version 9 persists those values without a
schema-breaking migration.

## v36 speech-table alignment

`MainForm.MakeSpeechColumnHeader()` uses a compact bold font and top-centred
text so both `Context` headings fit on two lines without widening their
columns.  `MainForm.ConfigureNumeric()` right-aligns every `NumericUpDown`
value.  Each voice selector is docked to the top of its table cell so its
vertical placement matches the spin controls.

## v35 compiler correction

`SessionLocator.ReadClaudeTitle()` receives the
`ConcurrentDictionary.TryGetValue()` output as `string?` and returns it only
after an explicit non-null check.  This satisfies nullable flow analysis
without changing cache lookup or title fallback behaviour.

## v34 compact controls, title authority, and stable follow state

`MainForm.AddSpeechHeader()` uses fixed-width two-line labels for Main and
Context rate/pitch columns.  Their widths match the associated
`NumericUpDown` controls, so header text no longer expands the table columns.

`MainForm.ProcessCmdKey()` permits bare transport shortcuts when focus is inside
an `UpDownBase`.  Editable `TextBoxBase` and editable `ComboBox` controls still
retain bare input, and every Alt shortcut remains global to the active form.

`SessionLocator.ReadClaudeTitle()` prefers Claude's authoritative `ai-title`,
then falls back to the first User or Assistant text.  Fallback extraction skips
Markdown fence-only lines.  `MainForm.UpdateControlState()` keeps Auto-follow
enabled for normal rendering while setting `AutoCheck` false during monitoring,
so its state remains visible but immutable until Stop.

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
  -> SpeechWordBoundary playback events
  -> TranscriptView WebView2 marker

Selected JSONL
  -> TranscriptMarkdownFormatter
  -> Markdig HTML
  -> TranscriptView WebView2 document
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
| `SpeechProfileCompactControl` | Draws and owns one Main/Context profile. |
| `SpeechProfilePopup` | Edits one compact profile in a child overlay. |
| `TranscriptMarkdownFormatter` | Produces rendered-session Markdown. |
| `TranscriptView` | Hosts WebView2 and synchronized transcript markers. |
| `TranscriptSettingsPopup` | Edits follow, colour, and fade settings. |
| `SpeechWordBoundary` | Maps PCM time to one source word. |
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
- the Play/Pause control starts monitoring when stopped;
- while monitoring, Play/Pause suspends or resumes both the active utterance
  and automatic speech of future live fragments;
- reaching the current live end remains an active waiting state.

Application-local hotkeys are processed first by `MainForm.PreFilterMessage`
and retain `MainForm.ProcessCmdKey` as a control-level fallback:

| Shortcut | Action |
| --- | --- |
| `U` / `Alt+U` | Previous opposite-speaker run |
| `H` / `Alt+H` | Previous node |
| `J` / `Alt+J` | Previous sentence/code line |
| `K` / `Alt+K` | Start, pause, or resume monitored playback |
| `L` / `Alt+L` | Next sentence/code line |
| `;` / `Alt+;` | Next node |
| `O` / `Alt+O` | Next opposite-speaker run |
| `'` / `Alt+'` | Speak AI processing time |

They operate only while the Agent Panel Speaker window has focus.  The invoked
button receives focus before `PerformClick` runs.  Bare keys remain active in
numeric spin controls and compact speech-profile sliders.  Editable text and
editable drop-down controls retain bare keys; Alt variants remain available
there.

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

A path displayed after Browse or Detect latest is passed as `ExplicitPath`
when Auto-follow is off.  Pausing and resuming therefore retain the same JSONL.
Only an explicit Auto-follow session may periodically call `FindLatest` and
switch to a newer file.  `SessionChanged` advances a generation counter and
clears active, pending, and indexed speech before history or live events from
the new file are accepted, preventing queued output from the previous
conversation.

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
Bluetooth wake settings, theme, transcript follow/highlight/fade/maximize
state, startup playback, polling interval, and normal window bounds.

Settings version 10 adds normalized `TranscriptSettings`.
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
- Rendered transcript content is derived from JSONL, not speech eligibility.
- WebView2 failure cannot disable monitoring or audio playback.
- Visible sub-agent IDs are never added to spoken fragments.
- A wake tone, settling silence, and its speech use one `waveOut` buffer.
- Play/Pause never treats the current live end as stopped; it remains armed
  for future accepted text until paused or internally cancelled.
- The IPA toolbar never inserts into a token or into its `ipa:` prefix.
- Settings writes never overwrite the live file partially.

## Deferred transcript directions

Version 40 deliberately excludes an Edge/WebView2 speech backend.  A future
implementation must first probe local/online voice catalogues, word boundaries,
pause/resume, Windows 10 behaviour, Edge policy, network/privacy implications,
and possible service cost.  Windows speech remains the offline default.

Code blocks remain ordinary fenced-code HTML.  Future syntax highlighting or
prettification should use a maintained JavaScript renderer such as Prism.js,
highlight.js, or Shiki rather than a custom parser.

Future gear options may read heading timestamps and record numbers.  Opaque
sub-agent IDs remain silent unless a separate explicit setting is added.

## Diagnostics

Logs record session selection/switching, JSONL classification, accepted nodes,
duplicate suppression, emitted fragments, playback/navigation, fence outcomes,
settings saves, missing voices, wake-tone output/failures, SAPI failures, and
form/screen geometry.  The bottom Activity tab shows operational events.  The
accepted-text tab shows a bounded monitor preview and does not represent the
active speech cursor.  Accepted conversation text is present in logs and should
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


## v69 interaction rules

Windows.Media bookmark timing is unconditional. Processing-time requests made during active playback wait only for the current speech fragment (sentence/code line), not the entire JSONL node. Requests made while paused interrupt the paused fragment, speak immediately, and return to a paused state with that fragment pending for replay. Hotkeys are persisted as unique single-character bindings.


## C#-owned transcript search

The rendered transcript search index is constructed in C# from the same HTML
and node-identity payload used to create the WebView document.  It stores
compact character-to-rendered-token mappings without DOM references.  Search
results remain in C# and the WebView receives only the selected match's token
range and optional speech node position.

Unrestricted regular expressions execute in a separate worker process.  The
main process cancels by terminating that worker; managed threads are never
forcibly aborted.

## Hover/focus popup lifecycle

`HoverPopupController` is the sole lifecycle implementation for transcript and
speech-profile popups.  Each instance owns one root popup tree; a centralized
registry coordinates mutually exclusive roots, outside clicks, Escape, and form
deactivation.  Nested transcript popups are child nodes in the transcript tree.
No popup class owns open/close timers or outside-click policy.


## Startup presentation

The native main window is created at zero opacity.  Synchronous control-tree
construction and initial settings/theme population occur under suspended
layout.  `Shown` completes any saved-session restoration while the window is
still transparent, then schedules a single deferred presentation pass.  That
pass performs final layout, restores full opacity, and repaints the completed
control tree.  Asynchronous transcript initialization remains represented by
its own stable loading UI rather than exposing intermediate WinForms layout.

## v127 settings transaction model

`UserSettingsStore` owns two immutable snapshots: `Saved` is the last snapshot
successfully written to disk and `Current` is the live working snapshot.
Ordinary UI updates replace only `Current`.  Persistence is explicit and atomic:
the candidate snapshot is written before `Saved` is advanced.  A property-level
`SettingsChangeSet` supplies display names, dirty-state detection, and selective
merging.  The Save button, selective-save popup, reset operation, and close
confirmation all use this one comparison/merge implementation.
