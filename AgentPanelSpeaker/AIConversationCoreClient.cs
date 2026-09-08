using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPanelSpeaker;

/// <summary>
/// Options passed through to AIConversationCore for one structured projection.
/// </summary>
/// <param name="IncludeRolledBackTurns">
/// Whether rolled-back/superseded Codex revisions should be visible.
/// </param>
/// <param name="CodexSessionIndexPath">
/// Optional caller-discovered Codex session-index path. The core reads/parses it.
/// </param>
/// <param name="IncludeUserContext">
/// Whether Core-identified User/IDE context participates in speech.
/// </param>
internal sealed record AIConversationCoreProjectOptions(
  bool IncludeRolledBackTurns = false,
  string? CodexSessionIndexPath = null,
  bool IncludeUserContext = false);

/// <summary>
/// Identifies one worker-retained canonical conversation and its latest projection.
/// </summary>
internal sealed record AIConversationCoreRetainedSession(
  string Id,
  AIConversationProjection Projection,
  AIConversationCoreSessionDiagnostics? Diagnostics);

/// <summary>
/// Counters reported by Core's retained-session API.
/// </summary>
internal sealed record AIConversationCoreSessionDiagnostics(
  [property: JsonPropertyName("initial_normalization_passes")]
    int InitialNormalizationPasses,
  [property: JsonPropertyName("full_renormalization_passes")]
    int FullRenormalizationPasses,
  [property: JsonPropertyName("appended_records_processed")]
    int AppendedRecordsProcessed,
  [property: JsonPropertyName("projection_count")]
    int ProjectionCount);

/// <summary>
/// Owns one persistent Node.js bridge process for AIConversationCore.
/// </summary>
internal sealed class AIConversationCoreClient : IDisposable
{
  internal const string ExpectedCoreCommit =
    "7eb7f4fca630aa0a132e93799e878120aaf353b9";
  private const int ExpectedPresentationSchemaVersion = 2;
  private const string ExpectedSplitPolicy =
    "presentation-tree";

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = false
  };
  private static readonly Encoding Utf8NoBom =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  private readonly object _sync = new();
  private readonly string _workerPath;
  private Process? _process;
  private StreamWriter? _input;
  private StreamReader? _output;
  private readonly StringBuilder _standardError = new();
  private bool _disposed;

  /// <summary>
  /// Creates a bridge client using the worker copied beside the application.
  /// </summary>
  public AIConversationCoreClient()
    : this(ResolveWorkerPath())
  {
  }

  /// <summary>
  /// Creates a bridge client using an explicit worker path.
  /// </summary>
  /// <param name="workerPath">Absolute or relative Node.js worker path.</param>
  internal AIConversationCoreClient(string workerPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
    _workerPath = Path.GetFullPath(workerPath);
  }

  /// <summary>
  /// Projects one ordered provider session through AIConversationCore.
  ///
  /// This compatibility operation sends all records. Interactive session paths
  /// should use the retained-session methods so projection settings and live
  /// append do not re-normalize the unchanged provider prefix.
  /// </summary>
  public AIConversationProjection Project(
    AgentSource source,
    IReadOnlyList<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    AIConversationCoreProjectOptions effective = options ?? new();
    var request = new CoreRequest(
      "project",
      null,
      ProviderName(source),
      ParseRecords(jsonLines),
      ToCoreOptions(effective),
      SupplementarySources(source, effective));
    CoreResponse response = SendRequest(request);
    return RequireProjection(response);
  }

  /// <summary>
  /// Creates one worker-retained canonical session from the complete initial
  /// provider record inventory. Provider normalization occurs at this boundary.
  /// </summary>
  public AIConversationCoreRetainedSession CreateRetainedSession(
    AgentSource source,
    IReadOnlyList<string> jsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    string sessionId = Guid.NewGuid().ToString("N");
    AIConversationCoreProjectOptions effective = options ?? new();
    var request = new CoreRequest(
      "session_create",
      sessionId,
      ProviderName(source),
      ParseRecords(jsonLines),
      ToCoreOptions(effective),
      null);
    CoreResponse response = SendRequest(request);
    return new AIConversationCoreRetainedSession(
      sessionId,
      RequireProjection(response),
      response.Diagnostics);
  }

  /// <summary>
  /// Projects one already-normalized worker session with new visibility/speech
  /// options without resending provider records.
  /// </summary>
  public AIConversationCoreRetainedSession ProjectRetainedSession(
    string sessionId,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    AIConversationCoreProjectOptions effective = options ?? new();
    var request = new CoreRequest(
      "session_project",
      sessionId,
      null,
      null,
      ToCoreOptions(effective),
      null);
    CoreResponse response = SendRequest(request);
    return new AIConversationCoreRetainedSession(
      sessionId,
      RequireProjection(response),
      response.Diagnostics);
  }

  /// <summary>
  /// Appends only newly observed provider records to an already-normalized Core
  /// session and returns the resulting projection.
  /// </summary>
  public AIConversationCoreRetainedSession AppendRetainedSession(
    string sessionId,
    IReadOnlyList<string> appendedJsonLines,
    AIConversationCoreProjectOptions? options = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    ArgumentNullException.ThrowIfNull(appendedJsonLines);
    AIConversationCoreProjectOptions effective = options ?? new();
    var request = new CoreRequest(
      "session_append",
      sessionId,
      null,
      ParseRecords(appendedJsonLines),
      ToCoreOptions(effective),
      null);
    CoreResponse response = SendRequest(request);
    return new AIConversationCoreRetainedSession(
      sessionId,
      RequireProjection(response),
      response.Diagnostics);
  }

  /// <summary>
  /// Releases one worker-retained canonical session.
  /// </summary>
  public void CloseRetainedSession(string sessionId)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      return;
    }
    _ = SendRequest(new CoreRequest(
      "session_close",
      sessionId,
      null,
      null,
      null,
      null));
  }

  /// <summary>
  /// Stops the bridge process and releases process resources.
  /// </summary>
  public void Dispose()
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      StopProcess();
    }
  }

  private static string ProviderName(AgentSource source)
  {
    return source switch
    {
      AgentSource.Claude => "claude",
      AgentSource.Codex => "codex",
      _ => throw new ArgumentOutOfRangeException(
        nameof(source),
        source,
        "AIConversationCore projection requires an explicit provider.")
    };
  }

  private static JsonElement ParseRecords(IReadOnlyList<string> jsonLines)
  {
    using JsonDocument recordsDocument = JsonDocument.Parse(
      "[" + string.Join(",", jsonLines.Where(line =>
        !string.IsNullOrWhiteSpace(line))) + "]");
    return recordsDocument.RootElement.Clone();
  }

  private static CoreOptions ToCoreOptions(
    AIConversationCoreProjectOptions options)
  {
    return new CoreOptions(
      options.IncludeRolledBackTurns,
      options.IncludeUserContext);
  }

  private static IReadOnlyDictionary<string, object>? SupplementarySources(
    AgentSource source,
    AIConversationCoreProjectOptions options)
  {
    if (source != AgentSource.Codex ||
        string.IsNullOrWhiteSpace(options.CodexSessionIndexPath))
    {
      return null;
    }
    return new Dictionary<string, object>
    {
      ["codexSessionIndex"] = new
      {
        path = Path.GetFullPath(options.CodexSessionIndexPath)
      }
    };
  }

  private static AIConversationProjection RequireProjection(CoreResponse response)
  {
    if (response.Projection is null)
    {
      throw new InvalidOperationException(
        "AIConversationCore returned no structured projection.");
    }
    ValidateProjectionContract(response.Projection);
    return response.Projection;
  }

  private static void ValidateProjectionContract(AIConversationProjection projection)
  {
    AIConversationPresentation? presentation = projection.Presentation;
    if (presentation is null)
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted its presentation contract.");
    }
    if (presentation.SchemaVersion != ExpectedPresentationSchemaVersion)
    {
      throw new InvalidOperationException(
        "AIConversationCore presentation schema mismatch: expected " +
        $"{ExpectedPresentationSchemaVersion}, received {presentation.SchemaVersion}.");
    }
    if (!string.Equals(
          presentation.SplitPolicy,
          ExpectedSplitPolicy,
          StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        "AIConversationCore presentation split policy mismatch: expected " +
        $"{ExpectedSplitPolicy}, received {presentation.SplitPolicy}.");
    }
  }

  private CoreResponse SendRequest(CoreRequest request)
  {
    lock (_sync)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      EnsureStarted();
      string requestJson = JsonSerializer.Serialize(request, JsonOptions);
      _input!.WriteLine(requestJson);
      _input.Flush();

      string? responseLine = _output!.ReadLine();
      if (responseLine is null)
      {
        string diagnostics;
        lock (_standardError)
        {
          diagnostics = _standardError.ToString();
        }
        StopProcess();
        throw new InvalidOperationException(
          "AIConversationCore worker terminated without a response." +
          (diagnostics.Length == 0 ? string.Empty : $" {diagnostics}"));
      }

      CoreResponse? response = JsonSerializer.Deserialize<CoreResponse>(
        responseLine,
        JsonOptions);
      if (response is null)
      {
        throw new InvalidOperationException(
          "AIConversationCore returned an invalid JSON response.");
      }
      if (!response.Ok)
      {
        throw new InvalidOperationException(
          $"AIConversationCore request failed: {response.Error}");
      }
      if (!string.Equals(
            response.CoreCommit,
            ExpectedCoreCommit,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "AIConversationCore response commit mismatch: expected " +
          $"{ExpectedCoreCommit}, received {response.CoreCommit}.");
      }
      return response;
    }
  }

  private void EnsureStarted()
  {
    if (_process is { HasExited: false })
    {
      return;
    }

    if (!File.Exists(_workerPath))
    {
      throw new FileNotFoundException(
        "AIConversationCore worker was not found.",
        _workerPath);
    }

    StopProcess();
    lock (_standardError)
    {
      _standardError.Clear();
    }
    var startInfo = new ProcessStartInfo
    {
      FileName = "node",
      UseShellExecute = false,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardInputEncoding = Utf8NoBom,
      StandardOutputEncoding = Utf8NoBom,
      StandardErrorEncoding = Utf8NoBom,
      CreateNoWindow = true
    };
    startInfo.ArgumentList.Add(_workerPath);

    var process = new Process
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true
    };
    process.ErrorDataReceived += (_, args) =>
    {
      if (args.Data is not null)
      {
        lock (_standardError)
        {
          _standardError.AppendLine(args.Data);
        }
      }
    };

    try
    {
      if (!process.Start())
      {
        throw new InvalidOperationException(
          "Node.js did not start the AIConversationCore worker.");
      }
      process.BeginErrorReadLine();
      _process = process;
      _input = process.StandardInput;
      _output = process.StandardOutput;

      var ping = new CoreRequest(
        "ping",
        null,
        null,
        null,
        null,
        null);
      CoreResponse response = SendRequestWithoutStartup(ping);
      if (!response.Ok ||
          !string.Equals(
            response.CoreCommit,
            ExpectedCoreCommit,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          response.Error ??
          "AIConversationCore worker failed version verification.");
      }

      DiagnosticLog.Write("core.worker_started", new
      {
        workerPath = _workerPath,
        coreCommit = response.CoreCommit,
        processId = process.Id
      });
    }
    catch
    {
      process.Dispose();
      _process = null;
      _input = null;
      _output = null;
      throw;
    }
  }

  private CoreResponse SendRequestWithoutStartup(CoreRequest request)
  {
    string requestJson = JsonSerializer.Serialize(request, JsonOptions);
    _input!.WriteLine(requestJson);
    _input.Flush();
    string? responseLine = _output!.ReadLine();
    if (responseLine is null)
    {
      Process? process = _process;
      if (process is not null)
      {
        try
        {
          process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
        }
      }

      string diagnostics;
      lock (_standardError)
      {
        diagnostics = _standardError.ToString().Trim();
      }
      throw new InvalidOperationException(
        "AIConversationCore worker terminated during startup." +
        (diagnostics.Length == 0
          ? string.Empty
          : Environment.NewLine + diagnostics));
    }
    return JsonSerializer.Deserialize<CoreResponse>(responseLine, JsonOptions) ??
      throw new InvalidOperationException(
        "AIConversationCore returned invalid startup JSON.");
  }

  private void StopProcess()
  {
    Process? process = _process;
    _process = null;
    _input = null;
    _output = null;
    if (process is null)
    {
      return;
    }

    try
    {
      process.StandardInput.Close();
      if (!process.HasExited && !process.WaitForExit(500))
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
      // The worker has already exited.
    }
    finally
    {
      process.Dispose();
    }
  }

  private static string ResolveWorkerPath()
  {
    string deployed = Path.Combine(
      AppContext.BaseDirectory,
      "tools",
      "AIConversationCore-worker.mjs");
    if (File.Exists(deployed))
    {
      return deployed;
    }

    return Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..",
      "..",
      "..",
      "..",
      "tools",
      "AIConversationCore-worker.mjs"));
  }

  private sealed record CoreRequest(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("records")] JsonElement? Records,
    [property: JsonPropertyName("options")] CoreOptions? Options,
    [property: JsonPropertyName("supplementary_sources")]
      IReadOnlyDictionary<string, object>? SupplementarySources);

  private sealed record CoreOptions(
    [property: JsonPropertyName("includeRolledBackTurns")]
      bool IncludeRolledBackTurns,
    [property: JsonPropertyName("includeUserContext")]
      bool IncludeUserContext);

  private sealed record CoreResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("core_commit")] string? CoreCommit,
    [property: JsonPropertyName("projection")]
      AIConversationProjection? Projection,
    [property: JsonPropertyName("diagnostics")]
      AIConversationCoreSessionDiagnostics? Diagnostics,
    [property: JsonPropertyName("error")] string? Error);
}

/// <summary>
/// Structured canonical projection returned by AIConversationCore.
/// </summary>
internal sealed record AIConversationProjection(
  [property: JsonPropertyName("schema_version")] int SchemaVersion,
  [property: JsonPropertyName("events")] JsonElement[] Events,
  [property: JsonPropertyName("turns")] CanonicalTurnProjection[] Turns,
  [property: JsonPropertyName("units")] CanonicalUnitProjection[] Units,
  [property: JsonPropertyName("presentation")]
    AIConversationPresentation? Presentation,
  [property: JsonPropertyName("markdown")] string Markdown,
  [property: JsonPropertyName("session_metadata")]
    AIConversationSessionMetadata? SessionMetadata = null);

/// <summary>
/// Provider session metadata resolved by AIConversationCore.
/// </summary>
internal sealed record AIConversationSessionMetadata(
  [property: JsonPropertyName("session_id")] string? SessionId,
  [property: JsonPropertyName("title")] string? Title,
  [property: JsonPropertyName("title_source")] string? TitleSource);

/// <summary>
/// Shared presentation contract returned by AIConversationCore.
/// </summary>
internal sealed record AIConversationPresentation(
  [property: JsonPropertyName("schema_version")] int SchemaVersion,
  [property: JsonPropertyName("split_policy")] string SplitPolicy,
  [property: JsonPropertyName("structural_units")]
    CanonicalStructuralUnitProjection[] StructuralUnits,
  [property: JsonPropertyName("tree")] JsonElement Tree);

/// <summary>
/// One core-declared atomic presentation unit.
/// </summary>
internal sealed record CanonicalStructuralUnitProjection(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("kind")] string Kind,
  [property: JsonPropertyName("atomic")] bool Atomic,
  [property: JsonPropertyName("source_indexes")] int[] SourceIndexes,
  [property: JsonPropertyName("source_record_ids")] string[] SourceRecordIds);

/// <summary>
/// Canonical derived turn returned by AIConversationCore.
/// </summary>
internal sealed record CanonicalTurnProjection(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("index")] int Index,
  [property: JsonPropertyName("role")] string Role,
  [property: JsonPropertyName("event_ids")] string[] EventIds,
  [property: JsonPropertyName("source")] JsonElement Source);

/// <summary>
/// Flattened canonical event/block unit used by interactive consumers.
/// </summary>
internal sealed record CanonicalUnitProjection(
  [property: JsonPropertyName("id")] string Id,
  [property: JsonPropertyName("event_id")] string EventId,
  [property: JsonPropertyName("provider")] string? Provider,
  [property: JsonPropertyName("source_record_id")] string? SourceRecordId,
  [property: JsonPropertyName("source_index")] int? SourceIndex,
  [property: JsonPropertyName("source_block_index")] int? SourceBlockIndex,
  [property: JsonPropertyName("event_kind")] string? EventKind,
  [property: JsonPropertyName("role")] string? Role,
  [property: JsonPropertyName("channel")] string? Channel,
  [property: JsonPropertyName("visibility")] string? Visibility,
  [property: JsonPropertyName("content_type")] string? ContentType,
  [property: JsonPropertyName("block_type")] string? BlockType,
  [property: JsonPropertyName("block")] JsonElement Block);
