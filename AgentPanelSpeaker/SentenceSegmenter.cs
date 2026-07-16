namespace AgentPanelSpeaker;

/// <summary>
/// Splits one complete JSONL narration node into replayable speech fragments.
/// </summary>
internal static class SentenceSegmenter
{
  /// <summary>
  /// Splits text at sentence punctuation and retains an unpunctuated tail.
  /// </summary>
  /// <param name="text">Normalized speech text.</param>
  /// <returns>Speech fragments in source order.</returns>
  public static IReadOnlyList<string> Split(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return Array.Empty<string>();
    }

    var result = new List<string>();
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

        int nextNonWhitespace = end;
        while (nextNonWhitespace < text.Length &&
               char.IsWhiteSpace(text[nextNonWhitespace]))
        {
          ++nextNonWhitespace;
        }
        bool precedesHeadingPause =
          nextNonWhitespace < text.Length &&
          text[nextNonWhitespace] == SpeechTextMarkers.HeadingPause;
        if (!precedesHeadingPause &&
            (end == text.Length || char.IsWhiteSpace(text[end])))
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

    return result;
  }

  /// <summary>
  /// Adds a non-empty trimmed fragment.
  /// </summary>
  private static void Add(List<string> result, string text)
  {
    string trimmed = text.Trim();
    if (trimmed.Length != 0)
    {
      result.Add(trimmed);
    }
  }
}
