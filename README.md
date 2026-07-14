# Agent Panel Speaker

A Windows 11 application that watches a selected UI Automation subtree and
speaks newly displayed agent text through Windows SAPI voices.

The monitor keeps the previous and current transcript paragraphs as anchors.
It speaks through `.`, `?`, or `!` when the terminator is followed by
whitespace or the end of the paragraph.  It flushes any remaining unspoken
suffix after the configured unchanged-text timeout, which defaults to 1000 ms.

## Requirements

- Windows 11, x64.
- .NET 10 SDK to compile or run from source.
- An installed Windows SAPI voice.
- Internet access for the first NuGet restore.

Install the SDK from an Administrator PowerShell or Command Prompt:

```bat
winget install Microsoft.DotNet.SDK.10
```

Verify it:

```bat
dotnet --info
```

## Compile

From this directory:

```bat
build.cmd
```

The executable is written beneath:

```text
AgentPanelSpeaker\bin\Release\net10.0-windows\
```

## Run from source

```bat
run.cmd
```

## Publish a standalone x64 executable

```bat
publish-win-x64.cmd
```

The standalone files are written beneath:

```text
publish\win-x64\
```

The published build includes the .NET runtime, so that published copy does not
require a separate .NET installation on the machine that runs it.

## Select the transcript

1. Keep the Claude Code or Codex transcript visible in VS Code.
2. In Agent Panel Speaker, select **Select under pointer (3 s)**.
3. Move the mouse over text in the agent transcript and leave it there.
4. After the application returns, inspect **Detected transcript tail**.
5. Select **Parent** until the preview contains the last transcript blocks and
   continues to include a newly created paragraph.  **Back to child** reverses
   one parent step.
6. Select **Start**.

The application establishes a baseline when monitoring starts, so existing
text is not spoken by default.  Enable **Speak current paragraph on start** to
speak the current paragraph immediately.

## Selection guidance

The selected subtree must contain both the current transcript paragraph and
its next sibling.  Selecting one text leaf reads changes to that leaf but
cannot see a newly created paragraph.  Selecting all of VS Code also works in
principle, but it captures unrelated editor and interface text.  Use the
smallest parent whose preview reliably contains the transcript tail.

If the preview never shows transcript text, the panel is not exposing that
content through Windows UI Automation.  This version does not use OCR or
inject code into the VS Code extension.

## Operational details

- The application polls UI Automation because Electron panels do not
  consistently emit useful text-change events.
- The default polling interval is 200 ms.
- The default no-change timeout is 1000 ms.
- **Cancel speech** stops both current and queued speech.
- Run Agent Panel Speaker at the same elevation level as VS Code.  When VS Code
  is elevated, this application must also be elevated for reliable access.

## Transcript-tail selection

Version 4 reads visible `ControlType.Text` elements first and orders them by
their screen position.  It uses `TextPattern` only as a fallback.  This avoids
selecting a larger, stale text provider located above the visible bottom of a
virtualized transcript.  The detected-tail preview updates live after
monitoring starts.
