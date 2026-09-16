# AgentPanelSpeaker build/install notes

## Clone the repository with AIConversationCore

`AIConversationCore` is a Git submodule pinned by AgentPanelSpeaker to the exact core revision it was developed and tested against.

For a new clone, initialize the submodule during clone:

```bash
git clone --recurse-submodules https://github.com/Ma-XX-oN/AgentPanelSpeaker.NET.git
cd AgentPanelSpeaker.NET
```

If the repository was already cloned normally, initialize/update the submodule before building:

```bash
git submodule update --init --recursive
```

When switching branches or pulling changes that may move the pinned core revision, run:

```bash
git submodule update --init --recursive
```

The checked-out dependency should appear at:

```text
dependencies/AIConversationCore/
```

Do not replace it with a separately checked-out or newer AIConversationCore revision unless deliberately testing a dependency change.  The parent repository's Git submodule entry is the authoritative version pin.

## Build

AgentPanelSpeaker requires .NET 10 and Node.js.  Build with:

```bash
dotnet build AgentPanelSpeaker/AgentPanelSpeaker.csproj --configuration Release
```

The build copies the runtime files required from `dependencies/AIConversationCore` into the AgentPanelSpeaker output under:

```text
tools/AIConversationCore-runtime/
```

The deployed application therefore does not require Git or a separate AIConversationCore checkout at runtime.
