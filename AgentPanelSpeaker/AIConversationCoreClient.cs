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
  internal const string ExpectedCoreCommit =
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
  /// Creates a bridge client for one explicit worker path.
  /// </summary>
  internal AIConversationCoreClient(string workerPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
    _workerPath = Path.GetFullPath(workerPath);
  }

  /// <summary>
  /// Projects provider records through the canonical core runtime.
  /// </summary>
  public AIConversationProjection Project(
    AgentSource source,
    IReadOnlyList<string> jsonLines)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);
    string provider = source switch
    {
      AgentSource.Claude => "claude",
      AgentSource.Codex => "codex",
      _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
    JsonElement[] records = jsonLines
      .Select(line => JsonDocument.Parse(line).RootElement.Clone())
      .ToArray();
    lock (_sync)
    {
      ThrowIfDisposed();
      EnsureStarted();
      var request = new WorkerRequest("project", provider, records);
      string serialized = JsonSerializer.Serialize(request, JsonOptions);
      try
      {
        _input!.WriteLine(serialized);
        _input.Flush();
      }
      catch (Exception exception) when (
        exception is IOException or InvalidOperationException)
      {
        ResetProcess();
        throw new InvalidOperationException(
          "AIConversationCore worker input failed.",
          exception);
      }

      string? line;
      try
      {
        line = _output!.ReadLine();
      }
      catch (IOException exception)
      {
        string stderr = StandardErrorText();
        ResetProcess();
        throw new InvalidOperationException(
          WorkerFailureMessage("AIConversationCore worker output failed.", stderr),
          exception);
      }
      if (line is null)
      {
        string stderr = StandardErrorText();
        ResetProcess();
        throw new InvalidOperationException(
          WorkerFailureMessage(
            "AIConversationCore worker terminated before returning a response.",
            stderr));
      }

      WorkerResponse? response;
      try
      {
        response = JsonSerializer.Deserialize<WorkerResponse>(line, JsonOptions);
      }
      catch (JsonException exception)
      {
        throw new InvalidOperationException(
          "AIConversationCore worker returned malformed JSON.",
          exception);
      }
      if (response is null)
      {
        throw new InvalidOperationException(
          "AIConversationCore worker returned an empty response.");
      }
      if (!response.Ok)
      {
        throw new InvalidOperationException(
          "AIConversationCore projection failed: " +
          (response.Error ?? "unknown worker error"));
      }
      if (!string.Equals(
            response.CoreCommit,
            ExpectedCoreCommit,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "AIConversationCore worker revision mismatch. Expected " +
          $"{ExpectedCoreCommit}, got {response.CoreCommit ?? "<missing>"}.");
      }
      if (response.Projection is null)
      {
        throw new InvalidOperationException(
          "AIConversationCore worker omitted the canonical projection.");
      }
      if (response.Projection.SchemaVersion != 2)
      {
        throw new InvalidOperationException(
          $"Unexpected AIConversationCore projection schema {response.Projection.SchemaVersion}.");
      }
      if (response.Projection.Presentation is null)
      {
        throw new InvalidOperationException(
          "AIConversationCore projection omitted presentation metadata.");
      }
      if (response.Projection.Presentation.SchemaVersion !=
          ExpectedPresentationSchemaVersion)
      {
        throw new InvalidOperationException(
          "Unexpected AIConversationCore presentation schema " +
          $"{response.Projection.Presentation.SchemaVersion}.");
      }
      if (!string.Equals(
            response.Projection.Presentation.SplitPolicy,
            ExpectedSplitPolicy,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "Unexpected AIConversationCore split policy " +
          $"'{response.Projection.Presentation.SplitPolicy}'.");
      }
      return response.Projection;
    }
  }

  public void Dispose()
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      ResetProcess();
    }
  }

  private void EnsureStarted()
  {
    if (_process is not null && !_process.HasExited)
    {
      return;
    }
    ResetProcess();
    if (!File.Exists(_workerPath))
    {
      throw new FileNotFoundException(
        "AIConversationCore worker was not found.",
        _workerPath);
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
    var process = new Process { StartInfo = startInfo };
    process.ErrorDataReceived += (_, args) =>
    {
      if (args.Data is null)
      {
        return;
      }
      lock (_standardError)
      {
        _standardError.AppendLine(args.Data);
      }
    };
    lock (_standardError)
    {
      _standardError.Clear();
    }
    try
    {
      if (!process.Start())
      {
        process.Dispose();
        throw new InvalidOperationException(
          "AIConversationCore worker could not be started.");
      }
      process.BeginErrorReadLine();
      _process = process;
      _input = process.StandardInput;
      _output = process.StandardOutput;
      VerifyWorkerRevision();
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

  private void VerifyWorkerRevision()
  {
    var request = new WorkerRequest("ping", null, null);
    _input!.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
    _input.Flush();
    string? line = _output!.ReadLine();
    if (line is null)
    {
      throw new InvalidOperationException(
        WorkerFailureMessage(
          "AIConversationCore worker terminated during startup.",
          StandardErrorText()));
    }
    WorkerResponse? response = JsonSerializer.Deserialize<WorkerResponse>(
      line,
      JsonOptions);
    if (response is null || !response.Ok)
    {
      throw new InvalidOperationException(
        "AIConversationCore worker startup check failed: " +
        (response?.Error ?? "missing response"));
    }
    if (!string.Equals(
          response.CoreCommit,
          ExpectedCoreCommit,
          StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        "AIConversationCore worker revision mismatch. Expected " +
        $"{ExpectedCoreCommit}, got {response.CoreCommit ?? "<missing>"}.");
    }
  }

  private void ResetProcess()
  {
    _input?.Dispose();
    _output?.Dispose();
    _input = null;
    _output = null;
    if (_process is not null)
    {
      try
      {
        if (!_process.HasExited)
        {
          _process.Kill(entireProcessTree: true);
          _process.WaitForExit(1000);
        }
      }
      catch (InvalidOperationException)
      {
      }
      finally
      {
        _process.Dispose();
        _process = null;
      }
    }
  }

  private string StandardErrorText()
  {
    lock (_standardError)
    {
      return _standardError.ToString().Trim();
    }
  }

  private static string WorkerFailureMessage(string prefix, string stderr)
  {
    return string.IsNullOrWhiteSpace(stderr)
      ? prefix
      : prefix + " Worker stderr: " + stderr;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  private static string ResolveWorkerPath()
  {
    return Path.Combine(
      AppContext.BaseDirectory,
      "tools",
      "AIConversationCore-worker.mjs");
  }

  private sealed record WorkerRequest(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("records")] IReadOnlyList<JsonElement>? Records);

  private sealed record WorkerResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("core_commit")] string? CoreCommit,
    [property: JsonPropertyName("projection")] AIConversationProjection? Projection);
}

internal sealed record AIConversationProjection(
  [property: JsonPropertyName("schema_version")] int SchemaVersion,
  [property: JsonPropertyName("provider")] string Provider,
  [property: JsonPropertyName("events")] IReadOnlyList<JsonElement> Events,
  [property: JsonPropertyName("units")] IReadOnlyList<AIConversationUnit> Units,
  [property: JsonPropertyName("presentation")] AIConversationPresentation? Presentation,
  [property: JsonPropertyName("markdown")] string Markdown);

internal sealed record AIConversationUnit(
  [property: JsonPropertyName("source_index")] int SourceIndex,
  [property: JsonPropertyName("source_record_id")] string SourceRecordId,
  [property: JsonPropertyName("event_index")] int EventIndex,
  [property: JsonPropertyName("block_index")] int BlockIndex,
  [property: JsonPropertyName("content_type")] string ContentType,
  [property: JsonPropertyName("block")] JsonElement Block);

internal sealed record AIConversationPresentation(
  [property: JsonPropertyName("schema_version")] int SchemaVersion,
  [property: JsonPropertyName("split_policy")] string SplitPolicy,
  [property: JsonPropertyName("units")] IReadOnlyList<JsonElement> Units,
  [property: JsonPropertyName("tree")] JsonElement Tree);
