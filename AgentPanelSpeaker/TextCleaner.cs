using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Converts Markdown into prose blocks and typed fenced-code lines.
/// </summary>
internal static partial class TextCleaner
{
  /// <summary>
  /// Parses Markdown while preserving structural block boundaries and every
  /// fenced block for live policy use.
  /// </summary>
  public static IReadOnlyList<SpeechTextPart> ParseForSpeech(string text)
  {
    var result = new List<SpeechTextPart>();
    if (string.IsNullOrWhiteSpace(text))
    {
      return result;
    }

    var prose = new StringBuilder();
    var fenceLines = new List<string>();
    bool inFence = false;
    char fenceCharacter = '\0';
    int fenceLength = 0;
    string fenceType = string.Empty;
    int fenceBlockId = 0;

    foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
    {
      string trimmed = line.TrimStart();
      if (TryReadFence(
            trimmed,
            out char currentCharacter,
            out int currentLength,
            out string currentType))
      {
        if (!inFence)
        {
          FlushProse(result, prose);
          inFence = true;
          fenceCharacter = currentCharacter;
          fenceLength = currentLength;
          fenceType = currentType.Length == 0 ? "untyped" : currentType;
          fenceLines.Clear();
        }
        else if (currentCharacter == fenceCharacter &&
                 currentLength >= fenceLength)
        {
          FlushFence(result, fenceLines, fenceType, fenceBlockId++);
          inFence = false;
          fenceCharacter = '\0';
          fenceLength = 0;
          fenceType = string.Empty;
        }
        else
        {
          fenceLines.Add(line);
        }

        continue;
      }

      if (inFence)
      {
        fenceLines.Add(line);
      }
      else
      {
        prose.AppendLine(line);
      }
    }

    if (inFence)
    {
      FlushFence(result, fenceLines, fenceType, fenceBlockId);
    }
    else
    {
      FlushProse(result, prose);
    }

    return result;
  }

  /// <summary>
  /// Cleans and appends every structural prose block in one accumulated section.
  /// </summary>
  private static void FlushProse(
    ICollection<SpeechTextPart> result,
    StringBuilder prose)
  {
    string text = SystemTagRegex().Replace(prose.ToString(), " ");
    prose.Clear();
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    AppendProseBlocks(result, text.Replace("\r\n", "\n").Split('\n'));
  }

  /// <summary>
  /// Converts Markdown source lines into independently navigable prose blocks.
  /// </summary>
  private static void AppendProseBlocks(
    ICollection<SpeechTextPart> result,
    IReadOnlyList<string> lines)
  {
    var current = new StringBuilder();
    ProseBlockKind currentKind = ProseBlockKind.None;

    for (int index = 0; index < lines.Count; ++index)
    {
      string line = lines[index];
      if (string.IsNullOrWhiteSpace(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        continue;
      }

      Match heading = AtxHeadingRegex().Match(line);
      if (heading.Success)
      {
        FlushCurrentBlock(result, current, ref currentKind);
        AddProseBlock(result, heading.Groups[1].Value);
        continue;
      }

      if (index + 1 < lines.Count &&
          SetextUnderlineRegex().IsMatch(lines[index + 1]) &&
          !string.IsNullOrWhiteSpace(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        AddProseBlock(result, line);
        ++index;
        continue;
      }

      if (ThematicBreakRegex().IsMatch(line) ||
          TableSeparatorRegex().IsMatch(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        continue;
      }

      if (ListItemRegex().IsMatch(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        currentKind = ProseBlockKind.ListItem;
        current.AppendLine(line);
        continue;
      }

      if (QuoteLineRegex().IsMatch(line))
      {
        if (currentKind != ProseBlockKind.Quote)
        {
          FlushCurrentBlock(result, current, ref currentKind);
          currentKind = ProseBlockKind.Quote;
        }
        current.AppendLine(line);
        continue;
      }

      if (IndentedCodeRegex().IsMatch(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        AddProseBlock(result, IndentedCodeRegex().Replace(line, "$1"));
        continue;
      }

      if (IsTableRow(line))
      {
        FlushCurrentBlock(result, current, ref currentKind);
        AddProseBlock(result, NormalizeTableRow(line));
        continue;
      }

      if (currentKind == ProseBlockKind.None)
      {
        currentKind = ProseBlockKind.Paragraph;
      }
      current.AppendLine(line);
    }

    FlushCurrentBlock(result, current, ref currentKind);
  }

  /// <summary>
  /// Appends one completed prose block after Markdown cleanup.
  /// </summary>
  private static void FlushCurrentBlock(
    ICollection<SpeechTextPart> result,
    StringBuilder current,
    ref ProseBlockKind currentKind)
  {
    if (current.Length != 0)
    {
      AddProseBlock(result, current.ToString());
      current.Clear();
    }
    currentKind = ProseBlockKind.None;
  }

  /// <summary>
  /// Cleans and appends one non-empty structural prose block.
  /// </summary>
  private static void AddProseBlock(
    ICollection<SpeechTextPart> result,
    string text)
  {
    string cleaned = CleanProseBlock(text);
    if (cleaned.Length == 0)
    {
      return;
    }

    result.Add(new SpeechTextPart(
      SpeechFragmentKind.Prose,
      cleaned,
      string.Empty,
      -1,
      -1,
      0,
      PauseAfter: true));
  }

  /// <summary>
  /// Appends one entry for every non-empty line in a fenced block.
  /// </summary>
  private static void FlushFence(
    ICollection<SpeechTextPart> result,
    IReadOnlyList<string> lines,
    string fenceType,
    int blockId)
  {
    string[] nonEmpty = lines
      .Select(line => line.Trim())
      .Where(line => line.Length != 0)
      .ToArray();
    for (int index = 0; index < nonEmpty.Length; ++index)
    {
      result.Add(new SpeechTextPart(
        SpeechFragmentKind.FencedCodeLine,
        nonEmpty[index],
        fenceType.ToLowerInvariant(),
        blockId,
        index,
        nonEmpty.Length,
        PauseAfter: true));
    }
  }

  /// <summary>
  /// Removes non-spoken Markdown structure from one prose block.
  /// </summary>
  private static string CleanProseBlock(string text)
  {
    string cleaned = ImageRegex().Replace(text, " ");
    cleaned = LinkRegex().Replace(cleaned, "$1");
    cleaned = RawUrlRegex().Replace(cleaned, " ");
    cleaned = HtmlTagRegex().Replace(cleaned, " ");
    cleaned = InlineCodeRegex().Replace(cleaned, "$1");
    cleaned = StripMarkdownPrefixes(cleaned);
    cleaned = MarkdownDecorationRegex().Replace(cleaned, "$1");
    cleaned = cleaned.Replace('\uFFFC', ' ');
    cleaned = WebUtility.HtmlDecode(cleaned);
    cleaned = WhitespaceRegex().Replace(cleaned, " ");
    cleaned = SpaceBeforePunctuationRegex().Replace(cleaned, "$1");
    return cleaned.Trim();
  }

  /// <summary>
  /// Removes nested block prefixes such as quote-plus-list markers.
  /// </summary>
  private static string StripMarkdownPrefixes(string text)
  {
    string current = text;
    for (int pass = 0; pass < 8; ++pass)
    {
      string next = MarkdownPrefixRegex().Replace(current, string.Empty);
      if (string.Equals(next, current, StringComparison.Ordinal))
      {
        return next;
      }
      current = next;
    }
    return current;
  }

  /// <summary>
  /// Returns whether one line is a pipe-delimited Markdown table row.
  /// </summary>
  private static bool IsTableRow(string line)
  {
    string trimmed = line.Trim();
    return trimmed.Length > 2 &&
      trimmed.Contains('|') &&
      (trimmed.StartsWith('|') || trimmed.EndsWith('|'));
  }

  /// <summary>
  /// Converts table-cell separators into spoken pauses between cell contents.
  /// </summary>
  private static string NormalizeTableRow(string line)
  {
    return string.Join(
      ", ",
      line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()));
  }

  /// <summary>
  /// Reads a backtick or tilde fence and its first info-string token.
  /// </summary>
  private static bool TryReadFence(
    string line,
    out char fenceCharacter,
    out int fenceLength,
    out string fenceType)
  {
    fenceCharacter = '\0';
    fenceLength = 0;
    fenceType = string.Empty;
    if (line.Length < 3 || line[0] is not ('`' or '~'))
    {
      return false;
    }

    fenceCharacter = line[0];
    while (fenceLength < line.Length &&
           line[fenceLength] == fenceCharacter)
    {
      ++fenceLength;
    }

    if (fenceLength < 3)
    {
      return false;
    }

    string info = line[fenceLength..].Trim();
    fenceType = info.Split(
      (char[]?)null,
      StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    return true;
  }

  [GeneratedRegex(
    @"<(?:ide_opened_file|ide_selection|system[-_]reminder|system|env|" +
    @"claude_background_info|user[-_]prompt[-_]submit[-_]hook|" +
    @"command[-_]name|antml:[a-z_]+)[^>]*>.*?</[^>]+>",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex SystemTagRegex();

  [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
  private static partial Regex ImageRegex();

  [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
  private static partial Regex LinkRegex();

  [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
  private static partial Regex RawUrlRegex();

  [GeneratedRegex(@"<[^>]+>")]
  private static partial Regex HtmlTagRegex();

  [GeneratedRegex(@"`+([^`]+?)`+")]
  private static partial Regex InlineCodeRegex();

  [GeneratedRegex(@"^\s{0,3}#{1,6}[ \t]+(.+?)(?:[ \t]+#+[ \t]*)?$")]
  private static partial Regex AtxHeadingRegex();

  [GeneratedRegex(@"^\s{0,3}(?:=+|-+)\s*$")]
  private static partial Regex SetextUnderlineRegex();

  [GeneratedRegex(@"^\s{0,3}(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$")]
  private static partial Regex ThematicBreakRegex();

  [GeneratedRegex(@"^\s{0,3}(?:[-+*]|\d+[.)])[ \t]+")]
  private static partial Regex ListItemRegex();

  [GeneratedRegex(@"^\s{0,3}>[ \t]?")]
  private static partial Regex QuoteLineRegex();

  [GeneratedRegex(@"^(?:\t| {4})(.*)$")]
  private static partial Regex IndentedCodeRegex();

  [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$")]
  private static partial Regex TableSeparatorRegex();

  [GeneratedRegex(
    @"(?m)^\s{0,3}(?:#{1,6}\s+|>\s*|[-+*]\s+|\d+[.)]\s+)")]
  private static partial Regex MarkdownPrefixRegex();

  [GeneratedRegex(@"(?:\*\*|__|~~)(.+?)(?:\*\*|__|~~)")]
  private static partial Regex MarkdownDecorationRegex();

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  [GeneratedRegex(@"\s+([,.;:!?])")]
  private static partial Regex SpaceBeforePunctuationRegex();

  /// <summary>
  /// Identifies the current structural prose block while source lines are read.
  /// </summary>
  private enum ProseBlockKind
  {
    None,
    Paragraph,
    ListItem,
    Quote
  }
}

/// <summary>
/// Describes one cleaned prose block or fenced-code line.
/// </summary>
internal sealed record SpeechTextPart(
  SpeechFragmentKind Kind,
  string Text,
  string FenceType,
  int FenceBlockId,
  int FenceLineIndex,
  int FenceLineCount,
  bool PauseAfter);
