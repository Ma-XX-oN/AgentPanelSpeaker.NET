namespace AgentPanelSpeaker;

/// <summary>
/// Selects which assistant-authored JSONL content is spoken.
/// </summary>
/// <param name="SpeakMessages">Speak assistant narration and final messages.</param>
/// <param name="SpeakReasoning">Speak reasoning/thinking records.</param>
/// <param name="SkipFencedCode">Remove fenced code blocks from spoken text.</param>
internal sealed record ExtractionOptions(
  bool SpeakMessages,
  bool SpeakReasoning,
  bool SkipFencedCode);

/// <summary>
/// Describes one assistant-authored text node extracted from a JSONL record.
/// </summary>
/// <param name="Kind">Source record/block kind.</param>
/// <param name="Text">Raw assistant text.</param>
/// <param name="Timestamp">Source timestamp when present.</param>
internal sealed record ExtractedNode(
  string Kind,
  string Text,
  string? Timestamp);

/// <summary>
/// Carries extraction output and diagnostic classification for one record.
/// </summary>
/// <param name="Nodes">Assistant text nodes accepted for speech.</param>
/// <param name="Decision">Diagnostic classification.</param>
/// <param name="RecordType">Top-level record type.</param>
/// <param name="PayloadType">Nested payload type when present.</param>
internal sealed record ExtractionResult(
  IReadOnlyList<ExtractedNode> Nodes,
  string Decision,
  string RecordType,
  string PayloadType);
