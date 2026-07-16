namespace AgentPanelSpeaker;

/// <summary>
/// Normalizes the user-maintained list of tokens that SAPI must spell.
/// </summary>
internal sealed class SpelledWordSet
{
  private readonly HashSet<string> _words;

  private SpelledWordSet(IReadOnlyList<string> orderedWords)
  {
    OrderedWords = orderedWords;
    NormalizedText = string.Join(Environment.NewLine, orderedWords);
    _words = new HashSet<string>(orderedWords, StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Gets normalized words in their first-entered order.
  /// </summary>
  public IReadOnlyList<string> OrderedWords { get; }

  /// <summary>
  /// Gets normalized one-word-per-line text.
  /// </summary>
  public string NormalizedText { get; }

  /// <summary>
  /// Gets whether the normalized set contains a token.
  /// </summary>
  public bool Contains(string value)
  {
    return _words.Contains(value);
  }

  /// <summary>
  /// Parses, trims, de-duplicates, and preserves first occurrence order.
  /// </summary>
  public static SpelledWordSet Parse(string? text)
  {
    var ordered = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string line in (text ?? string.Empty)
      .Replace("\r\n", "\n")
      .Replace('\r', '\n')
      .Split('\n'))
    {
      string word = line.Trim();
      if (word.Length != 0 && seen.Add(word))
      {
        ordered.Add(word);
      }
    }
    return new SpelledWordSet(ordered);
  }
}
