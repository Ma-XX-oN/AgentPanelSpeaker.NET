using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Extracts conversational narration from Claude and Codex JSONL records while
/// excluding non-user-facing tool calls, tool results, commands, diffs, and
/// status records.
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
            timestamp,
            startsUserTurn: true);
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
  /// Extracts Codex conversational events and the user-facing input-request
  /// function call while rejecting all other response-item tool data.
  /// </summary>
  private static ExtractionResult ExtractCodex(JsonElement root)
  {
    string recordType = GetString(root, "type");
    if (recordType == "response_item")
    {
      return ExtractCodexResponseItem(root, recordType);
    }

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
    if (payloadType == "task_complete")
    {
      return new ExtractionResult(
        Array.Empty<ExtractedNode>(),
        "accepted codex task completion marker",
        recordType,
        payloadType,
        timestamp);
    }

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
        timestamp,
        startsUserTurn: true),
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
  /// Extracts the user-facing portions of Codex response-item records.
  /// </summary>
  private static ExtractionResult ExtractCodexResponseItem(
    JsonElement root,
    string recordType)
  {
    if (!root.TryGetProperty("payload", out JsonElement payload) ||
        payload.ValueKind != JsonValueKind.Object)
    {
      return Empty(
        "codex response item has no payload",
        recordType,
        string.Empty);
    }

    string payloadType = GetString(payload, "type");
    if (string.Equals(
          payloadType,
          "function_call_output",
          StringComparison.Ordinal))
    {
      return ExtractCodexInputResponse(root, payload, recordType, payloadType);
    }

    string functionName = GetString(payload, "name");
    if (!string.Equals(
          payloadType,
          "function_call",
          StringComparison.Ordinal) ||
        !string.Equals(
          functionName,
          "request_user_input",
          StringComparison.Ordinal))
    {
      return Empty(
        "codex response-item tool/function/diff record is skipped",
        recordType,
        payloadType);
    }

    if (!payload.TryGetProperty("arguments", out JsonElement arguments))
    {
      return Empty(
        "codex request_user_input has no arguments",
        recordType,
        payloadType);
    }

    string callId = FirstNonEmptyString(payload, "call_id", "id");
    string? timestamp = GetOptionalString(root, "timestamp");
    if (arguments.ValueKind == JsonValueKind.Object)
    {
      return ExtractCodexInputQuestions(
        arguments,
        callId,
        timestamp,
        recordType,
        payloadType);
    }

    if (arguments.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(arguments.GetString()))
    {
      return Empty(
        "codex request_user_input arguments are not JSON",
        recordType,
        payloadType);
    }

    try
    {
      using JsonDocument argumentDocument = JsonDocument.Parse(
        arguments.GetString()!);
      return ExtractCodexInputQuestions(
        argumentDocument.RootElement,
        callId,
        timestamp,
        recordType,
        payloadType);
    }
    catch (JsonException)
    {
      return Empty(
        "codex request_user_input arguments contain invalid JSON",
        recordType,
        payloadType);
    }
  }

  /// <summary>
  /// Converts every request_user_input question and its options into one
  /// independently navigable Assistant node and retains its answer metadata.
  /// </summary>
  private static ExtractionResult ExtractCodexInputQuestions(
    JsonElement arguments,
    string callId,
    string? timestamp,
    string recordType,
    string payloadType)
  {
    if (arguments.ValueKind != JsonValueKind.Object ||
        !arguments.TryGetProperty("questions", out JsonElement questions) ||
        questions.ValueKind != JsonValueKind.Array)
    {
      return Empty(
        "codex request_user_input contains no questions",
        recordType,
        payloadType);
    }

    var nodes = new List<ExtractedNode>();
    var inputQuestions = new List<CodexInputQuestion>();
    foreach (JsonElement question in questions.EnumerateArray())
    {
      if (question.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      string questionText = GetString(question, "question").Trim();
      if (questionText.Length == 0)
      {
        questionText = GetString(question, "header").Trim();
      }
      if (questionText.Length == 0)
      {
        continue;
      }

      IReadOnlyList<CodexInputOption> options = ReadInputOptions(question);
      var spoken = new StringBuilder();
      AppendSentence(spoken, questionText);
      spoken.AppendLine();
      AppendInputOptions(spoken, options);
      AddNode(
        nodes,
        "codex.request_user_input",
        ContentCategory.Assistant,
        spoken.ToString().Trim(),
        timestamp);

      string questionId = GetString(question, "id").Trim();
      if (questionId.Length != 0)
      {
        inputQuestions.Add(new CodexInputQuestion(
          questionId,
          GetBoolean(question, "isSecret") ||
            GetBoolean(question, "is_secret"),
          options));
      }
    }

    if (nodes.Count == 0)
    {
      return Empty(
        "codex request_user_input contains no speakable questions",
        recordType,
        payloadType);
    }

    CodexInputRequest? inputRequest =
      callId.Length != 0 && inputQuestions.Count != 0
        ? new CodexInputRequest(callId, inputQuestions)
        : null;
    return new ExtractionResult(
      nodes,
      $"accepted {nodes.Count} codex request_user_input question(s)",
      recordType,
      payloadType,
      InputRequest: inputRequest);
  }

  /// <summary>
  /// Reads every labelled input option in source order.
  /// </summary>
  private static IReadOnlyList<CodexInputOption> ReadInputOptions(
    JsonElement question)
  {
    if (!question.TryGetProperty("options", out JsonElement options) ||
        options.ValueKind != JsonValueKind.Array)
    {
      return Array.Empty<CodexInputOption>();
    }

    var result = new List<CodexInputOption>();
    foreach (JsonElement option in options.EnumerateArray())
    {
      string label;
      string description;
      if (option.ValueKind == JsonValueKind.String)
      {
        label = option.GetString()?.Trim() ?? string.Empty;
        description = string.Empty;
      }
      else if (option.ValueKind == JsonValueKind.Object)
      {
        label = FirstNonEmptyString(
          option,
          "label",
          "title",
          "text",
          "value");
        description = GetString(option, "description").Trim();
      }
      else
      {
        continue;
      }

      if (label.Length != 0 || description.Length != 0)
      {
        result.Add(new CodexInputOption(label, description));
      }
    }
    return result;
  }

  /// <summary>
  /// Appends every input option in source order.
  /// </summary>
  private static void AppendInputOptions(
    StringBuilder spoken,
    IReadOnlyList<CodexInputOption> options)
  {
    for (int index = 0; index < options.Count; ++index)
    {
      CodexInputOption option = options[index];
      spoken.Append("- Option ");
      spoken.Append(index + 1);
      spoken.Append(": ");
      if (option.Label.Length != 0)
      {
        AppendSentence(spoken, option.Label);
      }
      if (option.Description.Length != 0)
      {
        if (option.Label.Length != 0)
        {
          spoken.Append(' ');
        }
        AppendSentence(spoken, option.Description);
      }
      spoken.AppendLine();
    }
  }

  /// <summary>
  /// Extracts structured answers from a function-call output.  The monitor
  /// accepts them only when the call ID matches a pending input request.
  /// </summary>
  private static ExtractionResult ExtractCodexInputResponse(
    JsonElement root,
    JsonElement payload,
    string recordType,
    string payloadType)
  {
    string callId = FirstNonEmptyString(payload, "call_id", "id");
    if (callId.Length == 0 ||
        !payload.TryGetProperty("output", out JsonElement output))
    {
      return Empty(
        "codex function-call output has no input-response identity",
        recordType,
        payloadType);
    }

    var answers = new Dictionary<string, IReadOnlyList<string>>(
      StringComparer.Ordinal);
    if (!TryReadInputAnswers(output, answers))
    {
      return Empty(
        "codex function-call output is not a user-input answer",
        recordType,
        payloadType);
    }

    return new ExtractionResult(
      Array.Empty<ExtractedNode>(),
      $"accepted codex input response for {answers.Count} question(s)",
      recordType,
      payloadType,
      InputResponse: new CodexInputResponse(
        callId,
        answers,
        GetOptionalString(root, "timestamp")));
  }

  /// <summary>
  /// Reads the supported direct, string-encoded, or body-wrapped answer form.
  /// </summary>
  private static bool TryReadInputAnswers(
    JsonElement output,
    IDictionary<string, IReadOnlyList<string>> answers)
  {
    if (output.ValueKind == JsonValueKind.String)
    {
      string? json = output.GetString();
      if (string.IsNullOrWhiteSpace(json))
      {
        return false;
      }

      try
      {
        using JsonDocument document = JsonDocument.Parse(json);
        return TryReadInputAnswers(document.RootElement, answers);
      }
      catch (JsonException)
      {
        return false;
      }
    }

    if (output.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    if (output.TryGetProperty("body", out JsonElement body) &&
        TryReadInputAnswers(body, answers))
    {
      return true;
    }

    if (!output.TryGetProperty("answers", out JsonElement answerMap) ||
        answerMap.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    foreach (JsonProperty answerProperty in answerMap.EnumerateObject())
    {
      IReadOnlyList<string> selected = ReadSelectedAnswers(
        answerProperty.Value);
      if (selected.Count != 0)
      {
        answers[answerProperty.Name] = selected;
      }
    }
    return answers.Count != 0;
  }

  /// <summary>
  /// Reads one question's selected labels or free-form response text.
  /// </summary>
  private static IReadOnlyList<string> ReadSelectedAnswers(JsonElement value)
  {
    JsonElement selected = value;
    if (value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("answers", out JsonElement nested))
    {
      selected = nested;
    }

    var result = new List<string>();
    if (selected.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement item in selected.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String)
        {
          AddDistinctAnswer(result, item.GetString());
        }
      }
    }
    else if (selected.ValueKind == JsonValueKind.String)
    {
      AddDistinctAnswer(result, selected.GetString());
    }
    return result;
  }

  /// <summary>
  /// Adds one non-empty answer without repeating it.
  /// </summary>
  private static void AddDistinctAnswer(
    ICollection<string> answers,
    string? answer)
  {
    string trimmed = answer?.Trim() ?? string.Empty;
    if (trimmed.Length != 0 &&
        !answers.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
    {
      answers.Add(trimmed);
    }
  }

  /// <summary>
  /// Returns the first non-empty string among the named properties.
  /// </summary>
  private static string FirstNonEmptyString(
    JsonElement element,
    params string[] propertyNames)
  {
    foreach (string propertyName in propertyNames)
    {
      string value = GetString(element, propertyName).Trim();
      if (value.Length != 0)
      {
        return value;
      }
    }
    return string.Empty;
  }

  /// <summary>
  /// Appends text and terminal punctuation suitable for sentence splitting.
  /// </summary>
  private static void AppendSentence(StringBuilder spoken, string text)
  {
    string trimmed = text.Trim();
    spoken.Append(trimmed);
    if (trimmed.Length != 0 && trimmed[^1] is not ('.' or '!' or '?' or ':'))
    {
      spoken.Append('.');
    }
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
    string? timestamp,
    bool startsUserTurn = false)
  {
    ExtractedNode? node = CreateNode(
      kind,
      category,
      text,
      timestamp,
      startsUserTurn);
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
    string? timestamp,
    bool startsUserTurn = false)
  {
    return string.IsNullOrWhiteSpace(text)
      ? null
      : new ExtractedNode(
        kind,
        category,
        text,
        timestamp,
        startsUserTurn);
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
