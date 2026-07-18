using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Extracts conversational narration from Claude and Codex JSONL records while
/// excluding tool calls, tool results, commands, diffs, and status records.
/// </summary>
internal static partial class JsonlRecordExtractor
{
  /// <summary>
  /// Detects the record format without consuming external state.
  /// </summary>
  public static AgentSource? DetectSource(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return null;
    }

    try
    {
      using JsonDocument document = JsonDocument.Parse(line);
      string type = GetString(document.RootElement, "type");
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
    }

    return null;
  }

  /// <summary>
  /// Extracts all conversational text from one complete JSONL record.
  /// </summary>
  public static ExtractionResult Extract(AgentSource source, string line)
  {
    using JsonDocument document = JsonDocument.Parse(line);
    return source switch
    {
      AgentSource.Claude => ExtractClaude(document.RootElement),
      AgentSource.Codex => ExtractCodex(document.RootElement),
      _ => throw new ArgumentException(
        "A concrete Claude or Codex source is required.",
        nameof(source))
    };
  }

  /// <summary>
  /// Extracts real Claude user text, assistant text, and thinking blocks.
  /// </summary>
  private static ExtractionResult ExtractClaude(JsonElement root)
  {
    string recordType = GetString(root, "type");
    if (GetBoolean(root, "isSidechain"))
    {
      return Empty("claude sidechain record", recordType, string.Empty);
    }

    if (recordType == "queue-operation")
    {
      return Empty("claude queue-operation record", recordType, string.Empty);
    }

    if (!root.TryGetProperty("message", out JsonElement message) ||
        message.ValueKind != JsonValueKind.Object)
    {
      return Empty("claude record has no message", recordType, string.Empty);
    }

    if (recordType == "assistant" && string.Equals(
          GetString(message, "model"),
          "<synthetic>",
          StringComparison.Ordinal))
    {
      return Empty("claude synthetic assistant record", recordType, string.Empty);
    }

    if (!message.TryGetProperty("content", out JsonElement content))
    {
      return Empty("claude message has no content", recordType, string.Empty);
    }

    string? timestamp = GetOptionalString(root, "timestamp");
    var nodes = new List<ExtractedNode>();
    if (recordType == "assistant")
    {
      foreach (JsonElement block in EnumerateContent(content))
      {
        string blockType = GetString(block, "type");
        if (blockType == "thinking")
        {
          AddNode(
            nodes,
            "claude.thinking",
            ContentCategory.Reasoning,
            GetString(block, "thinking"),
            timestamp);
        }
        else if (blockType == "text")
        {
          AddNode(
            nodes,
            "claude.text",
            ContentCategory.Assistant,
            GetString(block, "text"),
            timestamp);
        }
      }
    }
    else if (recordType == "user")
    {
      foreach (JsonElement block in EnumerateContent(content))
      {
        if (GetString(block, "type") == "text")
        {
          AddNode(
            nodes,
            "claude.user_text",
            ContentCategory.User,
            StripSystemText(GetString(block, "text")),
            timestamp);
        }
      }
    }
    else
    {
      return Empty("claude record is not user/assistant", recordType, string.Empty);
    }

    return new ExtractionResult(
      nodes,
      nodes.Count == 0
        ? "claude record contained no conversational text"
        : $"accepted {nodes.Count} claude conversational block(s)",
      recordType,
      string.Empty);
  }

  /// <summary>
  /// Extracts Codex event messages while rejecting response-item tool data.
  /// </summary>
  private static ExtractionResult ExtractCodex(JsonElement root)
  {
    string recordType = GetString(root, "type");
    if (recordType != "event_msg")
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
    string phase = GetString(payload, "phase");
    string? timestamp = GetOptionalString(root, "timestamp");
    ExtractedNode? node = payloadType switch
    {
      "agent_message" => CreateNode(
        string.IsNullOrWhiteSpace(phase)
          ? "codex.agent_message"
          : $"codex.agent_message.{phase}",
        GetCodexAgentMessageCategory(phase),
        GetString(payload, "message"),
        timestamp),
      "agent_reasoning" => CreateNode(
        "codex.agent_reasoning",
        ContentCategory.Reasoning,
        GetString(payload, "text"),
        timestamp),
      "user_message" => CreateNode(
        "codex.user_message",
        ContentCategory.User,
        StripCodexUserPreamble(GetString(payload, "message")),
        timestamp),
      "item_completed" => CreateCodexCompletedItemNode(payload, timestamp),
      _ => null
    };

    return node is null
      ? Empty(
        "codex event is not conversational text",
        recordType,
        payloadType)
      : new ExtractionResult(
        new[] { node },
        $"accepted codex {payloadType}",
        recordType,
        payloadType);
  }

  /// <summary>
  /// Maps Codex agent-message phases onto the separate speech profiles.
  /// </summary>
  private static ContentCategory GetCodexAgentMessageCategory(string phase)
  {
    return phase.ToLowerInvariant() switch
    {
      "analysis" or "commentary" or "reasoning" =>
        ContentCategory.Reasoning,
      _ => ContentCategory.Assistant
    };
  }

  /// <summary>
  /// Extracts a completed Codex Plan and rejects other completed items.
  /// </summary>
  private static ExtractedNode? CreateCodexCompletedItemNode(
    JsonElement payload,
    string? timestamp)
  {
    if (!payload.TryGetProperty("item", out JsonElement item) ||
        item.ValueKind != JsonValueKind.Object ||
        !string.Equals(
          GetString(item, "type"),
          "Plan",
          StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return CreateNode(
      "codex.plan",
      ContentCategory.Assistant,
      GetString(item, "text"),
      timestamp);
  }

  /// <summary>
  /// Enumerates object blocks from list content or synthesizes one text block.
  /// </summary>
  private static IEnumerable<JsonElement> EnumerateContent(JsonElement content)
  {
    if (content.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement block in content.EnumerateArray())
      {
        if (block.ValueKind == JsonValueKind.Object)
        {
          yield return block;
        }
      }
    }
  }

  /// <summary>
  /// Adds a non-empty node.
  /// </summary>
  private static void AddNode(
    ICollection<ExtractedNode> nodes,
    string kind,
    ContentCategory category,
    string text,
    string? timestamp)
  {
    ExtractedNode? node = CreateNode(kind, category, text, timestamp);
    if (node is not null)
    {
      nodes.Add(node);
    }
  }

  /// <summary>
  /// Creates a non-empty node or null.
  /// </summary>
  private static ExtractedNode? CreateNode(
    string kind,
    ContentCategory category,
    string text,
    string? timestamp)
  {
    return string.IsNullOrWhiteSpace(text)
      ? null
      : new ExtractedNode(kind, category, text, timestamp);
  }

  /// <summary>
  /// Removes Claude-injected context blocks from real user text.
  /// </summary>
  private static string StripSystemText(string text)
  {
    return SystemTagRegex().Replace(text, string.Empty).Trim();
  }

  /// <summary>
  /// Removes the IDE-context wrapper that Codex prepends to user messages.
  /// </summary>
  private static string StripCodexUserPreamble(string text)
  {
    Match match = CodexRequestRegex().Match(text);
    return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
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

  [GeneratedRegex(
    @"<(?:ide_opened_file|ide_selection|system[-_]reminder|system|env|" +
    @"claude_background_info|user[-_]prompt[-_]submit[-_]hook|" +
    @"command[-_]name|antml:[a-z_]+)[^>]*>.*?</[^>]+>",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex SystemTagRegex();

  [GeneratedRegex(@"## My request for Codex:\s*\r?\n([\s\S]+)")]
  private static partial Regex CodexRequestRegex();
}
