# Transcript virtualization, identity, speech, and highlighting

This document concentrates on the runtime area most likely to produce subtle regressions: the relationship between canonical identity, virtualized DOM, speech navigation, and the playback highlight.

## One identity namespace

```mermaid
flowchart LR
  SRC[Provider source record]
  CE[Canonical event ID / source index]
  TN[TranscriptNodeIdentity]
  FR[SpeechFragment]
  RA[DOM record/source anchor]
  VR[Virtual record/unit]
  SR[Search result]
  PP[TranscriptPlaybackPosition]
  HL[Highlighted DOM range]

  SRC --> CE
  CE --> TN
  TN --> FR
  CE --> RA
  RA --> VR
  VR --> SR
  FR --> PP
  TN --> PP
  PP --> HL
  RA --> HL
```

The same canonical provenance must connect the speech fragment and DOM anchor. Text matching is allowed only inside that already-established identity scope; text itself is not the global identity.

## Structural presentation path

```mermaid
flowchart TB
  PT[Core presentation tree]
  TPDF[TranscriptPresentationDomFormatter]
  DN[TranscriptDomNode tree]
  LEAF[Markdown leaf fragments]
  JS[WebView document.createElement / append]
  DOM[Browser DOM]
  VH[Serialized equivalent used by C# search/identity infrastructure]

  PT --> TPDF
  TPDF --> DN
  TPDF --> LEAF
  DN --> JS
  LEAF --> JS
  JS --> DOM
  TPDF --> VH
```

Structural elements such as turns, reasoning disclosures, tools, and User Context are created as DOM objects rather than by splitting one large HTML string and repeatedly assigning `innerHTML`. Markdown parsing is restricted to leaf content.

## Virtual document today

`TranscriptVirtualDocument` currently groups structural units, stores an estimated/measured height for each unit, and selects a window around a focal canonical unit.

```mermaid
flowchart LR
  A[Canonical structural unit 0]
  B[Unit 1]
  C[Unit 2]
  D[Unit 3 focal]
  E[Unit 4]
  F[Unit 5]
  G[Unit 6]

  TOP[Top spacer = sum unloaded heights]
  WIN[Materialized virtual window]
  BOT[Bottom spacer = sum unloaded heights]

  A --> TOP
  B --> TOP
  C --> WIN
  D --> WIN
  E --> WIN
  F --> BOT
  G --> BOT
```

Current implementation details:

- regions contain 20 structural records;
- loaded radius is two regions around the focal region;
- HTML materialization is capped at 1,000,000 characters;
- measured heights replace estimates;
- Core-declared structural units and multi-record `<details>` ranges are kept atomic.

## Visibility-aware virtual window target

Rolled-back history adds a second dimension: **canonical order remains fixed while effective visible height can become zero**.

```mermaid
flowchart LR
  V1[Visible A\ncanonical index 10]
  H1[Superseded B\nindex 11\neffective height 0]
  H2[Superseded C\nindex 12\neffective height 0]
  H3[Superseded D\nindex 13\neffective height 0]
  V2[Visible E\nindex 14]
  V3[Visible F\nindex 15]

  V1 --> H1 --> H2 --> H3 --> V2 --> V3
```

A virtual-window algorithm must not stop simply because it has collected enough **canonical records**. If the nominal window contains only hidden historical records, it must continue until it has useful visible material, while still respecting the hard DOM/character cap.

### Height model

```mermaid
classDiagram
  class VirtualUnitLayout {
    +int canonicalIndex
    +string canonicalId
    +bool visible
    +double estimatedHeight
    +double measuredHeight
    +long measurementGeneration
    +double EffectiveHeight()
  }

  class LayoutGeneration {
    +long id
    +double viewportWidth
    +double dpiOrZoom
    +string fontMetricsKey
  }

  VirtualUnitLayout --> LayoutGeneration : measured under
```

Conceptually:

```text
naturalHeight = valid measured height for this layout generation
                OR current estimate

effectiveHeight = visible ? naturalHeight : 0
```

A width change can rewrap text, so a measured height from a previous width must not be silently reused. Resize/DPI/font/layout changes advance the layout generation or invalidate incompatible measurements. Until remeasured, the current estimate is used.

## Hidden runs and materialization

```mermaid
flowchart TB
  F[Focal visible unit]
  L[Walk left]
  R[Walk right]
  VP[Accumulate useful visible height]
  CAP[Respect DOM/character cap]
  INT[Selected canonical interval]
  MAT[Materialize interval]

  F --> L
  F --> R
  L --> VP
  R --> VP
  VP --> CAP
  CAP --> INT
  INT --> MAT
```

Normally the complete canonical interval between selected visible endpoints can be materialized, including hidden historical nodes, so toggling history is a CSS/visibility operation for already-loaded content. If an extreme hidden run would violate the hard materialization cap, hidden nodes may remain only in the retained presentation model until needed; this is **rematerialization**, not provider reparsing.

## Speech state model

```mermaid
stateDiagram-v2
  [*] --> PausedAtLiveEnd

  PausedAtLiveEnd --> PausedOnContent: history/navigation selects content
  PausedOnContent --> Speaking: Play
  Speaking --> PausedOnContent: Pause
  Speaking --> WaitingAtLiveEnd: natural completion at live end
  WaitingAtLiveEnd --> Speaking: new eligible live fragment
  PausedAtLiveEnd --> PausedOnContent: new eligible fragment while paused

  Speaking --> CancellingForNavigation: forward/rewind/visibility move
  CancellingForNavigation --> InterFragmentPause: destination fixed
  InterFragmentPause --> Speaking: pause elapsed

  PausedOnContent --> PausedOnContent: navigation changes canonical destination
```

The exact internal implementation has more states and pending operations, but the diagram shows the review-relevant behavior.

## Turning rolled-back history OFF while speaking

```mermaid
sequenceDiagram
  participant UI as Settings
  participant Hist as Canonical history/eligibility
  participant SS as SpeechService
  participant Eng as Speech engine
  participant TV as TranscriptView

  UI->>Hist: historical visibility OFF
  Hist-->>SS: current canonical unit is now ineligible
  SS->>SS: find next visible eligible index
  SS->>Eng: cancel current utterance
  Eng-->>SS: cancellation completed
  SS->>TV: move playback marker to destination
  SS->>SS: normal inter-fragment pause
  SS->>Eng: synthesize/speak destination
```

Important ordering constraints:

1. Destination is computed in the stable canonical namespace.
2. Cancellation must not erase or recompute that destination.
3. The marker moves to the destination rather than staying on hidden content.
4. The normal deliberate inter-fragment pause still occurs.
5. Playback resumes automatically only after that pause.

If paused on historical content, cancellation is unnecessary; move the paused cursor directly to the next visible eligible unit. If none exists, move to `PausedAtLiveEnd`.

## Playback/highlight sequence

```mermaid
sequenceDiagram
  participant SS as SpeechService
  participant Eng as SapiSpeechEngine
  participant MB as Playback mailbox / MainForm
  participant TV as TranscriptView
  participant WV as WebView2

  SS->>Eng: speak SpeechFragment(nodeId, text)
  Eng-->>SS: WordBoundary(text, character range, timing)
  SS->>SS: resolve TranscriptPlaybackPosition
  SS-->>MB: PlaybackPositionChanged
  MB->>TV: ShowPlaybackPosition(position)

  alt node is outside current virtual window and follow is enabled
    TV->>TV: render window containing canonical record/source identity
  end

  TV->>WV: post playback JSON message
  WV->>WV: resolve range inside mapped node/fragment
  WV->>WV: apply speaking or paused marker
```

The WebView does not decide which provider record corresponds to a speech fragment. That relationship must already have been established by canonical identity mapping.

## Find and virtualization

```mermaid
flowchart LR
  IDX[TranscriptSearchIndex over complete canonical/search inventory]
  HIT[Search hit with record/source coordinates]
  VDOC[TranscriptVirtualDocument]
  WIN[Window containing hit]
  DOM[WebView DOM]
  SEEK[Optional speech seek]

  IDX --> HIT
  HIT --> VDOC
  VDOC --> WIN
  WIN --> DOM
  HIT --> SEEK
```

Find must be able to locate content that is not currently materialized. A hit first identifies its canonical record/source coordinates, then virtualization brings the relevant window into the DOM. If a search mode excludes hidden historical content, that is an eligibility filter over the same canonical index, not a different parsed transcript.

## Review invariants

- Canonical IDs and ordered indexes survive visibility changes.
- Hidden units contribute zero effective spacer height.
- Height measurements are invalidated when layout geometry changes.
- Virtualization may rematerialize DOM but must never cause provider reparsing.
- Speech navigation uses eligibility over stable history rather than rebuilding history.
- Cancellation/restart preserves the selected canonical destination.
- Display, speech, search, and highlighting use the same source/node namespace.
