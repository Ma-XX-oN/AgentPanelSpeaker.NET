# Agent Panel Speaker

Agent Panel Speaker reads newly exposed text from a selected Claude Code or
Codex panel region in Visual Studio Code and speaks it with an installed
Windows voice.

## Version 8 changes

- Writes a structured JSON Lines diagnostic log for:
  - selected window and transcript region;
  - UI Automation provider selection;
  - every changed transcript tail;
  - tracker anchors, decisions, and emitted fragments;
  - speech queued by the application;
  - monitor failures;
  - main-window DPI, screen, move, and resize events.
- Adds **Open diagnostic log**.
- Applies the DPI-suggested window bounds when moving between monitors.
- Uses DPI-based form autoscaling.
- Ignores one-word agent status labels even when UI Automation appends an icon,
  including `Thinking`, `Considering`, `Creating`, and `Baking`.
- Ignores `Working for ...` duration labels.
- When virtual scrolling removes both stored paragraph anchors, the tracker now
  rebinds without replaying the visible tail.  This prevents a known source of
  repeated speech.

## Region-based monitoring

The application does not retain an accessibility text node or one of its
parents.  Electron replaces and virtualizes those nodes while the agent is
streaming.

Instead, the user drags a rectangle around the transcript output area.  The
application stores:

- the owning top-level Visual Studio Code window;
- the owning process identifier;
- the selected rectangle relative to that window.

On every poll it reacquires the current UI Automation tree from the window and
reads the bottom of the selected region.  Moving or resizing Visual Studio Code
updates the absolute screen rectangle automatically.

The reader prefers `TextPattern` paragraph ranges.  It falls back to currently
visible `ControlType.Text` fragments and reconstructs visual lines.

## Requirements

- Windows 11
- .NET 10 SDK
- Visual Studio Code running at the same privilege level as this application
- Internet access during the first restore of the `System.Speech` package

Visual Studio is not required.

## Build

From the extracted directory:

```bat
dotnet restore AgentPanelSpeaker\AgentPanelSpeaker.csproj ^
  --configfile NuGet.Config

dotnet build AgentPanelSpeaker\AgentPanelSpeaker.csproj ^
  -c Release ^
  --no-restore
```

Or run:

```bat
build.cmd
```

## Run

```bat
run.cmd
```

## Select the transcript

1. Keep the Claude Code or Codex panel visible.
2. Select **Select transcript region**.
3. Drag around the transcript output area.  Include the prose output but omit
   the panel tabs, prompt input box, and side editor.
4. Check **Detected transcript tail**.
5. Select **Start**.

The preview updates while monitoring.  Press Escape while selecting to cancel.

## Speech behaviour

- Complete sentences ending in `.`, `?`, or `!` are spoken immediately.
- An unfinished suffix is spoken after the configured idle timeout.
- Only unspoken suffixes are queued.
- The previous and current meaningful paragraphs are retained to reconcile
  virtual scrolling.
- A completely lost anchor is rebased silently rather than replayed.

## Diagnostic log

The application displays the active log path in **Activity** at startup.
Select **Open diagnostic log** to open File Explorer with the file selected.
The default directory is:

```text
%LOCALAPPDATA%\AgentPanelSpeaker\Logs
```

The file name includes the start time and process ID.  It uses `.jsonl`: one
JSON object per line.

To diagnose a repeat or monitor-switch problem:

1. Start the application.
2. Reproduce the issue once.
3. Stop monitoring.
4. Select **Open diagnostic log**.
5. Send the newest `.jsonl` file.

The diagnostic log contains transcript text that was visible in the selected
region.  Review it before sharing it when the transcript is sensitive.

## Publish a standalone executable

```bat
publish-win-x64.cmd
```

The result is written to `publish\win-x64` and contains the .NET runtime.
