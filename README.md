# Agent Panel Speaker v25.6.1

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

### IPA pronunciations

Enter one rule per line:

```text
git=ipa:ɡɪt
git/i=ipa:ɡɪt
```

The first form matches exact case.  The `/i` form ignores case.  Both forms use
whole-token boundaries, and an exact-case rule takes precedence over `/i`.
Pronunciation rules take precedence over the spell-out list.

The pronunciation tab has a manually opened IPA symbol toolbar.  It never
closes by itself, and its horizontal splitter lets the user resize the toolbar
relative to the editor.  Clicking a symbol inserts it at the saved caret
position.  The adjacent **Pronounce** button exists only on the Pronunciations
tab and speaks the token on the caret's current line using the first enabled
speech profile.  When the value starts with `ipa:`, that IPA is used.  When the
prefix is absent, including an empty value such as `word=`, the token is spoken
with the voice's standard pronunciation.  Clicking a symbol still inserts the
`ipa:` prefix after `=` when required.

IPA buttons are enabled only when the caret or selection starts in a valid
value position:

- the current line must contain `=`;
- the caret must be to the right of `=`;
- when `ipa:` already exists, the caret must be after its colon.

Hovering for one second previews a symbol.  Holding Shift while entering a
button previews immediately.  The footer states both controls when they fit;
otherwise it alternates them at a readable interval until the pointer enters
an enabled symbol button.  The footer then shows the example with the active
phone bracketed, such as:

```text
æ → cat → /k[æ]t/ → middle
```

When a phone can be synthesized independently, the preview plays the phone,
waits for the configured IPA example delay, and then plays the example word.
Modifiers that cannot stand alone play only the example.

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

Every non-empty accepted code line is one rewind/forward entry.  Activity logs
both spoken and skipped blocks with the normalized fence type.

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
spelled words, IPA pronunciation rules, Bluetooth wake settings, theme,
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
