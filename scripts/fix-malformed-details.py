from pathlib import Path

renderer = Path('AgentPanelSpeaker/TranscriptHtmlRenderer.cs')
text = renderer.read_text(encoding='utf-8')
old = '''  private static string RenderDetailsBody(
    string body,
    MarkdownPipeline pipeline)
  {
    if (!TryStripOuterBlockquote(body, out string unquoted))
    {
      return RenderRange(body, pipeline);
    }

    var output = new StringBuilder(unquoted.Length + 32);
    output.AppendLine("<blockquote>");
    output.Append(RenderRange(unquoted, pipeline));
    output.AppendLine("</blockquote>");
    return output.ToString();
  }
'''
new = '''  private static string RenderDetailsBody(
    string body,
    MarkdownPipeline pipeline)
  {
    string normalizedBody = NormalizeNestedQuotedDetailsStructure(body);
    if (!TryStripOuterBlockquote(normalizedBody, out string unquoted))
    {
      return RenderRange(normalizedBody, pipeline);
    }

    var output = new StringBuilder(unquoted.Length + 32);
    output.AppendLine("<blockquote>");
    output.Append(RenderRange(unquoted, pipeline));
    output.AppendLine("</blockquote>");
    return output.ToString();
  }

  /// <summary>
  /// Removes Markdown quote prefixes from nested details/summary structural tag
  /// lines before recursively rendering a disclosure body. Structural tags must
  /// not be handed to Markdig as quoted fragments because Markdig can emit an
  /// opening details tag in one blockquote and its summary/closing tag outside
  /// that blockquote, producing invalid HTML that the browser then repairs.
  /// Content lines keep their quote prefixes; only the structural tag lines are
  /// unquoted.
  /// </summary>
  private static string NormalizeNestedQuotedDetailsStructure(string markdown)
  {
    string normalized = markdown
      .Replace("\\r\\n", "\\n", StringComparison.Ordinal)
      .Replace('\\r', '\\n');
    var output = new StringBuilder(normalized.Length);
    foreach (string line in normalized.Split('\\n'))
    {
      Match structural = QuotedDetailsStructuralPrefixRegex().Match(line);
      output.AppendLine(structural.Success
        ? line[structural.Length..]
        : line);
    }
    return output.ToString();
  }
'''
if old not in text:
  raise SystemExit('RenderDetailsBody block not found')
text = text.replace(old, new, 1)
old = '''  [GeneratedRegex(
    @"^[ \\t]*>[ \\t]?",
    RegexOptions.CultureInvariant)]
  private static partial Regex OuterBlockquotePrefixRegex();
}'''
new = '''  [GeneratedRegex(
    @"^[ \\t]*>[ \\t]?",
    RegexOptions.CultureInvariant)]
  private static partial Regex OuterBlockquotePrefixRegex();

  [GeneratedRegex(
    @"^[ \\t]*(?:>[ \\t]?)+(?=</?(?:details|summary)\\b)",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
  private static partial Regex QuotedDetailsStructuralPrefixRegex();
}'''
if old not in text:
  raise SystemExit('regex footer not found')
renderer.write_text(text.replace(old, new, 1), encoding='utf-8')

program = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
text = program.read_text(encoding='utf-8')
text = text.replace(
  'ValidateClaudeThoughtGroup(failures);\nValidateCodex(coreRoot, failures);',
  'ValidateClaudeThoughtGroup(failures);\nValidateClaudeInterleavedReasoningTool(failures);\nValidateCodex(coreRoot, failures);',
  1)
old = '''    Require(
      html,
      "<hr",
      "rendered Markdown thought separator in grouped Claude thoughts",
      failures);
'''
if old not in text:
  raise SystemExit('obsolete hr assertion not found')
text = text.replace(old, '', 1)
marker = '''static void ValidateDetailsRange(
  string html,
  string label,
  ICollection<string> failures)
{'''
if marker not in text:
  raise SystemExit('ValidateDetailsRange marker not found')
method = r'''static void ValidateClaudeInterleavedReasoningTool(
  ICollection<string> failures)
{
  string tempPath = Path.Combine(
    Path.GetTempPath(),
    $"AgentPanelSpeaker-interleaved-{Guid.NewGuid():N}.jsonl");
  string[] records =
  {
    "{\"type\":\"user\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:01.000Z\",\"uuid\":\"interleaved-user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Check the current time and continue reasoning.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:02.000Z\",\"uuid\":\"interleaved-thought-1\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"First reasoning before the time tool.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:03.000Z\",\"uuid\":\"interleaved-tool\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_time\",\"name\":\"Bash\",\"input\":{\"command\":\"date\",\"description\":\"Get current time\"}}]}}",
    "{\"type\":\"user\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:04.000Z\",\"uuid\":\"interleaved-tool-result\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_time\",\"content\":\"12:00:04\"}]},\"sourceToolAssistantUUID\":\"interleaved-tool\"}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:05.000Z\",\"uuid\":\"interleaved-thought-2\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Second reasoning after the time tool.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-06T12:00:06.000Z\",\"uuid\":\"interleaved-final\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Visible answer after reasoning.\"}]}}"
  };

  try
  {
    File.WriteAllLines(tempPath, records);
    string markdown = TranscriptMarkdownFormatter.Format(
      tempPath,
      AgentSource.Claude);
    string html = TranscriptHtmlRenderer.ToHtml(
      markdown,
      new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

    const string badOpen = "<blockquote>\n<details>\n</blockquote>";
    const string badClose = "<blockquote>\n</details>\n</blockquote>";
    if (html.Contains(badOpen, StringComparison.Ordinal) ||
        html.Contains(badClose, StringComparison.Ordinal))
    {
      Console.Error.WriteLine("INTERLEAVED_MARKDOWN_BEGIN");
      Console.Error.WriteLine(markdown);
      Console.Error.WriteLine("INTERLEAVED_MARKDOWN_END");
      Console.Error.WriteLine("INTERLEAVED_HTML_BEGIN");
      Console.Error.WriteLine(html);
      Console.Error.WriteLine("INTERLEAVED_HTML_END");
    }

    Reject(html, badOpen, "misnested nested-details opening", failures);
    Reject(html, badClose, "misnested details closing", failures);

    int outerStart = html.IndexOf("<details>", StringComparison.Ordinal);
    int outerEnd = outerStart < 0
      ? -1
      : html.LastIndexOf("</details>", StringComparison.Ordinal);
    int nestedSummary = html.IndexOf(
      "<summary>Get current time</summary>",
      StringComparison.Ordinal);
    int afterToolAnchor = html.IndexOf(
      "data-source-id=\"interleaved-thought-2\"",
      StringComparison.Ordinal);
    if (outerStart < 0 || outerEnd <= outerStart)
    {
      failures.Add("Interleaved Claude regression has no complete outer details.");
    }
    else
    {
      if (nestedSummary <= outerStart || nestedSummary >= outerEnd)
      {
        failures.Add("Nested time tool escaped the outer reasoning disclosure.");
      }
      if (afterToolAnchor <= outerStart || afterToolAnchor >= outerEnd)
      {
        failures.Add("Reasoning after the time tool escaped the outer disclosure.");
      }
    }
  }
  finally
  {
    try
    {
      File.Delete(tempPath);
    }
    catch (IOException)
    {
      // Best-effort cleanup of a temporary regression fixture.
    }
  }
}

'''
program.write_text(text.replace(marker, method + marker, 1), encoding='utf-8')
