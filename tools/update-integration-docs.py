from pathlib import Path

README_HEADING = b"## AIConversationCore integration branch"
DESIGN_HEADING = b"## AIConversationCore canonical transcript boundary"

README_TEXT = """## AIConversationCore integration branch

`feature/aiconversationcore-integration` migrates Claude/Codex transcript semantics to the shared `AIConversationCore` canonical model.  AgentPanelSpeaker now consumes canonical projection data for speech extraction, transcript rendering, stable source identity, search/highlight mapping, and live append/reload behaviour.

Provider-specific conversation normalization is no longer implemented in AgentPanelSpeaker.  `JsonlRecordExtractor` is retained only as a lightweight Claude/Codex file-format detector for manual session selection; it does not classify conversational content.  Stable record identity is taken from canonical provenance, and the old `JsonlRecordIdentity` parser has been removed.

The runtime uses one persistent Node bridge (`tools/AIConversationCore-worker.mjs`) pinned to AIConversationCore commit `a6fd322aece692cd0c90bc89f11228b3a4e83520`.  AgentPanelSpeaker-specific responsibilities remain in C#: session discovery/tailing, speech policy and SAPI playback, WebView2 presentation, search/navigation, highlighting, and UI behaviour.

The pre-migration v212 parser was retained only long enough to establish and pass migration parity gates.  After those gates passed, the legacy semantic parser/parity harness was removed so future provider semantics have one owner: AIConversationCore.

"""

DESIGN_TEXT = """## AIConversationCore canonical transcript boundary

Claude and Codex provider semantics are owned by `AIConversationCore`, not by AgentPanelSpeaker.  Raw JSONL records are accumulated by `CanonicalSessionExtractor`, projected through the persistent `AIConversationCoreClient`/Node worker, and converted by `CanonicalProjectionExtractor` into the app-owned speech/navigation model.

The canonical provenance chain is:

`source JSONL record -> canonical source_record_id/source_index -> TranscriptNodeIdentity -> rendered record/source anchor -> search/speech/highlight coordinates`.

`TranscriptMarkdownFormatter` renders canonical Markdown and only adds AgentPanelSpeaker DOM anchoring required by virtualization/search.  `TranscriptNodeIdentityMap` derives identity from the same canonical projection used for speech, so display and speech no longer run independent provider parsers.  `JsonlRecordExtractor` now performs format detection only; the former semantic extraction implementation and `JsonlRecordIdentity` were removed after the v212 migration parity gate passed.

AgentPanelSpeaker continues to own application policy: session discovery and follow-latest selection, live file tailing, duplicate suppression, speech segmentation and role/fenced-code policy, SAPI timing/playback, WebView2 virtualization, Find/navigation, and highlight behaviour.

The bridge is intentionally pinned to AIConversationCore commit `a6fd322aece692cd0c90bc89f11228b3a4e83520`; both the C# client and Node worker reject a mismatched core revision.

"""


def newline_of(data: bytes) -> bytes:
  return b"\r\n" if b"\r\n" in data else b"\n"


def encoded(text: str, newline: bytes) -> bytes:
  return text.replace("\n", newline.decode("ascii")).encode("utf-8")


readme = Path("README.md")
data = readme.read_bytes()
if README_HEADING not in data:
  newline = newline_of(data)
  readme.write_bytes(encoded(README_TEXT, newline) + data)


design = Path("DESIGN.md")
data = design.read_bytes()
if DESIGN_HEADING not in data:
  newline = newline_of(data)
  first_break = data.find(newline)
  if first_break < 0:
    raise SystemExit("DESIGN.md has no title line ending")
  insert_at = first_break + len(newline)
  section = encoded("\n" + DESIGN_TEXT, newline)
  design.write_bytes(data[:insert_at] + section + data[insert_at:])
