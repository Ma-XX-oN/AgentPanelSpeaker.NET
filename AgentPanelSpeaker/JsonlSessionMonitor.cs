using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Defines one JSONL monitoring session.
/// </summary>
/// <param name="RequestedSource">Source selected by the user.</param>
/// <param name="ExplicitPath">Optional fixed JSONL path.</param>
/// <param name="FollowLatest">
/// Whether a newer session file can replace the current one.
/// </param>
/// <param name="SpeakExistingLatestTurn">
/// Speak the complete latest turn at start.
/// </param>
/// <param name="PollInterval">File polling interval.</param>
/// <param name="PreindexedHistory">
/// Existing history for the selected session.  When supplied, monitoring
/// begins at the file end without reparsing or republishing old history.
/// </param>
internal sealed record MonitorSettings(
  AgentSource RequestedSource,
  string? ExplicitPath,
  bool FollowLatest,
  bool SpeakExistingLatestTurn,
  TimeSpan PollInterval,
  SpeechHistorySnapshot? PreindexedHistory = null,
  bool IncludeRolledBackTurns = false);

/// <summary>
/// Tails Claude or Codex session JSONL and emits conversational text.
/// </summary>
internal sealed class JsonlSessionMonitor : IDisposable
{
  private const int MaximumRecentFingerprints = 512;
  private const int MaximumPreviewNodes = 20;
  private static readonly TimeSpan LatestSessionRefreshInterval =
    TimeSpan.FromSeconds(1);

  private readonly object _sync = new();
  private readonly CanonicalSessionExtractor _canonicalExtractor = new();
  private CancellationTokenSource? _cancellation;
  private Thread? _thread;
  private bool _disposed;

  /// <summary>
  /// Raised when one sentence is ready for speech.
  /// </summary>
  public event Action<SpeechFragment>? TextReady;

  /// <summary>
  /// Raised when existing session history is ready for navigation.
  /// </summary>
  public event Action<SpeechHistorySnapshot>? HistoryLoaded;

  /// <summary>
  /// Raised when the source records a terminal AI-turn completion.
  /// </summary>
  public event Action<TurnCompletion>? TurnCompleted;

  /// <summary>
  /// Raised when one background-agent task starts or completes.
  /// </summary>
  public event Action<BackgroundWorkEvent>? BackgroundWorkChanged;

  /// <summary>
  /// Raised when the selected or followed session changes.
  /// </summary>
  public event Action<LocatedSession>? SessionChanged;

  /// <summary>
  /// Raised when the recent assistant-node preview changes.
  /// </summary>
  public event Action<IReadOnlyList<string>>? MessagesChanged;

  /// <summary>
  /// Raised when monitor status changes.
  /// </summary>
  public event Action<string>? StatusChanged;

  /// <summary>
  /// Raised when monitoring fails.
  /// </summary>
  public event Action<Exception>? Faulted;

  /// <summary>
  /// Gets whether the worker thread is active.
  /// </summary>
  public bool IsRunning
  {
    get
    {
      lock (_sync)
      {
        return _thread is not null;
      }
    }
  }

  /// <summary>
  /// Starts one JSONL monitoring session.
  /// </summary>
  /// <param name="settings">Monitoring and extraction settings.</param>
  public void Start(MonitorSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ThrowIfDisposed();
    if (settings.PollInterval <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(
        nameof(settings),
        settings.PollInterval,
        "The polling interval must be positive.");
    }

    lock (_sync)
    {
      if (_thread is not null)
      {
        throw new InvalidOperationException(
          "The JSONL monitor is already running.");
      }

      _cancellation = new CancellationTokenSource();
      CancellationToken token = _cancellation.Token;
      _thread = new Thread(() => Run(settings, token))
      {
        IsBackground = true,
        Name = "Agent panel JSONL monitor"
      };
      _thread.Start();
    }

    DiagnosticLog.Write("monitor.start_requested", settings);
  }

  /// <summary>
  /// Stops monitoring and waits briefly for the worker thread.
  /// </summary>
  public void Stop(string trigger = "unspecified")
  {
    Thread? thread;
    CancellationTokenSource? cancellation;
    lock (_sync)
    {
      thread = _thread;
      cancellation = _cancellation;
    }

    if (thread is null || cancellation is null)
    {
      return;
    }

    DiagnosticLog.Write("monitor.stop_requested", new { trigger });
    cancellation.Cancel();
    bool joined = thread.Join(TimeSpan.FromSeconds(2));
    DiagnosticLog.Write("monitor.stop_join", new { joined });

    lock (_sync)
    {
      if (ReferenceEquals(_thread, thread))
      {
        _thread = null;
        _cancellation?.Dispose();
        _cancellation = null;
      }
    }

    StatusChanged?.Invoke("Stopped.");
  }

  /// <summary>
  /// Stops monitoring and releases resources.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    Stop("dispose");
    _canonicalExtractor.Dispose();
    _disposed = true;
  }

  /// <summary>
  /// Builds the same existing-history snapshot used by monitoring without
  /// starting the tail thread.  The caller can load this snapshot in the
  /// paused state so the transcript has an authoritative startup marker.
  /// </summary>
  public SpeechHistorySnapshot LoadHistoryPreview(
    LocatedSession session,
    bool speakExistingLatestTurn,
    bool includeRolledBackTurns = false)
  {
    ArgumentNullException.ThrowIfNull(session);
    lock (_sync)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      if (_thread is not null)
      {
        throw new InvalidOperationException(
          "A history preview cannot be built while monitoring is active.");
      }
    }

    long nextNodeId = 1;
    var recentFingerprintQueue = new Queue<string>();
    var recentFingerprintSet = new HashSet<string>(StringComparer.Ordinal);
    var preview = new Queue<string>();
    var pendingInputRequests = new Dictionary<string, CodexInputRequest>(
      StringComparer.Ordinal);
    return LoadExistingHistory(
      session,
      speakExistingLatestTurn,
      ref nextNodeId,
      recentFingerprintQueue,
      recentFingerprintSet,
      preview,
      pendingInputRequests,
      includeRolledBackTurns);
  }

  /// <summary>
  /// Runs the file-tail loop.
  /// </summary>
  private void Run(MonitorSettings settings, CancellationToken token)
  {
    var recentFingerprintQueue = new Queue<string>();
    var recentFingerprintSet = new HashSet<string>(StringComparer.Ordinal);
    var preview = new Queue<string>();
    var pendingInputRequests = new Dictionary<string, CodexInputRequest>(
      StringComparer.Ordinal);
    long nextNodeId = 1;
    DateTime nextLatestRefreshUtc = DateTime.MinValue;

    try
    {
      LocatedSession session = ResolveInitialSession(settings);
      var tailReader = new JsonlTailReader(session.Path);
      StatusChanged?.Invoke(
        $"Monitoring {session.Source}: {session.Path}");
      DiagnosticLog.Write("monitor.session_selected", session);
      SessionChanged?.Invoke(session);

      if (settings.PreindexedHistory is SpeechHistorySnapshot preindexedHistory)
      {
        _canonicalExtractor.Prime(
          session.Source,
          ReadSharedLines(session.Path),
          ProjectionOptions(session, settings.IncludeRolledBackTurns));
        nextNodeId = preindexedHistory.Fragments.Count == 0
          ? 1
          : preindexedHistory.Fragments.Max(fragment => fragment.NodeId) + 1;
        DiagnosticLog.Write("monitor.preindexed_history_reused", new
        {
          session.Path,
          fragmentCount = preindexedHistory.Fragments.Count,
          nextNodeId,
          tailOffset = tailReader.Offset
        });
      }
      else
      {
        SpeechHistorySnapshot initialHistory = LoadExistingHistory(
          session,
          settings.SpeakExistingLatestTurn,
          ref nextNodeId,
          recentFingerprintQueue,
          recentFingerprintSet,
          preview,
          pendingInputRequests,
          settings.IncludeRolledBackTurns);
        HistoryLoaded?.Invoke(initialHistory);
        MessagesChanged?.Invoke(preview.ToArray());
      }

      while (!token.IsCancellationRequested)
      {
        DateTime nowUtc = DateTime.UtcNow;
        if (settings.FollowLatest &&
            string.IsNullOrWhiteSpace(settings.ExplicitPath) &&
            nowUtc >= nextLatestRefreshUtc)
        {
          nextLatestRefreshUtc = nowUtc + LatestSessionRefreshInterval;
          LocatedSession latest = SessionLocator.FindLatest(
            settings.RequestedSource);
          if (!string.Equals(
                latest.Path,
                session.Path,
                StringComparison.OrdinalIgnoreCase) &&
              latest.LastWriteUtc > GetCurrentLastWriteUtc(session.Path))
          {
            session = latest;
            tailReader = new JsonlTailReader(session.Path);
            DiagnosticLog.Write("monitor.session_switched", session);
            StatusChanged?.Invoke(
              $"Switched to {session.Source}: {session.Path}");
            SessionChanged?.Invoke(session);

            recentFingerprintQueue.Clear();
            recentFingerprintSet.Clear();
            preview.Clear();
            pendingInputRequests.Clear();
            SpeechHistorySnapshot switchedHistory = LoadExistingHistory(
              session,
              speakExistingLatestTurn: false,
              ref nextNodeId,
              recentFingerprintQueue,
              recentFingerprintSet,
              preview,
              pendingInputRequests,
              settings.IncludeRolledBackTurns);
            HistoryLoaded?.Invoke(switchedHistory);
            MessagesChanged?.Invoke(preview.ToArray());
          }
        }

        IReadOnlyList<string> lines = tailReader.ReadAvailableLines();
        foreach (string line in lines)
        {
          ProcessLine(
            session,
            line,
            ref nextNodeId,
            recentFingerprintQueue,
            recentFingerprintSet,
            preview,
            pendingInputRequests,
            tailReader.Offset);
        }

        if (token.WaitHandle.WaitOne(settings.PollInterval))
        {
          break;
        }
      }
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      JsonException or
      InvalidDataException or
      InvalidOperationException)
    {
      DiagnosticLog.Write("monitor.fault", new
      {
        type = exception.GetType().FullName,
        exception = exception.ToString()
      });
      Faulted?.Invoke(exception);
    }
    finally
    {
      DiagnosticLog.Write("monitor.thread_ending", new
      {
        cancelled = token.IsCancellationRequested
      });
      lock (_sync)
      {
        if (ReferenceEquals(Thread.CurrentThread, _thread))
        {
          _thread = null;
          _cancellation?.Dispose();
          _cancellation = null;
        }
      }
    }
  }

  /// <summary>
  /// Resolves a fixed or latest initial session.
  /// </summary>
  private static LocatedSession ResolveInitialSession(MonitorSettings settings)
  {
    return string.IsNullOrWhiteSpace(settings.ExplicitPath)
      ? SessionLocator.FindLatest(settings.RequestedSource)
      : SessionLocator.FromPath(
        settings.ExplicitPath,
        settings.RequestedSource);
  }

  /// <summary>
  /// Canonically classifies one newly appended JSONL line.
  /// </summary>
  private void ProcessLine(
    LocatedSession session,
    string line,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    long byteOffset)
  {
    try
    {
      ExtractionResult? result = _canonicalExtractor.Append(
        session.Source,
        line);
      if (result is null)
      {
        DiagnosticLog.Write("jsonl.invalid_record", new
        {
          session.Source,
          session.Path,
          byteOffset,
          exception = "Record is not a valid JSON object.",
          linePreview = Abbreviate(line, 240)
        });
        return;
      }

      RegisterInputRequest(result.InputRequest, pendingInputRequests);
      IReadOnlyList<ExtractedNode> responseNodes = ResolveInputResponse(
        result.InputResponse,
        pendingInputRequests);
      DiagnosticLog.Write("jsonl.record", new
      {
        session.Source,
        session.Path,
        byteOffset,
        result.RecordType,
        result.PayloadType,
        result.Decision,
        acceptedNodes = result.Nodes.Count + responseNodes.Count,
        linePreview = Abbreviate(line, 240)
      });

      foreach (ExtractedNode node in result.Nodes.Concat(responseNodes))
      {
        ProcessNode(
          session,
          node,
          ref nextNodeId,
          recentFingerprintQueue,
          recentFingerprintSet,
          preview);
      }

      foreach (BackgroundWorkEvent workEvent in
               result.BackgroundWorkEvents ??
                 Array.Empty<BackgroundWorkEvent>())
      {
        DiagnosticLog.Write("jsonl.background_work", workEvent);
        BackgroundWorkChanged?.Invoke(workEvent);
      }

      TurnCompletion? completion = CreateTurnCompletion(
        result.CompletionTimestamp);
      if (completion is not null)
      {
        DiagnosticLog.Write("jsonl.turn_completed", new
        {
          session.Source,
          session.Path,
          completion.TimestampUtc
        });
        TurnCompleted?.Invoke(completion);
      }
    }
    catch (JsonException exception)
    {
      DiagnosticLog.Write("jsonl.invalid_record", new
      {
        session.Source,
        session.Path,
        byteOffset,
        exception = exception.Message,
        linePreview = Abbreviate(line, 240)
      });
    }
  }

  /// <summary>
  /// Cleans, deduplicates, previews, segments, and emits one conversation node.
  /// </summary>
  private void ProcessNode(
    LocatedSession session,
    ExtractedNode node,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    List<SpeechFragment>? history = null,
    bool emitLive = true)
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(node.Text);
    if (parts.Count == 0)
    {
      DiagnosticLog.Write("jsonl.node_skipped", new
      {
        session.Source,
        session.Path,
        node.Kind,
        reason = "empty after speech cleanup"
      });
      return;
    }

    string fingerprint = CreateFingerprint(
      node.Category + "|" + node.Kind + "|" + node.Timestamp + "|" +
      node.StartsUserTurn + "|" + string.Join(
        "|",
        parts.Select(part =>
          $"{part.Kind}:{part.Style}:{part.FenceType}:{part.Text}")));
    if (recentFingerprintSet.Contains(fingerprint))
    {
      DiagnosticLog.Write("jsonl.node_skipped", new
      {
        session.Source,
        session.Path,
        node.Kind,
        reason = "recent duplicate",
        text = string.Join(" ", parts.Select(part => part.Text))
      });
      return;
    }

    RememberFingerprint(
      fingerprint,
      recentFingerprintQueue,
      recentFingerprintSet);
    long nodeId = nextNodeId++;
    DateTimeOffset? nodeTimestampUtc = ParseTimestampUtc(node.Timestamp);
    string previewText = string.Join(" ", parts.Select(part => part.Text));
    AddPreview(
      preview,
      $"[{session.Source} {node.Category} {node.Kind}] {previewText}");

    var fragments = new List<SpeechFragment>();
    for (int partIndex = 0; partIndex < parts.Count; ++partIndex)
    {
      SpeechTextPart part = parts[partIndex];
      ContentCategory fragmentCategory =
        node.Category == ContentCategory.User &&
        part.Style == SpeechTextStyle.Context
          ? ContentCategory.UserContext
          : node.Category;
      bool startsUserTurn = node.StartsUserTurn && partIndex == 0;
      if (part.Kind == SpeechFragmentKind.Prose)
      {
        SpeechFragmentKind fragmentKind = part.FenceType.Length == 0
          ? SpeechFragmentKind.Prose
          : SpeechFragmentKind.FencedCodeLine;
        IReadOnlyList<SentenceSegment> sentences = SentenceSegmenter
          .Split(part.Text, part.PauseAfter);
        for (int sentenceIndex = 0;
             sentenceIndex < sentences.Count;
             ++sentenceIndex)
        {
          SentenceSegment sentence = sentences[sentenceIndex];
          fragments.Add(new SpeechFragment(
            nodeId,
            fragmentCategory,
            fragmentKind,
            sentence.Text,
            part.FenceType,
            part.FenceBlockId,
            part.FenceLineIndex,
            part.FenceLineCount,
            PauseAfter: sentence.PauseAfter,
            NodeTimestampUtc: nodeTimestampUtc,
            StartsUserTurn: startsUserTurn && sentenceIndex == 0));
        }
      }
      else
      {
        fragments.Add(new SpeechFragment(
          nodeId,
          fragmentCategory,
          SpeechFragmentKind.FencedCodeLine,
          part.Text,
          part.FenceType,
          part.FenceBlockId,
          part.FenceLineIndex,
          part.FenceLineCount,
          PauseAfter: part.PauseAfter,
          NodeTimestampUtc: nodeTimestampUtc,
          StartsUserTurn: startsUserTurn));
      }
    }
    DiagnosticLog.Write("jsonl.node_accepted", new
    {
      session.Source,
      session.Path,
      node.Kind,
      node.Timestamp,
      nodeId,
      fragmentCount = fragments.Count,
      text = previewText
    });

    foreach (SpeechFragment fragment in fragments)
    {
      DiagnosticLog.Write("monitor.emit", new
      {
        nodeId,
        nodeKind = node.Kind,
        fragment.Category,
        fragmentKind = fragment.Kind,
        fragment.FenceType,
        text = fragment.Text
      });
      history?.Add(fragment);
      if (emitLive)
      {
        TextReady?.Invoke(fragment);
      }
    }

    if (emitLive)
    {
      MessagesChanged?.Invoke(preview.ToArray());
    }
  }

  /// <summary>
  /// Retains one request_user_input call until its matching output arrives.
  /// </summary>
  private static void RegisterInputRequest(
    CodexInputRequest? request,
    IDictionary<string, CodexInputRequest> pendingInputRequests)
  {
    if (request is not null)
    {
      pendingInputRequests[request.CallId] = request;
    }
  }

  /// <summary>
  /// Converts a matched input response into User-profile narration without
  /// treating the selection as a new conversational turn.
  /// </summary>
  private static IReadOnlyList<ExtractedNode> ResolveInputResponse(
    CodexInputResponse? response,
    IDictionary<string, CodexInputRequest> pendingInputRequests)
  {
    if (response is null)
    {
      return Array.Empty<ExtractedNode>();
    }

    if (!pendingInputRequests.TryGetValue(
          response.CallId,
          out CodexInputRequest? request) ||
        request is null)
    {
      return Array.Empty<ExtractedNode>();
    }
    pendingInputRequests.Remove(response.CallId);

    var nodes = new List<ExtractedNode>();
    foreach (CodexInputQuestion question in request.Questions)
    {
      if (question.IsSecret ||
          !response.Answers.TryGetValue(
            question.Id,
            out IReadOnlyList<string>? selectedAnswers) ||
          selectedAnswers is null)
      {
        continue;
      }

      var spokenSelections = new List<string>();
      foreach (string selectedAnswer in selectedAnswers)
      {
        int optionIndex = FindSelectedOptionIndex(
          selectedAnswer,
          question.Options);
        if (optionIndex >= 0)
        {
          CodexInputOption option = question.Options[optionIndex];
          string optionText = option.Label.Length != 0
            ? option.Label
            : option.Description;
          spokenSelections.Add(EnsureTerminalPunctuation(
            $"Selected option {optionIndex + 1}: {optionText}"));
        }
        else
        {
          spokenSelections.Add(EnsureTerminalPunctuation(
            $"Selected: {selectedAnswer}"));
        }
      }

      if (spokenSelections.Count != 0)
      {
        nodes.Add(new ExtractedNode(
          "codex.user_input_answer",
          ContentCategory.User,
          string.Join(" ", spokenSelections),
          response.Timestamp,
          StartsUserTurn: false));
      }
    }
    return nodes;
  }

  /// <summary>
  /// Matches a returned label or option-number form to its source option.
  /// </summary>
  private static int FindSelectedOptionIndex(
    string selectedAnswer,
    IReadOnlyList<CodexInputOption> options)
  {
    string trimmed = selectedAnswer.Trim();
    for (int index = 0; index < options.Count; ++index)
    {
      if (string.Equals(
            trimmed,
            options[index].Label,
            StringComparison.OrdinalIgnoreCase) ||
          string.Equals(
            trimmed,
            $"Option {index + 1}",
            StringComparison.OrdinalIgnoreCase) ||
          trimmed.StartsWith(
            $"Option {index + 1}:",
            StringComparison.OrdinalIgnoreCase))
      {
        return index;
      }
    }

    return int.TryParse(trimmed, out int optionNumber) &&
      optionNumber >= 1 &&
      optionNumber <= options.Count
        ? optionNumber - 1
        : -1;
  }

  /// <summary>
  /// Ensures each generated selection is one independently split sentence.
  /// </summary>
  private static string EnsureTerminalPunctuation(string text)
  {
    string trimmed = text.Trim();
    return trimmed.Length != 0 && trimmed[^1] is not ('.' or '!' or '?')
      ? trimmed + "."
      : trimmed;
  }

  /// <summary>
  /// Loads existing eligible speech into navigation history without replaying
  /// the whole conversation.
  /// </summary>
  private SpeechHistorySnapshot LoadExistingHistory(
    LocatedSession session,
    bool speakExistingLatestTurn,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    bool includeRolledBackTurns)
  {
    var fragments = new List<SpeechFragment>();
    EligibleHistory eligibleHistory = ReadEligibleHistory(
      session,
      pendingInputRequests,
      includeRolledBackTurns);
    foreach (ExtractedNode node in eligibleHistory.Nodes)
    {
      ProcessNode(
        session,
        node,
        ref nextNodeId,
        recentFingerprintQueue,
        recentFingerprintSet,
        preview,
        fragments,
        emitLive: false);
    }

    PlaybackStartMode startMode = speakExistingLatestTurn
      ? PlaybackStartMode.LatestTurn
      : PlaybackStartMode.LiveEnd;

    DiagnosticLog.Write("monitor.history_loaded", new
    {
      session.Source,
      session.Path,
      fragmentCount = fragments.Count,
      startMode
    });
    return new SpeechHistorySnapshot(
      fragments,
      eligibleHistory.Completions,
      eligibleHistory.BackgroundWorkEvents,
      startMode);
  }

  /// <summary>
  /// Reads all currently present conversational nodes and turn completions.
  /// </summary>
  private EligibleHistory ReadEligibleHistory(
    LocatedSession session,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    bool includeRolledBackTurns,
    DateTime? minimumTimestampUtc = null)
  {
    pendingInputRequests.Clear();
    var nodes = new List<ExtractedNode>();
    var completions = new List<TurnCompletion>();
    var backgroundWorkEvents = new List<BackgroundWorkEvent>();
    IReadOnlyList<ExtractionResult> results = _canonicalExtractor.Load(
      session.Source,
      ReadSharedLines(session.Path),
      ProjectionOptions(session, includeRolledBackTurns));
    foreach (ExtractionResult result in results)
    {
      RegisterInputRequest(result.InputRequest, pendingInputRequests);
      IReadOnlyList<ExtractedNode> responseNodes = ResolveInputResponse(
        result.InputResponse,
        pendingInputRequests);
      foreach (ExtractedNode node in result.Nodes.Concat(responseNodes))
      {
        if (minimumTimestampUtc is null ||
            IsAtOrAfter(node.Timestamp, minimumTimestampUtc.Value))
        {
          nodes.Add(node);
        }
      }

      foreach (BackgroundWorkEvent workEvent in
               result.BackgroundWorkEvents ??
                 Array.Empty<BackgroundWorkEvent>())
      {
        if (minimumTimestampUtc is null ||
            workEvent.StartUtc.UtcDateTime >= minimumTimestampUtc.Value ||
            workEvent.EndUtc is DateTimeOffset endUtc &&
              endUtc.UtcDateTime >= minimumTimestampUtc.Value)
        {
          backgroundWorkEvents.Add(workEvent);
        }
      }

      TurnCompletion? completion = CreateTurnCompletion(
        result.CompletionTimestamp);
      if (completion is not null &&
          (minimumTimestampUtc is null ||
           completion.TimestampUtc.UtcDateTime >= minimumTimestampUtc.Value))
      {
        completions.Add(completion);
      }
    }

    return new EligibleHistory(nodes, completions, backgroundWorkEvents);
  }

  /// <summary>
  /// Builds the core projection options for one selected session.
  /// </summary>
  private static AIConversationCoreProjectOptions ProjectionOptions(
    LocatedSession session,
    bool includeRolledBackTurns)
  {
    return new AIConversationCoreProjectOptions(
      IncludeRolledBackTurns: includeRolledBackTurns,
      CodexSessionIndexPath: session.Source == AgentSource.Codex
        ? SessionLocator.GetCodexSessionIndexPath()
        : null);
  }

  /// <summary>
  /// Parses one terminal event timestamp without exposing it as speech.
  /// </summary>
  private static TurnCompletion? CreateTurnCompletion(string? timestamp)
  {
    DateTimeOffset? parsed = ParseTimestampUtc(timestamp);
    return parsed is DateTimeOffset value
      ? new TurnCompletion(value)
      : null;
  }

  private sealed record EligibleHistory(
    IReadOnlyList<ExtractedNode> Nodes,
    IReadOnlyList<TurnCompletion> Completions,
    IReadOnlyList<BackgroundWorkEvent> BackgroundWorkEvents);

  /// <summary>
  /// Checks whether an ISO timestamp is at or after a UTC threshold.
  /// </summary>
  private static bool IsAtOrAfter(string? timestamp, DateTime minimumUtc)
  {
    DateTimeOffset? parsed = ParseTimestampUtc(timestamp);
    return parsed is not null && parsed.Value.UtcDateTime >= minimumUtc;
  }

  /// <summary>
  /// Parses one source timestamp and normalizes it to UTC.
  /// </summary>
  private static DateTimeOffset? ParseTimestampUtc(string? timestamp)
  {
    return timestamp is not null &&
      DateTimeOffset.TryParse(
        timestamp,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out DateTimeOffset parsed)
          ? parsed.ToUniversalTime()
          : null;
  }

  /// <summary>
  /// Reads a shared JSONL file line by line.
  /// </summary>
  private static IEnumerable<string> ReadSharedLines(string path)
  {
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(
      stream,
      Encoding.UTF8,
      detectEncodingFromByteOrderMarks: true,
      bufferSize: 64 * 1024,
      leaveOpen: false);

    while (reader.ReadLine() is string line)
    {
      if (!string.IsNullOrWhiteSpace(line))
      {
        yield return line;
      }
    }
  }

  /// <summary>
  /// Gets a current write timestamp without retaining stale FileInfo state.
  /// </summary>
  private static DateTime GetCurrentLastWriteUtc(string path)
  {
    try
    {
      return File.GetLastWriteTimeUtc(path);
    }
    catch (IOException)
    {
      return DateTime.MinValue;
    }
    catch (UnauthorizedAccessException)
    {
      return DateTime.MinValue;
    }
  }

  /// <summary>
  /// Creates a whitespace-insensitive content fingerprint.
  /// </summary>
  private static string CreateFingerprint(string text)
  {
    string canonical = new string(
      text
        .Where(character => !char.IsWhiteSpace(character))
        .Select(char.ToLowerInvariant)
        .ToArray());
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    return Convert.ToHexString(digest);
  }

  /// <summary>
  /// Retains a bounded set of recently accepted node fingerprints.
  /// </summary>
  private static void RememberFingerprint(
    string fingerprint,
    Queue<string> queue,
    HashSet<string> set)
  {
    queue.Enqueue(fingerprint);
    set.Add(fingerprint);
    while (queue.Count > MaximumRecentFingerprints)
    {
      string removed = queue.Dequeue();
      set.Remove(removed);
    }
  }

  /// <summary>
  /// Adds one node to the bounded preview and raises the change event.
  /// </summary>
  private void AddPreview(Queue<string> preview, string text)
  {
    preview.Enqueue(text);
    while (preview.Count > MaximumPreviewNodes)
    {
      preview.Dequeue();
    }
  }

  /// <summary>
  /// Bounds diagnostic text.
  /// </summary>
  private static string Abbreviate(string text, int maximum)
  {
    if (text.Length <= maximum)
    {
      return text;
    }

    return text[..maximum] + "...";
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
