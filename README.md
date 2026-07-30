# Agent Panel Speaker v43

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

System.Speech voices provide exact synthesis word boundaries.  Native SAPI and
Windows.Media voices use duration-weighted word estimates, with the same
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

System.Speech supplies exact word-boundary times.  Native SAPI and
Windows.Media use duration-weighted word estimates.  If a provider returns no
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
