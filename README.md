# Agent Panel Speaker v25.6.9

## v25.6.9

- Pronunciation rules now accept spoken-text aliases as well as IPA.  For
  example, `TODO/i=to do` speaks every case variation of `TODO` as “to do”.
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
- The Pronunciations button is disabled while monitoring, speaking, or paused.

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
speaks user, assistant, reasoning, and completed Codex Plan text with
separate voice profiles.  Plans use the Assistant profile.  Tool calls, tool
results, command output, patches, diffs, and unrelated status records are
excluded.

See [DESIGN.md](DESIGN.md) for the internal architecture and invariants.

## Speech by content

Each content row has its own:

- voice (`Not Spoken` disables that category);
- rate (`-10..10`);
- pitch (`-10..10`);
- volume slider (`0..100` percent);
- test button for that content voice.

Changes remain available while monitoring and apply to the next sentence or
code line.  The active fragment is not restarted.  Test buttons are disabled
while speech is active or paused, so a test cannot interrupt it.

The voice lists combine every enabled voice exposed through `System.Speech`
with the native `SAPI.SpVoice` list.  A voice present in both lists uses
native SAPI.  Voices exposed only through `System.Speech` use equivalent SSML
for rate, pitch, spelling, and pronunciation markup.  Date and time patterns
are expanded into natural spoken forms before synthesis.

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

The transport controls use the same height as **Silence**.  Their visible glyph
outlines are centred rather than their font line boxes, so the play, pause,
stop, rewind, and forward symbols remain optically centred in the shorter
buttons.

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
or tildes.  Activity logs both spoken and skipped blocks with the normalized
fence type.

## Playback controls and local hotkeys

| Control | Hotkey | Action |
| --- | --- | --- |
| `⏮` | `H` or `Alt+H` | Previous JSONL node |
| `⏪` | `J` or `Alt+J` | Previous sentence/code line |
| `⏸` / `▶` | `I` or `Alt+I` | Pause or resume speech |
| `▶` / `⏹` | `K` or `Alt+K` | Start or stop monitoring |
| `⏩` | `L` or `Alt+L` | Next sentence/code line |
| `⏭` | `;` or `Alt+;` | Next JSONL node |
| Silence | `'` or `Alt+'` | Cancel speech; keep monitoring |

Hotkeys work only while Agent Panel Speaker is active.  A hotkey focuses its
corresponding button before invoking it.  Bare hotkeys remain active while
focus is in the main window's text boxes, numeric fields, and voice dropdowns.
The fenced-code CSV box is the only main-window exception so its text can be
edited normally.  Alt variants continue to work there.

Forwarding past the final eligible entry cancels replay, returns to the live
end, and reports either:

```text
Past end of last sentence/code line.
Past end of last JSONL node.
```

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

Saved values include session choices, all voice profiles, fenced-code types,
spelled words, pronunciation rules, Bluetooth wake settings, theme,
polling interval, startup playback, and window placement.

**Save settings** flushes pending fenced-code edits and saves immediately.
**Reset defaults** restores defaults.

## Build and run

Requirements: Windows 11, .NET 10 SDK, and at least one enabled Windows
speech voice.

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

## Heading transitions

Markdown headings remain in the spoken text.  A 250 ms synthesis pause is
inserted between a heading and the following prose without splitting the two
parts into separate audio streams.
