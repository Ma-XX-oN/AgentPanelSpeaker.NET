using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns one persistent Node.js bridge process for AIConversationCore.
/// </summary>
internal sealed class AIConversationCoreClient : IDisposable
{
  private const string ExpectedCoreCommit =
    "2255b6603ef5f2ccbd4111a891375c9c4c246d3e";
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
  /// </summary>
  /// <param name="source">Selected provider.</param>
  /// <param name="jsonLines">Ordered raw JSONL records.</param>
  /// <returns>The canonical structured projection.</returns>
  public AIConversationProjection Project(
    AgentSource source,
    IReadOnlyList<string> jsonLines)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    string provider = source switch
    {
      AgentSource.Claude => "claude",
      AgentSource.Codex => "codex",
      _ => throw new ArgumentOutOfRangeException(
        nameof(source),
        source,
        "AIConversationCore projection requires an explicit provider.")
    };

    using var recordsDocument = JsonDocument.Parse(
      "[" + string.Join(",", jsonLines.Where(line =>
        !string.IsNullOrWhiteSpace(line))) + "]");
    var request = new CoreRequest(
      "project",
      provider,
      recordsDocument.RootElement.Clone());
    CoreResponse response = SendRequest(request);
    if (response.Projection is null)
    {
      throw new InvalidOperationException(
        "AIConversationCore returned no structured projection.");
    }
    ValidateProjectionContract(response.Projection);
    return response.Projection;
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

      var ping = new CoreRequest("ping", null, null);
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
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("records")] JsonElement? Records);

  private sealed record CoreResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("core_commit")] string? CoreCommit,
    [property: JsonPropertyName("projection")] AIConversationProjection? Projection,
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
  [property: JsonPropertyName("presentation")] AIConversationPresentation? Presentation,
  [property: JsonPropertyName("markdown")] string Markdown);

/// <summary>
/// Shared presentation contract returned by AIConversationCore.
/// </summary>
internal sealed record AIConversationPresentation(
  [property: JsonPropertyName("schema_version")] int SchemaVersion,
  [property: JsonPropertyName("split_policy")] string SplitPolicy,
  [property: JsonPropertyName("structural_units")] CanonicalStructuralUnitProjection[] StructuralUnits,
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
