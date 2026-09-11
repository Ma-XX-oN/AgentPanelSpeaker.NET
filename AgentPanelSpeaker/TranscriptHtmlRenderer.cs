using Markdig;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Converts transcript Markdown to HTML while preserving Markdown content inside
/// HTML <c>&lt;details&gt;</c> disclosures.
/// </summary>
internal static partial class TranscriptHtmlRenderer
{
  /// <summary>
  /// Renders transcript Markdown with nested details bodies parsed as Markdown.
  /// </summary>
  public static string ToHtml(string markdown, MarkdownPipeline pipeline)
  {
    ArgumentNullException.ThrowIfNull(markdown);
    ArgumentNullException.ThrowIfNull(pipeline);
    string normalized = markdown
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    return RenderRange(normalized, pipeline);
  }

  private static string RenderRange(string markdown, MarkdownPipeline pipeline)
  {
    var output = new StringBuilder(markdown.Length + 256);
    int position = 0;
    while (TryFindDetails(markdown, position, out DetailsRange range))
    {
      if (range.OpenStart > position)
      {
        output.Append(Markdown.ToHtml(
          markdown[position..range.OpenStart],
          pipeline));
      }

      string body = markdown[range.BodyStart..range.CloseStart];
      output.Append("<details>");
      output.Append("<summary>");
      output.Append(range.SummaryHtml);
      output.AppendLine("</summary>");
      output.Append(RenderDetailsBody(body, pipeline));
      output.AppendLine("</details>");
      position = range.CloseEnd;
    }

    if (position < markdown.Length)
    {
      output.Append(Markdown.ToHtml(markdown[position..], pipeline));
    }
    return output.ToString();
  }

  /// <summary>
  /// Renders a details body while preserving AIConversationCore's outer
  /// blockquote as HTML structure rather than Markdown syntax. AgentPanelSpeaker
  /// inserts hidden record anchors into these bodies; parsing the leading quote
  /// markers after that insertion can otherwise expose literal greater-than
  /// characters or break thought separators. Exactly one outer quote level is
  /// removed and restored as one HTML blockquote; nested quote levels remain.
  /// </summary>
  private static string RenderDetailsBody(
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
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    var output = new StringBuilder(normalized.Length);
    foreach (string line in normalized.Split('\n'))
    {
      Match structural = QuotedDetailsStructuralPrefixRegex().Match(line);
      output.AppendLine(structural.Success
        ? line[structural.Length..]
        : line);
    }
    return output.ToString();
  }

  /// <summary>
  /// Removes exactly one common Markdown blockquote level when every nonblank
  /// line in a disclosure body belongs to that outer quote.
  /// </summary>
  private static bool TryStripOuterBlockquote(
    string markdown,
    out string stripped)
  {
    string normalized = markdown
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');
    string[] lines = normalized.Split('\n');
    var output = new StringBuilder(normalized.Length);
    bool foundQuotedContent = false;

    foreach (string line in lines)
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        output.AppendLine();
        continue;
      }

      Match quote = OuterBlockquotePrefixRegex().Match(line);
      if (!quote.Success)
      {
        stripped = markdown;
        return false;
      }

      foundQuotedContent = true;
      output.AppendLine(line[quote.Length..]);
    }

    stripped = output.ToString();
    return foundQuotedContent;
  }

  /// <summary>
  /// Finds one balanced details disclosure. Nested disclosure tags may be
  /// blockquoted in the canonical Markdown, so balancing deliberately scans
  /// both quoted and unquoted details tags while the outer search remains
  /// restricted to an unquoted opening tag.
  /// </summary>
  private static bool TryFindDetails(
    string markdown,
    int start,
    out DetailsRange range)
  {
    range = default;
    Match open = DetailsOpenRegex().Match(markdown, start);
    if (!open.Success)
    {
      return false;
    }

    int depth = 1;
    int scan = open.Index + open.Length;
    Match close = default!;
    while (depth > 0)
    {
      Match nextOpen = DetailsScanOpenRegex().Match(markdown, scan);
      Match nextClose = DetailsScanCloseRegex().Match(markdown, scan);
      if (!nextClose.Success)
      {
        return false;
      }
      if (nextOpen.Success && nextOpen.Index < nextClose.Index)
      {
        depth++;
        scan = nextOpen.Index + nextOpen.Length;
        continue;
      }
      depth--;
      close = nextClose;
      scan = nextClose.Index + nextClose.Length;
    }

    string opening = open.Value;
    Match summary = SummaryRegex().Match(opening);
    int bodyStart = open.Index + open.Length;
    if (!summary.Success)
    {
      Match separateSummary = SummaryLineRegex().Match(markdown, bodyStart);
      if (!separateSummary.Success)
      {
        return false;
      }
      summary = separateSummary;
      bodyStart = separateSummary.Index + separateSummary.Length;
    }

    string summaryHtml = WebUtility.HtmlEncode(
      Regex.Replace(summary.Groups["text"].Value, "<[^>]+>", string.Empty));
    range = new DetailsRange(
      open.Index,
      bodyStart,
      close.Index,
      close.Index + close.Length,
      summaryHtml);
    return true;
  }

  private readonly record struct DetailsRange(
    int OpenStart,
    int BodyStart,
    int CloseStart,
    int CloseEnd,
    string SummaryHtml);

  [GeneratedRegex(
    @"(?m)^\s*<details>(?:\s*<summary>.*?</summary>)?\s*\n?",
    RegexOptions.CultureInvariant)]
  private static partial Regex DetailsOpenRegex();

  [GeneratedRegex(
    @"(?m)^\s*</details>\s*\n?",
    RegexOptions.CultureInvariant)]
  private static partial Regex DetailsCloseRegex();

  // Balancing must see nested disclosure tags even when the canonical Markdown
  // places them one or more blockquote levels deep. The outer search remains
  // unquoted; these expressions are used only after an outer details has been
  // found, to identify its matching close correctly.
  [GeneratedRegex(
    @"(?m)^[ \t]*(?:>[ \t]?)*<details>(?:\s*<summary>.*?</summary>)?\s*\n?",
    RegexOptions.CultureInvariant)]
  private static partial Regex DetailsScanOpenRegex();

  [GeneratedRegex(
    @"(?m)^[ \t]*(?:>[ \t]?)*</details>\s*\n?",
    RegexOptions.CultureInvariant)]
  private static partial Regex DetailsScanCloseRegex();

  [GeneratedRegex(
    @"<summary>(?<text>.*?)</summary>",
    RegexOptions.CultureInvariant | RegexOptions.Singleline)]
  private static partial Regex SummaryRegex();

  [GeneratedRegex(
    @"\G\s*<summary>(?<text>.*?)</summary>\s*\n?",
    RegexOptions.CultureInvariant | RegexOptions.Singleline)]
  private static partial Regex SummaryLineRegex();

  [GeneratedRegex(
    @"^[ \t]*>[ \t]?",
    RegexOptions.CultureInvariant)]
  private static partial Regex OuterBlockquotePrefixRegex();

  [GeneratedRegex(
    @"^[ \t]*(?:>[ \t]?)+(?=</?(?:details|summary)\b)",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
  private static partial Regex QuotedDetailsStructuralPrefixRegex();
}
