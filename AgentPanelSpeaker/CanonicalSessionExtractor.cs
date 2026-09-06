using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns the accumulated valid JSONL record set used by the persistent
/// AIConversationCore bridge for initial history and live appends.
/// </summary>
internal sealed class CanonicalSessionExtractor : IDisposable
{
  private readonly AIConversationCoreClient _client = new();
  private readonly List<string> _jsonLines = new();
  private AgentSource? _source;
  private AIConversationCoreProjectOptions _options = new();
  private bool _disposed;

  /// <summary>
  /// Primes the accumulated session without producing extraction output.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="jsonLines">Existing source records.</param>
  /// <param name="options">Canonical provider projection options.</param>
  public void Prime(
    AgentSource source,
    IEnumerable<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    ThrowIfDisposed();
    _source = source;
    _options = options ?? new();
    _jsonLines.Clear();
    foreach (string line in jsonLines)
    {
      if (IsValidJsonRecord(line))
      {
        _jsonLines.Add(line);
      }
    }
  }

  /// <summary>
  /// Loads and canonically extracts one complete current session.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="jsonLines">Existing source records.</param>
  /// <param name="options">Canonical provider projection options.</param>
  /// <returns>One extraction result per valid source record.</returns>
  public IReadOnlyList<ExtractionResult> Load(
    AgentSource source,
    IEnumerable<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    Prime(source, jsonLines, options);
    if (_jsonLines.Count == 0)
    {
      return Array.Empty<ExtractionResult>();
    }

    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      _client.Project(source, _jsonLines, _options));
    var results = new List<ExtractionResult>(_jsonLines.Count);
    for (int sourceIndex = 0; sourceIndex < _jsonLines.Count; ++sourceIndex)
    {
      results.Add(CanonicalProjectionExtractor.ExtractRecord(
        projection,
        source,
        sourceIndex));
    }
    return results;
  }

  /// <summary>
  /// Adds one newly appended valid record and returns its canonical extraction.
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="line">New complete JSONL record.</param>
  /// <returns>
  /// Canonical extraction for the appended record, or null when the line is
  /// malformed/blank and therefore is not added to the canonical record set.
  /// </returns>
  public ExtractionResult? Append(AgentSource source, string line)
  {
    ThrowIfDisposed();
    if (_source is not AgentSource currentSource || currentSource != source)
    {
      throw new InvalidOperationException(
        "Canonical session extraction must be primed for the selected source " +
        "before live records are appended.");
    }
    if (!IsValidJsonRecord(line))
    {
      return null;
    }

    _jsonLines.Add(line);
    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      _client.Project(source, _jsonLines, _options));
    return CanonicalProjectionExtractor.ExtractRecord(
      projection,
      source,
      _jsonLines.Count - 1);
  }

  /// <summary>
  /// Stops the persistent core worker and releases bridge resources.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _client.Dispose();
    _jsonLines.Clear();
    _source = null;
    _options = new();
  }

  /// <summary>
  /// Returns whether a line is one complete JSON value suitable as a record.
  /// </summary>
  private static bool IsValidJsonRecord(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return false;
    }
    try
    {
      using JsonDocument document = JsonDocument.Parse(line);
      return document.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
