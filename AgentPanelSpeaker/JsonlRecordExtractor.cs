using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Extracts assistant narration from Claude and Codex JSONL records while
/// excluding tool calls, tool results, commands, diffs, and status records.
/// </summary>
internal static class JsonlRecordExtractor
{
  /// <summary>
  /// Detects the record format without consuming any external state.
  /// </summary>
  /// <param name="line">One complete JSONL line.</param>
  /// <returns>The detected source, or null for an unrecognized record.</returns>
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
      string type = GetString(root, "type");
      if (type is "event_msg" or "response_item" or "session_meta")
      {
        return AgentSource.Codex;
      }

      if (type is "assistant" or "user" or "queue-operation")
      {
        return AgentSource.Claude;
      }
    }
    catch (JsonException)
    {
      return null;
    }

    return null;
  }

  /// <summary>
  /// Extracts speakable assistant text from one complete JSONL record.
  /// </summary>
  /// <param name="source">Known session format.</param>
  /// <param name="line">One complete JSONL line.</param>
  /// <param name="options">Content-selection options.</param>
  /// <returns>Extracted nodes and diagnostic classification.</returns>
  public static ExtractionResult Extract(
    AgentSource source,
    string line,
    ExtractionOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);

    using JsonDocument document = JsonDocument.Parse(line);
    return source switch
    {
      AgentSource.Claude => ExtractClaude(document.RootElement, options),
      AgentSource.Codex => ExtractCodex(document.RootElement, options),
      _ => throw new ArgumentException(
        "A concrete Claude or Codex source is required.",
        nameof(source))
    };
  }

  /// <summary>
  /// Extracts Claude assistant thinking and text blocks.
  /// </summary>
  private static ExtractionResult ExtractClaude(
    JsonElement root,
    ExtractionOptions options)
  {
    string recordType = GetString(root, "type");
    if (!string.Equals(recordType, "assistant", StringComparison.Ordinal))
    {
      return Empty("claude non-assistant record", recordType, string.Empty);
    }

    if (GetBoolean(root, "isSidechain"))
    {
      return Empty("claude sidechain record", recordType, string.Empty);
    }

    if (!root.TryGetProperty("message", out JsonElement message) ||
        message.ValueKind != JsonValueKind.Object)
    {
      return Empty("claude assistant record has no message", recordType, string.Empty);
    }

    if (string.Equals(
          GetString(message, "model"),
          "<synthetic>",
          StringComparison.Ordinal))
    {
      return Empty("claude synthetic assistant record", recordType, string.Empty);
    }

    if (!message.TryGetProperty("content", out JsonElement content) ||
        content.ValueKind != JsonValueKind.Array)
    {
      return Empty("claude assistant record has no content array", recordType, string.Empty);
    }

    var nodes = new List<ExtractedNode>();
    var acceptedKinds = new List<string>();
    string? timestamp = GetOptionalString(root, "timestamp");

    foreach (JsonElement block in content.EnumerateArray())
    {
      if (block.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      string blockType = GetString(block, "type");
      if (blockType == "thinking" && options.SpeakReasoning)
      {
        string thinking = GetString(block, "thinking");
        if (!string.IsNullOrWhiteSpace(thinking))
        {
          nodes.Add(new ExtractedNode("claude.thinking", thinking, timestamp));
          acceptedKinds.Add("thinking");
        }
      }
      else if (blockType == "text" && options.SpeakMessages)
      {
        string text = GetString(block, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
          nodes.Add(new ExtractedNode("claude.text", text, timestamp));
          acceptedKinds.Add("text");
        }
      }
      // tool_use blocks are intentionally ignored. Their matching tool_result
      // records are user records and are ignored by the record-type gate above.
    }

    string decision = nodes.Count == 0
      ? "claude assistant record contained no enabled text/thinking block"
      : $"accepted claude {string.Join("+", acceptedKinds)} block(s)";
    return new ExtractionResult(nodes, decision, recordType, string.Empty);
  }

  /// <summary>
  /// Extracts Codex agent messages and reasoning events.
  /// </summary>
  private static ExtractionResult ExtractCodex(
    JsonElement root,
    ExtractionOptions options)
  {
    string recordType = GetString(root, "type");
    if (!string.Equals(recordType, "event_msg", StringComparison.Ordinal))
    {
      return Empty(
        "codex non-event record; tool/function/diff records are skipped",
        recordType,
        string.Empty);
    }

    if (!root.TryGetProperty("payload", out JsonElement payload) ||
        payload.ValueKind != JsonValueKind.Object)
    {
      return Empty("codex event has no payload", recordType, string.Empty);
    }

    string payloadType = GetString(payload, "type");
    string? timestamp = GetOptionalString(root, "timestamp");
    if (payloadType == "agent_message")
    {
      if (!options.SpeakMessages)
      {
        return Empty("codex agent messages disabled", recordType, payloadType);
      }

      string message = GetString(payload, "message");
      if (string.IsNullOrWhiteSpace(message))
      {
        return Empty("empty codex agent message", recordType, payloadType);
      }

      string phase = GetString(payload, "phase");
      string kind = string.IsNullOrWhiteSpace(phase)
        ? "codex.agent_message"
        : $"codex.agent_message.{phase}";
      return new ExtractionResult(
        new[] { new ExtractedNode(kind, message, timestamp) },
        "accepted codex agent message",
        recordType,
        payloadType);
    }

    if (payloadType == "agent_reasoning")
    {
      if (!options.SpeakReasoning)
      {
        return Empty("codex reasoning disabled", recordType, payloadType);
      }

      string reasoning = GetString(payload, "text");
      if (string.IsNullOrWhiteSpace(reasoning))
      {
        return Empty("empty codex reasoning event", recordType, payloadType);
      }

      return new ExtractionResult(
        new[]
        {
          new ExtractedNode("codex.agent_reasoning", reasoning, timestamp)
        },
        "accepted codex reasoning event",
        recordType,
        payloadType);
    }

    return Empty(
      "codex event is not assistant narration/reasoning",
      recordType,
      payloadType);
  }

  /// <summary>
  /// Creates an empty extraction result.
  /// </summary>
  private static ExtractionResult Empty(
    string decision,
    string recordType,
    string payloadType)
  {
    return new ExtractionResult(
      Array.Empty<ExtractedNode>(),
      decision,
      recordType,
      payloadType);
  }

  /// <summary>
  /// Gets a JSON string property or an empty string.
  /// </summary>
  private static string GetString(JsonElement element, string propertyName)
  {
    return GetOptionalString(element, propertyName) ?? string.Empty;
  }

  /// <summary>
  /// Gets a JSON string property or null.
  /// </summary>
  private static string? GetOptionalString(
    JsonElement element,
    string propertyName)
  {
    return element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
  }

  /// <summary>
  /// Gets a JSON Boolean property or false.
  /// </summary>
  private static bool GetBoolean(JsonElement element, string propertyName)
  {
    return element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.True;
  }
}
