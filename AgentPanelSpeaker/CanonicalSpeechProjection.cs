using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentPanelSpeaker;

/// <summary>
/// Applies AIConversationCore speech participation/timer metadata to the
/// canonical event projection consumed by AgentPanelSpeaker speech/history.
/// </summary>
internal static class CanonicalSpeechProjection
{
  /// <summary>
  /// Removes canonical events explicitly marked non-speakable and applies the
  /// core-supplied background-work identity strategy.
  /// </summary>
  public static AIConversationProjection Prepare(
    AIConversationProjection projection)
  {
    ArgumentNullException.ThrowIfNull(projection);
    var events = new List<JsonElement>(projection.Events.Length);
    foreach (JsonElement eventElement in projection.Events)
    {
      if (TryGetSpeechEligibility(eventElement, out bool eligible) && !eligible)
      {
        continue;
      }

      events.Add(GetBackgroundIdentityKind(eventElement) == "task_timestamp"
        ? WithoutToolCallRelationship(eventElement)
        : eventElement.Clone());
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
    if (eventElement.ValueKind != JsonValueKind.Object ||
        !eventElement.TryGetProperty("speech", out JsonElement speech) ||
        speech.ValueKind != JsonValueKind.Object ||
        !speech.TryGetProperty("eligible", out JsonElement value) ||
        value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
    {
      return false;
    }

    eligible = value.GetBoolean();
    return true;
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
}
