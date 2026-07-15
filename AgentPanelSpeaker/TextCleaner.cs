using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Converts assistant Markdown into concise text suitable for speech.
/// </summary>
internal static partial class TextCleaner
{
  /// <summary>
  /// Removes Markdown structure, links, HTML, and optionally fenced code.
  /// </summary>
  /// <param name="text">Raw assistant text.</param>
  /// <param name="skipFencedCode">Whether fenced code bodies are omitted.</param>
  /// <returns>Normalized speech text.</returns>
  public static string CleanForSpeech(string text, bool skipFencedCode)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    string withoutFences = skipFencedCode
      ? RemoveFencedCode(text)
      : text;
    string cleaned = SystemTagRegex().Replace(withoutFences, " ");
    cleaned = ImageRegex().Replace(cleaned, " ");
    cleaned = LinkRegex().Replace(cleaned, "$1");
    cleaned = RawUrlRegex().Replace(cleaned, " ");
    cleaned = HtmlTagRegex().Replace(cleaned, " ");
    cleaned = InlineCodeRegex().Replace(cleaned, "$1");
    cleaned = MarkdownPrefixRegex().Replace(cleaned, string.Empty);
    cleaned = MarkdownDecorationRegex().Replace(cleaned, "$1");
    cleaned = cleaned.Replace('\uFFFC', ' ');
    cleaned = WebUtility.HtmlDecode(cleaned);
    return NormalizeWhitespace(cleaned);
  }

  /// <summary>
  /// Removes complete Markdown code fences and their bodies.
  /// </summary>
  private static string RemoveFencedCode(string text)
  {
    var output = new StringBuilder(text.Length);
    bool inFence = false;
    char fenceCharacter = '\0';
    int fenceLength = 0;

    foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
    {
      string trimmed = line.TrimStart();
      if (TryReadFence(trimmed, out char currentCharacter, out int currentLength))
      {
        if (!inFence)
        {
          inFence = true;
          fenceCharacter = currentCharacter;
          fenceLength = currentLength;
        }
        else if (currentCharacter == fenceCharacter &&
                 currentLength >= fenceLength)
        {
          inFence = false;
          fenceCharacter = '\0';
          fenceLength = 0;
        }

        continue;
      }

      if (!inFence)
      {
        output.AppendLine(line);
      }
    }

    return output.ToString();
  }

  /// <summary>
  /// Identifies a Markdown backtick or tilde fence.
  /// </summary>
  private static bool TryReadFence(
    string line,
    out char fenceCharacter,
    out int fenceLength)
  {
    fenceCharacter = '\0';
    fenceLength = 0;
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

    return fenceLength >= 3;
  }

  /// <summary>
  /// Collapses whitespace while preserving sentence separation.
  /// </summary>
  private static string NormalizeWhitespace(string text)
  {
    string normalized = text.Replace("\r\n", "\n");
    normalized = BlankLineRegex().Replace(normalized, ". ");
    normalized = WhitespaceRegex().Replace(normalized, " ");
    normalized = SpaceBeforePunctuationRegex().Replace(normalized, "$1");
    return normalized.Trim();
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
