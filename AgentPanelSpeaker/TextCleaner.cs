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
          fenceLineCount,
          SpeechTextStyle.Main);
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
          fenceLineCount,
          SpeechTextStyle.Main);
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
          fenceLineCount,
          SpeechTextStyle.Main);
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
          fenceLineCount,
          SpeechTextStyle.Main);
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
        fenceLineCount,
        currentKind == ProseBlockKind.Quote
          ? SpeechTextStyle.Context
          : SpeechTextStyle.Main);
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
    int fenceLineCount,
    SpeechTextStyle style)
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
      PauseAfter: true,
      Style: style));
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
        PauseAfter: true,
        Style: SpeechTextStyle.Main));
    }
  }

  /// <summary>
  /// Removes non-spoken Markdown structure from one prose block.
  /// </summary>
  private static string CleanProseBlock(string text)
  {
    var inlineCode = new List<string>();
    string cleaned = ProtectInlineCode(text, inlineCode);
    cleaned = ImageRegex().Replace(cleaned, " ");
    cleaned = LinkRegex().Replace(cleaned, "$1");
    cleaned = RawUrlRegex().Replace(cleaned, " ");
    cleaned = RemoveHtmlMarkup(cleaned);
    cleaned = StripMarkdownPrefixes(cleaned);
    cleaned = MarkdownDecorationRegex().Replace(cleaned, "$1");
    cleaned = cleaned.Replace('\uFFFC', ' ');
    cleaned = WebUtility.HtmlDecode(cleaned);
    cleaned = WhitespaceRegex().Replace(cleaned, " ");
    cleaned = SpaceBeforePunctuationRegex().Replace(cleaned, "$1");
    cleaned = RestoreInlineCode(cleaned, inlineCode);
    return cleaned.Trim();
  }

  /// <summary>
  /// Replaces Markdown inline-code spans with opaque placeholders before any
  /// prose cleanup can mistake code punctuation for links, HTML, or markup.
  /// </summary>
  private static string ProtectInlineCode(
    string text,
    ICollection<string> inlineCode)
  {
    var protectedText = new StringBuilder(text.Length);
    int index = 0;
    while (index < text.Length)
    {
      if (text[index] != '`')
      {
        protectedText.Append(text[index++]);
        continue;
      }

      int openingStart = index;
      while (index < text.Length && text[index] == '`')
      {
        ++index;
      }

      int markerLength = index - openingStart;
      int closingStart = FindClosingBacktickRun(text, index, markerLength);
      if (closingStart < 0)
      {
        protectedText.Append(text, openingStart, markerLength);
        continue;
      }

      string code = text[index..closingStart].Replace("\r\n", "\n");
      code = WhitespaceRegex().Replace(code, " ");
      if (code.Length >= 2 &&
          code[0] == ' ' &&
          code[^1] == ' ' &&
          code.Any(character => character != ' '))
      {
        code = code[1..^1];
      }

      int placeholderIndex = inlineCode.Count;
      inlineCode.Add(WebUtility.HtmlDecode(code));
      protectedText.Append(GetInlineCodePlaceholder(placeholderIndex));
      index = closingStart + markerLength;
    }

    return protectedText.ToString();
  }

  /// <summary>
  /// Finds the next backtick run whose length exactly matches the opener.
  /// </summary>
  private static int FindClosingBacktickRun(
    string text,
    int startIndex,
    int markerLength)
  {
    int index = startIndex;
    while (index < text.Length)
    {
      int runStart = text.IndexOf('`', index);
      if (runStart < 0)
      {
        return -1;
      }

      int runEnd = runStart;
      while (runEnd < text.Length && text[runEnd] == '`')
      {
        ++runEnd;
      }

      if (runEnd - runStart == markerLength)
      {
        return runStart;
      }

      index = runEnd;
    }

    return -1;
  }

  /// <summary>
  /// Restores protected inline-code contents after all destructive cleanup.
  /// </summary>
  private static string RestoreInlineCode(
    string text,
    IReadOnlyList<string> inlineCode)
  {
    string restored = text;
    for (int index = 0; index < inlineCode.Count; ++index)
    {
      restored = restored.Replace(
        GetInlineCodePlaceholder(index),
        inlineCode[index],
        StringComparison.Ordinal);
    }
    return restored;
  }

  /// <summary>
  /// Returns one placeholder that cannot be interpreted as Markdown or HTML.
  /// </summary>
  private static string GetInlineCodePlaceholder(int index)
  {
    return $"\uE000{index}\uE001";
  }

  /// <summary>
  /// Removes balanced common HTML elements and genuine void elements without
  /// treating arbitrary angle-bracket expressions as tags.
  /// </summary>
  private static string RemoveHtmlMarkup(string text)
  {
    string cleaned = HtmlCommentRegex().Replace(text, " ");
    for (int pass = 0; pass < 8; ++pass)
    {
      string next = HtmlPairedTagRegex().Replace(cleaned, "$2");
      if (string.Equals(next, cleaned, StringComparison.Ordinal))
      {
        break;
      }
      cleaned = next;
    }

    return HtmlVoidTagRegex().Replace(cleaned, " ");
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

  [GeneratedRegex(@"<!--[\s\S]*?-->")]
  private static partial Regex HtmlCommentRegex();

  [GeneratedRegex(
    @"<\s*(a|abbr|address|article|aside|audio|b|blockquote|button|" +
    @"canvas|caption|cite|code|data|datalist|dd|del|details|dfn|" +
    @"dialog|div|dl|dt|em|fieldset|figcaption|figure|footer|form|" +
    @"h[1-6]|header|hgroup|i|iframe|ins|kbd|label|legend|li|main|" +
    @"map|mark|menu|meter|nav|noscript|object|ol|optgroup|option|" +
    @"output|p|picture|pre|progress|q|rp|rt|ruby|s|samp|script|" +
    @"search|section|select|slot|small|span|strong|style|sub|summary|" +
    @"sup|table|tbody|td|template|textarea|tfoot|th|thead|time|" +
    @"title|tr|u|ul|var|video)\b[^<>]*>([\s\S]*?)" +
    @"<\s*/\s*\1\s*>",
    RegexOptions.IgnoreCase)]
  private static partial Regex HtmlPairedTagRegex();

  [GeneratedRegex(
    @"<\s*(?:area|base|br|col|embed|hr|img|input|link|meta|param|" +
    @"source|track|wbr)\b[^<>]*/?\s*>",
    RegexOptions.IgnoreCase)]
  private static partial Regex HtmlVoidTagRegex();

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
  bool PauseAfter,
  SpeechTextStyle Style);
