namespace AgentPanelSpeaker;

/// <summary>
/// Splits one complete JSONL narration block into replayable speech fragments.
/// </summary>
internal static class SentenceSegmenter
{
  /// <summary>
  /// Splits text at sentence punctuation and applies one structural pause to the
  /// final non-empty sentence in the block.
  /// </summary>
  /// <param name="text">Normalized speech text.</param>
  /// <param name="pauseAfterLast">
  /// Whether the final sentence ends a structural Markdown block.
  /// </param>
  /// <returns>Speech fragments in source order.</returns>
  public static IReadOnlyList<SentenceSegment> Split(
    string text,
    bool pauseAfterLast)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return Array.Empty<SentenceSegment>();
    }

    var result = new List<SentenceSegment>();
    int start = 0;
    int index = 0;
    while (index < text.Length)
    {
      char current = text[index];
      if (current is '.' or '?' or '!')
      {
        int end = index + 1;
        while (end < text.Length && text[end] is '"' or '\'' or ')' or ']' or '}')
        {
          ++end;
        }

        if (end == text.Length || char.IsWhiteSpace(text[end]))
        {
          Add(result, text[start..end]);
          start = end;
          while (start < text.Length && char.IsWhiteSpace(text[start]))
          {
            ++start;
          }
          index = start;
          continue;
        }
      }

      ++index;
    }

    if (start < text.Length)
    {
      Add(result, text[start..]);
    }

    if (pauseAfterLast && result.Count != 0)
    {
      result[^1] = result[^1] with { PauseAfter = true };
    }

    return result;
  }

  /// <summary>
  /// Adds a non-empty trimmed fragment without creating an empty boundary.
  /// </summary>
  private static void Add(List<SentenceSegment> result, string text)
  {
    string trimmed = text.Trim();
    if (trimmed.Length != 0)
    {
      result.Add(new SentenceSegment(trimmed, PauseAfter: false));
    }
  }
}

/// <summary>
/// Describes one sentence and whether a structural pause follows it.
/// </summary>
internal sealed record SentenceSegment(string Text, bool PauseAfter);
