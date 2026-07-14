# Agent Panel Speaker v9

Agent Panel Speaker reads text exposed by the Claude Code or Codex panel in
Visual Studio Code through Windows UI Automation and speaks new text through
installed Windows voices.

## Requirements

- Windows 11
- .NET 10 SDK
- Visual Studio Code and the agent panel running at the same privilege level
  as Agent Panel Speaker

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

1. Open the Claude Code or Codex panel and keep its transcript visible.
2. Select **Select transcript region**.
3. Drag around the transcript output area. Exclude the message-entry box and
   unrelated editor content.
4. Confirm that **Detected transcript tail** shows the correct content.
5. Select **Start**.

The inactivity timeout speaks an unfinished trailing fragment after the text
has remained unchanged for the configured period. Complete sentences ending
in `.`, `?`, or `!` are queued immediately.

## v9 changes

- Reads up to 64 meaningful transcript nodes instead of only three.
- Walks farther back through the selected TextPattern document, including
  nodes outside the currently visible portion of the selected region.
- Uses bounded normalized sentence history to suppress repeats when Electron
  replaces or re-creates accessibility nodes.
- Reconciles node identities separately for **Rewind sentence** and
  **Rewind node**.
- Joins text across accessibility-node boundaries before finding sentence
  endings, preventing fragments such as `In` from being spoken alone.
- Filters Codex/Claude tool cards such as `Ran ...`, `Running ...`,
  `Edited ...`, duration labels, clocks, and transient thinking statuses.
- Leaves per-monitor DPI scaling to WinForms instead of applying the suggested
  window rectangle a second time.
- Retains structured diagnostic logging under:

  `%LOCALAPPDATA%\AgentPanelSpeaker\Logs`

Diagnostic logs contain transcript text. Review them before sharing.

## Publish a self-contained executable

```powershell
.\publish-win-x64.cmd
```

The output is placed in `publish\win-x64`.
