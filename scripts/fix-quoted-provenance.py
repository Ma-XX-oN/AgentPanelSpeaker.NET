from pathlib import Path


def replace_once(path, old, new):
  p = Path(path)
  text = p.read_text(encoding='utf-8')
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one match, found {count}')
  p.write_text(text.replace(old, new, 1), encoding='utf-8')


path = 'AgentPanelSpeaker/TranscriptMarkdownFormatter.cs'
replace_once(
  path,
  '''    foreach (string line in normalized.Split('\\n'))
    {
      cancellationToken.ThrowIfCancellationRequested();
      MatchCollection matches = ProvenanceCommentRegex().Matches(line);
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
        output.Append("<span class=\\\"record-anchor\\\" data-jsonl-record=\\\"")
          .Append(sourceIndex + 1)
          .Append("\\\" data-source-id=\\\"")
          .Append(WebUtility.HtmlEncode(sourceId))
          .AppendLine("\\\"></span>");
      }

      output.AppendLine(ProvenanceCommentRegex().Replace(line, string.Empty));
    }
''',
  '''    foreach (string line in normalized.Split('\\n'))
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
          .Append("<span class=\\\"record-anchor\\\" data-jsonl-record=\\\"")
          .Append(sourceIndex + 1)
          .Append("\\\" data-source-id=\\\"")
          .Append(WebUtility.HtmlEncode(sourceId))
          .AppendLine("\\\"></span>");
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
'''
)

replace_once(
  path,
  '''    string result = ProvenanceCommentRegex().Replace(
      markdown,
      match => int.TryParse(
          match.Groups["index"].Value,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out int index) && relocationIndices.Contains(index)
        ? string.Empty
        : match.Value);
''',
  '''    string result = RemoveRelocatedProvenance(
      markdown,
      relocationIndices);
'''
)

replace_once(
  path,
  '''      string comment = sourceId.Length == 0
        ? $"<!-- record_index={sourceIndex} -->"
        : $"<!-- record_id={sourceId} record_index={sourceIndex} -->";
      result = result.Insert(lineStart, comment + "\\n");
      searchStart = textIndex + comment.Length + 1 + firstLine.Length;
''',
  '''      string comment = sourceId.Length == 0
        ? $"<!-- record_index={sourceIndex} -->"
        : $"<!-- record_id={sourceId} record_index={sourceIndex} -->";
      int lineEnd = result.IndexOf('\\n', lineStart);
      if (lineEnd < 0)
      {
        lineEnd = result.Length;
      }
      string quotePrefix = MarkdownQuotePrefix(result[lineStart..lineEnd]);
      string inserted = quotePrefix + comment + "\\n";
      result = result.Insert(lineStart, inserted);
      searchStart = textIndex + inserted.Length + firstLine.Length;
'''
)

replace_once(
  path,
  '''  private static bool TryGetFirstTextLine(
''',
  '''  /// <summary>
  /// Removes selected renderer provenance while preserving Markdown structure.
  /// Provenance-only quoted lines become blank lines instead of bare quote
  /// markers, which would otherwise render as visible greater-than symbols.
  /// </summary>
  private static string RemoveRelocatedProvenance(
    string markdown,
    IReadOnlySet<int> relocationIndices)
  {
    string normalized = markdown
      .Replace("\\r\\n", "\\n", StringComparison.Ordinal)
      .Replace('\\r', '\\n');
    var output = new StringBuilder(normalized.Length);
    foreach (string line in normalized.Split('\\n'))
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
'''
)

replace_once(
  path,
  '''  [GeneratedRegex(
    @"<!--\\s*(?:record_id=(?<id>[^\\s>]+)\\s+)?record_index=(?<index>\\d+)\\s*-->",
    RegexOptions.CultureInvariant)]
  private static partial Regex ProvenanceCommentRegex();
''',
  '''  [GeneratedRegex(
    @"<!--\\s*(?:record_id=(?<id>[^\\s>]+)\\s+)?record_index=(?<index>\\d+)\\s*-->",
    RegexOptions.CultureInvariant)]
  private static partial Regex ProvenanceCommentRegex();

  [GeneratedRegex(
    @"^(?:[ \\t]*>[ \\t]?)+",
    RegexOptions.CultureInvariant)]
  private static partial Regex MarkdownQuotePrefixRegex();

  [GeneratedRegex(
    @"^(?:[ \\t]*>[ \\t]?)+[ \\t]*$",
    RegexOptions.CultureInvariant)]
  private static partial Regex MarkdownQuoteOnlyRegex();
'''
)

harness = 'tools/AgentPanelSpeaker.DisplayParity/Program.cs'
replace_once(
  harness,
  '''    string html = TranscriptHtmlRenderer.ToHtml(markdown, pipeline);
    ValidateDetailsRange(html, "rendered Claude thought HTML", failures);
''',
  '''    string html = TranscriptHtmlRenderer.ToHtml(markdown, pipeline);
    ValidateDetailsRange(html, "rendered Claude thought HTML", failures);
    Reject(
      html,
      "<p>&gt;",
      "literal Markdown quote marker in grouped Claude thoughts",
      failures);
    Reject(
      html,
      "<p>***</p>",
      "literal Markdown thought separator in grouped Claude thoughts",
      failures);
'''
)
