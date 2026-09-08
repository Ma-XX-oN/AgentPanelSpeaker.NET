# Current runtime architecture

This document describes the **current** `feature/aiconversationcore-integration` branch. It deliberately shows transitional duplication so it is not mistaken for the intended final architecture.

## Runtime component diagram

```mermaid
flowchart TB
  subgraph UI[AgentPanelSpeaker UI process]
    MF[MainForm]
    TV[TranscriptView]
    JM[JsonlSessionMonitor]
    SS[SpeechService]

    subgraph SpeechProjection[Speech / history projection path]
      CSE[CanonicalSessionExtractor]
      CPC1[AIConversationCoreClient A - monitor lifetime]
      CSP[CanonicalSpeechProjection]
      CPE[CanonicalProjectionExtractor]
    end

    subgraph DisplayProjection[Transcript DOM path]
      TPDF[TranscriptPresentationDomFormatter]
      CPC2[AIConversationCoreClient B - static]
      TVD[TranscriptVirtualDocument]
      TSI[TranscriptSearchIndex]
    end

    subgraph IdentityProjection[Speech-display identity reconstruction]
      TNIM[TranscriptNodeIdentityMap]
      CPC3[AIConversationCoreClient C - per Build]
    end
  end

  subgraph WorkerProcesses[Node worker processes]
    NW1[Worker process A]
    NW2[Worker process B]
    NW3[Worker process C]
    CR[Same bundled pinned AIConversationCore runtime]
  end

  JSONL[(Claude / Codex JSONL)]

  MF --> JM
  MF --> SS
  MF --> TV

  JSONL --> JM
  JM --> CSE
  CSE --> CPC1
  CPC1 --> NW1
  NW1 --> CR
  CR --> NW1
  NW1 --> CPC1
  CPC1 --> CSP
  CSP --> CPE
  CPE --> JM
  JM --> MF
  MF --> SS

  JSONL --> TPDF
  TV --> TPDF
  TPDF --> CPC2
  CPC2 --> NW2
  NW2 --> CR
  CR --> NW2
  NW2 --> CPC2
  TPDF --> TV
  TV --> TVD
  TV --> TSI

  JSONL --> TNIM
  TV --> TNIM
  TNIM --> CPC3
  CPC3 --> NW3
  NW3 --> CR
  CR --> NW3
  NW3 --> CPC3
  TNIM --> TV

  SS -->|PlaybackPositionChanged| MF
  MF -->|ShowPlaybackPosition| TV
```

Each `AIConversationCoreClient` owns its own Node process. The clients all load the same pinned bundled Core runtime, but they are **not** one shared worker process.

### What is good here

- Provider semantics are obtained from AIConversationCore rather than from a parallel Claude/Codex semantic parser in the app.
- Each `AIConversationCoreClient` keeps its own Node bridge process alive for the lifetime of that client and validates the pinned Core commit.
- `CanonicalProjectionExtractor` maps canonical events into app-owned speech/navigation structures rather than examining provider-native rollback/tool/message shapes.
- WebView playback positions carry the same app node IDs used by speech history.

### What is transitional / wrong for the final design

The branch currently has **multiple Core clients/projection entry points**:

1. `CanonicalSessionExtractor` owns a client for monitor/speech extraction.
2. `TranscriptPresentationDomFormatter` owns another static client for the transcript DOM.
3. `TranscriptNodeIdentityMap.Build()` creates another client for identity reconstruction.

Keeping each worker alive solved repeated **process startup within that client lifetime**, but `CanonicalSessionExtractor.Append()` still stores every valid JSONL line and calls `Project()` on the complete accumulated line set. The display and identity paths also reread/reproject the complete file independently.

That means the current implementation does not yet satisfy **parse once, project many**.

## Current class/dependency view

```mermaid
classDiagram
  class MainForm {
    -JsonlSessionMonitor monitor
    -SpeechService speech
    -TranscriptView transcriptView
    +Start/stop/navigation commands
    +Settings orchestration
  }

  class JsonlSessionMonitor {
    -CanonicalSessionExtractor canonicalExtractor
    +Start(MonitorSettings)
    +Stop()
    +LoadHistoryPreview()
    +TextReady
    +HistoryLoaded
    +SessionChanged
  }

  class CanonicalSessionExtractor {
    -AIConversationCoreClient client
    -List~string~ jsonLines
    +Prime()
    +Load()
    +Append()
  }

  class AIConversationCoreClient {
    -Process nodeWorker
    +Project(source, jsonLines, options)
    +Dispose()
  }

  class CanonicalSpeechProjection {
    +Prepare(projection)
  }

  class CanonicalProjectionExtractor {
    +ExtractRecord(projection, source, sourceIndex)
  }

  class SpeechService {
    -List~SpeechFragment~ history
    +LoadHistory()
    +SpeakLive()
    +Navigation operations
    +PlaybackPositionChanged
  }

  class TranscriptView {
    -TranscriptVirtualDocument virtualDocument
    -TranscriptSearchIndex searchIndex
    -List~TranscriptNodeIdentity~ identities
    +SelectSession()
    +ApplySettings()
    +ShowPlaybackPosition()
  }

  class TranscriptPresentationDomFormatter {
    -AIConversationCoreClient CoreClient
    +Format(path, source, pipeline)
  }

  class TranscriptNodeIdentityMap {
    +Build(path, source, includeRolledBackTurns)
  }

  class TranscriptVirtualDocument {
    -TranscriptVirtualRecord[] records
    -double[] heights
    +CreateWindow(focalIndex)
    +UpdateMeasuredHeights()
  }

  MainForm *-- JsonlSessionMonitor
  MainForm *-- SpeechService
  MainForm *-- TranscriptView
  JsonlSessionMonitor *-- CanonicalSessionExtractor
  CanonicalSessionExtractor *-- AIConversationCoreClient
  CanonicalSessionExtractor ..> CanonicalSpeechProjection
  CanonicalSessionExtractor ..> CanonicalProjectionExtractor
  JsonlSessionMonitor --> MainForm : TextReady / HistoryLoaded
  MainForm --> SpeechService : load / append speech history
  TranscriptView ..> TranscriptPresentationDomFormatter
  TranscriptView *-- TranscriptVirtualDocument
  TranscriptView ..> TranscriptNodeIdentityMap
  TranscriptPresentationDomFormatter *-- AIConversationCoreClient
  TranscriptNodeIdentityMap ..> AIConversationCoreClient : creates per Build
  SpeechService --> MainForm : PlaybackPositionChanged
  MainForm --> TranscriptView : ShowPlaybackPosition
```

## Initial-load sequence today

```mermaid
sequenceDiagram
  actor User
  participant MF as MainForm
  participant TV as TranscriptView
  participant JM as JsonlSessionMonitor
  participant CSE as CanonicalSessionExtractor
  participant C1 as CoreClient A
  participant DOM as TranscriptPresentationDomFormatter
  participant C2 as CoreClient B
  participant ID as TranscriptNodeIdentityMap
  participant C3 as CoreClient C
  participant N1 as Worker A
  participant N2 as Worker B
  participant N3 as Worker C
  participant SS as SpeechService

  User->>MF: Select session / start monitoring

  par Speech/history preparation
    MF->>JM: LoadHistoryPreview or Start
    JM->>CSE: Load complete JSONL
    CSE->>C1: Project(all records)
    C1->>N1: project request
    N1-->>C1: canonical projection
    C1-->>CSE: projection
    CSE-->>JM: ExtractionResult per source record
    JM-->>MF: SpeechHistorySnapshot
    MF->>SS: LoadHistory(snapshot)
  and Transcript display preparation
    MF->>TV: SelectSession(path)
    TV->>DOM: Format(path)
    DOM->>C2: Project(all records)
    C2->>N2: project request
    N2-->>C2: canonical presentation
    DOM-->>TV: DOM model + serialized search HTML
    TV->>ID: Build(path)
    ID->>C3: Project(all records)
    C3->>N3: project request
    N3-->>C3: canonical projection
    ID-->>TV: node/source identity map
  end

  TV->>TV: Build search index / virtual document
  TV->>TV: Construct WebView DOM
```

The three projection calls are the central architectural problem addressed by AgentPanelSpeaker #35 / AIConversationCore #76.

## Live append sequence today

```mermaid
sequenceDiagram
  participant File as JSONL file
  participant JM as JsonlSessionMonitor
  participant CSE as CanonicalSessionExtractor
  participant CC as CoreClient A
  participant NW as Worker A
  participant SS as SpeechService
  participant TV as TranscriptView

  File-->>JM: one new complete JSONL record
  JM->>CSE: Append(new line)
  CSE->>CSE: append line to retained raw-line list
  CSE->>CC: Project(ALL accumulated lines)
  CC->>NW: project complete record array
  NW-->>CC: complete canonical projection
  CC-->>CSE: projection
  CSE-->>JM: extraction for newest source index
  JM-->>SS: new SpeechFragment(s) via MainForm

  Note over TV: File refresh independently detects the file change
  TV->>TV: reread/reproject transcript display + identity paths
```

The app currently avoids republishing old speech fragments, but Core still processes the unchanged prefix again. The target architecture removes that distinction by retaining canonical session state itself.

## Important source files

- `AgentPanelSpeaker/MainForm.cs` — top-level UI/application orchestration.
- `AgentPanelSpeaker/JsonlSessionMonitor.cs` — session selection, initial history, file tailing, duplicate suppression.
- `AgentPanelSpeaker/CanonicalSessionExtractor.cs` — current accumulated-record Core projection seam.
- `AgentPanelSpeaker/AIConversationCoreClient.cs` — Node process transport and Core contract validation; one process per client instance.
- `tools/AIConversationCore-worker.mjs` — Node bridge into the bundled Core runtime.
- `AgentPanelSpeaker/CanonicalProjectionExtractor.cs` — canonical events -> app-owned speech/timing data.
- `AgentPanelSpeaker/TranscriptPresentationDomFormatter.cs` — current Core presentation tree -> DOM-object model.
- `AgentPanelSpeaker/TranscriptNodeIdentityMap.cs` — current independent identity reconstruction.
- `AgentPanelSpeaker/TranscriptVirtualDocument.cs` — virtualized transcript structural units and spacer accounting.
- `AgentPanelSpeaker/SpeechService.cs` — retained navigation history, playback state, cancellation/restart, live-end behavior.
