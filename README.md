# Agent Panel Speaker v89

A new search now resolves its no-selection origin from the authoritative C#
playback marker.  It no longer depends on the voice word being present in the
currently virtualized WebView window.


## v89 Immediate indexed playback and pronunciation-safe highlighting

- Starting monitoring reuses the already-indexed paused history and resumes
  immediately instead of clearing it and waiting for a second full JSONL parse.
- The background monitor still rebuilds its own tailing state, but its initial
  history callback no longer replaces the actively playing indexed history.
- System.Speech progress events are matched monotonically against display
  tokens by spoken text before character-offset fallback.  Pronunciation
  substitutions can therefore change synthesis text length without shifting
  later highlight positions.

## v88 Follow control simplification

- The eye/equality/lips group is now one follow toggle button.
- The button is 50% opaque normally and fully visible on hover or keyboard focus.
- The fixed follow shortcut is now `=` (and `Alt+=` through the existing transport shortcut path).
- Follow mode is no longer listed in the hotkey-remapping dialog.


## v87 Find seek and first-play restoration

- Find seek token indices now use the same punctuation-inclusive token coordinate
  system as `SpeechService`.  A match no longer maps word index zero to a leading
  slash or other punctuation by accident.
- Starting monitoring preserves a Find-selected speech position across history
  re-indexing.
- The first Play request now resumes automatically after existing history has
  finished indexing instead of returning to the original paused position.

## v86 authoritative search origin

Selecting or restoring a session now indexes its speech history immediately, without starting the live monitor.  The resulting position is loaded into SpeechService in the paused state and published to the transcript before Play is pressed.  Startup virtualization therefore centres on the same fragment that Play will resume from instead of the newest-record fallback.

## v82 compile correction

Version 82 initializes the `TryResolvePositionIndex` out parameter before the
short-circuit identity lookup. This fixes CS0177 when no matching node identity
is present.

## v79 virtualized transcript and follow overlay

Version 79 keeps the complete transcript, search index, speech position, and
record mapping in C#, but renders only a bounded record window in WebView2.
Search and speech navigation request a new window around the target record
instead of scrolling through the complete transcript DOM.  Manual scrolling
shifts the virtual window while retaining a visible record anchor.

A translucent bottom-right `👁️ = 👄` / `👁️ ≠ 👄` control toggles transcript
follow mode.  `=` means that the view follows speech.  `≠` leaves the current
view independent while speech continues.  Manual scrolling automatically
disables follow mode.  Enabling it recentres on the current speech position.
When a transcript is first loaded, the initial window is centred on the known
speech-start record, or on the newest record until that position is known.

## v78 C# transcript search

Version 78 removes the browser-side transcript search corpus.  Search text,
rendered-token offsets, voiced-node identities, and result navigation are built
and retained by C# alongside transcript rendering.  The WebView receives only
compact match descriptors and touches the DOM only for the selected match.

Literal searches run on a cancellable background task.  Regular-expression
searches run in a separate instance of the application in worker mode, so Esc
can terminate catastrophic backtracking.  After five seconds the find popup
asks whether to continue waiting or cancel; continuing does not impose a hard
timeout.




## v75 find performance correction

Version 74 caches the voiced and all-text search corpora until the transcript is
replaced. Find highlighting now clears only words touched by the previous search
instead of scanning every rendered token. Input is also debounced for 150 ms,
and each new search still terminates the previous worker before it starts.


## v77 staged find diagnostics

Version 77 instruments the browser work performed after a find worker returns.
The diagnostic log now records separate elapsed times for result mapping, clearing
old result classes, applying match classes, changing the current-match class,
opening collapsed `details` ancestors, requesting the scroll, and the animation
frame after each stage.  It also records the number of highlighted words and the
number of collapsed `details` elements opened.  The stages yield to one animation
frame between operations so the log can identify which browser operation stalls.


## v73 find reliability and keyboard corrections

Version 73 focuses the find text box when `Ctrl+F` opens the popup, routes
Alt-modified transport hotkeys from the WebView, and maps `Alt+C`, `Alt+W`,
and `Alt+R` to the case, whole-word, and regular-expression toggles.  The
Voiced/All selector is now a compact mouth-icon toggle that defaults to
Voiced.

Find navigation now terminates every previous worker before starting a new
search, disables previous/next while a search is running, and records search,
cancellation, completion, and navigation events in the diagnostic log.  Match
mapping uses binary range lookup instead of scanning every transcript token
for every match.  This removes the UI-thread CPU spike observed when navigating
a large result set.

The transcript viewer now receives the saved highlight settings while the main
form loads its settings, rather than initially showing the built-in default
colour.

## v72 transcript find popup

Version 72 adds a VS Code-style transcript find popup opened with `Ctrl+F`.
It supports match-case, whole-word, regular-expression, and Voiced/All scope
controls.  Voiced is the default.  Enter selects the next match, Shift+Enter
selects the previous match, and Ctrl+Shift+Enter moves the paused speech marker
to the current voiced match.

Regular-expression matching runs in a disposable Web Worker.  Escape cancels a
running search immediately.  If it is still running after five seconds, the
viewer asks whether to continue waiting or cancel it; there is no fixed search
timeout.  Escape closes the popup when a find control owns the caret and no
search is running.

## v71 diagnostic and paused-seek corrections

- System.Speech `SpeakProgress` callbacks are logged with provider positions,
  normalized source ranges, token indices, and source-token text.
- Every speech boundary, transcript-marker post, and WebView marker application
  is logged instead of tracing one hard-coded sentence.
- Sentence, node, and speaker navigation now preserves the paused state.  The
  transcript marker moves to the selected entry, but audio does not resume until
  Play/Resume is explicitly requested.


## v69: transport timing, configurable hotkeys, and UI cleanup

- Processing-time announcements speak immediately while playback is paused and
  are inserted after the current sentence while playback is active.
- Bare `M` toggles the transcript area between normal and maximized views.
- Bare `F` toggles transcript follow mode.
- The new **Hotkeys...** dialog remaps all transport, processing-time,
  transcript-size, and follow shortcuts while rejecting duplicate keys.
- The speech-profile column formerly labelled **Context** is now **Thoughts**
  for both agents and subagents.
- Windows.Media bookmark timing is always enabled; the obsolete policy control
  has been removed.
- Pronunciations are available while stopped or paused.
- Option-row checkboxes now use a consistent vertical margin.

## v62: Windows.Media SSML cue-coordinate correction

Windows.Media `SpeechCue.StartPositionInInput` and `EndPositionInInput` are
reported in the coordinate space of the complete SSML document supplied to the
synthesizer.  The transcript tokens are indexed in the original plain-text
fragment.  v61 compared those positions directly, so the SSML document prefix
and inline tags caused most valid cues to miss their tokens.  It also accepted
any non-empty partial mapping as a complete schedule, which made highlighting
skip every rejected word.

The Windows.Media mapper now derives the SSML input-position offset from the
first timed word cue and its matching source token, then translates every cue
range before token lookup.  Cue text is used as a monotonic fallback when an
inline SSML element changes the local character offset.  Repeated cues for one
token retain the earliest boundary.

A partial exact schedule is no longer accepted merely because it contains one
or two mappings.  If fewer than 80 percent of usable word cues can be mapped
safely, the complete fragment uses the duration-weighted fallback instead of
silently omitting the unmatched words.  Diagnostics now record the cue count,
rejected-cue count, inferred input offset, and mapping coverage.


## v61: exact Windows.Media timed word metadata

Windows.Media voices now request `IncludeWordBoundaryMetadata` and consume the
returned `SpeechWord` timed-metadata track.  Each `SpeechCue` supplies its exact
`StartTime` plus `StartPositionInInput` and `EndPositionInInput`, which are
mapped to the corresponding transcript token.  Richard, Heera, Ravi, David,
and other modern Windows voices therefore use the synthesis engine's own word
timeline rather than duration-weighted estimates.

If a particular voice returns no usable `SpeechWord` track, the renderer logs
`speech.windows_media_boundaries_unavailable` and falls back to the previous
duration-weighted estimates.  System.Speech continues to use source-range
`SpeakProgress` events, while native SAPI continues to use estimates.


## v60: source-range System.Speech highlighting

The System.Speech path maps every `SpeakProgress` callback through its
`CharacterPosition` and `CharacterCount` source range.  It no longer assigns a
new transcript token merely because another callback arrived.

System.Speech reports character positions relative to the generated SSML
document.  The renderer normalizes that constant SSML offset against the first
token in the original fragment before storing a boundary.  Repeated callbacks
for a number such as `957` consequently retain the same source range and the
same displayed token throughout the number's spoken components.

Punctuation that receives its own progress range remains highlightable.
Punctuation that receives no progress event is skipped naturally.  This path
does not insert bookmarks and does not compact timestamp arrays.



## v58: synchronized System.Speech WAV timing

System.Speech Desktop voices are now rendered explicitly as 16,000 Hz,
16-bit, mono PCM.  Some Desktop voices, including Microsoft Hazel Desktop,
report `SpeakProgress.AudioPosition` and bookmark positions on a 16 kHz voice
timeline even when the default WAVE writer produces a 22.05 kHz file.  Using
those 16 kHz positions against the longer 22.05 kHz playback timeline causes a
proportional highlighting delay that grows throughout an utterance.  Keeping
the generated WAVE file on the same 16 kHz timeline removes that drift before
the existing playback path resamples the PCM for output.

A separate plausible source of local punctuation misalignment is that timing
markers and speech normalization need not interpret an attached period in the
same way.  A marker API may expose `.` as its own input token or possible
sentence boundary, while the selected voice can normalize an attached form
such as `.hpp` as part of a filename or abbreviation and assign the period no
independent audible duration.  This can make a marker placed around the period
appear associated with the following spoken letters.  Agent Panel Speaker
should therefore preserve attached punctuation in displayed text but must not
assume that every punctuation token has an independent audible interval.

The 16 kHz mismatch and attached-period interpretation are distinct possible
causes: the former produces global proportional drift; the latter can produce
a local token-boundary discrepancy even when the overall audio timeline is
correct.


## v56: controlled rollback of v54/v55 mapping changes

Version 56 restores the v53 transcript-rendering and playback-mapping path.
The transformed-text source mapping, segment-local identity, and sequential
`SpeakProgress.Text` lookup introduced in v54 and v55 have been removed.

The confirmed v51-v53 improvements remain:

- bounded configurable playback mailbox;
- exact `System.Speech` `SpeakProgress.AudioPosition` timings for compatible
  Desktop voices;
- v52 timing diagnostics;
- the original wake-audio and forced-preview behaviour.

This rollback intentionally does not add another mapping strategy.  It returns
the display path to the last known baseline before the v54/v55 regressions.

## v53: exact timings for Desktop voices

When a voice is exposed by both native SAPI and `System.Speech`, the
`System.Speech` registration is now preferred.  This keeps WAV rendering and
the Bluetooth wake-tone path, while collecting exact `SpeakProgress`
`AudioPosition` boundaries during synthesis.  Native SAPI remains a fallback
for voices that are not available through `System.Speech`.

## v51

Version 51 replaces the per-boundary `BeginInvoke` path with a bounded
`TranscriptPlaybackMailbox`.  Publishing a speech boundary overwrites the
oldest retained position when the mailbox is full and schedules at most one
UI-thread wake-up.  Positions that arrive while one wake-up is being processed
remain for one subsequent wake-up rather than adding unbounded WinForms queue
entries.

The **Highlight Queue** setting controls the circular mailbox capacity from
1--16 positions.  The default is 1, which retains only the newest visual
position and discards obsolete intermediate word markers.  Capacities above 1
are available for comparison without changing the speech engine or WebView
message format.

This version intentionally implements only the first playback-path experiment.
Control-command separation, hidden-view suppression, numeric fragment IDs,
performance/GC instrumentation, and shared memory remain unchanged so the
mailbox result can be measured independently.

## v50

Version 50 sends speech-marker updates through one-way WebView messages instead
of one acknowledged `ExecuteScriptAsync()` call per spoken token.  Playback
updates therefore no longer wait behind transcript-colour updates.

Transcript settings use a latest-value mailbox sampled at 100 ms.  Continuous
colour-wheel movement replaces the pending colour instead of adding WebView
work, and the nested picker limits parent notifications to one every 75 ms.
The final colour is flushed when the picker closes.

Retired word highlights now use Web Animations rather than inline styles plus
queued timers.  A new active word cancels any older fade on that word, preventing
a delayed WebView event loop from leaving a long highlighted trail.  Each word
update also clears only the previous marker instead of scanning every rendered
word in a large transcript.

The parent Transcript Settings overlay now closes directly when the nested
colour picker's three-second grace period expires, or when the pointer revisits
and leaves the colour swatch.  That close no longer depends on a second hover
check that kept the parent open while the pointer remained inside it.

## v49

Version 49 keeps an already rendered session in place when monitoring starts.
The monitor's initial `SessionChanged` notification no longer forces the same
JSONL file through a second complete Markdown and DOM render.

Transcript identity mapping now uses the same sentence and code-line segments
as speech history.  Prose such as `Excellent:` and the sentences preceding a
code block therefore map before playback begins instead of remaining
unhighlighted until the first `//` line.

WebView updates use coalescing, acknowledged `ExecuteScriptAsync` calls.  Only
the latest pending playback position and transcript colour settings are
applied after an in-flight update completes.  Colour-wheel movement also
uses a 250 ms settings-save debounce instead of writing the settings file for
every pointer event.

## v47

Version 47 fixes transcript loading that could remain blocked behind an older,
large saved-session render.  Selecting or clearing a session now cancels the
obsolete formatter and identity-map pass immediately.  The loading surface
states whether the WebView is being prepared, a saved transcript is being
restored, or a named selected transcript is being loaded.

Rendered words are indexed by their JSONL record before node mapping.  Node
segments now search only the words belonging to that record instead of scanning
the entire transcript for every segment.  Playback uses the segment ranges built
during that pass rather than rescanning all displayed words.  Render diagnostics
report preparation, DOM, and total durations.

Transport shortcuts no longer depend on `Button.PerformClick()`.  Maximized
Transcript mode hides the transport-button row, and WinForms refuses to perform
a click on a hidden button even after the shortcut itself was received.  The
shortcut dispatcher now invokes the matching transport command directly while
retaining the existing WinForms message-filter and WebView keyboard paths.

Auto-follow is now the default when no fixed JSONL path is saved.  A fixed path
selected with **Browse JSONL** still disables auto-follow, but the checkbox
remains enabled so it can release the fixed path.  Changing source returns to
auto-follow.

The nested highlight-colour popup restores the complete compact Cyotek picker:
a colour wheel, RGB/HSL/alpha editor, and previous/current swatches.  Escape,
Tab traversal, pointer dismissal, parent-overlay suppression, and dark mode keep
the v44 overlay semantics.

## v46

Version 46 removes the unsupported native accelerator subscription introduced
in v45.  The Microsoft.Web.WebView2 1.0.4078.44 WinForms control used by this
project exposes neither `CoreWebView2Controller` nor `AcceleratorKeyPressed`.
Transcript hotkeys therefore use the existing in-page `keydown` handler and
`CoreWebView2.WebMessageReceived` bridge, while `MainForm` continues to handle
keys received through normal WinForms message routing.

The v45 symbol-aware tokenization and highlighting changes are retained.

## v45

Version 45 introduced symbol-aware playback highlighting.

Transcript highlighting now wraps visible operators and punctuation as speech
units in addition to lexical words.  Node mapping first uses the complete
visible token stream and falls back to lexical matching when Markdown removes
source punctuation.  Playback uses the speech engine's reported token text to
select one or more rendered spans, so spoken symbols such as `/`, `+`, and
multi-character operators receive the same active, paused, and fading markers
as words.  Approximate word-boundary generation and paused-speech restart use
the same shared tokenization rule.

## v44

Version 44 moves the transcript highlight colour wheel into a compact nested
popup opened from the current-colour swatch.  Escape returns focus to the
Transcript Settings overlay.  Tab and Shift+Tab leave the nested popup only at
its final and first controls.  Pointer exit closes only the colour popup; the
parent overlay remains open until the pointer returns to the colour swatch and
leaves again, or until three seconds elapse.  Both overlays follow the active
light or dark theme.

Pausing monitored playback now unlocks the session source, detection, browse,
auto-follow, polling, and startup-history controls.  Selecting another session
while paused performs the internal monitor cancellation that the removed Stop
button previously provided.

Transcript formatting and node-identity construction now run away from the UI
thread.  A themed **Loading transcript view…** surface remains visible until
the WebView has installed the completed HTML and identity map.  Structural
blocks inside quoted Claude Code cards are parsed independently, so headings,
labels, fenced code, and following prose are separate navigation fragments even
when the preceding block lacks sentence punctuation.  This also keeps playback
highlighting aligned with the rendered block boundaries.

The application message filter now identifies key messages by their native
root window instead of relying on `Form.ActiveForm`, which is not dependable
while WebView2 owns focus in maximized Transcript mode.  The WebView keyboard
bridge remains as a second path.  **Open diagnostic log** reuses an existing
Explorer window, preferring one already showing the Logs folder and otherwise
navigating an existing Explorer window before opening a new one.

## v43

Version 43 renders Claude `attachment` / `queued_command` records in the
Transcript instead of indexing them for speech while omitting them from the
page.  Queued-command extraction separates Claude Code's generated quoted card from
the actual User text while retaining both in navigation and rendering.
Existing-history playback marks only the first actual User fragment as the
turn start, so startup does not rewind into the generated context.

Rendered records now carry their JSONL record number and source UUID in the
DOM.  `TranscriptNodeIdentityMap` uses the source ID and record number as the first
mapping scope, then uses cleaned segment text inside that record.  Unmapped node and
playback fragments are written to the diagnostic log.  Collapsed reasoning
remains in the DOM and opens automatically when its highlighted word is
reached.

Transport keys are intercepted by an application message filter before a
focused WebView2 child can consume them, including while Transcript is
maximized.  Editable text controls still retain bare keys and all Alt variants
remain transport shortcuts.

Transcript fade retains the 0--0.5 second range but now uses 1/64-second steps.
The `tools/ColourPickerComparison` project compares the current Cyotek editor,
preset swatches, RGBA sliders, and the standard Windows colour dialog without
changing the main application's picker.

## v42

Version 42 replaces the transcript RGB sliders with the MIT-licensed Cyotek
WinForms colour controls.  The themed editor combines a colour wheel, RGB/HSL
and hexadecimal entry, immediate light/dark highlight updates, and previous
and current colour swatches.  Clicking the transcript gear or a compact speech
profile opens its editor and moves focus to the first enabled control.  Clicking
unused space inside either editor does the same.

Transcript words are now associated with the monitor's exact accepted-node ID
before playback matching.  This prevents repeated or incrementally rewritten
Claude text from moving the marker to a later copy of the same phrase.  Live
word updates use WebView2 host messages rather than queued script evaluations.
The speech worker's word-boundary polling interval is configurable from 5 to
40 ms in the Transcript gear; 10 ms is the default, and the worker uses elevated
priority at the two fastest settings.

Transport, gear, and maximize/restore symbols are drawn as compact vector
icons rather than relying on font glyphs.  The Transcript settings layout also
provides enough width for **Follow Speech** and retains the corrected hover,
outside-click, Escape, tab-switch, and maximize dismissal rules.

## v41

Version 41 fixes the WebView2 WinForms reference warning, transcript-toolbar
clipping, transcript-settings dismissal, dark-theme colour editing, word-marker
tracking, and transport-glyph sizing.  Transcript fade duration now ranges from
0 to 0.5 seconds in 1/16-second increments.

## v40

Version 40 adds a rendered **Transcript** tab as the left-most and initially
selected bottom tab.  Claude and Codex JSONL are formatted as Markdown,
converted with Markdig, and displayed by the WinForms WebView2 control.  The
view is blank until a session is selected.  Claude `Agent` calls and completed
child-agent task notifications are included as top-level
`## Claude Sub-agent <id>` sections; the visible opaque ID is not spoken.

The transcript follows live file changes and preserves manual scroll and
`<details>` state while updating.  Its restrained light and dark palettes avoid
pure white and pure black reading surfaces.  A missing WebView2 Runtime disables
only rendered transcript display; speech and diagnostics remain available.

The transcript toolbar contains a settings gear and maximize/restore button.
The in-window settings overlay immediately applies **Follow Speech**, separate
light/dark highlight colours, and a fade duration from 0 to 0.5 seconds in
1/16-second increments.  Maximizing keeps the tabs and toolbar visible while
collapsing the rest of the window, and the state is persisted.

System.Speech and Windows.Media voices provide exact synthesis word boundaries.
Native SAPI voices use duration-weighted word estimates, with the same
fragment-level starting marker as a final fallback.  Spoken words receive a
filled background highlight; completed words fade for the configured duration.
Pause shows a blinking hollow outline around the word that will resume.  A
pause longer than one second restarts that word.  At the live end, Pause shows a
one-em hollow marker on the next line.  Existing seek hotkeys move the marker
and optionally scroll it into view.

`I` and `Alt+I` are removed.  `K` and `Alt+K` remain the sole Play/Pause
hotkeys.  The included `tools/AI-transcript.py` reference script now renders
parent and child Claude sub-agents.  `tools/test-subagents.py` verifies opaque
IDs from both result formats.

## v39

Version 39 replaces the separate Pause and Play/Stop buttons with one
Play/Pause control.  Play starts JSONL monitoring.  While monitoring, the same
control pauses or resumes playback.  Reaching the current live end remains an
active waiting state: the button continues to show Pause and newly accepted
text is spoken automatically until the user pauses.  `I` and `K`, with or
without Alt, are aliases for the same Play/Pause action.  There is no visible
Stop control; application shutdown and session changes retain their internal
cancellation paths.

The Activity and accepted-text diagnostics now share one bottom tab control.
The accepted-text tab is labelled **Accepted Text** while inactive and
**Recent Accepted JSONL** while selected.  Replacing its bounded preview now
keeps a manually chosen scroll position, while a view already following the
end continues to follow new accepted nodes.

Speech-profile editor titles now use consistent title case.  The User Context
profile is named **User Quoted Text Speech Profile**.  The Content, Voice,
Main, and Context table headings now use the same font family, size, weight,
and top alignment.

## v38

Version 38 marks the compact profile control's `Rate`, `Pitch`, and `Volume`
properties as hidden from WinForms designer serialization.  The properties
remain public runtime settings, but the designer no longer attempts to infer
how their values should be emitted into generated code.  This fixes the three
`WFO1000` warnings that were promoted to build errors in version 37.

## v37

Version 37 replaces the separate Main/Context rate, pitch, and volume spin
columns with two compact speech-profile controls per row.  Each control draws
rate, pitch, and volume in the selected no-band triangle design.  Volume zero
shows the crossed-out lips symbol and independently mutes only that Main or
Context profile.  The row's Voice dropdown remains the master switch for both.

Hovering for 250 ms or tabbing to a compact control opens an in-window editor
centred over it.  The editor provides Rate, Pitch, and Volume sliders, closes
200 ms after the pointer leaves both regions, and never creates a separate
window.  Escape and Alt+F4 dismiss only the editor.  Tab traverses Rate, Pitch,
and Volume before continuing through the form; while muted, only Volume is
focusable.  Existing transport keys remain active while a slider has focus.

Existing settings migrate without losing profile values.  Main and Context
now persist and preview their volumes independently; a profile is eligible for
speech only when its shared row voice is selected and its own volume is above
zero.

## v36

Version 36 makes the speech-profile table visually consistent.  Main and
Context headings use compact two-line labels that fit the same fixed-width
columns as their spin controls.  Numeric text is right-aligned, and each voice
selector is anchored to the top of its row rather than filling the cell
vertically.

## v35

Version 35 fixes the nullable-flow compiler error in
`SessionLocator.ReadClaudeTitle()`.  The cached title returned by
`ConcurrentDictionary.TryGetValue()` is treated as nullable until both lookup
success and a non-null value are established.  Session-title behaviour is
otherwise unchanged from version 34.

## v34

Version 34 compacts the Main/Context speech columns by wrapping their headings
onto two lines at the numeric-control width.  Bare transport hotkeys now remain
active while a spin control has focus, while editable text and editable combo
boxes continue to retain ordinary typing.

Claude session names now prefer the JSONL `ai-title` value shown by Claude Code.
The first-message fallback skips Markdown fence markers such as ` ```json `, so
a fence opener can no longer become the displayed session name.

While monitoring, **Auto-follow newest session** remains visibly checked at its
normal contrast.  The checkbox is temporarily non-toggling until monitoring
stops, rather than being disabled and rendered with a faded checkmark.

## v33

Version 33 renames the speech-style columns to **Main** and **Context** and
provides both styles for all three shared voice rows.  AI reasoning uses its
role's Context rate and pitch.  Explicit Markdown blockquotes in genuine User
records retain the User voice but use the User Context rate and pitch.

The quote rule is deliberately structural: source lines beginning with `>` and
their Markdown lazy-continuation lines are User Context.  Headings, ordinary
quotation marks, and unquoted prose remain User Main.  Agent Panel Speaker does
not infer who originally wrote quoted material.

## v32

Version 32 adds **Keep display on while speaking**.  When enabled, active
speech requests `ES_DISPLAY_REQUIRED` from Windows.  The request is released
when speech stops or pauses, when the option is disabled, and when the
application exits.  It does not request `ES_SYSTEM_REQUIRED`, so the option is
limited to the display behaviour it names.

## v31

Version 31 fixes the nullable-flow and definite-assignment compiler errors in
`SpeechService.RegisterBackgroundWorkEventLocked()` and
`SpeechService.TryBuildProcessingTimeAnnouncementLocked()`.  Background-agent
behaviour is otherwise unchanged from version 30.

- Replaced the separate Assistant, Reasoning, and User profile rows with this
  shared-voice matrix:

  | Content | Voice | Main R | Main P | Context R | Context P | Volume |
  | --- | --- | --- | --- | --- | --- | --- |
  | AI agent | Dropdown | Spin | Spin | Spin | Spin | Spin |
  | AI subagent | Dropdown | Spin | Spin | Spin | Spin | Spin |
  | User | Dropdown | Spin | Spin | Spin | Spin | Spin |

  Each row shares one voice and volume across Main and Context styles.  AI
  Context is reasoning; User Context is explicit Markdown blockquotes.
  Ordinary prompts, queued commands, and input selections use User Main.
- Claude `Agent` tool calls now announce `Starting subagent` with the AI
  subagent foreground profile.  A matching Agent result announces completion
  and authoritative duration with the AI agent foreground profile, then speaks
  the returned contents with the AI subagent foreground profile.
- Completed Claude task notifications are correlated by task ID, announced by
  the AI agent, and spoken completely with the AI subagent voice.  Wrapper
  output-file paths, token counts, parent agent-ID/worktree metadata, and
  self-reported timing footers are excluded.
- Concurrent background work is retained by ID.  Result nodes are queued as
  complete contiguous groups, so two subagent responses cannot interleave.
- The processing-time clock appends running background-agent durations, the
  completed top-level spawned-agent runtime, and the number of completed
  child-agent runs.
  Completed background timing remains attached to the turn until the next real
  User prompt.


## v29

- After each Codex `request_user_input` question and its ordered options, the
  selected response is spoken with the **User messages** voice.  A labelled
  choice is announced as `Selected option N: Label`; free-form input is
  announced as `Selected: text`.
- Input selections remain part of the existing AI turn.  They use the User
  voice for narration but do not become new User prompts for processing-time,
  latest-turn, or turn-boundary calculations.
- Selection output is accepted only when its call ID matches a retained
  `request_user_input` call.  Unrelated function outputs remain excluded, and
  answers to secret questions are never narrated.

## v28

- Removed the separate Silence control.  Contiguous WAV playback already stops
  immediately through the Play/Stop control, so the duplicate action and its
  speech-bubble-and-finger icon are no longer present.
- Reassigned `'` and `Alt+'` to the processing-time clock.  The former `T` and
  `Alt+T` shortcuts are removed.
- Codex `task_complete` records are retained as non-spoken terminal markers and
  take precedence when selecting a turn's end time.
- Without a terminal marker, a turn is complete when its last retained AI
  message is user-facing Assistant text.  A latest turn whose tail remains
  Reasoning/thinking is measured to the current request time and reported as
  still processing.  This corrects Claude timing after a final response.
- Transport buttons now use an explicit taller height, and custom speaker-turn
  icons retain additional vertical inset so their strokes do not crowd the
  button borders.
- Bare transport shortcuts remain inactive while an editable control has focus;
  their Alt variants remain available.

## v25.6.26

- Inline Markdown code is protected before prose cleanup and restored as
  ordinary speakable text.  C++ templates, casts, and comparisons such as
  `PacketRingForChannel<`, `static_cast<void*>`, and `a < b` are no longer
  mistaken for HTML and deleted.
- General HTML cleanup now removes only balanced common HTML elements,
  comments, and genuine void elements.  It no longer applies the destructive
  catch-all `<[^>]+>` expression to arbitrary prose.
- Codex `request_user_input` function calls are the sole user-facing exception
  to tool-call exclusion.  Each question is spoken with the Assistant profile,
  followed by its numbered options and option descriptions in source order.
  Starting in v29, the matched selection is then spoken with the User profile.
  Every unrelated function call, tool call, result, command, diff, and status
  record remains excluded.

## v25.6.14

- **Pronunciations...** remains available while monitoring, paused, or speaking,
  so spelling and pronunciation rules can be reviewed and edited at any time.
- Controls that would start preview audio remain unavailable while another
  utterance is active.  **Pronounce** is disabled, pending IPA hover playback is
  cancelled, and IPA keys remain usable for symbol insertion.

## v25.6.13

- Codex `agent_message` records whose phase is `commentary`, `analysis`, or
  `reasoning` now use the **Reasoning/thinking** voice profile.  Final-answer
  and unphased agent messages continue to use **Assistant messages**.
- Voice dropdowns now redraw after their table-layout width changes and update
  their popup width to the resized field.  This removes the stale old arrow and
  blank extension that appeared after resizing the main window.
- The existing speaker-turn speech-bubble icons and `U`/`O` shortcuts are
  unchanged.

## v25.6.12

- Added the modern `Windows.Media.SpeechSynthesis` voice catalogue and renderer.
  Voice entries retain their provider and provider-specific identifier, and
  duplicate catalogue entries are merged while preferring the modern backend,
  then native SAPI, then `System.Speech`.  Natural or Natural HD quality is
  displayed when the provider exposes that metadata.
- Fixed inline Markdown code such as
  `` `rt_logger/PolicyMachinery.hpp` now treats... `` so it remains ordinary
  spoken prose.  An inline closing marker can no longer start a false fenced
  block and consume the remainder of the message.
- **Speak complete latest turn on start** begins at the final User node and
  continues through every following AI reasoning/assistant node.
- Session selection is pinned by default.  Playback pause/resume retains the
  selected JSONL, and switching to another session clears queued speech from
  the old one.  Only **Auto-follow newest session** may switch files
  automatically.
- Added previous/next speaker-turn controls.  Their custom icons combine two
  speech bubbles with a directional arrow.  `U` rewinds to the preceding
  opposite-speaker run and `O` advances to the following one.  Consecutive AI
  reasoning and assistant nodes count as one AI run.

## v25.6.11

- Fixed the nullable-analysis build failure in the dark-theme ComboBox drawing
  path by converting a null item label to an empty string before drawing it.

## v25.6.10

- Enter and Shift+Enter insert another line in both pronunciation editors
  instead of activating **OK**, so either tab accepts any number of entries.
- Voice labels use structured fields in this initial order: location, language,
  name, Natural quality, and maker.  Clicking the **Voice** heading rotates the
  field order left and immediately resorts all three dropdowns by the new first
  field while preserving every selected provider voice.
- The voice **Test** column was removed.  Changing a row's voice, rate, pitch,
  or volume automatically speaks that row's test message after a short debounce
  whenever monitored playback is not active.

## v25.6.9

- Pronunciation rules accept spoken-text aliases as well as IPA.  For example,
  `TODO/i=to do` speaks every case variation of `TODO` as “to do”.
- `name=spoken text` is exact-case; `name/i=spoken text` ignores case.  Existing
  `name=ipa:...` and `name/i=ipa:...` rules remain unchanged.
- **Pronounce** previews the spoken-text value on the caret line instead of the
  original token.

## v25.6.8

- Non-`md` fenced blocks ignore sentence punctuation for navigation.  Every
  non-empty source line is exactly one navigation unit and receives one
  structural pause.
- Fences explicitly tagged `md` are parsed recursively with the same Markdown
  block and sentence rules as ordinary JSONL message text.
- Backtick and tilde fences are recognized with runs of one, two, three, or
  more markers.  A closing fence uses the same marker character, at least the
  opening run length, and no info-string token.

## v25.6.7

- Headings, paragraphs, list items, quote blocks, table rows, and each spoken
  fenced-code line are independent sentence-navigation units.
- A structural block boundary adds one 250 ms pause to the final sentence in
  that block.  Sentence punctuation at the same location reuses the same unit;
  it does not create an empty extra unit.
- The original playback lockout for **Pronunciations...** was removed in
  v25.6.14; only preview-audio actions are now disabled during speech.

## v25.6.6

- Tabbing to an enabled IPA key now uses the same information and
  delayed-preview path as pointer hover.  Pressing Shift while a key is
  hovered or focused displays its information and plays it immediately.
- Symbol information remains visible for seven seconds, then returns to the
  rotating helper text even when the pointer or focus remains on the key.
- Generic IPA examples are explicitly labelled `carrier`; the UI no longer
  presents synthetic `apa` text as a real word.  Carrier IPA has no ordinary
  word fallback when a voice rejects it.
- Reused real-word examples were audited.  Equivalent alternate marks use the
  same carrier phone, and the conflicting Dvořák/lobo examples were replaced
  by clearly labelled carriers.
- The toolbar preserves its scroll position across Pronounce, symbol insertion,
  and window deactivation/reactivation.  First expansion uses about 86% of the
  available height and puts the caret line at the top of the remaining editor.
  A manually chosen splitter height is retained for later toggles.
- The rich-text footer uses an IPA-capable semibold font for the displayed
  symbol, isolated phone, and every matching phone cluster in the example.

## v25.6.5

- IPA hover information now uses one consistent layout: the displayed symbol,
  an isolated pronunciation when one can be constructed, an example word or
  carrier word, and the complete example transcription.
- Every occurrence of the selected symbol is bold in both the isolated and
  example transcriptions.  Combining marks bold their complete carrier
  cluster, and tie bars bold the complete tied affricate.
- Square-bracket highlighting has been removed completely.
- Generic consonants and vowels now use explicitly labelled carrier frames.
- Hover audio uses the same isolated IPA shown in the footer before speaking
  the example word.

## v25.6.4

- The IPA information footer is now a read-only rich-text control.  During
  symbol hover, only the displayed symbol or dotted-circle combining-mark
  cluster is bold; the example word, transcription, arrows, and position
  remain normal weight.

## v25.6.3

- Every IPA diacritic now has a named example word and representative IPA
  transcription instead of the shared `carrier example` fallback.
- Combining marks are displayed with a dotted-circle carrier in the information
  line, and modifier examples no longer place square brackets around a
  combining mark.
- The upper and lower tie bars use **church** as their example.

Agent Panel Speaker reads Claude and Codex conversation JSONL directly and
speaks user, assistant, reasoning, completed Codex Plan text, and Codex
user-input questions with separate voice profiles.  Plans and interactive
questions use the Assistant profile; the selected input response uses the User
profile.  Other tool calls, tool results, command output, patches, diffs, and
unrelated status records are excluded.

See [DESIGN.md](DESIGN.md) for the internal architecture and invariants.

## Speech by content

Each row has one shared Voice dropdown and two independent compact profiles:
**Main** and **Context**.  Each compact profile contains:

- rate (`-10..10`);
- pitch (`-10..10`);
- volume (`0..100` percent).

`Not Spoken` in the Voice dropdown disables the entire row.  Volume zero in a
compact profile mutes only that profile and displays crossed-out lips.  AI
Context is reasoning; User Context is explicit Markdown blockquotes.

Hover over a compact profile for 250 ms, click it, or Tab to it to open its
centred in-window editor.  The editor closes 200 ms after the pointer leaves
both the control and editor.  Rate and Pitch are skipped while volume is zero.
Tab after Volume continues to the next form control; Shift+Tab before Rate
continues to the previous control.  Escape or Alt+F4 closes only the editor.

Changing a profile value automatically speaks that Main or Context test message
after 350 ms without another edit.  Automatic previews are suppressed during
monitored playback and do not alter transcript history.

Voice labels initially display location, language, name, Natural quality when
present, and maker.  The dropdown is sorted by the first displayed field.
Clicking the **Voice** heading rotates the field order left and resorts all
voice lists.  `Not Spoken` remains the first special entry, and the stored
provider name is unchanged by display reordering.

The voice lists combine every enabled voice exposed through:

- `Windows.Media.SpeechSynthesis`;
- native `SAPI.SpVoice`;
- `System.Speech`.

Each entry retains the provider-specific identifier needed to synthesize it.
Duplicate catalogue entries are merged while preferring the modern Windows
backend, then native SAPI, then `System.Speech`; missing display metadata is
filled from the other matching entries.  A modern Windows voice is rendered by
`Windows.Media.SpeechSynthesis`, while legacy voices continue through their
working SAPI or `System.Speech` backend.  Natural/Natural HD appears only when
an installed provider reports that quality; the application does not create
unusable entries from display labels alone.

All providers render to the same PCM composition path, so rate, pitch, volume,
Bluetooth wake audio, IPA previews, spelling, and pronunciation aliases retain
the existing serialized playback behaviour.  Date and time patterns are
expanded into natural spoken forms before synthesis.

## Rendered transcript

The bottom tab area contains **Transcript**, **Activity**, and
**Accepted Text**.  Transcript is selected by default and remains blank until a
Claude or Codex JSONL is selected.  Activity and Accepted Text are diagnostic
views.  Accepted Text changes to **Recent Accepted JSONL** while selected.

The transcript is formatted from the source JSONL rather than from cleaned
speech fragments.  Markdig converts the generated Markdown to HTML, and
WebView2 renders headings, blockquotes, tables, links, fenced code, and
`<details>` sections.  Claude sub-agents appear as visible top-level headings:

```markdown
## Claude Sub-agent <opaque-id> [timestamp]: <record-number>:
```

The sub-agent ID, timestamp, and record number are not read.  The first remains
visible for correlation; optional speech of timestamps and record numbers is a
future setting.

The gear at the right of the tab strip opens an in-window settings overlay:

- **Follow Speech** scrolls the active word into a comfortable viewport region.
- **Highlight Colour** stores a separate colour for light and dark themes.
- **Fade Duration** ranges from 0 to 0.5 seconds in 1/16-second increments.

Changes apply immediately.  The filled active-word background moves with
playback.  Previous words fade to the normal background.  Pause replaces the
fill with a blinking hollow outline around the word that will resume.  Pausing
for more than one second causes resume to restart that word.  A pause at the
current live end shows a blinking one-em marker on the next transcript line.

The `^` button maximizes the selected bottom tab while leaving the tab strip,
gear, and a `v` restore button visible.  The selected tab and maximized state
are preserved.  Transport hotkeys continue working while WebView2 or the
transcript settings controls have focus.

System.Speech and Windows.Media supply exact word-boundary times.  Native SAPI
uses duration-weighted word estimates.  If a provider returns no
usable boundaries, the active fragment remains highlighted as a defensive
fallback.

The Microsoft Edge WebView2 Runtime is required only for this tab.  When it is
missing or fails to initialize, a message is shown in Transcript while the rest
of Agent Panel Speaker remains usable.

## Pronunciations and spelling

**Pronunciations...** opens two editors.

### Spell out

Enter one token per line.  Matching is case-insensitive and uses whole-token
boundaries.  A match is emitted through native SAPI `spell` or the equivalent
System.Speech SSML `say-as` element, so `IDE` is spoken letter by letter.
Entries are trimmed and de-duplicated while preserving their first occurrence.

### Pronunciation aliases and IPA

Enter one rule per line:

```text
TODO=to do
TODO/i=to do
git=ipa:ɡɪt
git/i=ipa:ɡɪt
```

A value without `ipa:` is spoken as replacement text.  A value beginning with
`ipa:` is emitted as an IPA pronunciation.  The plain form matches exact case;
the `/i` form ignores case.  All forms use whole-token boundaries, and an
exact-case rule takes precedence over `/i`.  Pronunciation rules take
precedence over the spell-out list.

The pronunciation tab has a manually opened IPA symbol toolbar.  It never
closes by itself, and its horizontal splitter lets the user resize the toolbar
relative to the editor.  Clicking a symbol inserts it at the saved caret
position.  The adjacent **Pronounce** button exists only on the Pronunciations
tab and speaks the token on the caret's current line using the first enabled
speech profile.  When the value starts with `ipa:`, that IPA is used.
Otherwise,
the value is spoken as a text alias.  An incomplete empty value such as `word=`
previews the token's standard pronunciation but cannot be saved as a rule.
Clicking a symbol still inserts the `ipa:` prefix after `=` when required.

IPA buttons are enabled only when the caret or selection starts in a valid
value position:

- the current line must contain `=`;
- the caret must be to the right of `=`;
- when `ipa:` already exists, the caret must be after its colon.

Hovering over or Tabbing to a key displays its information and starts a
one-second delayed preview.  Pressing Shift while the key is hovered or focused
displays the same information and plays it immediately.  Symbol information
remains readable for seven seconds, then the footer returns to its rotating
helper text.  The footer bolds the displayed symbol and every occurrence of the
selected phone cluster in the isolated and example IPA.  Combining marks include
their carrier phone, and tie bars include both tied phones.  No square brackets
are inserted for highlighting.

When an independent pronunciation can be constructed, the preview plays that
phone or modified-phone cluster, waits for the configured IPA example delay,
and then plays the example.  Real words fall back to ordinary word pronunciation
when their IPA is rejected.  Synthetic carriers are labelled `carrier` and are
skipped when rejected rather than being spoken as a misleading fake word.  The
Activity log states when either recovery occurs.

## Bluetooth audio wake

**Bluetooth wake...** configures an optional high-frequency prefix intended to
wake a power-saving Bluetooth audio connection before speech.  It contains:

- enable/disable;
- quiet duration;
- tone frequency;
- tone volume;
- tone play duration;
- connection settle duration;
- IPA phone/example delay;
- a wake-tone test button;
- a test-profile selector and **Test wake + phrase** button.

Before playback, the selected provider renders the complete utterance to PCM.
The worker converts it to mono 48 kHz 16-bit audio, prepends the generated tone
and configured settling silence when wake is required, and submits the complete
buffer to one WinMM `waveOut` stream.  The audio device therefore remains open
for the complete tone → silence → speech sequence.

IPA phone and example segments are also rendered into one buffer with the saved
delay inserted as PCM silence.  When Bluetooth wake is enabled, explicit IPA
and Pronounce previews always prepend the configured tone and settling silence,
regardless of the quiet threshold.  This gives short preview sounds the same
minimum lead-in duration as a wake test.  The tone-only and tone-plus-phrase
buttons also force the prefix.

The tone is best-effort.  A codec, driver, amplifier, or speaker may filter it
or reproduce it audibly.

The transport controls share one height.  Font glyphs are centred by their
visible outlines, and the speaker, shush, and clock controls use custom vector
drawings so they remain clear at toolbar size.

## Theme

The theme selector provides:

- `System`, which follows the Windows app light/dark preference;
- `Light`;
- `Dark`.

The main window updates when the Windows preference changes while `System` is
selected.  Dialogs use the effective theme when opened.

## Fenced code

**Spoken fenced-code types** is a CSV allow-list.  Entries are trimmed,
case-insensitive, de-duplicated, and applied one second after editing stops.
No Enter key or Apply button is required.

- `untyped` enables fences without a language tag.
- `*` enables all fence types.
- An empty list skips all fenced blocks.
- Aliases are explicit: list both `cpp` and `c++` when both are wanted.

Every non-empty accepted line in a non-`md` fenced block is one
rewind/forward entry, regardless of punctuation inside that line.  An
explicitly tagged `md` fence is parsed recursively as Markdown, so its headings,
paragraphs, list items, sentences, and nested fences use the same segmentation
rules as ordinary message text.  Fence runs may contain one or more backticks
or tildes.  A marker run with a same-line closing marker is inline code, not a
block fence, so text following an inline code span remains in spoken prose.
Activity logs both spoken and skipped blocks with the normalized fence type.

## Playback controls and local hotkeys

| Control | Hotkey | Action |
| --- | --- | --- |
| bubbles + `←` | `U` or `Alt+U` | Previous opposite-speaker run |
| `⏮` | `H` or `Alt+H` | Previous JSONL node |
| `⏪` | `J` or `Alt+J` | Previous sentence/code line |
| `▶` / `⏸` | `K` or `Alt+K` | Play/pause monitored playback |
| `⏩` | `L` or `Alt+L` | Next sentence/code line |
| `⏭` | `;` or `Alt+;` | Next JSONL node |
| bubbles + `→` | `O` or `Alt+O` | Next opposite-speaker run |
| clock | `'` or `Alt+'` | Speak processing time for selected turn |

Hotkeys work only while Agent Panel Speaker is active.  A hotkey focuses its
corresponding button before invoking it.  Bare hotkeys remain active while a
numeric or compact profile control has focus because the shortcuts are not
numeric.  Editable
text and editable drop-down fields retain bare keys; Alt variants remain
available from those controls.

Speaker navigation treats consecutive User fragments as one User run and
consecutive Reasoning/Assistant fragments as one AI run.  From AI it jumps to
the adjacent User run; from User it jumps to the first eligible fragment of the
adjacent AI run.  It never stops at later AI nodes belonging to the same run.

Forwarding past the final eligible entry cancels replay, returns to the live
end, and reports the corresponding sentence, node, or speaker-turn end message.
When monitoring is active, the live end remains armed for newly accepted text.

## Session selection

- Claude: `%CLAUDE_CONFIG_DIR%\projects\**\*.jsonl`, or
  `%USERPROFILE%\.claude\projects\**\*.jsonl`.
- Codex: `%CODEX_HOME%\sessions\**\*.jsonl`, or
  `%USERPROFILE%\.codex\sessions\**\*.jsonl`.

**Detect latest** selects the newest matching session.  **Browse JSONL** opens
at the selected source's session directory.  The full session title and path
are displayed separately.  Pausing and resuming do not change the pinned path.
Selecting a different source, detecting another latest session, or browsing to
another file changes that pin and clears queued speech from the previous
conversation.

**Auto-follow newest session** is the only mode that may switch to a newly
modified JSONL while monitoring.  Its checkmark remains at normal contrast
while monitoring, but the value cannot be changed until monitoring stops.  An
automatic switch starts at the new file's live end rather than replaying old
content.

Existing conversation text is indexed at start, so rewind is immediately
available.  **Speak complete latest turn on start** begins at the final User
node and then speaks every following Reasoning/Assistant node in order.  If no
User node exists, it falls back to the final currently eligible node.

## Settings

Settings automatically persist at:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\settings.json
```

Saved values include session choices, all voice profiles, fenced-code types,
spelled words, pronunciation rules, Bluetooth wake settings, theme,
transcript follow/highlight/fade/tracking/maximize settings, polling interval,
startup
playback, and window placement.

**Save settings** flushes pending fenced-code edits and saves immediately.
**Reset defaults** restores defaults.

## Build and run

Requirements: Windows 10 version 2004 or later, the .NET 10 SDK, and at
least one enabled Windows speech voice.  The rendered Transcript tab also uses
the Microsoft Edge WebView2 Runtime; its absence does not disable speech.  The
transcript colour editor uses the MIT-licensed Cyotek WinForms ColorPicker; see
`THIRD-PARTY-NOTICES.md`.

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

The bottom area contains the human-facing **Transcript** tab plus two diagnostic
tabs.  **Activity** is the timestamped operational log.  The other diagnostic
tab is labelled **Accepted Text** while inactive and **Recent Accepted JSONL**
while selected; it shows the monitor's bounded preview of recently accepted
nodes, not the utterance currently being spoken.  A manually scrolled
accepted-text view keeps its position across updates; a view at the end
continues following new nodes.

Conversation text is included in diagnostics.  Review logs before sharing.

## Possible future features

These directions are documented but are not implemented in version 42:

- an optional WebView2/Edge speech backend that first probes available local
  and online voices, boundary events, pause/resume behaviour, Windows-version
  behaviour, Edge policy, network use, privacy implications, and possible
  service cost;
- optional fenced-code syntax highlighting or prettification through an
  established JavaScript renderer such as Prism.js, highlight.js, or Shiki;
- Transcript gear options to read visible heading timestamps and record
  numbers.  Opaque sub-agent IDs remain unspoken unless a separate explicit
  option is added later.

## Heading transitions

Markdown headings remain in the spoken text.  A 250 ms synthesis pause is
inserted between a heading and the following prose without splitting the two
parts into separate audio streams.


## Voice backend display and Windows.Media bookmark timing

Voice dropdown labels now include the provider type (`System.Speech`,
`Windows.Media`, or `SAPI`).  Clicking the Voice heading rotates through the
provider type as an available primary sort field.

The **Windows.Media bookmarks** setting has three values:

- `Off`: use the `SpeechWord` timed metadata track and its source ranges.
- `Fallback`: use `SpeechWord` metadata when it produces a reliable complete
  schedule; otherwise use explicit SSML bookmark cues and equal-time
  compaction.
- `Always`: use explicit SSML bookmark cues and equal-time compaction for every
  Windows.Media fragment.

Bookmark timing uses one bookmark between each pair of display tokens.  The
first token starts at time zero, so a three-token sequence has two bookmarks.  Bookmark
boundaries are ordered by audio time and then by token index before equal-time
compaction.  This guarantees that a silent punctuation token and the following
spoken token remain in source order, so compaction retains the spoken token.
When a period is directly attached to a following token that begins with a
word character, such as the period in `filename.hpp`, the displayed token
remains `.` but its synthesis text is `dot`.  A sentence-ending period followed
by punctuation, whitespace, or the end of the fragment remains silent
punctuation.  The filename sequence is therefore passed to Windows.Media as
`filename | dot | hpp`, with the same three token identities and two
bookmarks.  This keeps the period audible and avoids a separate token-index
array merely to omit it.

The app reads all current cues, including bookmark cues, from
`SpeechSynthesisStream.TimedMetadataTracks`.  The older
`SpeechSynthesisStream.Markers` property is a separate legacy collection and is
deprecated by Microsoft.  Deprecation of `Markers` does not mean SSML
bookmarks are deprecated: a `SpeechBookmark` timed-metadata track is the modern
representation of SSML bookmark timing.

## v68 decimal-aware Windows.Media bookmark tokenization

Windows.Media bookmark timing now tokenizes decimal values as uninterrupted
display units before inserting SSML bookmarks.  The decimal-aware alternative
is evaluated before period and word alternatives:

```regex
(?<![\p{L}\p{M}\p{N}_.])\d*\.\d+(?!\.\d)(?=[fFlL]|\b)
```

Thus `1.25` and `1031.75` contain no internal bookmark, while `3.14f` is
represented as `3.14 | f`.  A token matching `^\.\d+$` retains its complete
display/highlight range but is synthesized as `point` followed by its digits;
`.5` is therefore highlighted as one token while spoken as “point five”.

The existing dotted-identifier behaviour remains: `PolicyMachinery.hpp` is
represented as `PolicyMachinery | . | hpp`, and the period's synthesis text is
`dot`.  Runs of periods are single tokens, and the prior apostrophe/hyphen word
handling is preserved.


## v80 transcript windowing corrections

Version 80 keeps the complete transcript and search index in C#, but renders
only five adjacent record regions in WebView2.  Estimated heights for unloaded
records provide the full-document scrollbar.  Rendered records report their
measured heights back to C#, improving spacer accuracy as the user moves
through the transcript.

The current region remains loaded with two regions above and two below.  When
the visible record enters an outer loaded region, the next region is loaded and
the opposite region remains until the view has crossed the central region.
Window replacement preserves the first visible record and its viewport offset.

Transcript loading and followed playback centre the virtual window directly on
the current voice record.  Starting a Find operation disables follow mode
before navigation.  A new search begins after the current selection, or after
the voice marker when there is no selection.

## v81 startup voice positioning

Version 81 loads indexed history into a paused playback position instead of
starting speech immediately.  That paused position is published before the
transcript loading overlay is removed, and the virtual transcript window is
centred on its exact record.  A second position check after the first window
render closes the race where history indexing completes during transcript DOM
replacement.
