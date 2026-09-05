using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns the accumulated valid JSONL record set used by the persistent
/// AIConversationCore bridge for initial history and live appends.
/// </summary>
internal sealed class CanonicalSessionExtractor : IDisposable
{
  private readonly AIConversationCoreClient _client = new();
  private readonly List<string> _jsonLines = new();
  private AgentSource? _source;
  private bool _disposed;

  /// <summary>
  /// Primes the accumulated session without producing extraction output.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="jsonLines">Existing source records.</param>
  public void Prime(AgentSource source, IEnumerable<string> jsonLines)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    ThrowIfDisposed();
    _source = source;
    _jsonLines.Clear();
    foreach (string line in jsonLines)
    {
      if (IsValidJsonRecord(line))
      {
        _jsonLines.Add(line);
      }
    }
  }

  /// <summary>
  /// Loads and canonically extracts one complete current session.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="jsonLines">Existing source records.</param>
  /// <returns>One extraction result per valid source record.</returns>
  public IReadOnlyList<ExtractionResult> Load(
    AgentSource source,
    IEnumerable<string> jsonLines)
  {
    Prime(source, jsonLines);
    if (_jsonLines.Count == 0)
    {
      return Array.Empty<ExtractionResult>();
    }

    AIConversationProjection projection = PrepareSpeechProjection(
      _client.Project(source, _jsonLines));
    var results = new List<ExtractionResult>(_jsonLines.Count);
    for (int sourceIndex = 0; sourceIndex < _jsonLines.Count; ++sourceIndex)
    {
      results.Add(CanonicalProjectionExtractor.ExtractRecord(
        projection,
        source,
        sourceIndex));
    }
    return results;
  }

  /// <summary>
  /// Adds one newly appended valid record and returns its canonical extraction.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="line">New complete JSONL record.</param>
  /// <returns>
  /// Canonical extraction for the appended record, or null when the line is
  /// malformed/blank and therefore is not added to the canonical record set.
  /// </returns>
  public ExtractionResult? Append(AgentSource source, string line)
  {
    ThrowIfDisposed();
    if (_source is not AgentSource currentSource || currentSource != source)
    {
      throw new InvalidOperationException(
        "Canonical session extraction must be primed for the selected source " +
        "before live records are appended.");
    }
    if (!IsValidJsonRecord(line))
    {
      return null;
    }

    _jsonLines.Add(line);
    AIConversationProjection projection = PrepareSpeechProjection(
      _client.Project(source, _jsonLines));
    return CanonicalProjectionExtractor.ExtractRecord(
      projection,
      source,
      _jsonLines.Count - 1);
  }

  /// <summary>
  /// Applies core-supplied speech participation/timer identity metadata without
  /// reading any provider-native fields in C#.
  /// </summary>
  private static AIConversationProjection PrepareSpeechProjection(
    AIConversationProjection projection)
  {
    var events = new List<JsonElement>(projection.Events.Length);
    foreach (JsonElement eventElement in projection.Events)
    {
      if (TryGetSpeechEligibility(eventElement, out bool eligible) && !eligible)
      {
        continue;
      }

      if (GetBackgroundIdentityKind(eventElement) == "task_timestamp")
      {
        events.Add(WithoutToolCallRelationship(eventElement));
      }
      else
      {
        events.Add(eventElement.Clone());
      }
    }

    return projection with { Events = events.ToArray() };
  }

  /// <summary>
  /// Reads optional core-supplied speech eligibility metadata.
  /// </summary>
  private static bool TryGetSpeechEligibility(
    JsonElement eventElement,
    out bool eligible)
  {
    eligible = true;
    return eventElement.ValueKind == JsonValueKind.Object &&
      eventElement.TryGetProperty("speech", out JsonElement speech) &&
      speech.ValueKind == JsonValueKind.Object &&
      speech.TryGetProperty("eligible", out JsonElement value) &&
      (value.ValueKind == JsonValueKind.True ||
       value.ValueKind == JsonValueKind.False) &&
      (eligible = value.GetBoolean()) == eligible;
  }

  /// <summary>
  /// Reads the core-supplied background timer identity strategy.
  /// </summary>
  private static string GetBackgroundIdentityKind(JsonElement eventElement)
  {
    if (!eventElement.TryGetProperty("speech", out JsonElement speech) ||
        speech.ValueKind != JsonValueKind.Object ||
        !speech.TryGetProperty(
          "background_work_identity",
          out JsonElement identity) ||
        identity.ValueKind != JsonValueKind.Object ||
        !identity.TryGetProperty("kind", out JsonElement kind) ||
        kind.ValueKind != JsonValueKind.String)
    {
      return string.Empty;
    }
    return kind.GetString() ?? string.Empty;
  }

  /// <summary>
  /// Removes the tool-call identity from one queue completion projection so the
  /// existing app timing contract derives `taskId@timestamp` from canonical
  /// subagent identity and canonical timestamp.
  /// </summary>
  private static JsonElement WithoutToolCallRelationship(JsonElement eventElement)
  {
    JsonObject? root = JsonNode.Parse(eventElement.GetRawText()) as JsonObject;
    if (root?["relationships"] is JsonObject relationships)
    {
      relationships["tool_call_id"] = null;
    }

    using JsonDocument document = JsonDocument.Parse(
      root?.ToJsonString() ?? eventElement.GetRawText());
    return document.RootElement.Clone();
  }

  /// <summary>
  /// Stops the persistent core worker and releases bridge resources.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _client.Dispose();
    _jsonLines.Clear();
    _source = null;
  }

  /// <summary>
  /// Returns whether a line is one complete JSON value suitable as a record.
  /// </summary>
  private static bool IsValidJsonRecord(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return false;
    }
    try
    {
      using JsonDocument document = JsonDocument.Parse(line);
      return document.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
