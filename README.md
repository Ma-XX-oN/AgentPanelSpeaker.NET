# Agent Panel Speaker

Agent Panel Speaker reads newly exposed text from a selected Claude Code or
Codex panel region in Visual Studio Code and speaks it with an installed
Windows voice.

## Version 5 design

The application no longer stores a selected accessibility text node or one of
its parents.  Electron replaces and virtualizes those nodes while the agent is
streaming, so retaining them loses later paragraphs.

Instead, the user drags a rectangle around the transcript output area.  The
application stores:

- the owning top-level VS Code window;
- the owning process identifier;
- the selected rectangle relative to that window.

On every poll it reacquires the current UI Automation tree from the window and
reads the bottom of the selected region.  Moving or resizing VS Code updates
the absolute screen rectangle automatically.

The reader prefers `TextPattern` paragraph ranges.  It falls back to currently
visible `ControlType.Text` fragments and reconstructs visual lines.  It ignores
UI chrome and one-word transient status lines such as `Considering...`,
`Creating...`, and `Baking...`.

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

## Publish a standalone executable

```bat
publish-win-x64.cmd
```

The result is written to `publish\win-x64` and contains the .NET runtime.
