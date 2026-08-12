## v191 MainForm Tab/Shift+Tab activation diagnostics

> v191 adds diagnostics only for MainForm-level Tab and Shift+Tab traversal.
> It records focused/active controls, active tab stops, foreground and active
> HWNDs, MainForm Z-order rank, cursor position/monitor, the top-level window
> under the cursor, and immediate/deferred post-traversal state.  No intended
> keyboard traversal behavior changes are included.

## v190 tab-boundary focus retention and dark tab seam fix

> v190 fixes the shared popup traversal path that could close an exhausted
> parent popup with `returnFocus: false`, leaving `Form.ActiveForm` null when
> Tab/Shift+Tab ran past the outermost MainForm control. The shared controller
> now restores focus while unwinding popup levels and wraps at the containing
> form boundary. Dark-mode tab painting also explicitly covers the native
> bright page/header seam with the application dark surface and muted border.

## v189 centralized dark tab chrome

> v189 themes all application tab controls through `ThemedTabControl`: light
> text on dark tabs, a modestly lighter selected tab, and a muted border that
> replaces the bright native frame in dark mode. Light mode retains the native
> Windows tab rendering.

## v188 shared popup focus restoration fix

> v188 moves popup-close focus restoration into `HoverPopupController`: suppression of focus-triggered reopening is now strictly one-shot around the controller-owned `Focus()` call, and keyboard-closing the root popup restores focus to its anchor/owner instead of leaving no active application form.

## v187 native deactivation/Z-order isolation diagnostics

> v187: restores focus to a keyboard-active parent when a nested popup hover-closes, and logs every MainForm root Transcript Settings close request with an explicit reason.

- Adds logging only for the reported case where closing a popup appears to
  leave Agent Panel Speaker below another application window.
- Records foreground/active HWNDs, owning process, managed active form,
  MainForm/popup Z-order ranks, and all process top-level windows at
  MainForm deactivation, owner-deactivation evaluation, immediately before
  and after root-popup close, and one UI turn later.
- No popup activation, focus, close, or Z-order behaviour is intentionally
  changed in this version.

## v185 hover-close rollback and focus-open isolation diagnostics

- Reverts v184's keyboard-focus veto on mouse-leave closing, restoring the prior hover-close behaviour.
- Adds explicit diagnostics for anchor focus entry, focus-open suppression, queueing, deferred-open validation/abort reasons, and execution.
- No new focus/open behavioural workaround is introduced for the reported failure to reopen on Tab; the added logging is intended to isolate that path.

## v184 focused-popup hover-close fix

- A hover-close timer is no longer started while the deepest popup or its anchor still has keyboard focus.
- A close timer that was already pending rechecks keyboard focus before closing, so a focused popup cannot disappear merely because the pointer left it.
- v183 close-path diagnostics are retained.

## v183 popup-close isolation diagnostics

This version intentionally changes popup-close diagnostics rather than popup
focus behaviour. Every `CloseNode` path now records a concrete `closeReason`
and caller, and the delayed-close path logs the event that scheduled the timer,
the timer decision state, pointer/focus state, owner deactivation decisions,
and outside-pointer routing. This is intended to isolate exactly which path
removes focus or closes a nested popup before another focus change is made.

## v182 child-popup tab-exit focus restoration

- Tab/Shift+Tab past a child popup boundary now closes the child with focus
  restoration enabled before continuing traversal to the adjacent active parent
  control.
- This uses the existing close-path suppression that prevents restoring focus to
  the child anchor from immediately reopening the child.
- v181 close-path diagnostics are retained.

## v181 popup-close focus diagnostics

- Adds detailed diagnostics around nested popup hide, parent activation, target focus, and the next UI-message turn.
- Logs the controller path used when Tab/Shift+Tab exits a child popup into its parent.
- No focus behaviour is intentionally changed in this version.

## v180 popup focus restoration

Version 180 removes the focus-containment workarounds introduced in v178 and
v179.  When a child popup closes, its parent form is explicitly activated
before focus is restored to the anchor or advanced to the next/previous parent
control.  This keeps the established popup tab traversal while fixing the
missing parent-form activation at child close.

## v177 colour popup layout and placement

Version 179 keeps keyboard focus inside the deepest open popup: if a parent
popup receives Tab or Shift+Tab while one of its child popups is still open,
focus is redirected back to the child instead of traversing the parent.  The
colour editor is also compacted so the wheel height matches the RGB/HSL tabs
plus Alpha, the editor column is narrower, and the Cyotek editors fill their
final viewports so the right-side spinner controls are not cropped.


- Measures each Cyotek editor from an unconstrained probe size and the known controls in each RGB/Hex, HSL, and Alpha group.  It does not use Control.Visible while the popup Form is hidden, because WinForms reports hidden-form descendants as invisible.
- Removes the circular Dock=Fill/hidden-visibility measurement that collapsed the tab contents in v175/v176.
- Places the colour popup immediately above or below Transcript Settings when possible so the two owned windows do not overlap.
- Logs the measured colour-editor bounds and final popup placement for runtime verification.

## v176 popup ownership hierarchy and colour-layout measurement

Version 176 makes Transcript Settings a real owned popup Form.  The colour and
Advanced popups are owned directly by Transcript Settings, giving Windows an
explicit MainForm -> Transcript Settings -> nested popup ownership/Z-order
hierarchy.  The colour-tab sizing also removes the circular Dock=Fill
measurement: ColorEditor is first laid out using its own constructed height,
then the visible descendants are measured and the final tab row is fitted to
those measured controls.

## v175 popup Z-order diagnostics and compact colour tabs

Version 175 does not guess at another Z-order fix.  It adds native HWND
diagnostics around nested popup ownership/show/Z-order operations, including
managed and native owner handles, window styles, neighbouring Z-order handles,
foreground/active windows, top-level Z-order ranks, and all same-process
top-level windows before/after showing and after the UI settles.

The RGB/Hex and HSL tab area is also sized to the measured editor content
instead of consuming the remaining height forced by the colour wheel.

## v174 nested popup Z-order and tab-exit fix

- Nested colour/advanced popup forms explicitly remain above their owner.
- Leaving a nested popup by Tab/Shift+Tab resumes from the nested popup anchor in the parent popup, rather than re-entering the parent at its first control.

## v173 nested popup windows and re-entrant focus fix

Version 173 makes the transcript colour and advanced editors real borderless
owned popup windows.  They are positioned in screen working-area coordinates,
so they can extend beyond the transcript host without being clipped and remain
above the Transcript Settings overlay.  Focus-triggered popup opening is now
queued as a complete UI transition instead of synchronously creating a child
popup from another popup's focus event.  This preserves recursive forward and
backward tab traversal while preventing re-entrant popup creation during focus
changes.

## v171 colour popup sizing and sibling-popup stability

- Colour-popup sizing now measures visible descendants inside each Cyotek
  editor, not only the editor's immediate children. This includes nested
  alpha/HSL/RGB controls when determining the required popup height.
- Opening one nested popup now closes any open sibling popup first, matching
  the controller's documented mutually-exclusive sibling policy and cancelling
  stale deferred-focus work from the sibling.

## v170 colour-wheel alpha fix

- Fixes the colour picker failing to open after the v169 tabbed editor change.
- Converts WinForms `Color.A` (`0..255`) to the Cyotek `ColorWheel.Alpha` range (`0.0..1.0`) before synchronizing the wheel.

## v169 tabbed colour editor

- Transcript highlight colour popup now keeps the shared colour wheel on the left.
- RGB and six-digit Hex editing share one tab; HSL has its own tab.
- Alpha remains a shared editor below the tabs.
- Colour changes are synchronized across the wheel, both tab editors, Alpha, and the current-colour swatch.
- Popup sizing measures both tab pages plus Alpha so active controls are not clipped.

# Agent Panel Speaker v150


## v168 colour-editor content sizing

- Sizes the colour-picker popup from the actual DPI/font-scaled bounds of every
  visible Cyotek `ColorEditor` child instead of assuming its design-time height.
- Keeps RGB, Hex, HSL, and Alpha visible for evaluation and preserves their
  individual keyboard tab stops.
- Recalculates the popup size each time it opens before positioning it, so a
  focusable editor field cannot remain below the clipped client area.


## v150 master speech test buttons

- Adds six immediate test buttons below the Master Speech Profile.
- The order is Agent Main, Agent Context, Subagent Main, Subagent Context,
  User Main, and User Quote.
- Each test uses the selected role voice and profile after applying the current
  Master rate, pitch, and volume adjustments.

## v148 master speech and voice-state defaults

- Adds a Master speech profile using the same compact rate/pitch/volume editor.
- Master rate and pitch are additive offsets clipped to the supported range.
- Master volume scales each role volume, with 100 as neutral.
- Reset defaults choose the first, second, and third enumerated installed voices
  for Assistant, Subagent, and User, falling back cyclically when fewer voices
  are installed.
- Main defaults are rate 0, pitch 0, volume 100. Thoughts/Quote defaults are
  rate 0, pitch -10, volume 100.
- Voice selectors use a theme-aware caution-yellow background when set to
  Not Spoken while remaining enabled for reconfiguration.

## v146 click-only selective-save dropdown

- Changed the selective-save glyph to `▼` and narrowed its button.
- The selective-save popup now opens only from an explicit click on that
  dropdown button.  Hovering or tab-focusing the button does not open it.
- The click-only behaviour is configured through the shared popup controller,
  not through popup-specific event suppression.

## v144 dropdown affordance and inline close prompt

- Changed the selective-save split-button glyph from `›` to `⌄`.
- Rendered “Save changed settings before closing?” as one `LinkLabel`,
  with only “changed settings” linked, eliminating artificial word spacing.

## v143 changed-settings root and joined Save control

- Replaced the changed-settings popup's Select all/Select none buttons with a
  tri-state `All` root node.  Selecting the root selects or clears every changed
  setting, while partial selection is shown by the existing mixed state.
- Removed the visual gap between `Save settings` and its selective-save `›`
  disclosure button so they read as one split control.

## v142 advanced-popup value-label alignment

- Top-aligns the highlight-buffer value label in the existing slider row.
- Sizes the value column from the widest possible label (`16 positions`) so
  two-digit values are never clipped.
- Leaves the confirmed Ctrl+C shutdown behavior unchanged.


## v141 external termination and advanced-popup layout correction

- Bypasses the unsaved-settings prompt for console/task-manager and Windows shutdown close reasons.
- Replaces manual description-height padding with preferred-size layout based on the label's actual width.
- Vertically raises the highlight-buffer value label without increasing its row height.


## v140 shutdown, dialog spacing, and popup layout fixes

- Ctrl+C from the launching console is treated as external termination.  It
  bypasses the unsaved-settings prompt and does not save pending changes.
- The unsaved-settings sentence retains separate controls for popup anchoring,
  but removes their default margins so `changed settings` has normal sentence
  spacing.
- The Advanced Transcript Settings description now derives its height from the
  label's actual laid-out width and preferred size, with a full line of vertical
  safety space so the final line and `speech.` remain visible.
- Slider value wording remains unchanged.

## v138 hierarchical settings schema

- Settings are written using a versioned hierarchical schema.  Loading an older
  flat settings file migrates it in memory without marking settings as changed
  or forcing a save.  The new schema is written on the next genuine save.
- Speech settings store one shared voice for each of Assistant, Subagent, and
  User.  Main and Thoughts/Quote retain independent rate, pitch, and volume.
- The changed-settings tree mirrors the stored hierarchy, including Transcript
  highlight colour/timing groups, Bluetooth wake Tone/Timing groups, and Hotkey
  Navigation/Playback/Status announcements/Display groups.
- User quoted-text settings are labelled Quote.
- Obsolete Windows.Media highlight-timing persistence and UI wiring were
  removed.  Any legacy field is ignored and disappears on the next genuine
  save.
- Spelled words report only Added and Removed items.  Pronunciations report
  Added, Modified, and Removed items.


## v137 popup activation race fix

- Popup forms no longer close when their owner deactivates because focus moved
  into that owner's popup form.
- Popup initial focus is now performed only by the shared popup controller.
- Deferred focus retries verify the node generation, visibility, and active-leaf
  identity before acting, and retry the configured initial control after form
  activation settles.

## v136 popup-form focus activation

When keyboard or click interaction requests focus inside a popup hosted by its own
`Form`, the shared popup controller now activates that form before selecting the
popup's initial control.  Hover-only opening remains non-activating.

## v135 popup focus traversal

- Tabbing forward past the final control in a popup closes the popup and
  advances to the control after its opener.
- Shift+Tab before the first control closes the popup and moves to the control
  before its opener.
- Escape, Alt+F4, pointer-leave, and outside-click closure do not assign focus.
- Keyboard closure still suppresses hover reopening until the pointer leaves
  the opener.


## v133 hierarchical changed-settings tree

- Replaced the flat changed-settings checklist with a shared hierarchical tree.
- Speech profiles can be selected at category, role, or individual property level.
- Pronunciation changes are shown as ordered `Added`, `Modified`, then `Removed` entries.
- Group selection propagates to descendants and partially selected groups show a mixed state.
- The popup sizes to its expanded content up to the current monitor working area, using scrollbars only when the screen cannot contain it.

## v132 user-context preview phrase

The User Context profile preview now says “User quoted text speech is working.”
instead of incorrectly referring to user thoughts.  Agent and subagent Context
profiles continue to use their thoughts preview phrases.

## v131 context heading, close-dialog focus, and non-client popup dismissal

- Restored the shared speech-profile column heading from `Thoughts` to
  `Context`, since the User row represents quoted text rather than thoughts.
- The unsaved-settings dialog prevents its changed-settings link from receiving
  construction-time focus.  Cancel is selected first; tab order proceeds from
  Cancel to OK to the link.
- Dark-mode link labels now use a centralized high-contrast link palette.
- The centralized popup pointer router now treats non-client title-bar pointer
  presses as outside clicks, so clicking a parent dialog title bar collapses its
  active popup leaf.


## v104 live-end waiting and live transcript reveal

- Reaching the live end while monitoring no longer hides the end cursor.  The
  player remains unpaused, displays the flashing end marker, and waits for new
  fragments.
- The last located spoken position is retained across slow transcript refreshes.
  Once a newly appended record is rendered, its containing User, Thoughts, or
  user-facing details are expanded before the live-end marker is restored.
- Follow mode moves through the newly rendered content to the live end instead
  of losing the active node when speech finishes before rendering completes.

## v102 non-wrapping voiced seek and transcript-end marker

- Ctrl+Shift+Enter and the arrow-to-eye button search only later Find
  results for a voiced target; they do not wrap to the beginning.
- When no later voiced result exists, Find and speech navigation move to the
  blank position after the final transcript fragment.
- Explicit forward sentence, node, and speaker navigation use the same paused
  end state.
- The transcript-end marker is a blinking blank rectangle one em wide and
  approximately one capital-letter high.


## v101 search-corpus whitespace preservation

- Search corpus construction now preserves whether rendered tokens were adjacent or separated by whitespace.
- Punctuation is no longer separated from the preceding token by an invented space, so expressions such as `\d:` match timestamps and record headings correctly.
- Whitespace between rendered tokens is normalized to one space; block boundaries remain newlines and record boundaries remain separate regex inputs.


## v98 Regex record boundaries and smooth Find navigation

- Search corpora now preserve transcript-record boundaries with newlines instead
  of flattening the entire session into one line.
- Regex `.` no longer crosses into unrelated transcript records.  `^` and `$`
  operate per record through multiline regex semantics.
- A regex match maps and highlights the complete token range within its record.
- Find navigation uses smooth scrolling.  When virtualization must load another
  window, it keeps the current scroll position until the target window is ready
  and then smoothly scrolls to the result instead of pre-centring with a jump.

## v95 Find readiness and follow shortcut

- Find requests received before transcript indexing completes are retained and
  run when the index becomes available.
- Waiting for the index does not count toward the five-second active-search
  warning timer.
- The latest pending query replaces any earlier pending query.
- `=` and `Alt+=` are routed through the same WebView2 transport-key path as
  `K` and `Alt+K`.

A new search now resolves its no-selection origin from the authoritative C#
playback marker.  It no longer depends on the voice word being present in the
currently virtualized WebView window.



## v95 Single-owner monitor startup

- The selected session's paused history snapshot is retained by `MainForm`.
- Starting monitoring passes that exact snapshot to `JsonlSessionMonitor`.
- The monitor begins tailing at the current file end and does not parse or
  republish the existing session history a second time.
- The duplicate monitor `HistoryLoaded` callback is treated as an invariant
  violation and cannot restore a stale seek or pause active playback.
- Same-session monitor confirmation preserves the existing player state.

## v91 Preserve reused history when the monitor confirms the same session

When playback starts from already-indexed paused history, the monitor's initial
`SessionChanged` notification refers to the same JSONL file.  That notification
must not call `BeginLiveSession()`: doing so clears the reused history and
cancels the utterance that has just started.  The notification now compares the
confirmed path with the selected path and preserves speech state when they are
the same.  A genuinely different session still resets speech state.

## v90 Exact Find resume and non-destructive monitor startup

- Playback from a Find-selected word now starts from that word rather than the
  beginning of its fragment.
- The full transcript fragment remains authoritative while only the remaining
  suffix is synthesized, so later word boundaries retain full-fragment indices.
- Starting monitoring with pre-indexed paused history suppresses the monitor's
  initial historical fragment replay until `HistoryLoaded`; those duplicate
  fragments can no longer cancel or replace the active utterance.
- Captured SAPI boundaries continue to be emitted from the WinMM playback clock,
  not from synthesis-time callback timing.

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

## v95

- Find result counters now retain each match's absolute ordinal from the
  beginning of the file, even when the initial result list is rotated to start
  after the voice position.
- Removed the Follow Speech checkbox from Transcript Settings.  Follow remains
  controlled by the transcript overlay and the `=` / `Alt+=` shortcut.

## v98 regex block model

Transcript regex search now uses rendered structural blocks rather than one
flattened record string.  Each paragraph, list item, heading, whole fenced code
block, and disclosure summary is a block.  Collapsed Thoughts and tool/activity
content are indexed as though expanded and use the same internal block rules.

Blocks in one record are separated by `\n`, while records are searched as
independent inputs.  Consequently `.` cannot cross a block, `^` and `$` refer
to block boundaries under multiline regex semantics, an explicit `\n` can
match a block boundary, and no expression can cross a record boundary.

## v110 centralized hover/focus popups

All overlays that open from pointer hover or keyboard focus now use the same
`HoverPopupController`.  The controller owns the open and close timers and the
three shared states: closed, open awaiting popup entry, and open after entry.
It opens from delayed pointer hover or anchor focus, does not auto-close before
the popup has been entered, starts delayed closing only after leaving the
combined anchor/popup region, cancels that close on re-entry, and closes
immediately on the control's existing Escape path.  Transcript Settings, its
nested colour picker, and every speech-profile editor use this controller;
their former copied suppression flags and timer state machines were removed.


## v113 advanced transcript setting

The highlight-buffer capacity is no longer shown as an unexplained numeric row
in the compact Transcript Settings popup.  An `Advanced >` hover/focus control
opens a larger popup using the shared `HoverPopupController`.  The advanced
popup provides a visible explanation of the latency-versus-intermediate-motion
tradeoff and an accessible 1–16 slider.  A value of 1 is labelled as the
recommended latest-only behaviour.

## v114 advanced-description sizing

The Advanced Transcript Settings popup now measures the wrapped highlight-buffering explanation at the popup's actual content width and derives the description row and popup height from that result.  The explanation is no longer constrained to a fixed 82-pixel row.


## v116 compile correction

- `HoverPopupController.PopupHandle` can now be constructed by its owning
  controller without violating nested-type accessibility.
- The Transcript Settings root popup now supplies its root control directly to
  the popup tree; the removed `GetHoverRegionControls` API is no longer called.

## v115 centralized popup tree

All transcript-settings overlays now belong to one `HoverPopupController` tree.
The root settings popup, colour editor, and Advanced popup share one implementation
for hover/focus opening, delayed closing, sibling replacement, outside-click
membership, deepest-first Escape handling, and event cleanup.  MainForm no longer
maintains a type whitelist for transcript popup descendants.

## v117 compile correction

- Aligns `PopupState` accessibility with the internal `PopupNode.State` property.


## v118 centralized popup background focus

The shared `HoverPopupController` now owns background-click focus behaviour for
every hover/focus popup.  Clicking a popup background marks that popup entered,
cancels pending closure for it and its ancestors, and focuses that popup's
configured initial enabled control.  Clicking a specific focusable control still
uses the control's normal focus behaviour.

Transcript Settings, Highlight Colour, Advanced Transcript Settings, and all
speech-profile popups now supply only their initial-focus callback; their copied
background `MouseDown` handlers have been removed.

## v119 popup lifecycle preflight

All hover/focus popups now use the same `HoverPopupController` lifecycle and
one global coordination path.  The controller owns delayed opening and closing,
focus retention, popup-background activation, root mutual exclusion, nested
sibling replacement, outside-click dismissal, deepest-first Escape handling,
application-deactivation dismissal, and event cleanup.  The former profile
activation loop and recursively wired outside-click handlers were removed.

Opening an already open popup is idempotent and cannot demote its state.  A new
popup enters its awaiting-entry state before any show or focus callback runs,
and a failed show returns it to closed.  Clicking a popup background focuses
that popup's configured first enabled control; clicking a specific control
retains normal control focus behaviour.


## v120 popup focus diagnostics

Version 120 adds structured diagnostics around popup-background focus transfer.
The log records the clicked surface, popup node, active form, active and focused
controls before the focus request, immediately after it, and after the current
Windows message has completed.  This instrumentation is intended to identify
why a popup background click does not focus the configured initial control.


## v124 mouse-focus cue correction

Version 124 starts from v120.  After the shared popup controller moves focus
to a popup's initial control, it sends `WM_CHANGEUISTATE` with
`UIS_CLEAR | UISF_HIDEFOCUS` to the containing popup form.  This clears the
Windows UI-state flag that hides keyboard focus cues after mouse input while
leaving the actual focused control unchanged.  Existing popup focus
diagnostics remain enabled and now also record `popup.focus_cue_shown` or
`popup.focus_cue_unavailable`.


## v125 startup presentation

The main form remains transparent while its ordinary WinForms controls are
constructed, populated, themed, and laid out.  After the synchronous `Shown`
work completes, one deferred presentation pass performs the final layout and
reveals the completed window.  This prevents startup from visibly painting the
form a control at a time.


## v126 low-opacity startup paint

The main form now starts at 1/255 opacity rather than fully transparent.  It
performs a complete layout and child-control paint at that opacity, waits one
additional UI-message turn, and only then restores full opacity.  This keeps
the native child windows technically visible during their first paint while
preventing their progressive construction from being perceptible.

## v128 explicit settings persistence

Settings now use a saved snapshot and an in-memory working snapshot.  Changing a
control updates application behaviour immediately but does not write the setting
to disk.  **Save settings** is enabled only while the working snapshot differs
from the persisted snapshot.  The adjacent disclosure control opens a shared
hover/focus popup listing only changed settings; checked items can be saved
selectively.  Resetting defaults changes the working snapshot and becomes
persistent only after saving.

Closing with unsaved settings displays **Save changed settings before closing?**
with **Cancel** as the default action.  The **changed settings** link opens a
secondary popup containing only modified settings, rooted by a tri-state All
item that selects or clears every changed setting.  Pressing OK saves checked changes, discards unchecked changes,
and closes.  Cancel returns to the application.

### v128

- Centralizes popup leaf/ancestor click routing, one-second leaf leave closing,
  keyboard dismissal, and keyboard-close hover suppression.
- Makes the unsaved-settings dialog focus Cancel first, followed by OK and the
  changed-settings link.
- Applies the shared theme manager to the unsaved-settings dialog and changed-
  settings selector.
- Adds dirty-setting and close-prompt diagnostics.

### v129

- Fixed the centralized popup keyboard router so popup content implemented as a
  `UserControl` can use the same Escape, Alt+F4, and boundary-Tab handling as
  popup forms.  The router now accepts any `Control`, resolves its containing
  form for popup ownership, and retains the popup control as the tab-order
  boundary.


### v146

- Replaced the simulated save arrow button with a real ComboBox dropdown affordance.
- Deferred theme application until the theme dropdown closes.
- Added synchronous uncaught-exception diagnostics for UI, AppDomain, and task failures.

## v154

The Save, Reset, Hotkeys, and Diagnostic Log utility buttons now use the icon
artwork approved in the icon-size test harness.  The PNG alpha masks are tinted
with the current theme's foreground colour and scaled from their visible bounds,
so the same artwork remains usable in dark, light, and system themes.


## v155

- Increased the utility toolbar icon rendering size.
- Gave the keyboard icon extra width so it reads larger in the compact toolbar.
- Kept the approved Save, Reset, Hotkeys, and Diagnostic Log artwork.


## v156

- Fixed the Save/Reset settings-selection dialogs so their explanation and action
  button remain inside the client area.
- The dialogs now fit themselves to the current monitor working area.
- Fixed tree selection so clicking the custom checkbox/state-image area toggles
  the item instead of requiring a label click.


## v157

- Centralized light/dark WinForms palette application in `ThemeManager`.
- Tree selection-state artwork now comes from the active application theme instead
  of fixed system/visual-style colours.
- Removed duplicated popup palette recursion from speech/transcript popup controls.
- Track bars and TreeViews now receive their themed colours through `ThemeManager`.


## v158

- Made three-state tree checkboxes use the same Windows checkbox renderer as
  ordinary application checkboxes, with ThemeManager remaining the single owner
  of their state-image generation.
- Changed the settings-selection action row to auto-size so Save selected and
  Reset selected are not clipped.
- Removed the post-show unscaled 780 x 720 size reset; the autoscaled dialog size
  is now preserved and only constrained when it exceeds the monitor working area.
- Switched the selection dialogs from the cramped tool-window caption to the
  normal resizable dialog caption so the native close button has standard spacing.


## v159

- Matched the Save, Reset, Hotkeys, Diagnostic Log, and Bluetooth utility-button
  height to the seek/play transport buttons.
- Replaced the Bluetooth wake text button with an icon-only Bluetooth button and
  placed it immediately to the right of the Theme control.
- Moved JSONL polling interval controls to the top JSONL/session control row.
- Right-aligned the Theme-and-utility control group.
- Reduced and centred the themed three-state tree checkbox artwork so it fits
  without clipping.


## v160

- Matched the Detect latest and Browse JSONL button heights to the Source dropdown.
- Widened the Theme dropdown so System is not clipped.
- Matched the Pronunciations button height to the fenced-code-types textbox.


## v161

- Tab and Shift+Tab leaving a popup now continue through the popup anchor's
  parent control hierarchy, so focus advances to the next or previous control
  in the parent UI instead of stopping at the popup boundary.
- The Poll ms numeric control now matches the Source dropdown height.


## v162

- Omits the Natural voice sort field when no installed voice contains Natural
  metadata.
- Keeps disabled checkboxes and their labels readable in dark mode through the
  centralized ThemeManager disabled-state rendering.
- Refreshes dark-theme disabled foreground colours whenever controls are enabled
  or disabled during playback.


## v163

- Centralized the dark-theme disabled-button appearance in `ThemeManager`.
- Disabled standard buttons now keep a readable border and label in dark mode.
- Disabled `GlyphButton` icons now use the theme-managed disabled foreground instead
  of `SystemColors.GrayText`.


## v164

- Shift+Tab into a focus-opened popup now enters at the popup's last selectable
  control; Tab continues to enter at the first/initial control.


## v165

- Popup keyboard entry now targets the first active logical control for Tab and
  the last active logical control for Shift+Tab.
- If that boundary control opens a nested popup, traversal continues recursively
  in the same direction.
- Keyboard-entry focus is deferred by one UI turn at each popup level so nested
  popup traversal does not recursively re-enter popup creation/focus on the same
  synchronous event stack.


## v166

- Fixed tab traversal out of nested popup controls such as the transcript colour picker.
- A selectable composite control now counts as one logical active tab stop; its internal child controls are not also inserted into the popup boundary list.
- Tab from the colour picker's last logical active control now closes that popup and continues to the next logical active control in the parent popup; Shift+Tab does the symmetric operation from the first control.


## v167

- Expanded the transcript highlight colour popup so the complete Cyotek
  `ColorEditor` is visible instead of clipping its Hex, HSL, and Alpha controls.
- Restored recursive logical tab traversal through composite popup controls so
  the visible RGB/Hex/HSL/Alpha editor controls remain individually keyboard
  accessible rather than treating the entire colour editor as one tab stop.
- This version intentionally leaves all of the Cyotek colour editor fields
  visible so their usefulness can be evaluated before deciding whether to hide
  any of them.
