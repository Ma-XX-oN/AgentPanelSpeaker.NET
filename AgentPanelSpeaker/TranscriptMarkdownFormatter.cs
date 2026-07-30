using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgentPanelSpeaker;

/// <summary>
/// Converts Claude and Codex JSONL records into the human-facing Markdown
/// transcript used by the embedded renderer.
/// </summary>
internal static partial class TranscriptMarkdownFormatter
{
  /// <summary>
  /// Formats one complete selected session.
  /// </summary>
  public static string Format(string path, AgentSource source)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var records = new List<TranscriptRecord>();
    int recordNumber = 0;
    foreach (string line in File.ReadLines(path))
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      recordNumber++;
      using JsonDocument document = JsonDocument.Parse(line);
      records.Add(new TranscriptRecord(
        recordNumber,
        document.RootElement.Clone()));
    }

    return source == AgentSource.Codex
      ? FormatCodex(path, records)
      : FormatClaude(path, records);
  }

  private static string FormatClaude(
    string path,
    IReadOnlyList<TranscriptRecord> records)
  {
    var output = new StringBuilder();
    AppendSessionHeader(output, path, AgentSource.Claude, records);
    var toolResults = BuildClaudeToolResultMap(records);
    string? lastUserText = null;

    foreach (TranscriptRecord record in records)
    {
      JsonElement root = record.Root;
      if (GetBoolean(root, "isSidechain") ||
          !TryGetString(root, "type", out string type))
      {
        continue;
      }

      if (type == "queue-operation")
      {
        if (TryBuildTaskNotification(record, root,
              out SubagentTranscript? notification) &&
            notification is not null)
        {
          AppendSubagent(output, notification);
        }
        continue;
      }

      if (type == "attachment")
      {
        if (TryExtractClaudeQueuedCommandText(root, out string queuedText) &&
            queuedText.Length != 0 && queuedText != lastUserText)
        {
          lastUserText = queuedText;
          AppendHeading(output, "User", record, root);
          output.AppendLine(QuoteMarkdown(queuedText));
          output.AppendLine();
        }
        continue;
      }

      if (type == "user")
      {
        if (!TryGetMessageContent(root, out JsonElement content))
        {
          continue;
        }
        string userText = ExtractClaudeUserText(content);
        if (userText.Length == 0 || userText == lastUserText)
        {
          continue;
        }
        lastUserText = userText;
        AppendHeading(output, "User", record, root);
        output.AppendLine(QuoteMarkdown(userText));
        output.AppendLine();
        continue;
      }

      if (type != "assistant" ||
          !TryGetMessageContent(root, out JsonElement assistantContent))
      {
        continue;
      }

      string model = TryGetMessage(root, out JsonElement message) &&
        TryGetString(message, "model", out string modelName)
          ? modelName
          : string.Empty;
      if (model == "<synthetic>")
      {
        foreach (JsonElement block in EnumerateArray(assistantContent))
        {
          if (TryGetString(block, "type", out string blockType) &&
              blockType == "text" &&
              TryGetString(block, "text", out string notice) &&
              notice.Length != 0)
          {
            output.AppendLine($"> *(system: {notice})*");
            output.AppendLine();
          }
        }
        continue;
      }

      var thinking = new List<string>();
      var assistantText = new List<string>();
      var toolDetails = new List<string>();
      var subagents = new List<SubagentTranscript>();
      foreach (JsonElement block in EnumerateArray(assistantContent))
      {
        if (!TryGetString(block, "type", out string blockType))
        {
          continue;
        }
        if (blockType == "thinking" &&
            TryGetString(block, "thinking", out string thought) &&
            thought.Length != 0)
        {
          thinking.Add(thought);
        }
        else if (blockType == "text" &&
                 TryGetString(block, "text", out string text) &&
                 text.Length != 0)
        {
          assistantText.Add(text);
        }
        else if (blockType == "tool_use")
        {
          string detail = RenderClaudeTool(block, toolResults);
          if (detail.Length != 0)
          {
            toolDetails.Add(detail);
          }
          if (TryBuildSubagent(block, toolResults, record, root,
                out SubagentTranscript? subagent) && subagent is not null)
          {
            subagents.Add(subagent);
          }
        }
      }

      if (thinking.Count != 0 || assistantText.Count != 0 ||
          toolDetails.Count != 0)
      {
        AppendHeading(output, "Claude", record, root);
        if (thinking.Count != 0 || toolDetails.Count != 0)
        {
          var thoughtParts = new List<string>(thinking);
          thoughtParts.AddRange(toolDetails);
          output.AppendLine("> <details>");
          output.AppendLine(
            $"> <summary>Thoughts ({thoughtParts.Count})</summary>");
          output.AppendLine(">");
          for (int index = 0; index < thoughtParts.Count; index++)
          {
            if (index != 0)
            {
              output.AppendLine("> ");
              output.AppendLine("> ***");
              output.AppendLine("> ");
            }
            output.AppendLine(QuoteMarkdown(thoughtParts[index]));
          }
          output.AppendLine(">");
          output.AppendLine("> </details>");
          output.AppendLine();
        }

        foreach (string text in assistantText)
        {
          output.AppendLine(QuoteMarkdown(text));
          output.AppendLine();
        }
      }

      foreach (SubagentTranscript subagent in subagents)
      {
        AppendSubagent(output, subagent);
      }
    }

    return output.ToString();
  }

  private static string FormatCodex(
    string path,
    IReadOnlyList<TranscriptRecord> records)
  {
    var output = new StringBuilder();
    AppendSessionHeader(output, path, AgentSource.Codex, records);
    foreach (TranscriptRecord record in records)
    {
      JsonElement root = record.Root;
      if (!TryGetString(root, "type", out string type))
      {
        continue;
      }

      if (type == "event_msg" && TryGetProperty(root, "payload", out JsonElement payload))
      {
        if (!TryGetString(payload, "type", out string payloadType))
        {
          continue;
        }
        if (payloadType == "user_message" &&
            TryGetString(payload, "message", out string userText) &&
            userText.Length != 0)
        {
          AppendHeading(output, "User", record, root);
          output.AppendLine(QuoteMarkdown(userText));
          output.AppendLine();
        }
        else if (payloadType == "agent_message" &&
                 TryGetString(payload, "message", out string agentText) &&
                 agentText.Length != 0)
        {
          string phase = TryGetString(payload, "phase", out string value)
            ? value
            : string.Empty;
          AppendHeading(output, "Codex", record, root);
          if (phase == "commentary")
          {
            output.AppendLine("> <details>");
            output.AppendLine("> <summary>Thoughts (1)</summary>");
            output.AppendLine(">");
            output.AppendLine(QuoteMarkdown(agentText));
            output.AppendLine(">");
            output.AppendLine("> </details>");
          }
          else
          {
            output.AppendLine(QuoteMarkdown(agentText));
          }
          output.AppendLine();
        }
      }
      else if (type == "response_item" &&
               TryGetProperty(root, "payload", out JsonElement item) &&
               TryGetString(item, "type", out string itemType) &&
               itemType == "message")
      {
        string role = TryGetString(item, "role", out string roleValue)
          ? roleValue
          : string.Empty;
        string text = ExtractCodexMessageText(item);
        if (text.Length != 0)
        {
          AppendHeading(
            output,
            role == "user" ? "User" : "Codex",
            record,
            root);
          output.AppendLine(QuoteMarkdown(text));
          output.AppendLine();
        }
      }
    }
    return output.ToString();
  }

  private static void AppendSessionHeader(
    StringBuilder output,
    string path,
    AgentSource source,
    IReadOnlyList<TranscriptRecord> records)
  {
    DateTimeOffset? first = records
      .Select(item => ReadTimestamp(item.Root))
      .FirstOrDefault(value => value is not null);
    DateTimeOffset? last = records
      .Select(item => ReadTimestamp(item.Root))
      .LastOrDefault(value => value is not null);
    string title = ReadSessionTitle(records) ?? Path.GetFileNameWithoutExtension(path);
    string sourceName = source == AgentSource.Codex ? "codex" : "claude";
    output.Append('[').Append(sourceName).Append(']');
    if (first is not null || last is not null)
    {
      output.Append(' ')
        .Append('[')
        .Append(FormatTimestamp(first))
        .Append("]-[")
        .Append(FormatTimestamp(last))
        .Append(']');
    }
    output.Append(" records: ").Append(records.Count).AppendLine();
    output.Append('(')
      .Append(Path.GetFileNameWithoutExtension(path)[..Math.Min(
        8,
        Path.GetFileNameWithoutExtension(path).Length)])
      .Append(") ")
      .AppendLine(title);
    output.AppendLine();
  }

  private static void AppendHeading(
    StringBuilder output,
    string speaker,
    TranscriptRecord record,
    JsonElement root)
  {
    AppendRecordAnchor(output, record, root);
    output.Append("## ").Append(speaker);
    AppendRecordSuffix(output, record, root);
    output.AppendLine();
    output.AppendLine();
  }

  private static void AppendRecordAnchor(
    StringBuilder output,
    TranscriptRecord record,
    JsonElement root)
  {
    string sourceId = JsonlRecordIdentity.GetSourceId(
      TryGetString(root, "type", out string type) &&
      type is "event_msg" or "response_item" or "session_meta"
        ? AgentSource.Codex
        : AgentSource.Claude,
      root,
      record.Number);
    output.Append("<span class=\"record-anchor\" data-jsonl-record=\"")
      .Append(record.Number)
      .Append("\" data-source-id=\"")
      .Append(HtmlEscape(sourceId))
      .AppendLine("\"></span>");
  }

  private static void AppendRecordSuffix(
    StringBuilder output,
    TranscriptRecord record,
    JsonElement root)
  {
    DateTimeOffset? timestamp = ReadTimestamp(root);
    if (timestamp is not null)
    {
      output.Append(" [")
        .Append(timestamp.Value.ToLocalTime().ToString(
          "yyyy-MM-dd HH:mm:ss",
          CultureInfo.InvariantCulture))
        .Append(']');
    }
    output.Append(": ").Append(record.Number).Append(':');
  }

  private static Dictionary<string, string> BuildClaudeToolResultMap(
    IReadOnlyList<TranscriptRecord> records)
  {
    var results = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (TranscriptRecord record in records)
    {
      if (!TryGetString(record.Root, "type", out string type) ||
          type != "user" ||
          !TryGetMessageContent(record.Root, out JsonElement content))
      {
        continue;
      }
      foreach (JsonElement block in EnumerateArray(content))
      {
        if (TryGetString(block, "type", out string blockType) &&
            blockType == "tool_result" &&
            TryGetString(block, "tool_use_id", out string id))
        {
          results[id] = ExtractTextContent(block, "content");
        }
      }
    }
    return results;
  }

  private static string RenderClaudeTool(
    JsonElement block,
    IReadOnlyDictionary<string, string> results)
  {
    if (!TryGetString(block, "name", out string name) || name == "Agent")
    {
      return string.Empty;
    }
    string id = TryGetString(block, "id", out string idValue)
      ? idValue
      : string.Empty;
    JsonElement input = TryGetProperty(block, "input", out JsonElement value)
      ? value
      : default;
    string result = id.Length != 0 && results.TryGetValue(id, out string? text)
      ? text
      : string.Empty;

    if (name == "Bash")
    {
      string command = TryGetString(input, "command", out string commandValue)
        ? commandValue
        : string.Empty;
      string description = TryGetString(input, "description", out string descriptionValue)
        ? descriptionValue
        : command.Split('\n').FirstOrDefault() ?? "command";
      var output = new StringBuilder();
      output.AppendLine("<details>");
      output.Append("<summary>").Append(HtmlEscape(description)).AppendLine("</summary>");
      output.AppendLine();
      output.AppendLine(CodeFence(command, "bash"));
      if (result.Length != 0)
      {
        output.AppendLine();
        output.AppendLine("**OUT**");
        output.AppendLine();
        output.AppendLine(CodeFence(result, string.Empty));
      }
      output.AppendLine();
      output.Append("</details>");
      return output.ToString();
    }

    if (name is "Edit" or "Write" or "NotebookEdit")
    {
      string file = TryGetString(input, "file_path", out string filePath)
        ? filePath
        : TryGetString(input, "notebook_path", out string notebookPath)
          ? notebookPath
          : string.Empty;
      return $"<details>\n<summary>file change</summary>\n\n" +
        $"**{name}** `{file}`\n\n</details>";
    }
    return string.Empty;
  }

  private static void AppendSubagent(
    StringBuilder output,
    SubagentTranscript subagent)
  {
    AppendRecordAnchor(output, subagent.Record, subagent.Root);
    output.Append("## Claude Sub-agent ");
    output.Append(subagent.Id);
    AppendRecordSuffix(output, subagent.Record, subagent.Root);
    output.AppendLine();
    output.AppendLine();
    if (subagent.Description.Length != 0)
    {
      output.AppendLine(QuoteMarkdown(
        $"**{subagent.Description}**"));
      output.AppendLine();
    }
    output.AppendLine(QuoteMarkdown(
      subagent.Result.Length == 0
        ? "*(completed without output)*"
        : subagent.Result));
    output.AppendLine();
  }

  private static bool TryBuildTaskNotification(
    TranscriptRecord record,
    JsonElement root,
    out SubagentTranscript? subagent)
  {
    subagent = null;
    if (!TryGetString(root, "operation", out string operation) ||
        !operation.Equals("enqueue", StringComparison.OrdinalIgnoreCase) ||
        !TryGetString(root, "content", out string content) ||
        string.IsNullOrWhiteSpace(content))
    {
      return false;
    }

    try
    {
      XDocument document = XDocument.Parse(
        content,
        LoadOptions.PreserveWhitespace);
      XElement? notification = document.Root;
      if (notification is null ||
          notification.Name.LocalName != "task-notification" ||
          !ElementValue(notification, "status").Equals(
            "completed",
            StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      string taskId = ElementValue(notification, "task-id");
      if (taskId.Length == 0)
      {
        return false;
      }
      string summary = ElementValue(notification, "summary");
      Match descriptionMatch = TaskDescriptionRegex().Match(summary);
      string description = descriptionMatch.Success
        ? descriptionMatch.Groups[1].Value.Trim()
        : summary.Trim();
      subagent = new SubagentTranscript(
        taskId,
        description,
        ElementValue(notification, "result"),
        record,
        root);
      return true;
    }
    catch (System.Xml.XmlException)
    {
      return false;
    }
  }

  private static string ElementValue(XElement parent, string localName)
  {
    XElement? child = parent.Elements().FirstOrDefault(
      element => element.Name.LocalName == localName);
    return child?.Value.Trim() ?? string.Empty;
  }

  private static bool TryBuildSubagent(
    JsonElement block,
    IReadOnlyDictionary<string, string> results,
    TranscriptRecord record,
    JsonElement root,
    out SubagentTranscript? subagent)
  {
    subagent = null;
    if (!TryGetString(block, "name", out string name) || name != "Agent")
    {
      return false;
    }
    string toolId = TryGetString(block, "id", out string idValue)
      ? idValue
      : "unknown";
    JsonElement input = TryGetProperty(block, "input", out JsonElement value)
      ? value
      : default;
    string description = TryGetString(input, "description", out string desc)
      ? desc.Trim()
      : string.Empty;
    string rawResult = results.TryGetValue(toolId, out string? result)
      ? result
      : string.Empty;
    Match idMatch = AgentIdRegex().Match(rawResult);
    string agentId = idMatch.Success ? idMatch.Groups[1].Value : toolId;
    string visibleResult = string.Join(
      "\n",
      rawResult.Split('\n').Where(line => !SubagentMetadataRegex().IsMatch(
        line.Trim()))).Trim();
    subagent = new SubagentTranscript(
      agentId,
      description,
      visibleResult,
      record,
      root);
    return true;
  }

  private static bool TryExtractClaudeQueuedCommandText(
    JsonElement root,
    out string text)
  {
    text = string.Empty;
    if (!TryGetProperty(root, "attachment", out JsonElement attachment) ||
        attachment.ValueKind != JsonValueKind.Object ||
        !TryGetString(attachment, "type", out string attachmentType) ||
        !attachmentType.Equals(
          "queued_command",
          StringComparison.OrdinalIgnoreCase) ||
        !TryGetProperty(attachment, "prompt", out JsonElement prompt))
    {
      return false;
    }

    var parts = new List<string>();
    if (prompt.ValueKind == JsonValueKind.String)
    {
      string value = StripInjectedXml(prompt.GetString() ?? string.Empty);
      if (value.Length != 0)
      {
        parts.Add(value);
      }
    }
    else
    {
      foreach (JsonElement block in EnumerateArray(prompt))
      {
        if (TryGetString(block, "type", out string blockType) &&
            blockType == "text" &&
            TryGetString(block, "text", out string value))
        {
          value = StripInjectedXml(value);
          if (value.Length != 0)
          {
            parts.Add(value);
          }
        }
      }
    }

    text = string.Join("\n\n", parts).Trim();
    return text.Length != 0;
  }

  private static string ExtractClaudeUserText(JsonElement content)
  {
    var parts = new List<string>();
    foreach (JsonElement block in EnumerateArray(content))
    {
      if (!TryGetString(block, "type", out string type))
      {
        continue;
      }
      if (type == "text" && TryGetString(block, "text", out string text))
      {
        string cleaned = StripInjectedXml(text).Trim();
        if (cleaned.Length != 0)
        {
          parts.Add(cleaned);
        }
      }
      else if (type == "image" &&
               TryGetProperty(block, "source", out JsonElement source))
      {
        if (TryGetString(source, "type", out string sourceType) &&
            sourceType == "url" &&
            TryGetString(source, "url", out string url))
        {
          parts.Add($"![image]({url})");
        }
      }
    }
    return string.Join("\n\n", parts);
  }

  private static string ExtractCodexMessageText(JsonElement item)
  {
    if (!TryGetProperty(item, "content", out JsonElement content))
    {
      return string.Empty;
    }
    var parts = new List<string>();
    foreach (JsonElement block in EnumerateArray(content))
    {
      if (TryGetString(block, "text", out string text) && text.Length != 0)
      {
        parts.Add(text);
      }
    }
    return string.Join("\n\n", parts);
  }

  private static string ExtractTextContent(JsonElement parent, string name)
  {
    if (!TryGetProperty(parent, name, out JsonElement content))
    {
      return string.Empty;
    }
    if (content.ValueKind == JsonValueKind.String)
    {
      return content.GetString() ?? string.Empty;
    }
    var parts = new List<string>();
    foreach (JsonElement item in EnumerateArray(content))
    {
      if (TryGetString(item, "text", out string text))
      {
        parts.Add(text);
      }
    }
    return string.Join("\n", parts);
  }

  private static string QuoteMarkdown(string text)
  {
    return string.Join(
      "\n",
      text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n')
        .Select(line => line.Length == 0 ? ">" : $"> {line}"));
  }

  private static string CodeFence(string text, string language)
  {
    int longest = LongestBacktickRunRegex()
      .Matches(text)
      .Cast<Match>()
      .Select(match => match.Length)
      .DefaultIfEmpty(0)
      .Max();
    string fence = new('`', Math.Max(3, longest + 1));
    return $"{fence}{language}\n{text}\n{fence}";
  }

  private static string HtmlEscape(string text)
  {
    return SecurityElement.Escape(text) ?? string.Empty;
  }

  private static DateTimeOffset? ReadTimestamp(JsonElement root)
  {
    if (!TryGetString(root, "timestamp", out string timestamp) ||
        !DateTimeOffset.TryParse(
          timestamp,
          CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeUniversal,
          out DateTimeOffset value))
    {
      return null;
    }
    return value;
  }

  private static string? ReadSessionTitle(
    IReadOnlyList<TranscriptRecord> records)
  {
    foreach (TranscriptRecord record in records.Reverse())
    {
      if (TryGetString(record.Root, "type", out string type) &&
          type == "ai-title" &&
          TryGetString(record.Root, "aiTitle", out string title) &&
          !string.IsNullOrWhiteSpace(title))
      {
        return FirstMeaningfulLine(title);
      }
    }

    foreach (string desiredType in new[] { "user", "assistant" })
    {
      foreach (TranscriptRecord record in records)
      {
        if (!TryGetString(record.Root, "type", out string type) ||
            type != desiredType ||
            !TryGetMessageContent(record.Root, out JsonElement content))
        {
          continue;
        }
        foreach (JsonElement block in EnumerateArray(content))
        {
          if (TryGetString(block, "type", out string blockType) &&
              blockType == "text" &&
              TryGetString(block, "text", out string text))
          {
            string candidate = FirstMeaningfulLine(StripInjectedXml(text));
            if (candidate.Length != 0)
            {
              return candidate;
            }
          }
        }
      }
    }
    return null;
  }

  private static string FirstMeaningfulLine(string text)
  {
    foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal)
               .Replace('\r', '\n')
               .Split('\n'))
    {
      string candidate = line.Trim();
      if (candidate.Length == 0 ||
          Regex.IsMatch(candidate, @"^`{3,}[^`]*$") ||
          Regex.IsMatch(candidate, @"^~{3,}[^~]*$"))
      {
        continue;
      }
      return candidate.Length <= 120 ? candidate : candidate[..120];
    }
    return string.Empty;
  }

  private static string FormatTimestamp(DateTimeOffset? timestamp)
  {
    return timestamp?.ToLocalTime().ToString(
      "yyyy-MM-dd HH:mm",
      CultureInfo.InvariantCulture) ?? string.Empty;
  }

  private static bool TryGetMessageContent(
    JsonElement root,
    out JsonElement content)
  {
    content = default;
    return TryGetMessage(root, out JsonElement message) &&
      TryGetProperty(message, "content", out content) &&
      content.ValueKind == JsonValueKind.Array;
  }

  private static bool TryGetMessage(JsonElement root, out JsonElement message)
  {
    return TryGetProperty(root, "message", out message) &&
      message.ValueKind == JsonValueKind.Object;
  }

  private static IEnumerable<JsonElement> EnumerateArray(JsonElement value)
  {
    return value.ValueKind == JsonValueKind.Array
      ? value.EnumerateArray()
      : Enumerable.Empty<JsonElement>();
  }

  private static bool TryGetProperty(
    JsonElement value,
    string name,
    out JsonElement property)
  {
    property = default;
    return value.ValueKind == JsonValueKind.Object &&
      value.TryGetProperty(name, out property);
  }

  private static bool TryGetString(
    JsonElement value,
    string name,
    out string text)
  {
    text = string.Empty;
    if (!TryGetProperty(value, name, out JsonElement property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }
    text = property.GetString() ?? string.Empty;
    return true;
  }

  private static bool GetBoolean(JsonElement value, string name)
  {
    return TryGetProperty(value, name, out JsonElement property) &&
      property.ValueKind is JsonValueKind.True;
  }

  private static string StripInjectedXml(string text)
  {
    return InjectedXmlRegex().Replace(text, string.Empty).Trim();
  }

  private sealed record TranscriptRecord(int Number, JsonElement Root);

  private sealed record SubagentTranscript(
    string Id,
    string Description,
    string Result,
    TranscriptRecord Record,
    JsonElement Root);

  [GeneratedRegex(@"(?m)^agentId:\s*([^\s]+)")]
  private static partial Regex AgentIdRegex();

  [GeneratedRegex(
    @"Agent\s+[""“](.*?)[""”]\s+came to rest",
    RegexOptions.IgnoreCase)]
  private static partial Regex TaskDescriptionRegex();

  [GeneratedRegex(
    @"^(?:agentId|worktreePath|worktreeBranch|subagent_tokens|tool_uses|duration_ms):|^</?usage>",
    RegexOptions.IgnoreCase)]
  private static partial Regex SubagentMetadataRegex();

  [GeneratedRegex(@"(?s)<(?:system-reminder|local-command-caveat|command-name|command-message|command-args)>.*?</(?:system-reminder|local-command-caveat|command-name|command-message|command-args)>")]
  private static partial Regex InjectedXmlRegex();

  [GeneratedRegex("`+")]
  private static partial Regex LongestBacktickRunRegex();
}
