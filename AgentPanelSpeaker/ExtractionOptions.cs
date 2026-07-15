namespace AgentPanelSpeaker;

/// <summary>
/// Describes one conversational text node extracted from a JSONL record.
/// </summary>
/// <param name="Kind">Source record/block kind.</param>
/// <param name="Category">Conversation role.</param>
/// <param name="Text">Raw Markdown text.</param>
/// <param name="Timestamp">Source timestamp when present.</param>
internal sealed record ExtractedNode(
  string Kind,
  ContentCategory Category,
  string Text,
  string? Timestamp);

/// <summary>
/// Carries extraction output and diagnostic classification for one record.
/// </summary>
internal sealed record ExtractionResult(
  IReadOnlyList<ExtractedNode> Nodes,
  string Decision,
  string RecordType,
  string PayloadType);
