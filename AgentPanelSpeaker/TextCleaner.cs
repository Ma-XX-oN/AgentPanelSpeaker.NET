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

    int nextFenceBlockId = 0;
    ParseMarkdownInto(
      result,
      text,
      ref nextFenceBlockId,
      string.Empty,
      -1,
      0);
    return result;
  }

  /// <summary>
  /// Parses one Markdown source region, including nested fenced blocks.
  /// </summary>
  private static void ParseMarkdownInto(
    ICollection<SpeechTextPart> result,
    string text,
    ref int nextFenceBlockId,
    string enclosingFenceType,
    int enclosingFenceBlockId,
    int enclosingFenceLineCount)
  {
    var prose = new StringBuilder();
    var fenceLines = new List<string>();
    bool inFence = false;
    char fenceCharacter = '\0';
    int fenceLength = 0;
    string fenceType = string.Empty;
    int fenceBlockId = -1;

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
          FlushProse(
            result,
            prose,
            enclosingFenceType,
            enclosingFenceBlockId,
            enclosingFenceLineCount);
          inFence = true;
          fenceCharacter = currentCharacter;
          fenceLength = currentLength;
          fenceType = currentType.Length == 0 ? "untyped" : currentType;
          fenceBlockId = nextFenceBlockId++;
          fenceLines.Clear();
        }
        else if (currentCharacter == fenceCharacter &&
                 currentLength >= fenceLength &&
                 currentType.Length == 0)
        {
          FlushFence(
            result,
            fenceLines,
            fenceType,
            fenceBlockId,
            ref nextFenceBlockId);
          inFence = false;
          fenceCharacter = '\0';
          fenceLength = 0;
          fenceType = string.Empty;
          fenceBlockId = -1;
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
      FlushFence(
        result,
        fenceLines,
        fenceType,
        fenceBlockId,
        ref nextFenceBlockId);
    }
    else
    {
      FlushProse(
        result,
        prose,
        enclosingFenceType,
        enclosingFenceBlockId,
        enclosingFenceLineCount);
    }
  }

  /// <summary>
  /// Cleans and appends every structural prose block in one accumulated section.
  /// </summary>
  private static void FlushProse(
    ICollection<SpeechTextPart> result,
    StringBuilder prose,
    string fenceType,
    int fenceBlockId,
    int fenceLineCount)
  {
    string text = SystemTagRegex().Replace(prose.ToString(), " ");
    prose.Clear();
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    AppendProseBlocks(
      result,
      text.Replace("\r\n", "\n").Split('\n'),
      fenceType,
      fenceBlockId,
      fenceLineCount);
  }

  /// <summary>
  /// Converts Markdown source lines into independently navigable prose blocks.
  /// </summary>
  private static void AppendProseBlocks(
    ICollection<SpeechTextPart> result,
    IReadOnlyList<string> lines,
    string fenceType,
    int fenceBlockId,
    int fenceLineCount)
  {
    var current = new StringBuilder();
    ProseBlockKind currentKind = ProseBlockKind.None;

    for (int index = 0; index < lines.Count; ++index)
    {
      string line = lines[index];
      if (string.IsNullOrWhiteSpace(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        continue;
      }

      Match heading = AtxHeadingRegex().Match(line);
      if (heading.Success)
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        AddProseBlock(
          result,
          heading.Groups[1].Value,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        continue;
      }

      if (index + 1 < lines.Count &&
          SetextUnderlineRegex().IsMatch(lines[index + 1]) &&
          !string.IsNullOrWhiteSpace(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        AddProseBlock(
          result,
          line,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        ++index;
        continue;
      }

      if (ThematicBreakRegex().IsMatch(line) ||
          TableSeparatorRegex().IsMatch(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        continue;
      }

      if (ListItemRegex().IsMatch(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        currentKind = ProseBlockKind.ListItem;
        current.AppendLine(line);
        continue;
      }

      if (QuoteLineRegex().IsMatch(line))
      {
        if (currentKind != ProseBlockKind.Quote)
        {
          FlushCurrentBlock(
            result,
            current,
            ref currentKind,
            fenceType,
            fenceBlockId,
            fenceLineCount);
          currentKind = ProseBlockKind.Quote;
        }
        current.AppendLine(line);
        continue;
      }

      if (IndentedCodeRegex().IsMatch(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        AddProseBlock(
          result,
          IndentedCodeRegex().Replace(line, "$1"),
          fenceType,
          fenceBlockId,
          fenceLineCount);
        continue;
      }

      if (IsTableRow(line))
      {
        FlushCurrentBlock(
          result,
          current,
          ref currentKind,
          fenceType,
          fenceBlockId,
          fenceLineCount);
        AddProseBlock(
          result,
          NormalizeTableRow(line),
          fenceType,
          fenceBlockId,
          fenceLineCount);
        continue;
      }

      if (currentKind == ProseBlockKind.None)
      {
        currentKind = ProseBlockKind.Paragraph;
      }
      current.AppendLine(line);
    }

    FlushCurrentBlock(
      result,
      current,
      ref currentKind,
      fenceType,
      fenceBlockId,
      fenceLineCount);
  }

  /// <summary>
  /// Appends one completed prose block after Markdown cleanup.
  /// </summary>
  private static void FlushCurrentBlock(
    ICollection<SpeechTextPart> result,
    StringBuilder current,
    ref ProseBlockKind currentKind,
    string fenceType,
    int fenceBlockId,
    int fenceLineCount)
  {
    if (current.Length != 0)
    {
      AddProseBlock(
        result,
        current.ToString(),
        fenceType,
        fenceBlockId,
        fenceLineCount);
      current.Clear();
    }
    currentKind = ProseBlockKind.None;
  }

  /// <summary>
  /// Cleans and appends one non-empty structural prose block.
  /// </summary>
  private static void AddProseBlock(
    ICollection<SpeechTextPart> result,
    string text,
    string fenceType,
    int fenceBlockId,
    int fenceLineCount)
  {
    string cleaned = CleanProseBlock(text);
    if (cleaned.Length == 0)
    {
      return;
    }

    result.Add(new SpeechTextPart(
      SpeechFragmentKind.Prose,
      cleaned,
      fenceType,
      fenceBlockId,
      -1,
      fenceLineCount,
      PauseAfter: true));
  }

  /// <summary>
  /// Appends Markdown fence contents through the prose parser, and every other
  /// fenced block as one entry per non-empty source line.
  /// </summary>
  private static void FlushFence(
    ICollection<SpeechTextPart> result,
    IReadOnlyList<string> lines,
    string fenceType,
    int blockId,
    ref int nextFenceBlockId)
  {
    string normalizedType = fenceType.ToLowerInvariant();
    int nonEmptyLineCount = lines.Count(line => !string.IsNullOrWhiteSpace(line));
    if (string.Equals(normalizedType, "md", StringComparison.Ordinal))
    {
      ParseMarkdownInto(
        result,
        string.Join("\n", lines),
        ref nextFenceBlockId,
        normalizedType,
        blockId,
        nonEmptyLineCount);
      return;
    }

    string[] nonEmpty = lines
      .Select(line => line.Trim())
      .Where(line => line.Length != 0)
      .ToArray();
    for (int index = 0; index < nonEmpty.Length; ++index)
    {
      result.Add(new SpeechTextPart(
        SpeechFragmentKind.FencedCodeLine,
        nonEmpty[index],
        normalizedType,
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
    if (line.Length == 0 || line[0] is not ('`' or '~'))
    {
      return false;
    }

    fenceCharacter = line[0];
    while (fenceLength < line.Length &&
           line[fenceLength] == fenceCharacter)
    {
      ++fenceLength;
    }

    string info = line[fenceLength..].Trim();
    if (info.Contains(fenceCharacter))
    {
      return false;
    }

    if (fenceLength < 3 && info.Any(char.IsWhiteSpace))
    {
      return false;
    }

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
