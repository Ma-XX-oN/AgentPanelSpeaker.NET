using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Converts Markdown into prose sections and typed fenced-code lines.
/// </summary>
internal static partial class TextCleaner
{
  /// <summary>
  /// Parses Markdown while preserving every fenced block for live policy use.
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
  /// Cleans and appends one accumulated prose section.
  /// </summary>
  private static void FlushProse(
    ICollection<SpeechTextPart> result,
    StringBuilder prose)
  {
    string cleaned = CleanProse(prose.ToString());
    prose.Clear();
    if (cleaned.Length != 0)
    {
      result.Add(new SpeechTextPart(
        SpeechFragmentKind.Prose,
        cleaned,
        string.Empty,
        -1,
        -1,
        0));
    }
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
        nonEmpty.Length));
    }
  }

  /// <summary>
  /// Removes non-spoken Markdown structure from prose.
  /// </summary>
  private static string CleanProse(string text)
  {
    string cleaned = SystemTagRegex().Replace(text, " ");
    cleaned = ImageRegex().Replace(cleaned, " ");
    cleaned = LinkRegex().Replace(cleaned, "$1");
    cleaned = RawUrlRegex().Replace(cleaned, " ");
    cleaned = HtmlTagRegex().Replace(cleaned, " ");
    cleaned = InlineCodeRegex().Replace(cleaned, "$1");
    cleaned = MarkdownPrefixRegex().Replace(cleaned, string.Empty);
    cleaned = MarkdownDecorationRegex().Replace(cleaned, "$1");
    cleaned = cleaned.Replace('\uFFFC', ' ');
    cleaned = WebUtility.HtmlDecode(cleaned);
    cleaned = BlankLineRegex().Replace(cleaned, ". ");
    cleaned = WhitespaceRegex().Replace(cleaned, " ");
    cleaned = SpaceBeforePunctuationRegex().Replace(cleaned, "$1");
    return cleaned.Trim();
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

  [GeneratedRegex(
    @"(?m)^\s{0,3}(?:#{1,6}\s+|>\s*|[-+*]\s+|\d+[.)]\s+)")]
  private static partial Regex MarkdownPrefixRegex();

  [GeneratedRegex(@"(?:\*\*|__|~~)(.+?)(?:\*\*|__|~~)")]
  private static partial Regex MarkdownDecorationRegex();

  [GeneratedRegex(@"\n\s*\n+")]
  private static partial Regex BlankLineRegex();

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  [GeneratedRegex(@"\s+([,.;:!?])")]
  private static partial Regex SpaceBeforePunctuationRegex();
}

/// <summary>
/// Describes one cleaned prose section or fenced-code line.
/// </summary>
internal sealed record SpeechTextPart(
  SpeechFragmentKind Kind,
  string Text,
  string FenceType,
  int FenceBlockId,
  int FenceLineIndex,
  int FenceLineCount);
