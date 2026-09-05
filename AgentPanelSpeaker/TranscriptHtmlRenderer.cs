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
      output.Append(RenderRange(body, pipeline));
      output.AppendLine("</details>");
      position = range.CloseEnd;
    }

    if (position < markdown.Length)
    {
      output.Append(Markdown.ToHtml(markdown[position..], pipeline));
    }
    return output.ToString();
  }

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
      Match nextOpen = DetailsOpenRegex().Match(markdown, scan);
      Match nextClose = DetailsCloseRegex().Match(markdown, scan);
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

  [GeneratedRegex(
    @"<summary>(?<text>.*?)</summary>",
    RegexOptions.CultureInvariant | RegexOptions.Singleline)]
  private static partial Regex SummaryRegex();

  [GeneratedRegex(
    @"\G\s*<summary>(?<text>.*?)</summary>\s*\n?",
    RegexOptions.CultureInvariant | RegexOptions.Singleline)]
  private static partial Regex SummaryLineRegex();
}
