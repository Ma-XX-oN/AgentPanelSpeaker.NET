using System.Text.Json.Serialization;

namespace AgentPanelSpeaker;

/// <summary>
/// One AIConversationCore-rendered HTML unit supplied to interactive consumers.
/// </summary>
internal sealed record CanonicalHtmlUnitProjection(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("kind")] string Kind,
  [property: JsonPropertyName("atomic")] bool Atomic,
  [property: JsonPropertyName("source")]
    CanonicalHtmlSourceProjection[] Source,
  [property: JsonPropertyName("html")] string Html);

/// <summary>
/// Canonical source identity carried by one Core-rendered HTML unit.
/// </summary>
internal sealed record CanonicalHtmlSourceProjection(
  [property: JsonPropertyName("event_id")] string EventId,
  [property: JsonPropertyName("provider")] string? Provider,
  [property: JsonPropertyName("record_id")] string? RecordId,
  [property: JsonPropertyName("record_index")] int? RecordIndex,
  [property: JsonPropertyName("block_indexes")] int[]? BlockIndexes);
