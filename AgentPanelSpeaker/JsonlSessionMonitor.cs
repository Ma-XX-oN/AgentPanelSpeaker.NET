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
/// <param name="FollowLatest">Whether a newer session file can replace the current one.</param>
/// <param name="SpeakExistingLastMessage">Speak the last existing eligible node at start.</param>
/// <param name="PollInterval">File polling interval.</param>
internal sealed record MonitorSettings(
  AgentSource RequestedSource,
  string? ExplicitPath,
  bool FollowLatest,
  bool SpeakExistingLastMessage,
  TimeSpan PollInterval);

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
  public void Stop()
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

    DiagnosticLog.Write("monitor.stop_requested");
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

    Stop();
    _disposed = true;
  }

  /// <summary>
  /// Runs the file-tail loop.
  /// </summary>
  private void Run(MonitorSettings settings, CancellationToken token)
  {
    var recentFingerprintQueue = new Queue<string>();
    var recentFingerprintSet = new HashSet<string>(StringComparer.Ordinal);
    var preview = new Queue<string>();
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

      SpeechHistorySnapshot initialHistory = LoadExistingHistory(
        session,
        settings.SpeakExistingLastMessage,
        ref nextNodeId,
        recentFingerprintQueue,
        recentFingerprintSet,
        preview);
      HistoryLoaded?.Invoke(initialHistory);
      MessagesChanged?.Invoke(preview.ToArray());

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
            SpeechHistorySnapshot switchedHistory = LoadExistingHistory(
              session,
              speakLastExistingNode: false,
              ref nextNodeId,
              recentFingerprintQueue,
              recentFingerprintSet,
              preview,
              playbackFromBeginning: true);
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
  /// Parses and classifies one newly appended JSONL line.
  /// </summary>
  private void ProcessLine(
    LocatedSession session,
    string line,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    long byteOffset)
  {
    try
    {
      ExtractionResult result = JsonlRecordExtractor.Extract(
        session.Source,
        line);
      DiagnosticLog.Write("jsonl.record", new
      {
        session.Source,
        session.Path,
        byteOffset,
        result.RecordType,
        result.PayloadType,
        result.Decision,
        acceptedNodes = result.Nodes.Count,
        linePreview = Abbreviate(line, 240)
      });

      foreach (ExtractedNode node in result.Nodes)
      {
        ProcessNode(
          session,
          node,
          ref nextNodeId,
          recentFingerprintQueue,
          recentFingerprintSet,
          preview);
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
      node.Category + "|" + string.Join(
        "|",
        parts.Select(part =>
          $"{part.Kind}:{part.FenceType}:{part.Text}")));
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
    string previewText = string.Join(" ", parts.Select(part => part.Text));
    AddPreview(
      preview,
      $"[{session.Source} {node.Category} {node.Kind}] {previewText}");

    var fragments = new List<SpeechFragment>();
    foreach (SpeechTextPart part in parts)
    {
      if (part.Kind == SpeechFragmentKind.Prose)
      {
        fragments.AddRange(SentenceSegmenter
          .Split(part.Text, part.PauseAfter)
          .Select(sentence => new SpeechFragment(
            nodeId,
            node.Category,
            SpeechFragmentKind.Prose,
            sentence.Text,
            PauseAfter: sentence.PauseAfter)));
      }
      else
      {
        fragments.Add(new SpeechFragment(
          nodeId,
          node.Category,
          SpeechFragmentKind.FencedCodeLine,
          part.Text,
          part.FenceType,
          part.FenceBlockId,
          part.FenceLineIndex,
          part.FenceLineCount,
          PauseAfter: part.PauseAfter));
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
  /// Loads existing eligible speech into navigation history without replaying
  /// the whole conversation.
  /// </summary>
  private SpeechHistorySnapshot LoadExistingHistory(
    LocatedSession session,
    bool speakLastExistingNode,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    bool playbackFromBeginning = false)
  {
    var fragments = new List<SpeechFragment>();
    foreach (ExtractedNode node in ReadEligibleNodes(session))
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

    PlaybackStartMode startMode = playbackFromBeginning
      ? PlaybackStartMode.Beginning
      : speakLastExistingNode
        ? PlaybackStartMode.LastEnabledNode
        : PlaybackStartMode.LiveEnd;

    DiagnosticLog.Write("monitor.history_loaded", new
    {
      session.Source,
      session.Path,
      fragmentCount = fragments.Count,
      startMode
    });
    return new SpeechHistorySnapshot(fragments, startMode);
  }

  /// <summary>
  /// Reads all currently present conversational nodes.
  /// </summary>
  private static IReadOnlyList<ExtractedNode> ReadEligibleNodes(
    LocatedSession session,
    DateTime? minimumTimestampUtc = null)
  {
    var nodes = new List<ExtractedNode>();
    foreach (string line in ReadSharedLines(session.Path))
    {
      try
      {
        ExtractionResult result = JsonlRecordExtractor.Extract(
          session.Source,
          line);
        foreach (ExtractedNode node in result.Nodes)
        {
          if (minimumTimestampUtc is null ||
              IsAtOrAfter(node.Timestamp, minimumTimestampUtc.Value))
          {
            nodes.Add(node);
          }
        }
      }
      catch (JsonException)
      {
        // Ignore malformed historical records; live parsing logs new failures.
      }
    }

    return nodes;
  }


  /// <summary>
  /// Checks whether an ISO timestamp is at or after a UTC threshold.
  /// </summary>
  private static bool IsAtOrAfter(string? timestamp, DateTime minimumUtc)
  {
    return timestamp is not null &&
      DateTimeOffset.TryParse(
        timestamp,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out DateTimeOffset parsed) &&
      parsed.UtcDateTime >= minimumUtc;
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
