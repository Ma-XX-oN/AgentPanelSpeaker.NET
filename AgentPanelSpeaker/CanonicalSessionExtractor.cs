using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns one retained AIConversationCore session for initial history and live
/// appends. Projection changes and appended records never resend the unchanged
/// provider prefix.
/// </summary>
internal sealed class CanonicalSessionExtractor : IDisposable
{
  private readonly AIConversationCoreClient _client = new();
  private readonly List<string> _jsonLines = new();
  private AgentSource? _source;
  private AIConversationCoreProjectOptions _options = new();
  private string? _sessionId;
  private AIConversationProjection? _projection;
  private bool _disposed;

  /// <summary>
  /// Primes the retained canonical session without producing extraction output.
  /// Re-priming the exact same source inventory only changes projection options;
  /// it does not normalize the provider records again.
  /// </summary>
  public void Prime(
    AgentSource source,
    IEnumerable<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    ThrowIfDisposed();
    AIConversationCoreProjectOptions effective = options ?? new();
    string[] validLines = jsonLines.Where(IsValidJsonRecord).ToArray();

    if (_sessionId is not null &&
        _source == source &&
        _jsonLines.SequenceEqual(validLines, StringComparer.Ordinal))
    {
      _options = effective;
      AIConversationCoreRetainedSession projected =
        _client.ProjectRetainedSession(_sessionId, _options);
      _projection = projected.Projection;
      LogSessionDiagnostics("reproject", projected.Diagnostics);
      return;
    }

    CloseRetainedSession();
    _source = source;
    _options = effective;
    _jsonLines.Clear();
    _jsonLines.AddRange(validLines);
    if (_jsonLines.Count == 0)
    {
      _projection = null;
      return;
    }

    AIConversationCoreRetainedSession retained =
      _client.CreateRetainedSession(source, _jsonLines, _options);
    _sessionId = retained.Id;
    _projection = retained.Projection;
    LogSessionDiagnostics("create", retained.Diagnostics);
  }

  /// <summary>
  /// Loads and canonically extracts one complete current session.
  /// </summary>
  public IReadOnlyList<ExtractionResult> Load(
    AgentSource source,
    IEnumerable<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    Prime(source, jsonLines, options);
    if (_projection is null)
    {
      return Array.Empty<ExtractionResult>();
    }

    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      _projection);
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
  /// Reprojects the already-normalized session with new presentation/speech
  /// options. Provider records are not resent or adapted again.
  /// </summary>
  public AIConversationProjection? Reproject(
    AIConversationCoreProjectOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    ThrowIfDisposed();
    _options = options;
    if (_sessionId is null)
    {
      return null;
    }

    AIConversationCoreRetainedSession projected =
      _client.ProjectRetainedSession(_sessionId, _options);
    _projection = projected.Projection;
    LogSessionDiagnostics("reproject", projected.Diagnostics);
    return _projection;
  }

  /// <summary>
  /// Adds one newly appended valid record and returns its canonical extraction.
  /// Only this new record is sent to Core; the unchanged prefix remains retained
  /// by the worker session.
  /// </summary>
  public ExtractionResult? Append(AgentSource source, string line)
  {
    ThrowIfDisposed();
    if (_source is not AgentSource currentSource || currentSource != source ||
        _sessionId is null)
    {
      throw new InvalidOperationException(
        "Canonical session extraction must be primed for the selected source " +
        "before live records are appended.");
    }
    if (!IsValidJsonRecord(line))
    {
      return null;
    }

    int sourceIndex = _jsonLines.Count;
    AIConversationCoreRetainedSession appended =
      _client.AppendRetainedSession(
        _sessionId,
        new[] { line },
        _options);
    _jsonLines.Add(line);
    _projection = appended.Projection;
    LogSessionDiagnostics("append", appended.Diagnostics);
    AIConversationProjection speechProjection = CanonicalSpeechProjection.Prepare(
      _projection);
    return CanonicalProjectionExtractor.ExtractRecord(
      speechProjection,
      source,
      sourceIndex);
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
    CloseRetainedSession();
    _disposed = true;
    _client.Dispose();
    _jsonLines.Clear();
    _source = null;
    _options = new();
    _projection = null;
  }

  private void CloseRetainedSession()
  {
    if (_sessionId is not null)
    {
      _client.CloseRetainedSession(_sessionId);
      _sessionId = null;
    }
    _projection = null;
  }

  private static void LogSessionDiagnostics(
    string operation,
    AIConversationCoreSessionDiagnostics? diagnostics)
  {
    if (diagnostics is null)
    {
      return;
    }
    DiagnosticLog.Write("core.retained_session", new
    {
      operation,
      diagnostics.InitialNormalizationPasses,
      diagnostics.FullRenormalizationPasses,
      diagnostics.AppendedRecordsProcessed,
      diagnostics.ProjectionCount
    });
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

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
