# Agent Panel Speaker v12

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

## v12 changes

- Rewind now restarts playback from the selected sentence or accessibility
  node and continues through every later history entry instead of speaking
  only the selected item.
- Adds **Forward sentence** and **Forward node** controls for correcting an
  over-rewind while preserving continuous playback.
- Serializes speech one history entry at a time so the application always
  knows the current navigation position.
- New live transcript fragments append to the active replay and are spoken
  after the historical entries catch up.

## v11 changes

- **Stop** now cancels current and queued speech before waiting for the
  monitoring thread to exit.
- Speech fragments already posted to the UI queue are tagged with their
  monitoring session and discarded after **Stop**, so they cannot restart
  the voice after cancellation.

## v10 changes

- Excludes text ranges that UI Automation explicitly marks as hidden.
- Treats `Ran ...`, `Edited ...`, shell headings, and their expandable child
  details as one tool block, while preserving the next agent narration.
- Excludes tool output without usable screen bounds unless it is recognizable
  agent narration recovered after rapid scrolling.
- Filters `Worked for ...` and `Context automatically compacted` status text.
- Suppresses repeats when Electron inserts or removes whitespace at an
  accessibility boundary.
- Reads up to 64 narration nodes while scanning up to 512 raw paragraphs.
- Retains **Rewind sentence**, **Rewind node**, per-monitor DPI handling, and
  structured diagnostic logging under:

  `%LOCALAPPDATA%\AgentPanelSpeaker\Logs`

Diagnostic logs contain transcript text. Review them before sharing.

## Publish a self-contained executable

```powershell
.\publish-win-x64.cmd
```

The output is placed in `publish\win-x64`.
