# Agent Panel Speaker v17

Agent Panel Speaker reads text exposed by the Claude Code or Codex panel in
Visual Studio Code through Windows UI Automation and speaks new narration
through installed Windows voices.

## v17 fast tree traversal and level diagnostics

Version 17 removes the full descendant scan that version 16 performed for
**every** ancestor between the hovered paragraph and the VS Code window root.
That repeated scan was the reason selection could take roughly 30 seconds on a
large Codex transcript.

The selector now uses two passes:

1. The blocking pass reads only each ancestor's own properties and immediate
   children. It logs the text directly exposed at that level, runtime ID,
   native window handle, automation ID, class, control type, bounds, supported
   patterns, and the time spent on that level.
2. After the target has been selected and the application window is restored,
   a background diagnostic scans only the selected container's descendants.

The fast pass recognizes a `thread-scroll-container`-style class first, then
falls back to the nearest bounded ancestor that exposes `ScrollPattern`.

New diagnostic events include:

- `target.tree_level`: one shallow record per ancestor, with direct text and
  identifying properties;
- `target.tree_path`: one compact leaf-to-root summary for comparing levels;
- `target.selected_subtree_diagnostics`: the expensive text sample and metrics
  for the selected container only;
- per-level and total elapsed milliseconds, which identify a slow provider.

## v16 container selection correction

Version 16 uses the traversal diagnostics to reject transcript-content
ancestors whose bounds are taller than the owning VS Code window. Those
elements represent accumulated or virtualized message content rather than
the stable transcript viewport.

A scrollable ancestor can now qualify from its narration descendants even
when the accessibility tree exposes only one immediate content child. This
selects the stable `thread-scroll-container`-style viewport instead of one
large historical assistant-message subtree.

## v15 container selection

Version 15 removes rectangle selection.

1. Select **Select under pointer (3 s)**.
2. Move the pointer over a normal Claude or Codex narration paragraph.
3. The program starts at the UI Automation element beneath the pointer and
   walks upward through the raw accessibility tree.
4. It selects the smallest ancestor that behaves like a transcript container.
5. Monitoring retains that container and reads its current descendants on each
   poll. Individual paragraph and status nodes can be replaced without changing
   the selected container.
6. The container is reacquired from the VS Code window only if the retained
   element becomes unavailable.

The preview must contain several narration paragraphs from the agent panel and
must not contain editor or diff text. Reselect over a normal narration paragraph
if it does not.

## Tree traversal diagnostics

The diagnostic log records the complete selection traversal:

- the physical pointer coordinate;
- the hovered element and top-level window;
- every raw-view ancestor from the hovered leaf to the window root;
- runtime ID, control type, name, automation ID, class, framework, bounds,
  visibility, focusability, and supported patterns;
- immediate child summaries and direct text exposed at each level;
- per-level runtime ID, native window handle, automation ID, class, and bounds;
- scroll-pattern availability, candidate tier, score, and rejection reason;
- a detailed descendant-text scan for the selected container only;
- the selected container and selection reason;
- every attempted container reacquisition if the retained element disappears.

Logs are stored under:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\Logs
```

The logs contain transcript text. Review them before sharing.

## Requirements

- Windows 11
- .NET 10 SDK
- Visual Studio Code and Agent Panel Speaker running at the same privilege level

Visual Studio is not required.

## Build

From PowerShell in the extracted directory:

```powershell
.\build.cmd
```

Run it with:

```powershell
.\run.cmd
```

The project includes `NuGet.Config` and restores `System.Speech` from
NuGet.org.

## Use

1. Open the Claude Code or Codex panel and keep the transcript visible.
2. Select **Select under pointer (3 s)**.
3. During the delay, place the pointer over ordinary agent narration rather
   than a status, command card, user bubble, link, or inline code fragment.
4. Confirm that **Detected transcript tail** contains the correct transcript.
5. Select **Start**.

The inactivity timeout speaks an unfinished trailing fragment after the text
has remained unchanged for the configured period. Complete sentences ending in
`.`, `?`, or `!` are queued immediately.

## Existing controls

- **Stop** immediately stops monitoring and current speech.
- **Cancel speech** stops current and queued speech without stopping monitoring.
- **Rewind sentence** and **Forward sentence** move through spoken sentences.
- **Rewind node** and **Forward node** move through accessibility-node groups.
- Rewind and forward continue playing from the selected position.

## Publish a self-contained executable

```powershell
.\publish-win-x64.cmd
```

The output is placed in `publish\win-x64`.
