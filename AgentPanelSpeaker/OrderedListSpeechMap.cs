using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Adds non-visual ordered-list marker text to Markdown before HTML rendering so
/// speech/display mapping can address browser-generated list ordinals.  This is
/// presentation integration only; it does not interpret provider semantics.
/// </summary>
internal static partial class OrderedListSpeechMap
{
  /// <summary>
  /// Duplicates each Markdown ordered-list marker inside an aria-hidden inline
  /// span.  The browser's native list marker remains the only visible ordinal,
  /// while rendered-token mapping can see the same ordinal spoken by TTS.
  /// Fenced code is left untouched.
  /// </summary>
  public static string DecorateMarkdown(string markdown)
  {
    ArgumentNullException.ThrowIfNull(markdown);
    if (markdown.Length == 0)
    {
      return markdown;
    }

    string[] lines = markdown.Split('\n');
    var output = new StringBuilder(markdown.Length + 64);
    char fenceCharacter = '\0';
    int fenceLength = 0;

    for (int index = 0; index < lines.Length; ++index)
    {
      string line = lines[index];
      bool hadCarriageReturn = line.EndsWith('\r');
      string content = hadCarriageReturn ? line[..^1] : line;
      Match fence = FenceRegex().Match(content);
      if (fence.Success)
      {
        char candidate = fence.Groups["fence"].Value[0];
        int candidateLength = fence.Groups["fence"].Value.Length;
        if (fenceCharacter == '\0')
        {
          fenceCharacter = candidate;
          fenceLength = candidateLength;
        }
        else if (candidate == fenceCharacter && candidateLength >= fenceLength)
        {
          fenceCharacter = '\0';
          fenceLength = 0;
        }
      }
      else if (fenceCharacter == '\0')
      {
        Match marker = OrderedMarkerRegex().Match(content);
        if (marker.Success)
        {
          string ordinal = marker.Groups["ordinal"].Value;
          string delimiter = marker.Groups["delimiter"].Value;
          content = marker.Groups["prefix"].Value + ordinal + delimiter +
            marker.Groups["space"].Value +
            "<span class=\"speech-ordinal-map\" aria-hidden=\"true\" " +
            "style=\"display:none\">" + ordinal + delimiter + " </span>" +
            marker.Groups["rest"].Value;
        }
      }

      output.Append(content);
      if (hadCarriageReturn)
      {
        output.Append('\r');
      }
      if (index + 1 < lines.Length)
      {
        output.Append('\n');
      }
    }
    return output.ToString();
  }

  [GeneratedRegex(
    @"^(?<prefix>(?:[ \t]*>[ \t]*)*[ \t]*)(?<ordinal>\d{1,9})" +
    @"(?<delimiter>[.)])(?<space>[ \t]+)(?<rest>.*)$",
    RegexOptions.CultureInvariant)]
  private static partial Regex OrderedMarkerRegex();

  [GeneratedRegex(
    @"^[ \t]*(?:>[ \t]*)*(?<fence>`{3,}|~{3,})",
    RegexOptions.CultureInvariant)]
  private static partial Regex FenceRegex();
}
