namespace AgentPanelSpeaker;

/// <summary>
/// Reconciles a virtualized two-paragraph transcript tail and emits new speech.
/// </summary>
internal sealed class TailTracker
{
  private readonly bool _speakExistingText;
  private string _previousParagraph = string.Empty;
  private string _currentParagraph = string.Empty;
  private int _spokenLength;
  private DateTime _lastChangeUtc;
  private bool _initialized;

  /// <summary>
  /// Initializes a tracker.
  /// </summary>
  /// <param name="speakExistingText">
  /// Whether text already present at startup should be spoken.
  /// </param>
  public TailTracker(bool speakExistingText)
  {
    _speakExistingText = speakExistingText;
  }

  /// <summary>
  /// Reconciles a newly observed transcript tail.
  /// </summary>
  /// <param name="tail">Up to the final visible transcript paragraphs.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <returns>New complete speech fragments.</returns>
  public IReadOnlyList<string> Observe(
    IReadOnlyList<string> tail,
    DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(tail);

    var output = new List<string>();
    IReadOnlyList<string> cleanTail = RemoveEmptyAdjacentDuplicates(tail);
    if (cleanTail.Count == 0)
    {
      return output;
    }

    if (!_initialized)
    {
      Initialize(cleanTail, nowUtc, output);
      return output;
    }

    int currentIndex = FindCurrentAnchor(cleanTail);
    if (currentIndex >= 0)
    {
      UpdateCurrent(cleanTail[currentIndex], nowUtc, output);
      AdvanceThroughNewParagraphs(
        cleanTail,
        currentIndex + 1,
        nowUtc,
        output);
      return output;
    }

    int previousIndex = FindExact(cleanTail, _previousParagraph);
    if (previousIndex >= 0 && previousIndex + 1 < cleanTail.Count)
    {
      string next = cleanTail[previousIndex + 1];
      if (CanSafelyReplaceCurrent(next))
      {
        ReplaceCurrent(next, nowUtc, output);
      }
      else
      {
        AdvanceToParagraph(next, nowUtc, output);
      }

      AdvanceThroughNewParagraphs(
        cleanTail,
        previousIndex + 2,
        nowUtc,
        output);
      return output;
    }

    string latest = cleanTail[^1];
    if (CanSafelyReplaceCurrent(latest))
    {
      ReplaceCurrent(latest, nowUtc, output);
      return output;
    }

    RecoverAfterLostAnchor(cleanTail, nowUtc, output);
    return output;
  }

  /// <summary>
  /// Emits an unfinished suffix after the configured inactivity period.
  /// </summary>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="idleTimeout">Required unchanged duration.</param>
  /// <returns>An unfinished speech fragment, when one is ready.</returns>
  public IReadOnlyList<string> FlushIfIdle(
    DateTime nowUtc,
    TimeSpan idleTimeout)
  {
    if (idleTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(
        nameof(idleTimeout),
        idleTimeout,
        "The idle timeout must be positive.");
    }

    if (!_initialized ||
        _spokenLength >= _currentParagraph.Length ||
        nowUtc - _lastChangeUtc < idleTimeout)
    {
      return Array.Empty<string>();
    }

    var output = new List<string>(1);
    FlushCurrent(output);
    return output;
  }

  /// <summary>
  /// Initializes state from the first observed tail.
  /// </summary>
  /// <param name="tail">The initial transcript tail.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for immediate speech.</param>
  private void Initialize(
    IReadOnlyList<string> tail,
    DateTime nowUtc,
    List<string> output)
  {
    _previousParagraph = tail.Count >= 2 ? tail[^2] : string.Empty;
    _currentParagraph = tail[^1];
    _spokenLength = _speakExistingText ? 0 : _currentParagraph.Length;
    _lastChangeUtc = nowUtc;
    _initialized = true;

    if (_speakExistingText)
    {
      ExtractCompleteSentences(output);
    }
  }

  /// <summary>
  /// Finds the paragraph that represents the stored current paragraph.
  /// </summary>
  /// <param name="tail">The newly observed transcript tail.</param>
  /// <returns>The matching index, or -1.</returns>
  private int FindCurrentAnchor(IReadOnlyList<string> tail)
  {
    for (int index = tail.Count - 1; index >= 0; --index)
    {
      string candidate = tail[index];
      if (string.Equals(candidate, _currentParagraph,
            StringComparison.Ordinal) ||
          candidate.StartsWith(_currentParagraph,
            StringComparison.Ordinal))
      {
        return index;
      }

      if (_spokenLength != 0 &&
          _spokenLength <= _currentParagraph.Length &&
          candidate.Length >= _spokenLength &&
          candidate.StartsWith(
            _currentParagraph[.._spokenLength],
            StringComparison.Ordinal))
      {
        return index;
      }
    }

    return -1;
  }

  /// <summary>
  /// Finds an exact paragraph match.
  /// </summary>
  /// <param name="tail">The observed tail.</param>
  /// <param name="value">The paragraph to find.</param>
  /// <returns>The matching index, or -1.</returns>
  private static int FindExact(
    IReadOnlyList<string> tail,
    string value)
  {
    if (value.Length == 0)
    {
      return -1;
    }

    for (int index = tail.Count - 1; index >= 0; --index)
    {
      if (string.Equals(tail[index], value, StringComparison.Ordinal))
      {
        return index;
      }
    }

    return -1;
  }

  /// <summary>
  /// Updates the current paragraph after an append or pending-text rewrite.
  /// </summary>
  /// <param name="observed">The observed current paragraph.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for complete speech.</param>
  private void UpdateCurrent(
    string observed,
    DateTime nowUtc,
    List<string> output)
  {
    if (string.Equals(observed, _currentParagraph,
          StringComparison.Ordinal))
    {
      return;
    }

    if (observed.StartsWith(_currentParagraph,
          StringComparison.Ordinal))
    {
      _currentParagraph = observed;
      _lastChangeUtc = nowUtc;
      ExtractCompleteSentences(output);
      return;
    }

    ReplaceCurrent(observed, nowUtc, output);
  }

  /// <summary>
  /// Replaces a rewritten current paragraph without repeating spoken text.
  /// </summary>
  /// <param name="replacement">The replacement paragraph.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for complete speech.</param>
  private void ReplaceCurrent(
    string replacement,
    DateTime nowUtc,
    List<string> output)
  {
    if (string.Equals(replacement, _currentParagraph,
          StringComparison.Ordinal))
    {
      return;
    }

    bool spokenPrefixPreserved =
      _spokenLength <= replacement.Length &&
      _spokenLength <= _currentParagraph.Length &&
      replacement.StartsWith(
        _currentParagraph[.._spokenLength],
        StringComparison.Ordinal);

    _currentParagraph = replacement;
    _lastChangeUtc = nowUtc;

    if (!spokenPrefixPreserved)
    {
      _spokenLength = replacement.Length;
      return;
    }

    ExtractCompleteSentences(output);
  }

  /// <summary>
  /// Processes all paragraphs that follow the current anchor.
  /// </summary>
  /// <param name="tail">The observed tail.</param>
  /// <param name="startIndex">The first new paragraph index.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for speech.</param>
  private void AdvanceThroughNewParagraphs(
    IReadOnlyList<string> tail,
    int startIndex,
    DateTime nowUtc,
    List<string> output)
  {
    for (int index = startIndex; index < tail.Count; ++index)
    {
      AdvanceToParagraph(tail[index], nowUtc, output);
    }
  }

  /// <summary>
  /// Completes the old paragraph and begins tracking a new paragraph.
  /// </summary>
  /// <param name="paragraph">The new paragraph.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for speech.</param>
  private void AdvanceToParagraph(
    string paragraph,
    DateTime nowUtc,
    List<string> output)
  {
    if (string.Equals(paragraph, _currentParagraph,
          StringComparison.Ordinal))
    {
      return;
    }

    FlushCurrent(output);
    _previousParagraph = _currentParagraph;
    _currentParagraph = paragraph;
    _spokenLength = 0;
    _lastChangeUtc = nowUtc;
    ExtractCompleteSentences(output);
  }

  /// <summary>
  /// Determines whether a current rewrite preserves all spoken characters.
  /// </summary>
  /// <param name="candidate">The replacement candidate.</param>
  /// <returns>True when replacing the current paragraph is safe.</returns>
  private bool CanSafelyReplaceCurrent(string candidate)
  {
    if (_spokenLength == 0)
    {
      return true;
    }

    return _spokenLength <= candidate.Length &&
      _spokenLength <= _currentParagraph.Length &&
      candidate.StartsWith(
        _currentParagraph[.._spokenLength],
        StringComparison.Ordinal);
  }

  /// <summary>
  /// Recovers when virtual scrolling removes both stored anchors.
  /// </summary>
  /// <param name="tail">The new unanchored tail.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="output">Destination for speech.</param>
  private void RecoverAfterLostAnchor(
    IReadOnlyList<string> tail,
    DateTime nowUtc,
    List<string> output)
  {
    FlushCurrent(output);

    int first = Math.Max(0, tail.Count - 2);
    _previousParagraph = _currentParagraph;
    _currentParagraph = string.Empty;
    _spokenLength = 0;

    for (int index = first; index < tail.Count; ++index)
    {
      AdvanceToParagraph(tail[index], nowUtc, output);
    }
  }

  /// <summary>
  /// Emits all complete sentences in the unspoken suffix.
  /// </summary>
  /// <param name="output">Destination for speech.</param>
  private void ExtractCompleteSentences(List<string> output)
  {
    int end = FindLastSentenceBoundary(
      _currentParagraph,
      _spokenLength);
    if (end <= _spokenLength)
    {
      return;
    }

    AddSpeech(
      _currentParagraph[_spokenLength..end],
      output);
    _spokenLength = SkipWhitespace(_currentParagraph, end);
  }

  /// <summary>
  /// Emits the complete remaining suffix of the current paragraph.
  /// </summary>
  /// <param name="output">Destination for speech.</param>
  private void FlushCurrent(List<string> output)
  {
    if (_spokenLength >= _currentParagraph.Length)
    {
      return;
    }

    AddSpeech(_currentParagraph[_spokenLength..], output);
    _spokenLength = _currentParagraph.Length;
  }

  /// <summary>
  /// Finds the final sentence terminator followed by whitespace or text end.
  /// </summary>
  /// <param name="text">The current paragraph.</param>
  /// <param name="start">The first unspoken character.</param>
  /// <returns>The exclusive speech boundary, or the original start.</returns>
  private static int FindLastSentenceBoundary(string text, int start)
  {
    int boundary = start;

    for (int index = start; index < text.Length; ++index)
    {
      char character = text[index];
      if (character != '.' && character != '?' && character != '!')
      {
        continue;
      }

      int next = index + 1;
      if (next == text.Length || char.IsWhiteSpace(text[next]))
      {
        boundary = next;
      }
    }

    return boundary;
  }

  /// <summary>
  /// Skips whitespace after a spoken boundary.
  /// </summary>
  /// <param name="text">The paragraph.</param>
  /// <param name="start">The first candidate character.</param>
  /// <returns>The first non-whitespace character or text length.</returns>
  private static int SkipWhitespace(string text, int start)
  {
    int index = start;
    while (index < text.Length && char.IsWhiteSpace(text[index]))
    {
      ++index;
    }

    return index;
  }

  /// <summary>
  /// Adds a non-empty speech fragment.
  /// </summary>
  /// <param name="text">Text to add.</param>
  /// <param name="output">Destination list.</param>
  private static void AddSpeech(string text, List<string> output)
  {
    string trimmed = text.Trim();
    if (trimmed.Length != 0)
    {
      output.Add(trimmed);
    }
  }

  /// <summary>
  /// Removes empty values and adjacent duplicate paragraphs.
  /// </summary>
  /// <param name="tail">Raw observed paragraphs.</param>
  /// <returns>A compact tail.</returns>
  private static IReadOnlyList<string> RemoveEmptyAdjacentDuplicates(
    IReadOnlyList<string> tail)
  {
    var result = new List<string>(tail.Count);

    foreach (string paragraph in tail)
    {
      if (string.IsNullOrWhiteSpace(paragraph))
      {
        continue;
      }

      string trimmed = paragraph.Trim();
      if (result.Count != 0 &&
          string.Equals(result[^1], trimmed,
            StringComparison.Ordinal))
      {
        continue;
      }

      result.Add(trimmed);
    }

    return result;
  }
}
