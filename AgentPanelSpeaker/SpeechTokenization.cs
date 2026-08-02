using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Identifies visible speech units, including operators and punctuation that a
/// voice reports as spoken progress.
/// </summary>
internal static partial class SpeechTokenization
{
  /// <summary>
  /// Returns every lexical word or individual non-whitespace symbol in order.
  /// </summary>
  public static MatchCollection Matches(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    return TokenRegex().Matches(text);
  }

  /// <summary>
  /// Returns the first visible speech unit, or an empty string for blank text.
  /// </summary>
  public static string First(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    Match match = TokenRegex().Match(text);
    return match.Success ? match.Value : string.Empty;
  }

  [GeneratedRegex(
    @"(?<![\p{L}\p{M}\p{N}_.])\d*\.\d+(?!\.\d)(?=[fFlL]|\b)" +
    @"|\.+" +
    @"|[\p{L}\p{M}\p{N}_]+(?:['’\-][\p{L}\p{M}\p{N}_]+)*" +
    @"|[^\s]")]
  private static partial Regex TokenRegex();
}
