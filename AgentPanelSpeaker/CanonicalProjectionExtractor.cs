using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Maps AIConversationCore canonical events onto AgentPanelSpeaker's app-owned
/// speech/timing contract without interpreting provider-native JSON.
/// </summary>
internal static class CanonicalProjectionExtractor
{
  /// <summary>
  /// Extracts the canonical events attributable to one source record.
  /// </summary>
  /// <param name="projection">Complete canonical session projection.</param>
  /// <param name="source">Selected provider.</param>
  /// <param name="sourceIndex">Zero-based valid-record index.</param>
  /// <returns>AgentPanelSpeaker extraction data for that canonical source slice.</returns>
  public static ExtractionResult ExtractRecord(
    AIConversationProjection projection,
    AgentSource source,
    int sourceIndex)
  {
    ArgumentNullException.ThrowIfNull(projection);
    if (sourceIndex < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sourceIndex));
    }

    var nodes = new List<ExtractedNode>();
    var backgroundWorkEvents = new List<BackgroundWorkEvent>();
    CodexInputRequest? inputRequest = null;
    CodexInputResponse? inputResponse = null;
    string? completionTimestamp = null;
    var contentTypes = new HashSet<string>(StringComparer.Ordinal);

    foreach (JsonElement eventElement in projection.Events)
    {
      if (GetInt32(eventElement, "source_index") != sourceIndex)
      {
        continue;
      }

      string kind = GetString(eventElement, "kind");
      string role = GetString(eventElement, "role");
      string channel = GetString(eventElement, "channel");
      string contentType = GetString(eventElement, "content_type");
      string? timestamp = GetSourceTimestamp(eventElement);
      if (contentType.Length != 0)
      {
        contentTypes.Add(contentType);
      }

      if (contentType == "task_complete")
      {
        completionTimestamp = timestamp ??
          GetNestedString(eventElement, "lifecycle", "timestamp");
        continue;
      }

      if (contentType == "completed_plan")
      {
        AddTextBlocks(
          nodes,
          eventElement,
          "codex.plan",
          ContentCategory.Assistant,
          timestamp,
          startsUserTurn: false);
        continue;
      }

      if (contentType == "subagent_start")
      {
        AddSubagentStart(
          nodes,
          backgroundWorkEvents,
          eventElement,
          timestamp);
        continue;
      }

      if (kind == "subagent")
      {
        AddSubagentCompletion(
          nodes,
          backgroundWorkEvents,
          eventElement,
          timestamp);
        continue;
      }

      if (kind == "tool_call" &&
          TryBuildCodexInputRequest(
            eventElement,
            timestamp,
            nodes,
            out CodexInputRequest? request))
      {
        inputRequest = request;
        continue;
      }

      if (kind == "tool_result" &&
          TryBuildCodexInputResponse(
            eventElement,
            timestamp,
            out CodexInputResponse? response))
      {
        inputResponse = response;
        continue;
      }

      if (kind == "notice")
      {
        // Ordinary canonical notices are display-only.  Lifecycle notices are
        // handled explicitly above.
        continue;
      }

      if (role == "user" && kind == "message")
      {
        if (contentType == "queued_command")
        {
          AddQueuedCommandNodes(nodes, eventElement, timestamp);
        }
        else
        {
          AddTextBlocks(
            nodes,
            eventElement,
            CanonicalNodeKind(source, kind, contentType, role, channel),
            ContentCategory.User,
            timestamp,
            startsUserTurn: true);
        }
        continue;
      }

      if (role == "assistant" &&
          kind is "message" or "commentary" or "reasoning_summary")
      {
        ContentCategory category = IsReasoning(kind, channel)
          ? ContentCategory.Reasoning
          : ContentCategory.Assistant;
        AddCanonicalAssistantBlocks(
          nodes,
          eventElement,
          CanonicalNodeKind(source, kind, contentType, role, channel),
          category,
          timestamp);
      }
    }

    string provider = source == AgentSource.Codex ? "codex" : "claude";
    string payloadSummary = string.Join(",", contentTypes.OrderBy(value => value));
    int acceptedCount = nodes.Count;
    string decision = acceptedCount == 0 &&
      inputRequest is null && inputResponse is null &&
      backgroundWorkEvents.Count == 0 && completionTimestamp is null
        ? "canonical source record contained no conversational data"
        : $"accepted {acceptedCount} canonical conversational node(s)";

    return new ExtractionResult(
      nodes,
      decision,
      $"canonical.{provider}",
      payloadSummary,
      completionTimestamp,
      inputRequest,
      inputResponse,
      backgroundWorkEvents);
  }

  /// <summary>
  /// Maps canonical message/reasoning blocks into Assistant speech nodes.
  /// </summary>
  private static void AddCanonicalAssistantBlocks(
    ICollection<ExtractedNode> nodes,
    JsonElement eventElement,
    string nodeKind,
    ContentCategory category,
    string? timestamp)
  {
    foreach (JsonElement block in EnumerateBlocks(eventElement))
    {
      string blockType = GetString(block, "type");
      string text = blockType == "reasoning_summary"
        ? FirstNonEmpty(
          GetString(block, "content"),
          GetString(block, "summary"))
        : GetString(block, "text");
      AddNode(nodes, nodeKind, category, text, timestamp);
    }
  }

  /// <summary>
  /// Maps visible canonical text blocks onto one app category.
  /// </summary>
  private static void AddTextBlocks(
    ICollection<ExtractedNode> nodes,
    JsonElement eventElement,
    string nodeKind,
    ContentCategory category,
    string? timestamp,
    bool startsUserTurn)
  {
    bool first = true;
    foreach (JsonElement block in EnumerateBlocks(eventElement))
    {
      if (GetString(block, "type") != "text")
      {
        continue;
      }
      AddNode(
        nodes,
        nodeKind,
        category,
        GetString(block, "text"),
        timestamp,
        startsUserTurn && first);
      first = false;
    }
  }

  /// <summary>
  /// Maps the canonical queued-command split into context and User nodes.
  /// </summary>
  private static void AddQueuedCommandNodes(
    ICollection<ExtractedNode> nodes,
    JsonElement eventElement,
    string? timestamp)
  {
    foreach (JsonElement block in EnumerateBlocks(eventElement))
    {
      if (GetString(block, "type") != "text")
      {
        continue;
      }

      string generatedContext = GetNestedString(
        block,
        "queued_command",
        "generated_context") ?? string.Empty;
      string userText = GetNestedString(
        block,
        "queued_command",
        "user_text") ?? string.Empty;
      if (generatedContext.Length == 0 && userText.Length == 0)
      {
        userText = GetString(block, "text");
      }

      AddNode(
        nodes,
        "claude.queued_command.context",
        ContentCategory.UserContext,
        generatedContext,
        timestamp);
      AddNode(
        nodes,
        "claude.queued_command",
        ContentCategory.User,
        userText,
        timestamp,
        startsUserTurn: true);
    }
  }

  /// <summary>
  /// Emits the app-owned announcement/timer state for a canonical subagent start.
  /// </summary>
  private static void AddSubagentStart(
    ICollection<ExtractedNode> nodes,
    ICollection<BackgroundWorkEvent> workEvents,
    JsonElement eventElement,
    string? timestamp)
  {
    JsonElement? block = FirstBlock(eventElement, "tool_call");
    if (block is null)
    {
      return;
    }

    string description = GetNestedString(
      block.Value,
      "subagent_start",
      "description") ?? string.Empty;
    string announcement = description.Length == 0
      ? "Starting subagent."
      : $"Starting subagent: {description}.";
    AddNode(
      nodes,
      "claude.subagent.started",
      ContentCategory.SubagentAssistant,
      announcement,
      timestamp);

    string id = FirstNonEmpty(
      GetString(block.Value, "call_id"),
      GetRelationshipString(eventElement, "tool_call_id"));
    DateTimeOffset? startUtc = ParseTimestamp(timestamp);
    if (id.Length != 0 && startUtc is not null)
    {
      workEvents.Add(new BackgroundWorkEvent(
        id,
        description,
        startUtc.Value,
        EndUtc: null));
    }
  }

  /// <summary>
  /// Emits app-owned completion narration/timer state for a canonical subagent.
  /// </summary>
  private static void AddSubagentCompletion(
    ICollection<ExtractedNode> nodes,
    ICollection<BackgroundWorkEvent> workEvents,
    JsonElement eventElement,
    string? timestamp)
  {
    JsonElement? block = FirstBlock(eventElement, "subagent");
    if (block is null)
    {
      return;
    }

    string description = GetString(block.Value, "description").Trim();
    string output = GetString(block.Value, "output").Trim();
    long durationMilliseconds = GetInt64(block.Value, "duration_ms") ?? -1L;
    DateTimeOffset? endUtc = ParseTimestamp(timestamp);

    if (durationMilliseconds >= 0)
    {
      TimeSpan duration = TimeSpan.FromMilliseconds(durationMilliseconds);
      string descriptionSuffix = description.Length == 0
        ? string.Empty
        : $": {description}";
      AddNode(
        nodes,
        "claude.subagent.finished",
        ContentCategory.Assistant,
        $"Subagent finished{descriptionSuffix}. Took " +
          $"{FormatDetailedDuration(duration)} to complete.",
        timestamp);
    }

    AddNode(
      nodes,
      "claude.subagent.result",
      ContentCategory.SubagentAssistant,
      output,
      timestamp);

    if (endUtc is not null && durationMilliseconds >= 0)
    {
      string relationshipId = GetRelationshipString(
        eventElement,
        "tool_call_id");
      string agentId = GetString(block.Value, "agent_id").Trim();
      string id = relationshipId.Length != 0
        ? relationshipId
        : agentId.Length == 0
          ? string.Empty
          : agentId + "@" + endUtc.Value.ToString("O");
      if (id.Length != 0)
      {
        TimeSpan duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        workEvents.Add(new BackgroundWorkEvent(
          id,
          description,
          endUtc.Value - duration,
          endUtc.Value));
      }
    }
  }

  /// <summary>
  /// Converts a canonical request_user_input call to app question narration.
  /// </summary>
  private static bool TryBuildCodexInputRequest(
    JsonElement eventElement,
    string? timestamp,
    ICollection<ExtractedNode> nodes,
    out CodexInputRequest? request)
  {
    request = null;
    JsonElement? block = FirstBlock(eventElement, "tool_call");
    if (block is null ||
        GetString(block.Value, "name") != "request_user_input" ||
        !TryGetNestedProperty(
          block.Value,
          out JsonElement requestElement,
          "request_user_input") ||
        !requestElement.TryGetProperty("questions", out JsonElement questions) ||
        questions.ValueKind != JsonValueKind.Array)
    {
      return false;
    }

    string callId = FirstNonEmpty(
      GetString(block.Value, "call_id"),
      GetRelationshipString(eventElement, "tool_call_id"));
    var inputQuestions = new List<CodexInputQuestion>();
    foreach (JsonElement question in questions.EnumerateArray())
    {
      if (question.ValueKind != JsonValueKind.Object)
      {
        continue;
      }
      string questionText = FirstNonEmpty(
        GetString(question, "question"),
        GetString(question, "header"));
      if (questionText.Length == 0)
      {
        continue;
      }

      IReadOnlyList<CodexInputOption> options = ReadInputOptions(question);
      var spoken = new StringBuilder();
      AppendSentence(spoken, questionText);
      spoken.AppendLine();
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
          GetBoolean(question, "is_secret"),
          options));
      }
    }

    if (callId.Length != 0 && inputQuestions.Count != 0)
    {
      request = new CodexInputRequest(callId, inputQuestions);
    }
    return true;
  }

  /// <summary>
  /// Converts a canonical request_user_input result into app answer metadata.
  /// </summary>
  private static bool TryBuildCodexInputResponse(
    JsonElement eventElement,
    string? timestamp,
    out CodexInputResponse? response)
  {
    response = null;
    JsonElement? block = FirstBlock(eventElement, "tool_result");
    if (block is null ||
        !TryGetNestedProperty(
          block.Value,
          out JsonElement responseElement,
          "request_user_input_response") ||
        !responseElement.TryGetProperty("answers", out JsonElement answers) ||
        answers.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    string callId = FirstNonEmpty(
      GetString(block.Value, "call_id"),
      GetRelationshipString(eventElement, "tool_call_id"));
    if (callId.Length == 0)
    {
      return true;
    }

    var values = new Dictionary<string, IReadOnlyList<string>>(
      StringComparer.Ordinal);
    foreach (JsonProperty property in answers.EnumerateObject())
    {
      var selected = new List<string>();
      if (property.Value.ValueKind == JsonValueKind.Array)
      {
        foreach (JsonElement value in property.Value.EnumerateArray())
        {
          if (value.ValueKind == JsonValueKind.String &&
              !string.IsNullOrWhiteSpace(value.GetString()))
          {
            selected.Add(value.GetString()!.Trim());
          }
        }
      }
      if (selected.Count != 0)
      {
        values[property.Name] = selected;
      }
    }

    if (values.Count != 0)
    {
      response = new CodexInputResponse(callId, values, timestamp);
    }
    return true;
  }

  /// <summary>
  /// Reads canonical request_user_input options in source order.
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
      if (option.ValueKind != JsonValueKind.Object)
      {
        continue;
      }
      string label = GetString(option, "label").Trim();
      string description = GetString(option, "description").Trim();
      if (label.Length != 0 || description.Length != 0)
      {
        result.Add(new CodexInputOption(label, description));
      }
    }
    return result;
  }

  /// <summary>
  /// Returns whether the canonical event belongs to an app Reasoning profile.
  /// </summary>
  private static bool IsReasoning(string kind, string channel)
  {
    return kind is "reasoning_summary" or "commentary" ||
      channel.Equals("analysis", StringComparison.OrdinalIgnoreCase) ||
      channel.Equals("commentary", StringComparison.OrdinalIgnoreCase) ||
      channel.Equals("reasoning", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Creates a stable app-facing node-kind label from canonical semantics.
  /// </summary>
  private static string CanonicalNodeKind(
    AgentSource source,
    string kind,
    string contentType,
    string role,
    string channel)
  {
    string provider = source == AgentSource.Codex ? "codex" : "claude";
    string semantic = FirstNonEmpty(contentType, kind, role, channel, "content");
    return $"{provider}.canonical.{semantic}";
  }

  /// <summary>
  /// Adds one non-empty extracted node.
  /// </summary>
  private static void AddNode(
    ICollection<ExtractedNode> nodes,
    string kind,
    ContentCategory category,
    string text,
    string? timestamp,
    bool startsUserTurn = false)
  {
    if (!string.IsNullOrWhiteSpace(text))
    {
      nodes.Add(new ExtractedNode(
        kind,
        category,
        text.Trim(),
        timestamp,
        startsUserTurn));
    }
  }

  /// <summary>
  /// Enumerates canonical blocks from one event.
  /// </summary>
  private static IEnumerable<JsonElement> EnumerateBlocks(JsonElement eventElement)
  {
    if (!eventElement.TryGetProperty("blocks", out JsonElement blocks) ||
        blocks.ValueKind != JsonValueKind.Array)
    {
      yield break;
    }
    foreach (JsonElement block in blocks.EnumerateArray())
    {
      if (block.ValueKind == JsonValueKind.Object)
      {
        yield return block;
      }
    }
  }

  /// <summary>
  /// Finds the first canonical block with the requested type.
  /// </summary>
  private static JsonElement? FirstBlock(
    JsonElement eventElement,
    string blockType)
  {
    foreach (JsonElement block in EnumerateBlocks(eventElement))
    {
      if (GetString(block, "type") == blockType)
      {
        return block;
      }
    }
    return null;
  }

  /// <summary>
  /// Reads canonical source timestamp provenance.
  /// </summary>
  private static string? GetSourceTimestamp(JsonElement eventElement)
  {
    return GetNestedString(eventElement, "source", "timestamp");
  }

  /// <summary>
  /// Reads one canonical relationship string.
  /// </summary>
  private static string GetRelationshipString(
    JsonElement eventElement,
    string relationshipName)
  {
    return GetNestedString(
      eventElement,
      "relationships",
      relationshipName) ?? string.Empty;
  }

  /// <summary>
  /// Reads one nested string property.
  /// </summary>
  private static string? GetNestedString(
    JsonElement element,
    string parent,
    string child)
  {
    return TryGetNestedProperty(element, out JsonElement value, parent, child) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
  }

  /// <summary>
  /// Resolves an arbitrary nested canonical property path.
  /// </summary>
  private static bool TryGetNestedProperty(
    JsonElement element,
    out JsonElement value,
    params string[] path)
  {
    value = element;
    foreach (string name in path)
    {
      if (value.ValueKind != JsonValueKind.Object ||
          !value.TryGetProperty(name, out JsonElement next))
      {
        value = default;
        return false;
      }
      value = next;
    }
    return true;
  }

  /// <summary>
  /// Reads a canonical string property or an empty string.
  /// </summary>
  private static string GetString(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  /// <summary>
  /// Reads a canonical Boolean property.
  /// </summary>
  private static bool GetBoolean(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.True;
  }

  /// <summary>
  /// Reads a canonical Int32 property or -1.
  /// </summary>
  private static int GetInt32(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.Number &&
      value.TryGetInt32(out int result)
        ? result
        : -1;
  }

  /// <summary>
  /// Reads a canonical non-negative Int64 property.
  /// </summary>
  private static long? GetInt64(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.Number &&
      value.TryGetInt64(out long result) && result >= 0
        ? result
        : null;
  }

  /// <summary>
  /// Returns the first non-empty value.
  /// </summary>
  private static string FirstNonEmpty(params string[] values)
  {
    foreach (string value in values)
    {
      if (!string.IsNullOrWhiteSpace(value))
      {
        return value.Trim();
      }
    }
    return string.Empty;
  }

  /// <summary>
  /// Appends terminal punctuation suitable for the existing sentence splitter.
  /// </summary>
  private static void AppendSentence(StringBuilder output, string text)
  {
    string trimmed = text.Trim();
    output.Append(trimmed);
    if (trimmed.Length != 0 && trimmed[^1] is not ('.' or '!' or '?' or ':'))
    {
      output.Append('.');
    }
  }

  /// <summary>
  /// Parses an ISO source timestamp and normalizes it to UTC.
  /// </summary>
  private static DateTimeOffset? ParseTimestamp(string? timestamp)
  {
    return DateTimeOffset.TryParse(
      timestamp,
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
      out DateTimeOffset parsed)
        ? parsed.ToUniversalTime()
        : null;
  }

  /// <summary>
  /// Formats a subagent duration with second precision.
  /// </summary>
  private static string FormatDetailedDuration(TimeSpan duration)
  {
    long totalSeconds = Math.Max(0L, (long)Math.Round(duration.TotalSeconds));
    long hours = totalSeconds / 3600;
    long minutes = totalSeconds % 3600 / 60;
    long seconds = totalSeconds % 60;
    var parts = new List<string>();
    if (hours != 0)
    {
      parts.Add(hours == 1 ? "1 hour" : $"{hours} hours");
    }
    if (minutes != 0)
    {
      parts.Add(minutes == 1 ? "1 minute" : $"{minutes} minutes");
    }
    if (seconds != 0 || parts.Count == 0)
    {
      parts.Add(seconds == 1 ? "1 second" : $"{seconds} seconds");
    }
    return parts.Count == 1
      ? parts[0]
      : string.Join(", ", parts.Take(parts.Count - 1)) +
        " and " + parts[^1];
  }
}
