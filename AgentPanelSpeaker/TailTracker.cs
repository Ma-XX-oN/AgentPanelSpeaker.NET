using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Reconciles a virtualized transcript window, emits unseen sentences, and
/// suppresses text already spoken before accessibility nodes are replaced.
/// </summary>
internal sealed class TailTracker
{
  private const int MaximumRememberedSentences = 512;
  private const int MaximumLostOverlapRecoveryNodes = 12;
  private const int MinimumPrefixMatch = 16;

  private readonly bool _speakExistingText;
  private readonly Queue<string> _sentenceHistory = new();
  private readonly HashSet<string> _sentenceSet = new(
    StringComparer.Ordinal);
  private List<NodeState> _nodes = new();
  private string _snapshotText = string.Empty;
  private string _spokenPartial = string.Empty;
  private DateTime _lastChangeUtc;
  private long _nextNodeId = 1;
  private bool _initialized;
  private int _lastOverlapCount;
  private int _lastOverlapEnd;
  private string _lastDecision = "not initialized";

  /// <summary>
  /// Initializes a tracker.
  /// </summary>
  /// <param name="speakExistingText">
  /// Whether text already visible on the first observation should be spoken.
  /// </param>
  public TailTracker(bool speakExistingText)
  {
    _speakExistingText = speakExistingText;
  }

  /// <summary>
  /// Gets the latest reconciliation decision for diagnostics.
  /// </summary>
  public string LastDecision => _lastDecision;

  /// <summary>
  /// Creates a compact snapshot of the tracking state.
  /// </summary>
  /// <returns>Current node, history, partial, and timing state.</returns>
  public string DescribeState()
  {
    return $"initialized={_initialized}; " +
      $"nodes={_nodes.Count}; " +
      $"snapshotLength={_snapshotText.Length}; " +
      $"rememberedSentences={_sentenceHistory.Count}; " +
      $"overlap={_lastOverlapCount}@{_lastOverlapEnd}; " +
      $"spokenPartial={Abbreviate(_spokenPartial)}; " +
      $"lastChangeUtc={_lastChangeUtc:O}; " +
      $"decision={_lastDecision}";
  }

  /// <summary>
  /// Reconciles a newly observed transcript window.
  /// </summary>
  /// <param name="paragraphs">Meaningful transcript paragraphs.</param>
  /// <param name="nowUtc">Current UTC time.</param>
  /// <returns>New sentence fragments in transcript order.</returns>
  public IReadOnlyList<SpeechFragment> Observe(
    IReadOnlyList<string> paragraphs,
    DateTime nowUtc)
  {
    ArgumentNullException.ThrowIfNull(paragraphs);

    IReadOnlyList<string> clean = NormalizeParagraphs(paragraphs);
    if (clean.Count == 0)
    {
      _lastDecision = "empty observation";
      return Array.Empty<SpeechFragment>();
    }

    ObservationOverlap overlap = _initialized
      ? FindObservationOverlap(_nodes, clean)
      : ObservationOverlap.None;
    _lastOverlapCount = overlap.Count;
    _lastOverlapEnd = overlap.CurrentEndIndex;
    List<NodeState> reconciledNodes = ReconcileNodes(clean);
    Snapshot snapshot = BuildSnapshot(reconciledNodes);
    bool changed = !string.Equals(
      snapshot.Text,
      _snapshotText,
      StringComparison.Ordinal);
    _nodes = reconciledNodes;

    if (!_initialized)
    {
      _initialized = true;
      _snapshotText = snapshot.Text;
      _lastChangeUtc = nowUtc;

      IReadOnlyList<SentenceSpan> initialSentences =
        ExtractCompleteSentences(snapshot);
      if (!_speakExistingText)
      {
        MarkSnapshotAsSpoken(snapshot);
        _lastDecision = "initialized and marked visible text as spoken";
        return Array.Empty<SpeechFragment>();
      }

      _lastDecision = "initialized and emitted visible sentences";
      return EmitUnseen(initialSentences);
    }

    if (!changed)
    {
      _lastDecision = "unchanged observation";
      return Array.Empty<SpeechFragment>();
    }

    _snapshotText = snapshot.Text;
    _lastChangeUtc = nowUtc;
    if (_lastOverlapCount == 0)
    {
      int recoveryStart = Math.Max(
        0,
        _nodes.Count - MaximumLostOverlapRecoveryNodes);
      Snapshot recoverySnapshot = BuildSnapshot(
        _nodes.Skip(recoveryStart).ToArray());
      IReadOnlyList<SpeechFragment> recoveryOutput = EmitUnseen(
        ExtractCompleteSentences(recoverySnapshot));
      _lastDecision = recoveryOutput.Count == 0
        ? "lost observation overlap; no unseen complete sentence"
        : $"lost observation overlap; emitted " +
          $"{recoveryOutput.Count} unseen sentence(s)";
      return recoveryOutput;
    }

    int speechStart = Math.Max(0, overlap.CurrentEndIndex - 1);
    Snapshot speechSnapshot = BuildSnapshot(
      _nodes.Skip(speechStart).ToArray());
    IReadOnlyList<SpeechFragment> output = EmitUnseen(
      ExtractCompleteSentences(speechSnapshot));
    _lastDecision = output.Count == 0
      ? "changed observation; no unseen complete sentence"
      : $"changed observation; emitted {output.Count} sentence(s)";
    return output;
  }

  /// <summary>
  /// Emits an unfinished trailing fragment after the inactivity timeout.
  /// </summary>
  /// <param name="nowUtc">Current UTC time.</param>
  /// <param name="idleTimeout">Required unchanged duration.</param>
  /// <returns>An unspoken trailing fragment, when one exists.</returns>
  public IReadOnlyList<SpeechFragment> FlushIfIdle(
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
        nowUtc - _lastChangeUtc < idleTimeout ||
        _nodes.Count == 0)
    {
      return Array.Empty<SpeechFragment>();
    }

    Snapshot snapshot = BuildSnapshot(_nodes);
    SentenceSpan trailing = GetTrailingFragment(snapshot);
    if (trailing.CanonicalText.Length == 0)
    {
      return Array.Empty<SpeechFragment>();
    }

    string suffix = GetUnspokenPartialSuffix(trailing.CanonicalText);
    if (suffix.Length == 0)
    {
      return Array.Empty<SpeechFragment>();
    }

    _spokenPartial = trailing.CanonicalText;
    _lastDecision = "idle timeout emitted trailing fragment";
    return new[] { new SpeechFragment(trailing.NodeId, suffix) };
  }

  /// <summary>
  /// Marks every sentence and trailing partial in a snapshot as already read.
  /// </summary>
  /// <param name="snapshot">Snapshot to absorb without speaking.</param>
  private void MarkSnapshotAsSpoken(Snapshot snapshot)
  {
    foreach (SentenceSpan sentence in ExtractCompleteSentences(snapshot))
    {
      RememberSentence(sentence.CanonicalText);
    }

    _spokenPartial = GetTrailingFragment(snapshot).CanonicalText;
  }

  /// <summary>
  /// Finds a stable contiguous overlap between consecutive observations.
  /// </summary>
  /// <param name="previous">Previously reconciled nodes.</param>
  /// <param name="current">Current normalized paragraphs.</param>
  /// <returns>The strongest overlap and its current forward edge.</returns>
  private static ObservationOverlap FindObservationOverlap(
    IReadOnlyList<NodeState> previous,
    IReadOnlyList<string> current)
  {
    int bestCount = 0;
    int bestCharacters = 0;
    int bestCurrentEnd = 0;

    for (int oldStart = 0; oldStart < previous.Count; ++oldStart)
    {
      for (int newStart = 0; newStart < current.Count; ++newStart)
      {
        int oldIndex = oldStart;
        int newIndex = newStart;
        int count = 0;
        int characters = 0;

        while (oldIndex < previous.Count && newIndex < current.Count)
        {
          if (!ParagraphsOverlap(
                previous[oldIndex].Text,
                current[newIndex],
                out int matchedCharacters))
          {
            break;
          }

          ++count;
          characters += matchedCharacters;
          ++oldIndex;
          ++newIndex;
        }

        if (count > bestCount ||
            (count == bestCount && characters > bestCharacters))
        {
          bestCount = count;
          bestCharacters = characters;
          bestCurrentEnd = newStart + count;
        }
      }
    }

    return bestCount >= 2 || bestCharacters >= 32
      ? new ObservationOverlap(bestCount, bestCurrentEnd)
      : ObservationOverlap.None;
  }

  /// <summary>
  /// Determines whether two paragraphs are equal or one is a streaming prefix.
  /// </summary>
  /// <param name="left">Previous paragraph.</param>
  /// <param name="right">Current paragraph.</param>
  /// <param name="matchedCharacters">Stable prefix length.</param>
  /// <returns>True when the paragraphs overlap safely.</returns>
  private static bool ParagraphsOverlap(
    string left,
    string right,
    out int matchedCharacters)
  {
    matchedCharacters = CommonPrefixLength(left, right);
    int shorter = Math.Min(left.Length, right.Length);
    return matchedCharacters == shorter &&
      shorter >= MinimumPrefixMatch;
  }

  /// <summary>
  /// Emits complete sentences not present in bounded speech history.
  /// </summary>
  /// <param name="sentences">Current complete sentences.</param>
  /// <returns>Unseen speech fragments.</returns>
  private IReadOnlyList<SpeechFragment> EmitUnseen(
    IReadOnlyList<SentenceSpan> sentences)
  {
    var output = new List<SpeechFragment>();

    foreach (SentenceSpan sentence in sentences)
    {
      if (_sentenceSet.Contains(
            CreateDuplicateKey(sentence.CanonicalText)))
      {
        continue;
      }

      string text = sentence.Text;
      if (_spokenPartial.Length != 0 &&
          sentence.CanonicalText.StartsWith(
            _spokenPartial,
            StringComparison.Ordinal))
      {
        text = GetCanonicalSuffix(
          sentence.CanonicalText,
          _spokenPartial.Length);
      }

      RememberSentence(sentence.CanonicalText);
      _spokenPartial = string.Empty;
      if (HasSpeakableCharacters(text))
      {
        output.Add(new SpeechFragment(sentence.NodeId, text));
      }
    }

    return output;
  }

  /// <summary>
  /// Reconciles current paragraphs with the previous node identities.
  /// </summary>
  /// <param name="paragraphs">Current normalized paragraphs.</param>
  /// <returns>Current nodes in transcript order.</returns>
  private List<NodeState> ReconcileNodes(
    IReadOnlyList<string> paragraphs)
  {
    var result = new List<NodeState>(paragraphs.Count);
    var used = new bool[_nodes.Count];

    for (int newIndex = 0; newIndex < paragraphs.Count; ++newIndex)
    {
      string text = paragraphs[newIndex];
      int bestIndex = -1;
      int bestScore = 0;

      for (int oldIndex = 0; oldIndex < _nodes.Count; ++oldIndex)
      {
        if (used[oldIndex])
        {
          continue;
        }

        int score = CalculateNodeMatchScore(
          _nodes[oldIndex].Text,
          text,
          Math.Abs(oldIndex - newIndex));
        if (score > bestScore)
        {
          bestScore = score;
          bestIndex = oldIndex;
        }
      }

      if (bestIndex >= 0)
      {
        used[bestIndex] = true;
        result.Add(new NodeState(_nodes[bestIndex].Id, text));
      }
      else
      {
        result.Add(new NodeState(_nextNodeId++, text));
      }
    }

    return result;
  }

  /// <summary>
  /// Scores exact and streaming-prefix relationships between two nodes.
  /// </summary>
  /// <param name="oldText">Previous node text.</param>
  /// <param name="newText">Current node text.</param>
  /// <param name="indexDistance">Distance between list positions.</param>
  /// <returns>Zero for no match, otherwise a larger-is-better score.</returns>
  private static int CalculateNodeMatchScore(
    string oldText,
    string newText,
    int indexDistance)
  {
    if (string.Equals(oldText, newText, StringComparison.Ordinal))
    {
      return 1_000_000 - indexDistance;
    }

    int common = CommonPrefixLength(oldText, newText);
    int shorter = Math.Min(oldText.Length, newText.Length);
    if (common == shorter && shorter >= MinimumPrefixMatch)
    {
      return 500_000 + shorter - indexDistance;
    }

    return common >= 32
      ? common - indexDistance
      : 0;
  }

  /// <summary>
  /// Builds one text stream while retaining node spans for rewind grouping.
  /// </summary>
  /// <param name="nodes">Current nodes.</param>
  /// <returns>Combined text and source spans.</returns>
  private static Snapshot BuildSnapshot(IReadOnlyList<NodeState> nodes)
  {
    var builder = new StringBuilder();
    var spans = new List<NodeSpan>(nodes.Count);

    foreach (NodeState node in nodes)
    {
      if (builder.Length != 0)
      {
        builder.Append(' ');
      }

      int start = builder.Length;
      builder.Append(node.Text);
      spans.Add(new NodeSpan(start, builder.Length, node.Id));
    }

    return new Snapshot(builder.ToString(), spans);
  }

  /// <summary>
  /// Extracts every punctuation-complete sentence in display order.
  /// </summary>
  /// <param name="snapshot">Current transcript snapshot.</param>
  /// <returns>Complete sentences with source node identifiers.</returns>
  private static IReadOnlyList<SentenceSpan> ExtractCompleteSentences(
    Snapshot snapshot)
  {
    var result = new List<SentenceSpan>();
    int start = 0;

    for (int index = 0; index < snapshot.Text.Length; ++index)
    {
      char character = snapshot.Text[index];
      if (character is not ('.' or '?' or '!'))
      {
        continue;
      }

      int next = index + 1;
      if (next != snapshot.Text.Length &&
          !char.IsWhiteSpace(snapshot.Text[next]))
      {
        continue;
      }

      AddSentence(snapshot, start, next, result);
      start = SkipWhitespace(snapshot.Text, next);
      index = start - 1;
    }

    return result;
  }

  /// <summary>
  /// Returns the incomplete text after the final sentence boundary.
  /// </summary>
  /// <param name="snapshot">Current transcript snapshot.</param>
  /// <returns>Trailing fragment and source node.</returns>
  private static SentenceSpan GetTrailingFragment(Snapshot snapshot)
  {
    int start = 0;
    for (int index = 0; index < snapshot.Text.Length; ++index)
    {
      char character = snapshot.Text[index];
      if (character is not ('.' or '?' or '!'))
      {
        continue;
      }

      int next = index + 1;
      if (next == snapshot.Text.Length ||
          char.IsWhiteSpace(snapshot.Text[next]))
      {
        start = SkipWhitespace(snapshot.Text, next);
      }
    }

    string text = NormalizeSpeechText(snapshot.Text[start..]);
    return new SentenceSpan(
      FindNodeId(snapshot.Spans, Math.Max(start, snapshot.Text.Length - 1)),
      text,
      Canonicalize(text));
  }

  /// <summary>
  /// Adds one normalized sentence range.
  /// </summary>
  /// <param name="snapshot">Current transcript snapshot.</param>
  /// <param name="start">Inclusive source index.</param>
  /// <param name="end">Exclusive source index.</param>
  /// <param name="result">Destination list.</param>
  private static void AddSentence(
    Snapshot snapshot,
    int start,
    int end,
    List<SentenceSpan> result)
  {
    string text = NormalizeSpeechText(snapshot.Text[start..end]);
    string canonical = Canonicalize(text);
    if (canonical.Length == 0)
    {
      return;
    }

    result.Add(new SentenceSpan(
      FindNodeId(snapshot.Spans, Math.Max(start, end - 1)),
      text,
      canonical));
  }

  /// <summary>
  /// Finds the node containing a character position.
  /// </summary>
  /// <param name="spans">Ordered node spans.</param>
  /// <param name="position">Character position.</param>
  /// <returns>The containing or nearest node identifier.</returns>
  private static long FindNodeId(
    IReadOnlyList<NodeSpan> spans,
    int position)
  {
    foreach (NodeSpan span in spans)
    {
      if (position >= span.Start && position < span.End)
      {
        return span.NodeId;
      }
    }

    return spans.Count == 0 ? 0 : spans[^1].NodeId;
  }

  /// <summary>
  /// Remembers a sentence while bounding duplicate-suppression memory.
  /// </summary>
  /// <param name="canonical">Canonical sentence text.</param>
  private void RememberSentence(string canonical)
  {
    string duplicateKey = CreateDuplicateKey(canonical);
    if (duplicateKey.Length == 0 || !_sentenceSet.Add(duplicateKey))
    {
      return;
    }

    _sentenceHistory.Enqueue(duplicateKey);
    while (_sentenceHistory.Count > MaximumRememberedSentences)
    {
      string removed = _sentenceHistory.Dequeue();
      _sentenceSet.Remove(removed);
    }
  }

  /// <summary>
  /// Returns only the new suffix of an idle-flushed partial fragment.
  /// </summary>
  /// <param name="current">Current canonical trailing fragment.</param>
  /// <returns>Unspoken suffix or empty text.</returns>
  private string GetUnspokenPartialSuffix(string current)
  {
    if (string.Equals(current, _spokenPartial, StringComparison.Ordinal))
    {
      return string.Empty;
    }

    if (_spokenPartial.Length != 0 &&
        current.StartsWith(_spokenPartial, StringComparison.Ordinal))
    {
      return GetCanonicalSuffix(current, _spokenPartial.Length);
    }

    return current;
  }

  /// <summary>
  /// Returns a canonical suffix after skipping separating whitespace.
  /// </summary>
  /// <param name="text">Canonical full text.</param>
  /// <param name="offset">Already spoken prefix length.</param>
  /// <returns>Remaining text.</returns>
  private static string GetCanonicalSuffix(string text, int offset)
  {
    int index = Math.Clamp(offset, 0, text.Length);
    while (index < text.Length && char.IsWhiteSpace(text[index]))
    {
      ++index;
    }

    return text[index..].Trim();
  }

  /// <summary>
  /// Normalizes paragraphs and removes adjacent duplicates.
  /// </summary>
  /// <param name="paragraphs">Raw paragraphs.</param>
  /// <returns>Normalized paragraphs.</returns>
  private static IReadOnlyList<string> NormalizeParagraphs(
    IReadOnlyList<string> paragraphs)
  {
    var result = new List<string>(paragraphs.Count);
    foreach (string paragraph in paragraphs)
    {
      string normalized = NormalizeSpeechText(paragraph);
      if (normalized.Length == 0)
      {
        continue;
      }

      if (result.Count != 0 &&
          string.Equals(result[^1], normalized, StringComparison.Ordinal))
      {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  /// <summary>
  /// Removes accessibility placeholders, backticks, and excess whitespace.
  /// </summary>
  /// <param name="text">Text to normalize.</param>
  /// <returns>Speakable normalized text.</returns>
  private static string NormalizeSpeechText(string text)
  {
    var builder = new StringBuilder(text.Length);
    bool previousWasWhitespace = false;

    foreach (char character in text)
    {
      if (character is '\uFFFC' or '\u200B' or '\uFEFF' or '`')
      {
        continue;
      }

      if (char.IsWhiteSpace(character))
      {
        if (!previousWasWhitespace && builder.Length != 0)
        {
          builder.Append(' ');
        }

        previousWasWhitespace = true;
        continue;
      }

      builder.Append(character);
      previousWasWhitespace = false;
    }

    return builder.ToString().Trim();
  }

  /// <summary>
  /// Creates a stable duplicate-suppression representation.
  /// </summary>
  /// <param name="text">Normalized speech text.</param>
  /// <returns>Canonical text.</returns>
  private static string Canonicalize(string text)
  {
    return NormalizeSpeechText(text);
  }

  /// <summary>
  /// Creates a duplicate key that tolerates UI Automation whitespace changes.
  /// </summary>
  /// <param name="canonical">Canonical sentence or fragment text.</param>
  /// <returns>Case-insensitive text with all whitespace removed.</returns>
  private static string CreateDuplicateKey(string canonical)
  {
    var builder = new StringBuilder(canonical.Length);
    foreach (char character in canonical)
    {
      if (!char.IsWhiteSpace(character))
      {
        builder.Append(char.ToUpperInvariant(character));
      }
    }

    return builder.ToString();
  }

  /// <summary>
  /// Determines whether a fragment contains something other than punctuation.
  /// </summary>
  /// <param name="text">Candidate speech text.</param>
  /// <returns>True when the synthesizer should receive it.</returns>
  private static bool HasSpeakableCharacters(string text)
  {
    return text.Any(char.IsLetterOrDigit);
  }

  /// <summary>
  /// Counts the common prefix of two strings.
  /// </summary>
  /// <param name="left">First string.</param>
  /// <param name="right">Second string.</param>
  /// <returns>Number of equal leading characters.</returns>
  private static int CommonPrefixLength(string left, string right)
  {
    int limit = Math.Min(left.Length, right.Length);
    int index = 0;
    while (index < limit && left[index] == right[index])
    {
      ++index;
    }

    return index;
  }

  /// <summary>
  /// Skips whitespace after a sentence boundary.
  /// </summary>
  /// <param name="text">Source text.</param>
  /// <param name="start">First candidate index.</param>
  /// <returns>First non-whitespace index.</returns>
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
  /// Truncates text for one-line diagnostics.
  /// </summary>
  /// <param name="text">Text to abbreviate.</param>
  /// <returns>A quoted diagnostic value.</returns>
  private static string Abbreviate(string text)
  {
    const int maximumLength = 120;
    string escaped = text
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("\r", "\\r", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal)
      .Replace(";", "\\;", StringComparison.Ordinal);
    if (escaped.Length > maximumLength)
    {
      escaped = escaped[..maximumLength] + "…";
    }

    return $"\"{escaped}\"";
  }

  /// <summary>
  /// Stores the strongest overlap between consecutive observations.
  /// </summary>
  private sealed record ObservationOverlap(
    int Count,
    int CurrentEndIndex)
  {
    /// <summary>
    /// Gets an empty overlap.
    /// </summary>
    public static ObservationOverlap None { get; } = new(0, 0);
  }

  /// <summary>
  /// Stores one reconciled accessibility node.
  /// </summary>
  private sealed record NodeState(long Id, string Text);

  /// <summary>
  /// Stores a node's range in combined snapshot text.
  /// </summary>
  private sealed record NodeSpan(int Start, int End, long NodeId);

  /// <summary>
  /// Stores combined transcript text and node ranges.
  /// </summary>
  private sealed record Snapshot(
    string Text,
    IReadOnlyList<NodeSpan> Spans);

  /// <summary>
  /// Stores a complete sentence or trailing fragment.
  /// </summary>
  private sealed record SentenceSpan(
    long NodeId,
    string Text,
    string CanonicalText);
}
