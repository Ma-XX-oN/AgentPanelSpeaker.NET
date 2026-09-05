using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Detects whether one JSONL record belongs to Claude or Codex.
/// </summary>
/// <remarks>
/// This helper intentionally performs format detection only.  Conversation
/// semantics are normalized exclusively by AIConversationCore.
/// </remarks>
internal static class JsonlRecordExtractor
{
  /// <summary>
  /// Detects the record format without consuming external state.
  /// </summary>
  /// <param name="line">One complete JSONL record.</param>
  /// <returns>The detected source, or null when the record is unrecognized.</returns>
  public static AgentSource? DetectSource(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return null;
    }

    try
    {
      using JsonDocument document = JsonDocument.Parse(line);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty("type", out JsonElement typeElement) ||
          typeElement.ValueKind != JsonValueKind.String)
      {
        return null;
      }

      string type = typeElement.GetString() ?? string.Empty;
      if (type is "event_msg" or "response_item" or "session_meta")
      {
        return AgentSource.Codex;
      }

      if (type is "assistant" or "user" or "queue-operation" or
          "attachment")
      {
        return AgentSource.Claude;
      }
    }
    catch (JsonException)
    {
    }

    return null;
  }
}
