using System.Text.Json;
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
  [property: JsonPropertyName("html")] string Html,
  [property: JsonPropertyName("speech_words")]
    CanonicalSpeechWordProjection[]? SpeechWords = null);

/// <summary>
/// One Core-owned transcript-global word handle embedded in canonical HTML.
/// </summary>
internal sealed record CanonicalSpeechWordProjection(
  [property: JsonPropertyName("id")] long Id,
  [property: JsonPropertyName("text")] string Text,
  [property: JsonPropertyName("separator_before")] string SeparatorBefore,
  [property: JsonPropertyName("provenance")]
    CanonicalSpeechWordProvenanceProjection? Provenance);

/// <summary>
/// Core-owned semantic provenance for one canonical transcript word.
/// </summary>
internal sealed record CanonicalSpeechWordProvenanceProjection(
  [property: JsonPropertyName("presentation_id")] string? PresentationId,
  [property: JsonPropertyName("event_id")] string? EventId,
  [property: JsonPropertyName("block_id")] string? BlockId,
  [property: JsonPropertyName("block_word_index")] int BlockWordIndex,
  [property: JsonPropertyName("source")] JsonElement? Source);

/// <summary>
/// Canonical source identity carried by one Core-rendered HTML unit.
/// </summary>
internal sealed record CanonicalHtmlSourceProjection(
  [property: JsonPropertyName("event_id")] string EventId,
  [property: JsonPropertyName("provider")] string? Provider,
  [property: JsonPropertyName("record_id")] string? RecordId,
  [property: JsonPropertyName("record_index")] int? RecordIndex,
  [property: JsonPropertyName("block_indexes")] int[]? BlockIndexes);
