# Target Core boundary: parse once, project many

This document is the **target** architecture tracked by AgentPanelSpeaker.NET #35 and AIConversationCore #76. It is not a claim that PR #15 already implements every part shown here.

## Ownership model

```mermaid
flowchart LR
  subgraph Input[Provider input]
    CJSON[Claude JSONL]
    XJSON[Codex JSONL]
  end

  subgraph Core[AIConversationCore]
    Parse[Provider parser / normalizer]
    Session[Retained canonical session]
    Revision[Canonical revision state]
    Projection[Projection engine]
    PT[Presentation tree]
    MD[Canonical Markdown]
    HTML[Canonical HTML semantics]
    SpeechMeta[Speech eligibility metadata]
  end

  subgraph App[AgentPanelSpeaker]
    Discover[Session discovery]
    Tail[Live tail reader]
    ViewPolicy[UI visibility settings]
    SpeechPolicy[Voice / fence / navigation policy]
    VDoc[Virtual document]
    Web[WebView2]
    Speech[SpeechService]
  end

  CJSON --> Discover
  XJSON --> Discover
  Discover --> Parse
  Tail -->|new records only| Session
  Parse --> Session
  Session --> Revision
  Revision --> Projection
  ViewPolicy --> Projection
  Projection --> PT
  Projection --> MD
  Projection --> HTML
  Projection --> SpeechMeta

  PT --> VDoc
  HTML --> VDoc
  VDoc --> Web
  SpeechMeta --> Speech
  SpeechPolicy --> Speech
```

### Ownership rules

**Core owns:**

- provider-native parsing and normalization;
- rollback/edit/superseded/aborted semantics;
- complete canonical source/event identity;
- canonical presentation structure;
- semantic Markdown and HTML rendering;
- provider-derived speech eligibility facts such as User/IDE context identity.

**AgentPanelSpeaker owns:**

- locating and following files;
- deciding when a newly appended source record is available;
- user settings and application presentation policy;
- virtual-window materialization;
- WebView2 transport and DOM lifecycle;
- speech voice/profile/fence policy;
- navigation, pause/resume, audio playback, and highlight behavior.

AgentPanelSpeaker may filter or present canonical facts. It must not rediscover what a provider record *means*.

## Retained session object

```mermaid
classDiagram
  class CanonicalConversationSession {
    +Provider provider
    +CanonicalEvent[] events
    +SourceRecordState[] sourceRecords
    +RevisionState revisionState
    +Append(records)
    +Project(options)
  }

  class CanonicalEvent {
    +string id
    +int source_index
    +string source_record_id
    +string revision_status
    +string execution_status
    +CanonicalBlock[] blocks
  }

  class ProjectionOptions {
    +bool includeRolledBackTurns
    +bool includeUserContext
    +PresentationTheme theme
  }

  class CanonicalProjection {
    +CanonicalEvent[] events
    +PresentationTree presentation
    +string markdown
    +HtmlSemantics html
  }

  class PresentationTree {
    +PresentationTurn[] turns
  }

  CanonicalConversationSession *-- CanonicalEvent
  CanonicalConversationSession --> ProjectionOptions : accepts repeatedly
  CanonicalConversationSession --> CanonicalProjection : creates without reparse
  CanonicalProjection *-- PresentationTree
```

The central invariant is that `Project(options)` changes presentation/eligibility metadata but **does not create a new canonical identity namespace**.

## Initial load

```mermaid
sequenceDiagram
  participant APS as AgentPanelSpeaker
  participant Core as CanonicalConversationSession
  participant Parser as Provider normalizer
  participant Proj as Core projection engine

  APS->>Core: Create(provider, initial records)
  Core->>Parser: normalize initial records once
  Parser-->>Core: complete canonical events + revision state
  APS->>Core: Project(history OFF, user context OFF)
  Core->>Proj: project retained canonical state
  Proj-->>APS: presentation + speech metadata + canonical IDs

  APS->>Core: Project(history ON)
  Core->>Proj: same canonical state, different visibility
  Proj-->>APS: same IDs/order, changed visibility only
```

## Live append

```mermaid
sequenceDiagram
  participant File as Session JSONL
  participant APS as JsonlSessionMonitor
  participant Core as CanonicalConversationSession
  participant Parser as Provider normalizer
  participant Proj as Core projection engine

  File-->>APS: appended record N
  APS->>Core: Append(record N)
  Core->>Parser: normalize record N only
  Parser-->>Core: canonical delta
  Core->>Core: update retained revision/canonical state
  APS->>Core: Project(current options)
  Core->>Proj: project retained state
  Proj-->>APS: updated projection

  Note over Core,Parser: Records 0..N-1 are not reparsed
```

Rollback records can modify the **status** of earlier canonical events, but their IDs and source indexes remain stable.

## Rolled-back history visibility

```mermaid
flowchart TB
  C[Complete canonical revision inventory]
  O[Original revision]
  S[Superseded revision]
  E[Edited/active revision]

  C --> O
  C --> S
  C --> E

  O -->|history OFF| OH[visible=false]
  S -->|history OFF| SH[visible=false]
  E -->|history OFF| EV[visible=true]

  O -->|history ON| OV[visible=true]
  S -->|history ON| SV[visible=true]
  E -->|history ON| EV2[visible=true]
```

The setting changes a visibility/eligibility projection. It does **not** remove historical events from the canonical inventory and must not cause a provider reparse.

## Display implementation

Core should expose revision status on canonical presentation nodes. AgentPanelSpeaker can map that to stable DOM attributes/classes:

```html
<section
  class="transcript-turn revision-superseded"
  data-revision-status="superseded"
  data-canonical-id="...">
```

The checkbox then changes root presentation state or a visibility mask. For already materialized DOM, CSS can show/hide the same nodes. For nodes outside the virtual window, the same visibility mask guides window materialization.

## Why identity must not depend on visibility

```mermaid
flowchart LR
  ID[Canonical source/event ID]
  IDX[Canonical ordered index]
  NODE[Speech node identity]
  DOM[DOM record/source anchor]
  FIND[Search coordinates]
  PLAY[Playback/highlight coordinates]

  ID --> IDX
  ID --> NODE
  ID --> DOM
  NODE --> PLAY
  DOM --> FIND
  DOM --> PLAY

  VIS[Visibility setting] -.-> DOM
  VIS -.-> FIND
  VIS -.-> PLAY
```

`Visibility setting` influences eligibility/presentation but **never feeds back into ID generation**.

## Setting change while speaking historical content

When `Show rolled-back Codex history` changes from ON to OFF:

```mermaid
sequenceDiagram
  actor User
  participant UI as Transcript settings
  participant Core as Retained Core session
  participant Speech as SpeechService
  participant DOM as TranscriptView/WebView

  User->>UI: uncheck Show rolled-back history
  UI->>Core: Project(history OFF)
  Core-->>UI: same canonical IDs, historical units now hidden
  UI->>DOM: apply visibility mask

  alt current speech unit remains visible
    UI->>Speech: eligibility changed; current index still valid
    Speech-->>Speech: continue normally
  else current speech unit is now hidden
    UI->>Speech: hide current historical unit
    Speech->>Speech: cancel current utterance
    Speech->>Speech: find next visible eligible canonical unit
    Speech->>DOM: move marker to destination
    Speech->>Speech: apply normal inter-fragment pause
    Speech->>Speech: resume automatically
  end
```

If paused on a historical unit, move directly to the next visible eligible unit; do not preserve a hidden paused cursor. If no visible eligible unit follows, enter the appropriate paused/live-end state.

## Cross-repository responsibility

AIConversationCore #76 must provide the retained canonical session/project-many semantics first. AgentPanelSpeaker #35 should consume that API rather than implementing its own provider-aware rollback cache.
