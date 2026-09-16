using Markdig;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Obtains completed canonical HTML units directly from AIConversationCore for
/// transcript display and virtualization.
/// </summary>
internal static class TranscriptPresentationDomFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptPresentationDomFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Projects one selected provider JSONL session through a retained Core
  /// session and returns Core-rendered HTML units without rebuilding semantic
  /// HTML in C#.
  /// </summary>
  public static TranscriptPresentationDomResult Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(pipeline);

    IReadOnlyList<string> jsonLines = ReadJsonLines(path, cancellationToken);
    if (jsonLines.Count == 0)
    {
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        string.Empty,
        Array.Empty<CanonicalHtmlUnitProjection>(),
        null);
    }

    AIConversationCoreRetainedSession? retained = null;
    try
    {
      retained = CoreClient.CreateRetainedSession(
        source,
        jsonLines,
        new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));
      cancellationToken.ThrowIfCancellationRequested();
      AIConversationProjection projection = retained.Projection;

      CanonicalHtmlUnitProjection[] units = projection.HtmlUnits ??
        throw new InvalidOperationException(
          "AIConversationCore projection omitted canonical HTML units.");
      foreach (CanonicalHtmlUnitProjection unit in units)
      {
        if (!unit.Atomic ||
            !string.Equals(unit.Kind, "turn", StringComparison.Ordinal))
        {
          throw new InvalidOperationException(
            "AIConversationCore returned an unsupported HTML virtualization unit.");
        }
      }

      string html = string.Concat(units.Select(unit => unit.Html));
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        html,
        units,
        retained.Id);
    }
    catch
    {
      if (retained is not null)
      {
        CoreClient.CloseRetainedSession(retained.Id);
      }
      throw;
    }
  }

  /// <summary>
  /// Resolves one transcript-global Core word in the retained display session.
  /// </summary>
  internal static AIConversationCoreWordLocation? LocateRetainedWord(
    string sessionId,
    long wordId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    if (wordId < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(wordId));
    }
    return CoreClient.LocateRetainedWord(
      sessionId,
      wordId,
      new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));
  }

  /// <summary>
  /// Releases one retained display session when its rendered projection is no
  /// longer the active transcript inventory.
  /// </summary>
  internal static void CloseRetainedSession(string? sessionId)
  {
    if (!string.IsNullOrWhiteSpace(sessionId))
    {
      CoreClient.CloseRetainedSession(sessionId);
    }
  }

  private static IReadOnlyList<string> ReadJsonLines(
    string path,
    CancellationToken cancellationToken)
  {
    var jsonLines = new List<string>();
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is string line)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }
      using JsonDocument document = JsonDocument.Parse(line);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new JsonException("A transcript JSONL record must be an object.");
      }
      jsonLines.Add(line);
    }
    return jsonLines;
  }
}

/// <summary>
/// Completed Core HTML plus its retained Core session identity. The legacy
/// DOM-node slot remains only for API compatibility while callers migrate to
/// Core-owned units.
/// </summary>
internal sealed record TranscriptPresentationDomResult(
  IReadOnlyList<TranscriptDomNode> Nodes,
  string Html,
  IReadOnlyList<CanonicalHtmlUnitProjection> Units,
  string? CoreSessionId = null);

/// <summary>
/// Legacy browser-DOM instruction shape retained for compatibility. Production
/// transcript rendering no longer constructs semantic nodes through this type.
/// </summary>
internal sealed record TranscriptDomNode(
  string Kind,
  string? Tag,
  IReadOnlyDictionary<string, string>? Attributes,
  string? Text,
  string? Html,
  IReadOnlyList<TranscriptDomNode>? Children);
