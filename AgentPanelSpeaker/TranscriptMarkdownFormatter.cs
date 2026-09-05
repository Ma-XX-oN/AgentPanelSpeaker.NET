using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Formats AIConversationCore canonical transcript output for the WebView host.
/// Provider semantics remain exclusively in AIConversationCore; this class adds
/// only AgentPanelSpeaker session chrome and virtualization anchors.
/// </summary>
internal static partial class TranscriptMarkdownFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptMarkdownFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Formats one complete selected session from the canonical core projection.
  /// </summary>
  public static string Format(
    string path,
    AgentSource source,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var jsonLines = new List<string>();
    foreach (string line in ReadSharedLines(path))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      // Preserve the prior formatter's behavior of rejecting malformed
      // non-empty JSONL records rather than silently changing the transcript.
      using JsonDocument document = JsonDocument.Parse(line);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new JsonException("A transcript JSONL record must be an object.");
      }
      jsonLines.Add(line);
    }

    if (jsonLines.Count == 0)
    {
      return BuildSessionHeader(
        path,
        source,
        recordCount: 0,
        Array.Empty<JsonElement>());
    }

    AIConversationProjection projection = CoreClient.Project(source, jsonLines);
    cancellationToken.ThrowIfCancellationRequested();
    string header = BuildSessionHeader(
      path,
      source,
      jsonLines.Count,
      projection.Events);
    string canonicalMarkdown = NormalizeQuotedDetailsForMarkdig(
      projection.Markdown);
    string anchored = AddRecordAnchors(
      canonicalMarkdown,
      projection.Events,
      cancellationToken);
    return header + anchored;
  }

  /// <summary>
  /// Adds the AgentPanelSpeaker session heading around canonical content.
  /// </summary>
  private static string BuildSessionHeader(
    string path,
    AgentSource source,
    int recordCount,
    IReadOnlyList<JsonElement> events)
  {
    DateTimeOffset? first = null;
    DateTimeOffset? last = null;
    foreach (JsonElement eventElement in events)
    {
      DateTimeOffset? timestamp = ReadSourceTimestamp(eventElement);
      if (timestamp is null)
      {
        continue;
      }
      first ??= timestamp;
      last = timestamp;
    }

    string sourceName = source == AgentSource.Codex ? "codex" : "claude";
    string title = ReadCanonicalSessionTitle(events) ??
      Path.GetFileNameWithoutExtension(path);
    string fileStem = Path.GetFileNameWithoutExtension(path);
    string shortId = fileStem[..Math.Min(8, fileStem.Length)];
    var output = new StringBuilder();
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
    output.Append(" records: ").Append(recordCount).AppendLine();
    output.Append('(').Append(shortId).Append(") ").AppendLine(title);
    output.AppendLine();
    return output.ToString();
  }

  /// <summary>
  /// Derives the session title from the first visible canonical User message.
  /// </summary>
  private static string? ReadCanonicalSessionTitle(
    IReadOnlyList<JsonElement> events)
  {
    foreach (JsonElement eventElement in events)
    {
      if (GetString(eventElement, "role") != "user" ||
          GetString(eventElement, "kind") != "message" ||
          GetString(eventElement, "visibility") == "hidden" ||
          !eventElement.TryGetProperty("blocks", out JsonElement blocks) ||
          blocks.ValueKind != JsonValueKind.Array)
      {
        continue;
      }

      foreach (JsonElement block in blocks.EnumerateArray())
      {
        if (GetString(block, "type") != "text")
        {
          continue;
        }
        string text = GetString(block, "text").Trim();
        if (text.Length != 0)
        {
          string firstLine = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?
            .Trim() ?? string.Empty;
          if (firstLine.Length != 0)
          {
            return firstLine;
          }
        }
      }
    }
    return null;
  }

  /// <summary>
  /// Rewrites AIConversationCore's blockquoted HTML-details wrapper into the
  /// equivalent HTML container with the Claude blockquote retained inside it.
  /// Markdig otherwise ends the raw details block before parsing the quoted
  /// Markdown body, leaving the thoughts visibly outside the disclosure.
  /// </summary>
  private static string NormalizeQuotedDetailsForMarkdig(string markdown)
  {
    string normalized = markdown
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    string[] lines = normalized.Split('\n');
    var output = new StringBuilder(normalized.Length + 64);
    bool inQuotedDetails = false;

    for (int index = 0; index < lines.Length; ++index)
    {
      string line = lines[index];
      if (!inQuotedDetails &&
          line.StartsWith("> <details>", StringComparison.Ordinal))
      {
        output.AppendLine(line[2..]);
        inQuotedDetails = true;
        continue;
      }

      if (inQuotedDetails &&
          line.StartsWith("> <summary>", StringComparison.Ordinal))
      {
        output.AppendLine(line[2..]);
        continue;
      }

      if (inQuotedDetails &&
          line.StartsWith("> </details>", StringComparison.Ordinal))
      {
        output.AppendLine(line[2..]);
        inQuotedDetails = false;
        continue;
      }

      if (inQuotedDetails && line == ">")
      {
        bool immediatelyAfterSummary = index > 0 &&
          lines[index - 1].StartsWith("> <summary>", StringComparison.Ordinal);
        bool immediatelyBeforeClose = index + 1 < lines.Length &&
          lines[index + 1].StartsWith("> </details>", StringComparison.Ordinal);
        if (immediatelyAfterSummary || immediatelyBeforeClose)
        {
          output.AppendLine();
          continue;
        }
      }

      output.AppendLine(line);
    }

    return output.ToString();
  }

  /// <summary>
  /// Converts canonical renderer provenance comments into the hidden record
  /// anchors used by AgentPanelSpeaker virtualization. The conversion reads
  /// only canonical record identity/index metadata, never provider-native JSON.
  /// </summary>
  private static string AddRecordAnchors(
    string markdown,
    IReadOnlyList<JsonElement> events,
    CancellationToken cancellationToken)
  {
    string provenanceComplete = EnsureVisibleMessageProvenance(
      markdown,
      events,
      cancellationToken);
    var sourceIds = new Dictionary<int, string>();
    foreach (JsonElement eventElement in events)
    {
      int sourceIndex = GetInt32(eventElement, "source_index");
      if (sourceIndex < 0 || sourceIds.ContainsKey(sourceIndex))
      {
        continue;
      }
      string sourceId = GetString(eventElement, "source_record_id").Trim();
      sourceIds[sourceIndex] = sourceId.Length == 0
        ? (sourceIndex + 1).ToString(CultureInfo.InvariantCulture)
        : sourceId;
    }

    var emitted = new HashSet<int>();
    var output = new StringBuilder(provenanceComplete.Length + 1024);
    string normalized = provenanceComplete
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    foreach (string line in normalized.Split('\n'))
    {
      cancellationToken.ThrowIfCancellationRequested();
      MatchCollection matches = ProvenanceCommentRegex().Matches(line);
      string quotePrefix = MarkdownQuotePrefix(line);
      bool emittedAnchor = false;
      foreach (Match match in matches)
      {
        if (!int.TryParse(
              match.Groups["index"].Value,
              NumberStyles.Integer,
              CultureInfo.InvariantCulture,
              out int sourceIndex) ||
            sourceIndex < 0 ||
            !emitted.Add(sourceIndex))
        {
          continue;
        }

        string sourceId = match.Groups["id"].Success
          ? match.Groups["id"].Value
          : sourceIds.TryGetValue(sourceIndex, out string? mapped)
            ? mapped
            : (sourceIndex + 1).ToString(CultureInfo.InvariantCulture);
        output.Append(quotePrefix)
          .Append("<span class=\"record-anchor\" data-jsonl-record=\"")
          .Append(sourceIndex + 1)
          .Append("\" data-source-id=\"")
          .Append(WebUtility.HtmlEncode(sourceId))
          .AppendLine("\"></span>");
        emittedAnchor = true;
      }

      string remainder = ProvenanceCommentRegex().Replace(line, string.Empty);
      if (matches.Count != 0 &&
          (string.IsNullOrWhiteSpace(remainder) || IsMarkdownQuoteOnly(remainder)))
      {
        if (!emittedAnchor)
        {
          output.AppendLine();
        }
        continue;
      }
      output.AppendLine(remainder);
    }
    return output.ToString();
  }

  /// <summary>
  /// Positions one provenance marker immediately before the first rendered text
  /// owned by each visible canonical source record. AIConversationCore can group
  /// several reasoning records in one details block and place all of their debug
  /// comments at the summary. Those comments identify the right records but are
  /// not content boundaries, so leaving them there makes record-scoped speech
  /// mapping assign the grouped body to the last record only.
  /// </summary>
  private static string EnsureVisibleMessageProvenance(
    string markdown,
    IReadOnlyList<JsonElement> events,
    CancellationToken cancellationToken)
  {
    var relocations = new List<(int SourceIndex, string SourceId, string FirstLine)>();
    var positionedSourceIndices = new HashSet<int>();

    foreach (JsonElement eventElement in events)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int sourceIndex = GetInt32(eventElement, "source_index");
      string kind = GetString(eventElement, "kind");
      if (sourceIndex < 0 ||
          positionedSourceIndices.Contains(sourceIndex) ||
          GetString(eventElement, "visibility") == "hidden" ||
          kind is not ("message" or "commentary" or "reasoning_summary") ||
          !TryGetFirstTextLine(eventElement, out string firstLine))
      {
        continue;
      }

      positionedSourceIndices.Add(sourceIndex);
      relocations.Add((
        sourceIndex,
        GetString(eventElement, "source_record_id").Trim(),
        firstLine));
    }

    if (relocations.Count == 0)
    {
      return markdown;
    }

    var relocationIndices = relocations
      .Select(item => item.SourceIndex)
      .ToHashSet();
    string result = RemoveRelocatedProvenance(
      markdown,
      relocationIndices);

    int searchStart = 0;
    foreach ((int sourceIndex, string sourceId, string firstLine) in relocations)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int textIndex = result.IndexOf(
        firstLine,
        Math.Min(searchStart, result.Length),
        StringComparison.Ordinal);
      if (textIndex < 0)
      {
        textIndex = result.IndexOf(firstLine, StringComparison.Ordinal);
      }
      if (textIndex < 0)
      {
        continue;
      }

      int lineStart = result.LastIndexOf('\n', Math.Max(0, textIndex - 1));
      lineStart = lineStart < 0 ? 0 : lineStart + 1;
      string comment = sourceId.Length == 0
        ? $"<!-- record_index={sourceIndex} -->"
        : $"<!-- record_id={sourceId} record_index={sourceIndex} -->";
      int lineEnd = result.IndexOf('\n', lineStart);
      if (lineEnd < 0)
      {
        lineEnd = result.Length;
      }
      string quotePrefix = MarkdownQuotePrefix(result[lineStart..lineEnd]);
      string inserted = quotePrefix + comment + "\n";
      result = result.Insert(lineStart, inserted);
      searchStart = textIndex + inserted.Length + firstLine.Length;
    }
    return result;
  }

  /// <summary>
  /// Removes selected renderer provenance while preserving Markdown structure.
  /// Provenance-only quoted lines become blank lines instead of bare quote
  /// markers, which would otherwise render as visible greater-than symbols.
  /// </summary>
  private static string RemoveRelocatedProvenance(
    string markdown,
    IReadOnlySet<int> relocationIndices)
  {
    string normalized = markdown
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    var output = new StringBuilder(normalized.Length);
    foreach (string line in normalized.Split('\n'))
    {
      string replaced = ProvenanceCommentRegex().Replace(
        line,
        match => int.TryParse(
            match.Groups["index"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int index) && relocationIndices.Contains(index)
          ? string.Empty
          : match.Value);
      output.AppendLine(IsMarkdownQuoteOnly(replaced) ? string.Empty : replaced);
    }
    return output.ToString();
  }

  /// <summary>
  /// Returns the complete leading Markdown blockquote prefix for one line.
  /// </summary>
  private static string MarkdownQuotePrefix(string line)
  {
    Match match = MarkdownQuotePrefixRegex().Match(line);
    return match.Success ? match.Value : string.Empty;
  }

  /// <summary>
  /// Returns whether a line contains only Markdown blockquote markers/spacing.
  /// </summary>
  private static bool IsMarkdownQuoteOnly(string line)
  {
    return MarkdownQuoteOnlyRegex().IsMatch(line);
  }

  private static bool TryGetFirstTextLine(
    JsonElement eventElement,
    out string firstLine)
  {
    firstLine = string.Empty;
    if (!eventElement.TryGetProperty("blocks", out JsonElement blocks) ||
        blocks.ValueKind != JsonValueKind.Array)
    {
      return false;
    }

    foreach (JsonElement block in blocks.EnumerateArray())
    {
      string blockType = GetString(block, "type");
      string text;
      if (blockType == "text")
      {
        text = GetString(block, "text");
      }
      else if (blockType == "reasoning_summary")
      {
        text = GetString(block, "content");
        if (string.IsNullOrWhiteSpace(text))
        {
          text = GetString(block, "summary");
        }
      }
      else
      {
        continue;
      }

      text = text
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
      firstLine = text.Split('\n')
        .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?
        .Trim() ?? string.Empty;
      if (firstLine.Length != 0)
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>
  /// Reads canonical source timestamp provenance.
  /// </summary>
  private static DateTimeOffset? ReadSourceTimestamp(JsonElement eventElement)
  {
    if (!eventElement.TryGetProperty("source", out JsonElement source) ||
        source.ValueKind != JsonValueKind.Object ||
        !source.TryGetProperty("timestamp", out JsonElement timestamp) ||
        timestamp.ValueKind != JsonValueKind.String)
    {
      return null;
    }

    return DateTimeOffset.TryParse(
      timestamp.GetString(),
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
      out DateTimeOffset parsed)
        ? parsed
        : null;
  }

  /// <summary>
  /// Formats one timestamp using the previous transcript header convention.
  /// </summary>
  private static string FormatTimestamp(DateTimeOffset? timestamp)
  {
    return timestamp?.ToLocalTime().ToString(
      "yyyy-MM-dd HH:mm",
      CultureInfo.InvariantCulture) ?? string.Empty;
  }

  /// <summary>
  /// Reads one JSON string property or an empty string.
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
  /// Reads one JSON integer property or -1.
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
  /// Reads a JSONL file while allowing the writer to keep appending/replacing it.
  /// </summary>
  private static IEnumerable<string> ReadSharedLines(string path)
  {
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(
      stream,
      Encoding.UTF8,
      detectEncodingFromByteOrderMarks: true,
      bufferSize: 64 * 1024,
      leaveOpen: false);
    while (reader.ReadLine() is string line)
    {
      yield return line;
    }
  }

  [GeneratedRegex(
    @"<!--\s*(?:record_id=(?<id>[^\s>]+)\s+)?record_index=(?<index>\d+)\s*-->",
    RegexOptions.CultureInvariant)]
  private static partial Regex ProvenanceCommentRegex();

  [GeneratedRegex(
    @"^(?:[ \t]*>[ \t]?)+",
    RegexOptions.CultureInvariant)]
  private static partial Regex MarkdownQuotePrefixRegex();

  [GeneratedRegex(
    @"^(?:[ \t]*>[ \t]?)+[ \t]*$",
    RegexOptions.CultureInvariant)]
  private static partial Regex MarkdownQuoteOnlyRegex();
}
