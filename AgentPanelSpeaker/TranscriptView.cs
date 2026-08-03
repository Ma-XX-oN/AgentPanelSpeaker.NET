using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Renders the selected JSONL session as Markdown-derived HTML and tracks the
/// current speech position.
/// </summary>
internal sealed class TranscriptView : UserControl
{
  private readonly WebView2 _webView = new();
  private readonly Label _loadingLabel = new();
  private readonly Label _failureLabel = new();
  private readonly System.Windows.Forms.Timer _refreshTimer = new();
  private readonly System.Windows.Forms.Timer _settingsApplyTimer = new();
  private readonly MarkdownPipeline _pipeline;
  private string? _sessionPath;
  private string _sessionDisplayName = string.Empty;
  private bool _restoredFromSettings;
  private AgentSource _source;
  private DateTime _lastWriteUtc;
  private long _lastLength = -1;
  private bool _initialized;
  private bool _dark;
  private bool _refreshInProgress;
  private bool _refreshPending;
  private bool _refreshPendingForce;
  private int _renderGeneration;
  private int _activeRenderGeneration = -1;
  private CancellationTokenSource? _renderCancellation;
  private TranscriptSettings _settings = TranscriptSettings.Default;
  private TranscriptPlaybackPosition? _pendingPosition;
  private bool _settingsApplyPending;
  private long _settingsMessageSequence;
  private long _playbackMessageSequence;
  private TranscriptSearchIndex? _searchIndex;
  private TranscriptVirtualDocument? _virtualDocument;
  private IReadOnlyList<TranscriptNodeIdentity> _identities =
    Array.Empty<TranscriptNodeIdentity>();
  private int _windowStartIndex = -1;
  private int _windowEndIndex = -1;
  private CancellationTokenSource? _findCancellation;

  /// <summary>
  /// Initializes the embedded renderer and live-file refresh timer.
  /// </summary>
  public TranscriptView()
  {
    _pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .Build();

    Dock = DockStyle.Fill;
    _webView.Dock = DockStyle.Fill;
    _webView.Visible = false;
    _loadingLabel.Dock = DockStyle.Fill;
    _loadingLabel.Text = "Preparing transcript viewer…";
    _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;
    _loadingLabel.Visible = true;
    _failureLabel.Dock = DockStyle.Fill;
    _failureLabel.TextAlign = ContentAlignment.MiddleCenter;
    _failureLabel.Visible = false;
    Controls.Add(_webView);
    Controls.Add(_loadingLabel);
    Controls.Add(_failureLabel);

    _webView.CoreWebView2InitializationCompleted +=
      WebViewInitializationCompleted;
    _refreshTimer.Interval = 250;
    _refreshTimer.Tick += RefreshTimerTick;
    _settingsApplyTimer.Interval = 100;
    _settingsApplyTimer.Tick += SettingsApplyTimerTick;
    _ = InitializeAsync();
  }

  /// <summary>
  /// Raised when the rendered page receives a transport hotkey.
  /// </summary>
  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;

  /// <summary>
  /// Raised when the find popup requests that the speech marker move to a match.
  /// </summary>
  public event EventHandler<FindSeekRequestedEventArgs>? FindSeekRequested;

  /// <summary>
  /// Raised when the transcript overlay or manual scrolling changes follow mode.
  /// </summary>
  public event Action<bool>? FollowSpeechChanged;

  /// <summary>
  /// Selects a transcript source and immediately renders its current content.
  /// </summary>
  public void SelectSession(
    string path,
    AgentSource source,
    string displayName,
    bool restoredFromSettings = false)
  {
    bool sameSession = string.Equals(
      _sessionPath,
      path,
      StringComparison.OrdinalIgnoreCase) && _source == source;
    _sessionPath = path;
    _sessionDisplayName = string.IsNullOrWhiteSpace(displayName)
      ? Path.GetFileName(path)
      : displayName;
    _restoredFromSettings = restoredFromSettings;
    _source = source;
    _refreshTimer.Start();
    if (sameSession)
    {
      QueueRefresh(force: false);
      return;
    }

    _pendingPosition = null;
    _searchIndex = null;
    _virtualDocument = null;
    _identities = Array.Empty<TranscriptNodeIdentity>();
    _windowStartIndex = -1;
    _windowEndIndex = -1;
    CancelFindSearch();
    _lastWriteUtc = DateTime.MinValue;
    _lastLength = -1;
    _renderGeneration++;
    CancelActiveRender();
    ShowLoading(GetLoadingText());
    QueueRefresh(force: true);
  }

  /// <summary>
  /// Clears the rendered page when no transcript is selected.
  /// </summary>
  public void ClearSession()
  {
    _pendingPosition = null;
    _searchIndex = null;
    _virtualDocument = null;
    _identities = Array.Empty<TranscriptNodeIdentity>();
    _windowStartIndex = -1;
    _windowEndIndex = -1;
    CancelFindSearch();
    _sessionPath = null;
    _sessionDisplayName = string.Empty;
    _restoredFromSettings = false;
    _lastWriteUtc = DateTime.MinValue;
    _lastLength = -1;
    _renderGeneration++;
    CancelActiveRender();
    _refreshTimer.Stop();
    ShowLoading("Select a session to view its transcript.");
    if (_initialized)
    {
      _ = ExecuteAsync("replaceTranscript('', false, []);");
    }
  }

  /// <summary>
  /// Applies current renderer settings immediately.
  /// </summary>
  public void ApplySettings(TranscriptSettings settings, bool dark)
  {
    _settings = settings.Normalize();
    _dark = dark;
    Color page = dark
      ? Color.FromArgb(30, 32, 35)
      : Color.FromArgb(247, 247, 245);
    Color text = dark
      ? Color.FromArgb(217, 220, 225)
      : Color.FromArgb(36, 38, 41);
    _loadingLabel.BackColor = page;
    _loadingLabel.ForeColor = text;
    _failureLabel.BackColor = page;
    _failureLabel.ForeColor = text;
    if (_initialized)
    {
      QueueSettingsApply(immediate: false);
    }
  }

  /// <summary>
  /// Updates the filled or paused transcript marker through a low-latency,
  /// one-way WebView message.
  /// </summary>
  public void ShowPlaybackPosition(TranscriptPlaybackPosition position)
  {
    _pendingPosition = position;
    if (!_initialized || _refreshInProgress)
    {
      return;
    }

    TranscriptNodeIdentity? identity = _identities.FirstOrDefault(
      item => item.NodeId == position.NodeId);
    if (_settings.FollowSpeech && identity is not null &&
        _virtualDocument is TranscriptVirtualDocument document &&
        document.TryGetIndex(
          identity.RecordNumber,
          identity.SourceId,
          out int index) &&
        (index < _windowStartIndex || index > _windowEndIndex))
    {
      _ = RenderWindowForRecordAsync(
        identity.RecordNumber,
        identity.SourceId,
        "playback-position",
        matchIndex: null);
      return;
    }

    PostPlaybackPosition(position);
  }

  private void QueueSettingsApply(bool immediate)
  {
    if (!_initialized)
    {
      return;
    }

    _settingsApplyPending = true;
    if (immediate)
    {
      _settingsApplyTimer.Stop();
      PostLatestSettings();
      return;
    }
    if (!_settingsApplyTimer.Enabled)
    {
      _settingsApplyTimer.Start();
    }
  }

  private void SettingsApplyTimerTick(object? sender, EventArgs eventArgs)
  {
    _settingsApplyTimer.Stop();
    PostLatestSettings();
  }

  private void PostLatestSettings()
  {
    if (!_initialized || !_settingsApplyPending)
    {
      return;
    }

    _settingsApplyPending = false;
    TranscriptSettings settings = _settings;
    bool dark = _dark;
    Color colour = settings.GetHighlightColour(dark);
    PostMessage(new
    {
      type = "settings",
      sequence = ++_settingsMessageSequence,
      highlight = ToCss(colour),
      duration = settings.FadeMilliseconds,
      follow = settings.FollowSpeech,
      dark
    });
  }

  private void PostPlaybackPosition(TranscriptPlaybackPosition position)
  {
    DiagnosticLog.Write("transcript.marker_posted", new
    {
      position.State,
      position.NodeId,
      position.FragmentText,
      position.WordIndex,
      position.Word,
      position.CharacterPosition,
      position.CharacterCount,
      position.BoundaryTimestamp,
      postedTimestamp = Stopwatch.GetTimestamp()
    });
    PostMessage(new
    {
      type = "playback",
      sequence = ++_playbackMessageSequence,
      state = ToScriptState(position.State),
      fragmentText = position.FragmentText,
      wordIndex = position.WordIndex,
      wordText = position.Word,
      nodeId = position.NodeId,
      characterPosition = position.CharacterPosition,
      characterCount = position.CharacterCount,
      boundaryTimestamp = position.BoundaryTimestamp,
      follow = _settings.FollowSpeech
    });
  }

  private void PostMessage<T>(T message)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (!_initialized || _webView.IsDisposed || core is null)
      {
        return;
      }
      core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException)
    {
      DiagnosticLog.Write("transcript.message_failed", new
      {
        exception = exception.ToString()
      });
    }
  }

  /// <summary>
  /// Opens and focuses the transcript find popup.
  /// </summary>
  public void OpenFind()
  {
    if (!_initialized)
    {
      return;
    }

    _webView.Focus();
    BeginInvoke(new Action(() =>
    {
      if (!_webView.IsDisposed)
      {
        _webView.Focus();
        _ = ExecuteAsync("openFind();");
      }
    }));
  }

  /// <summary>
  /// Applies the application theme to the transcript.
  /// </summary>
  public void ApplyTheme(bool dark)
  {
    ApplySettings(_settings, dark);
  }

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _renderGeneration++;
      CancelActiveRender();
      _refreshTimer.Stop();
      _refreshTimer.Dispose();
      _settingsApplyTimer.Stop();
      _settingsApplyTimer.Dispose();
      _webView.Dispose();
    }
    base.Dispose(disposing);
  }

  private async Task InitializeAsync()
  {
    try
    {
      await _webView.EnsureCoreWebView2Async();
    }
    catch (Exception exception)
    {
      ShowInitializationFailure(exception);
    }
  }

  private void WebViewInitializationCompleted(
    object? sender,
    CoreWebView2InitializationCompletedEventArgs eventArgs)
  {
    if (!eventArgs.IsSuccess)
    {
      ShowInitializationFailure(
        eventArgs.InitializationException ??
        new InvalidOperationException("WebView2 initialization failed."));
      return;
    }

    CoreWebView2? core = _webView.CoreWebView2;
    if (core is null)
    {
      ShowInitializationFailure(new InvalidOperationException(
        "WebView2 reported success without creating its core instance."));
      return;
    }
    core.Settings.AreDefaultContextMenusEnabled = true;
    core.Settings.AreDevToolsEnabled = false;
    core.Settings.IsStatusBarEnabled = false;
    core.WebMessageReceived += WebMessageReceived;
    _webView.NavigationCompleted += WebViewNavigationCompleted;
    core.NavigateToString(BuildShellHtml());
  }

  private void WebViewNavigationCompleted(
    object? sender,
    CoreWebView2NavigationCompletedEventArgs eventArgs)
  {
    if (!eventArgs.IsSuccess)
    {
      ShowInitializationFailure(new InvalidOperationException(
        $"Transcript page navigation failed: {eventArgs.WebErrorStatus}."));
      return;
    }

    _initialized = true;
    _failureLabel.Visible = false;
    _webView.Visible = true;
    ApplySettings(_settings, _dark);
    QueueSettingsApply(immediate: true);
    if (string.IsNullOrWhiteSpace(_sessionPath))
    {
      ShowLoading("Select a session to view its transcript.");
    }
    else
    {
      QueueRefresh(force: true);
    }
  }

  private void RefreshTimerTick(object? sender, EventArgs eventArgs)
  {
    QueueRefresh(force: false);
  }

  private void QueueRefresh(bool force)
  {
    if (!_initialized || string.IsNullOrWhiteSpace(_sessionPath))
    {
      return;
    }
    if (_refreshInProgress &&
        _activeRenderGeneration == _renderGeneration)
    {
      _refreshPending = true;
      _refreshPendingForce |= force;
      return;
    }
    _ = RefreshTranscriptAsync(force, _renderGeneration);
  }

  private async Task RefreshTranscriptAsync(bool force, int generation)
  {
    string? path = _sessionPath;
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      ShowLoading("The selected transcript file is unavailable.");
      return;
    }

    var info = new FileInfo(path);
    if (!force && info.LastWriteTimeUtc == _lastWriteUtc &&
        info.Length == _lastLength)
    {
      return;
    }
    if (generation != _renderGeneration)
    {
      return;
    }

    var cancellation = new CancellationTokenSource();
    _renderCancellation = cancellation;
    _activeRenderGeneration = generation;
    _refreshInProgress = true;
    if (force)
    {
      ShowLoading(GetLoadingText());
    }
    DiagnosticLog.Write("transcript.render_started", new
    {
      path,
      force,
      info.Length
    });
    var renderTimer = Stopwatch.StartNew();

    try
    {
      AgentSource source = _source;
      CancellationToken token = cancellation.Token;
      TranscriptRenderPayload payload = await Task.Run(() =>
      {
        string markdown = string.Empty;
        IReadOnlyList<TranscriptNodeIdentity> identities =
          Array.Empty<TranscriptNodeIdentity>();
        var options = new ParallelOptions
        {
          CancellationToken = token
        };
        Parallel.Invoke(
          options,
          () => identities = TranscriptNodeIdentityMap.Build(
            path,
            source,
            token),
          () => markdown = TranscriptMarkdownFormatter.Format(
            path,
            source,
            token));
        token.ThrowIfCancellationRequested();
        string html = Markdown.ToHtml(markdown, _pipeline);
        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
          html,
          identities,
          token);
        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
        return new TranscriptRenderPayload(document, identities, searchIndex);
      }, token);

      long preparationMilliseconds = renderTimer.ElapsedMilliseconds;
      cancellation.Token.ThrowIfCancellationRequested();
      if (generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      _virtualDocument = payload.Document;
      _identities = payload.Identities;
      int focalIndex = ResolveInitialWindowIndex(payload.Document, payload.Identities);
      TranscriptWindow window = payload.Document.CreateWindow(focalIndex);
      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: focalIndex);
      long domStartMilliseconds = renderTimer.ElapsedMilliseconds;
      if (!await ExecuteAsync(script))
      {
        ShowLoading("Unable to load transcript view. See diagnostic log.");
        return;
      }
      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;

      if (_pendingPosition is TranscriptPlaybackPosition latestPosition &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            latestPosition,
            out int latestIndex) &&
          (latestIndex < _windowStartIndex || latestIndex > _windowEndIndex))
      {
        window = payload.Document.CreateWindow(latestIndex);
        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex)))
        {
          ShowLoading("Unable to position transcript at the voice marker. " +
            "See diagnostic log.");
          return;
        }
        _windowStartIndex = window.StartIndex;
        _windowEndIndex = window.EndIndex;
        focalIndex = latestIndex;
      }

      long domMilliseconds =
        renderTimer.ElapsedMilliseconds - domStartMilliseconds;
      _searchIndex = payload.SearchIndex;
      _lastWriteUtc = info.LastWriteTimeUtc;
      _lastLength = info.Length;
      HideLoading();
      _restoredFromSettings = false;
      QueueSettingsApply(immediate: true);
      if (_pendingPosition is TranscriptPlaybackPosition pending)
      {
        PostPlaybackPosition(pending);
      }
      DiagnosticLog.Write("transcript.render_completed", new
      {
        path,
        force,
        identityCount = payload.Identities.Count,
        preparationMilliseconds,
        domMilliseconds,
        totalMilliseconds = renderTimer.ElapsedMilliseconds
      });
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      DiagnosticLog.Write("transcript.render_cancelled", new
      {
        path,
        generation,
        elapsedMilliseconds = renderTimer.ElapsedMilliseconds
      });
    }
    catch (Exception exception)
    {
      DiagnosticLog.Write("transcript.render_failed", new
      {
        path,
        exception = exception.ToString()
      });
      ShowLoading("Unable to load transcript view. See diagnostic log.");
    }
    finally
    {
      if (ReferenceEquals(_renderCancellation, cancellation))
      {
        _renderCancellation = null;
      }
      cancellation.Dispose();
      if (generation == _renderGeneration &&
          _activeRenderGeneration == generation)
      {
        _refreshInProgress = false;
        if (_refreshPending)
        {
          bool pendingForce = _refreshPendingForce;
          _refreshPending = false;
          _refreshPendingForce = false;
          QueueRefresh(pendingForce);
        }
      }
    }
  }

  private void CancelActiveRender()
  {
    CancellationTokenSource? cancellation = _renderCancellation;
    _renderCancellation = null;
    cancellation?.Cancel();
    _refreshInProgress = false;
    _activeRenderGeneration = -1;
    _refreshPending = false;
    _refreshPendingForce = false;
  }

  private string GetLoadingText()
  {
    string prefix = _restoredFromSettings
      ? "Restoring saved transcript"
      : "Loading transcript";
    string name = string.IsNullOrWhiteSpace(_sessionDisplayName)
      ? Path.GetFileName(_sessionPath) ?? string.Empty
      : _sessionDisplayName;
    return string.IsNullOrWhiteSpace(name)
      ? prefix + "…"
      : prefix + ":" + Environment.NewLine + name + "…";
  }

  private void ShowLoading(string text)
  {
    _loadingLabel.Text = text;
    _loadingLabel.Visible = true;
    _loadingLabel.BringToFront();
  }

  private void HideLoading()
  {
    _loadingLabel.Visible = false;
  }

  private static string ToScriptState(TranscriptPlaybackState state)
  {
    return state switch
    {
      TranscriptPlaybackState.Speaking => "speaking",
      TranscriptPlaybackState.Paused => "paused",
      TranscriptPlaybackState.PausedAtLiveEnd => "paused-end",
      TranscriptPlaybackState.WaitingAtLiveEnd => "waiting-end",
      _ => "none"
    };
  }

  private void WebMessageReceived(
    object? sender,
    CoreWebView2WebMessageReceivedEventArgs eventArgs)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse(
        eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (!root.TryGetProperty("type", out JsonElement typeElement))
      {
        return;
      }

      string type = typeElement.GetString() ?? string.Empty;
      if (type == "playback-applied")
      {
        DiagnosticLog.Write("transcript.marker_applied", new
        {
          sequence = ReadOptionalInt64(root, "sequence"),
          nodeId = ReadOptionalInt64(root, "nodeId"),
          wordIndex = ReadOptionalInt32(root, "wordIndex"),
          wordText = ReadOptionalString(root, "wordText"),
          fragmentText = ReadOptionalString(root, "fragmentText"),
          state = ReadOptionalString(root, "state"),
          rangeStart = ReadOptionalInt32(root, "rangeStart"),
          rangeEnd = ReadOptionalInt32(root, "rangeEnd"),
          boundaryWordIndex = ReadOptionalInt32(root, "boundaryWordIndex"),
          boundaryTimestamp = ReadOptionalInt64(root, "boundaryTimestamp"),
          javascriptTimestamp = ReadOptionalString(root, "javascriptTimestamp"),
          receivedTimestamp = Stopwatch.GetTimestamp()
        });
        return;
      }
      if (type == "mapping-failure" || type == "playback-unmatched")
      {
        DiagnosticLog.Write($"transcript.{type}", new
        {
          nodeId = ReadOptionalInt64(root, "nodeId"),
          recordNumber = ReadOptionalInt32(root, "recordNumber"),
          sourceId = ReadOptionalString(root, "sourceId"),
          text = ReadOptionalString(root, "text")
        });
        return;
      }
      if (type == "find-query")
      {
        _ = HandleFindQueryAsync(root);
        return;
      }
      if (type == "find-cancel")
      {
        CancelFindSearch();
        return;
      }
      if (type == "find-diagnostic")
      {
        DiagnosticLog.Write("transcript.find", new
        {
          action = ReadOptionalString(root, "action"),
          query = ReadOptionalString(root, "query"),
          caseEnabled = ReadOptionalBoolean(root, "caseEnabled"),
          wordEnabled = ReadOptionalBoolean(root, "wordEnabled"),
          regexEnabled = ReadOptionalBoolean(root, "regexEnabled"),
          voicedEnabled = ReadOptionalBoolean(root, "voicedEnabled"),
          matchCount = ReadOptionalInt32(root, "matchCount"),
          currentMatch = ReadOptionalInt32(root, "currentMatch"),
          targetIndex = ReadOptionalInt32(root, "targetIndex"),
          trigger = ReadOptionalString(root, "trigger"),
          error = ReadOptionalString(root, "error"),
          elapsedMilliseconds = ReadOptionalInt32(root, "elapsedMilliseconds"),
          mapElapsedMilliseconds = ReadOptionalInt32(root, "mapElapsedMilliseconds"),
          rawMatchCount = ReadOptionalInt32(root, "rawMatchCount"),
          mappedMatchCount = ReadOptionalInt32(root, "mappedMatchCount"),
          highlightedWordCount = ReadOptionalInt32(root, "highlightedWordCount"),
          wordCount = ReadOptionalInt32(root, "wordCount"),
          openedDetailsCount = ReadOptionalInt32(root, "openedDetailsCount"),
          detailsAncestorCount = ReadOptionalInt32(root, "detailsAncestorCount"),
          detailsAncestors = ReadOptionalString(root, "detailsAncestors"),
          corpusLength = ReadOptionalInt32(root, "corpusLength"),
          corpusWords = ReadOptionalInt32(root, "corpusWords")
        });
        return;
      }
      if (type == "window-measured")
      {
        TranscriptVirtualDocument? virtualDocument = _virtualDocument;
        if (virtualDocument is not null &&
            root.TryGetProperty("measurements", out JsonElement measurements) &&
            measurements.ValueKind == JsonValueKind.Array)
        {
          var values = new Dictionary<int, double>();
          foreach (JsonElement measurement in measurements.EnumerateArray())
          {
            if (measurement.TryGetProperty("index", out JsonElement indexElement) &&
                indexElement.TryGetInt32(out int index) &&
                measurement.TryGetProperty("height", out JsonElement heightElement) &&
                heightElement.TryGetDouble(out double height))
            {
              values[index] = height;
            }
          }
          virtualDocument.UpdateMeasuredHeights(values);
        }
        return;
      }
      if (type == "window-shift")
      {
        int? focalIndex = ReadOptionalInt32(root, "focalIndex");
        if (focalIndex is int validFocalIndex)
        {
          _ = RenderWindowForIndexAsync(
            validFocalIndex,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "anchorRecordNumber"),
            ReadOptionalString(root, "anchorSourceId"),
            ReadOptionalDouble(root, "anchorOffset"));
        }
        return;
      }
      if (type == "window-edge")
      {
        string edge = ReadOptionalString(root, "edge");
        if (edge is "start" or "end")
        {
          _ = RenderWindowForEdgeAsync(edge);
        }
        return;
      }
      if (type == "window-request")
      {
        int? recordNumber = ReadOptionalInt32(root, "recordNumber");
        string sourceId = ReadOptionalString(root, "sourceId");
        if (recordNumber is int validRecordNumber)
        {
          _ = RenderWindowForRecordAsync(
            validRecordNumber,
            sourceId,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "matchIndex"));
        }
        return;
      }
      if (type == "window-for-node")
      {
        long? nodeId = ReadOptionalInt64(root, "nodeId");
        if (nodeId is long validNodeId)
        {
          _ = RenderWindowForNodeAsync(validNodeId, "playback");
        }
        return;
      }
      if (type == "follow-changed")
      {
        bool enabled = ReadOptionalBoolean(root, "enabled") == true;
        FollowSpeechChanged?.Invoke(enabled);
        return;
      }
      if (type == "find-seek")
      {
        long? nodeId = ReadOptionalInt64(root, "nodeId");
        int? nodeWordIndex = ReadOptionalInt32(root, "nodeWordIndex");
        if (nodeId is long validNodeId &&
            nodeWordIndex is int validNodeWordIndex &&
            validNodeWordIndex >= 0)
        {
          FindSeekRequested?.Invoke(
            this,
            new FindSeekRequestedEventArgs(validNodeId, validNodeWordIndex));
        }
        return;
      }
      if (type != "transport" ||
          !root.TryGetProperty("key", out JsonElement keyElement))
      {
        return;
      }

      string key = keyElement.GetString() ?? string.Empty;
      bool alt = root.TryGetProperty("alt", out JsonElement altElement) &&
        altElement.ValueKind == JsonValueKind.True;
      Keys keys = KeyNameToKeys(key);
      if (keys == Keys.None)
      {
        return;
      }
      if (alt)
      {
        keys |= Keys.Alt;
      }
      TransportKeyPressed?.Invoke(
        this,
        new TransportKeyPressedEventArgs(keys));
    }
    catch (JsonException exception)
    {
      DiagnosticLog.Write("transcript.web_message_invalid", new
      {
        exception = exception.ToString()
      });
    }
  }

  private async Task HandleFindQueryAsync(JsonElement root)
  {
    TranscriptSearchIndex? index = _searchIndex;
    long? requestId = ReadOptionalInt64(root, "requestId");
    string query = ReadOptionalString(root, "query");
    if (index is null || requestId is not long validRequestId || query.Length == 0)
    {
      return;
    }

    CancelFindSearch();
    var cancellation = new CancellationTokenSource();
    _findCancellation = cancellation;
    bool caseEnabled = ReadOptionalBoolean(root, "caseEnabled") == true;
    bool wordEnabled = ReadOptionalBoolean(root, "wordEnabled") == true;
    bool regexEnabled = ReadOptionalBoolean(root, "regexEnabled") == true;
    bool voicedEnabled = ReadOptionalBoolean(root, "voicedEnabled") != false;
    int originRecordNumber = ReadOptionalInt32(root, "originRecordNumber") ?? 0;
    string originSourceId = ReadOptionalString(root, "originSourceId");
    int originWordIndex = ReadOptionalInt32(root, "originWordIndex") ?? -1;
    var request = new TranscriptSearchRequest(
      validRequestId,
      query,
      caseEnabled,
      wordEnabled,
      regexEnabled,
      voicedEnabled);
    var timer = Stopwatch.StartNew();
    try
    {
      IReadOnlyList<TranscriptSearchMatch> matches = await index.SearchAsync(
        request,
        cancellation.Token);
      matches = RotateMatchesAfterOrigin(
        matches,
        originRecordNumber,
        originSourceId,
        originWordIndex);
      if (cancellation.IsCancellationRequested ||
          !ReferenceEquals(_findCancellation, cancellation))
      {
        return;
      }
      PostMessage(new
      {
        type = "find-results",
        requestId = validRequestId,
        matches,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
      DiagnosticLog.Write("transcript.find_csharp_completed", new
      {
        requestId = validRequestId,
        query,
        request.Regex,
        request.VoicedOnly,
        matchCount = matches.Count,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      DiagnosticLog.Write("transcript.find_csharp_cancelled", new
      {
        requestId = validRequestId,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    catch (ArgumentException exception) when (request.Regex)
    {
      PostMessage(new
      {
        type = "find-error",
        requestId = validRequestId,
        errorKind = "regex",
        error = exception.Message
      });
    }
    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
    {
      PostMessage(new
      {
        type = "find-error",
        requestId = validRequestId,
        errorKind = "search",
        error = exception.Message
      });
      DiagnosticLog.Write("transcript.find_csharp_failed", new
      {
        requestId = validRequestId,
        query,
        request.Regex,
        exception = exception.ToString()
      });
    }
    finally
    {
      if (ReferenceEquals(_findCancellation, cancellation))
      {
        _findCancellation = null;
      }
      cancellation.Dispose();
    }
  }

  private static IReadOnlyList<TranscriptSearchMatch> RotateMatchesAfterOrigin(
    IReadOnlyList<TranscriptSearchMatch> matches,
    int recordNumber,
    string sourceId,
    int wordIndex)
  {
    if (matches.Count < 2 || wordIndex < 0)
    {
      return matches;
    }
    int first = -1;
    for (int index = 0; index < matches.Count; ++index)
    {
      TranscriptSearchMatch match = matches[index];
      bool sameSource = string.Equals(
        match.SourceId,
        sourceId,
        StringComparison.Ordinal);
      if (match.RecordNumber > recordNumber ||
          (match.RecordNumber == recordNumber && sameSource &&
           match.StartWordIndex > wordIndex))
      {
        first = index;
        break;
      }
    }
    if (first <= 0)
    {
      return matches;
    }
    return matches.Skip(first).Concat(matches.Take(first)).ToArray();
  }

  private void CancelFindSearch()
  {
    CancellationTokenSource? cancellation = _findCancellation;
    _findCancellation = null;
    cancellation?.Cancel();
  }

  private static string ReadOptionalString(
    JsonElement root,
    string propertyName)
  {
    return root.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  private static long? ReadOptionalInt64(
    JsonElement root,
    string propertyName)
  {
    return root.TryGetProperty(propertyName, out JsonElement value) &&
      value.TryGetInt64(out long result)
        ? result
        : null;
  }

  private static int? ReadOptionalInt32(
    JsonElement root,
    string propertyName)
  {
    return root.TryGetProperty(propertyName, out JsonElement value) &&
      value.TryGetInt32(out int result)
        ? result
        : null;
  }

  private static bool? ReadOptionalBoolean(
    JsonElement root,
    string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out JsonElement value))
    {
      return null;
    }
    return value.ValueKind switch
    {
      JsonValueKind.True => true,
      JsonValueKind.False => false,
      _ => null
    };
  }

  private static double? ReadOptionalDouble(
    JsonElement root,
    string propertyName)
  {
    return root.TryGetProperty(propertyName, out JsonElement value) &&
      value.TryGetDouble(out double result)
        ? result
        : null;
  }

  private static Keys KeyNameToKeys(string key)
  {
    if (key.Length != 1)
    {
      return Keys.None;
    }
    char value = char.ToUpperInvariant(key[0]);
    if (value is >= 'A' and <= 'Z')
    {
      return (Keys)((int)Keys.A + (value - 'A'));
    }
    return value switch
    {
      ';' => Keys.OemSemicolon,
      '\'' => Keys.OemQuotes,
      ',' => Keys.Oemcomma,
      '.' => Keys.OemPeriod,
      '/' => Keys.OemQuestion,
      _ => Keys.None
    };
  }

  private async Task<bool> ExecuteAsync(string script)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (!_initialized || _webView.IsDisposed || core is null)
      {
        return false;
      }
      await core.ExecuteScriptAsync(script);
      return true;
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException)
    {
      DiagnosticLog.Write("transcript.script_failed", new
      {
        exception = exception.ToString()
      });
      return false;
    }
  }

  private void ShowInitializationFailure(Exception exception)
  {
    DiagnosticLog.Write("transcript.webview_unavailable", new
    {
      exception = exception.ToString()
    });
    _initialized = false;
    _loadingLabel.Visible = false;
    _webView.Visible = false;
    _failureLabel.Text =
      "The Microsoft Edge WebView2 Runtime is required to render the " +
      "transcript. The remaining AgentPanelSpeaker features are still " +
      "available.";
    _failureLabel.Visible = true;
    _failureLabel.BringToFront();
  }

  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    TranscriptSearchIndex SearchIndex);


  private int ResolveInitialWindowIndex(
    TranscriptVirtualDocument document,
    IReadOnlyList<TranscriptNodeIdentity> identities)
  {
    return _pendingPosition is TranscriptPlaybackPosition position &&
      TryResolvePositionIndex(document, identities, position, out int index)
        ? index
        : Math.Max(0, document.Count - 1);
  }

  private static bool TryResolvePositionIndex(
    TranscriptVirtualDocument document,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    TranscriptPlaybackPosition position,
    out int index)
  {
    index = -1;
    TranscriptNodeIdentity? identity = identities.FirstOrDefault(
      item => item.NodeId == position.NodeId);
    return identity is not null &&
      document.TryGetIndex(identity.RecordNumber, identity.SourceId, out index);
  }

  private string BuildReplaceWindowScript(
    TranscriptWindow window,
    bool preserve,
    int? anchorRecordNumber = null,
    string? anchorSourceId = null,
    double? anchorOffset = null,
    int? focusVirtualIndex = null,
    string? focusEdge = null)
  {
    var keys = window.Records
      .Select(record => record.SourceId + "\0" + record.RecordNumber)
      .ToHashSet(StringComparer.Ordinal);
    IReadOnlyList<TranscriptNodeIdentity> identities = _identities
      .Where(identity => keys.Contains(identity.SourceId + "\0" + identity.RecordNumber))
      .ToArray();
    return "replaceTranscriptWindow(" +
      JsonSerializer.Serialize(window.Html) + "," +
      JsonSerializer.Serialize(preserve) + "," +
      JsonSerializer.Serialize(identities) + "," +
      window.StartIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.EndIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.TopSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.BottomSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      JsonSerializer.Serialize(anchorRecordNumber) + "," +
      JsonSerializer.Serialize(anchorSourceId) + "," +
      JsonSerializer.Serialize(anchorOffset) + "," +
      JsonSerializer.Serialize(focusVirtualIndex) + "," +
      JsonSerializer.Serialize(focusEdge) + ");";
  }

  private async Task RenderWindowForRecordAsync(
    int recordNumber,
    string sourceId,
    string reason,
    int? matchIndex)
  {
    TranscriptVirtualDocument? document = _virtualDocument;
    if (document is null ||
        !document.TryGetIndex(recordNumber, sourceId, out int focalIndex))
    {
      return;
    }
    if (focalIndex >= _windowStartIndex && focalIndex <= _windowEndIndex)
    {
      if (matchIndex is int existingMatch)
      {
        PostMessage(new { type = "window-ready", matchIndex = existingMatch });
      }
      return;
    }
    TranscriptWindow window = document.CreateWindow(focalIndex);
    var timer = Stopwatch.StartNew();
    if (!await ExecuteAsync(BuildReplaceWindowScript(
          window,
          preserve: false,
          focusVirtualIndex: focalIndex)))
    {
      return;
    }
    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    if (_pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    if (matchIndex is int currentMatch)
    {
      PostMessage(new { type = "window-ready", matchIndex = currentMatch });
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason,
      recordNumber,
      sourceId,
      window.StartIndex,
      window.EndIndex,
      recordCount = window.Records.Count,
      htmlCharacters = window.Html.Length,
      elapsedMilliseconds = timer.ElapsedMilliseconds
    });
  }

  private async Task RenderWindowForEdgeAsync(string edge)
  {
    TranscriptVirtualDocument? document = _virtualDocument;
    if (document is null || document.Count == 0)
    {
      return;
    }
    int focalIndex = edge == "start" ? 0 : document.Count - 1;
    TranscriptWindow window = document.CreateWindow(focalIndex);
    var timer = Stopwatch.StartNew();
    if (!await ExecuteAsync(BuildReplaceWindowScript(
          window,
          preserve: false,
          focusVirtualIndex: focalIndex,
          focusEdge: edge)))
    {
      return;
    }
    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    if (_pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason = "keyboard-" + edge,
      focalIndex,
      window.StartIndex,
      window.EndIndex,
      recordCount = window.Records.Count,
      htmlCharacters = window.Html.Length,
      elapsedMilliseconds = timer.ElapsedMilliseconds
    });
  }

  private async Task RenderWindowForIndexAsync(
    int focalIndex,
    string reason,
    int? anchorRecordNumber,
    string anchorSourceId,
    double? anchorOffset)
  {
    TranscriptVirtualDocument? document = _virtualDocument;
    if (document is null)
    {
      return;
    }
    TranscriptWindow window = document.CreateWindow(focalIndex);
    if (window.StartIndex == _windowStartIndex && window.EndIndex == _windowEndIndex)
    {
      return;
    }
    var timer = Stopwatch.StartNew();
    if (!await ExecuteAsync(BuildReplaceWindowScript(
          window,
          preserve: false,
          anchorRecordNumber: anchorRecordNumber,
          anchorSourceId: anchorSourceId,
          anchorOffset: anchorOffset)))
    {
      return;
    }
    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    if (_pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason,
      focalIndex,
      window.StartIndex,
      window.EndIndex,
      recordCount = window.Records.Count,
      htmlCharacters = window.Html.Length,
      elapsedMilliseconds = timer.ElapsedMilliseconds
    });
  }

  private Task RenderWindowForNodeAsync(long nodeId, string reason)
  {
    TranscriptNodeIdentity? identity = _identities.FirstOrDefault(
      item => item.NodeId == nodeId);
    return identity is null
      ? Task.CompletedTask
      : RenderWindowForRecordAsync(
          identity.RecordNumber,
          identity.SourceId,
          reason,
          matchIndex: null);
  }

  private static string ToCss(Color colour)
  {
    return $"rgba({colour.R},{colour.G},{colour.B}," +
      $"{colour.A / 255.0:0.###})";
  }

  private static string BuildShellHtml()
  {
    return """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline'; script-src 'nonce-agent-panel-speaker'; worker-src blob:; object-src 'none'; frame-src 'none'; base-uri 'none'">
<style>
:root {
  color-scheme: light;
  --page: #f7f7f5;
  --text: #242629;
  --muted: #686b70;
  --panel: #ecece8;
  --code: #eeeeea;
  --quote: #d2d5d8;
  --link: #315f87;
  --highlight: rgba(255,222,149,1);
  --fade-ms: 250ms;
}
html.dark {
  color-scheme: dark;
  --page: #1e2023;
  --text: #d9dce1;
  --muted: #a5a9b0;
  --panel: #292c30;
  --code: #272a2e;
  --quote: #555b63;
  --link: #88b6dc;
}
html, body { margin: 0; min-height: 100%; background: var(--page); }
body {
  color: var(--text);
  font: 15px/1.58 "Segoe UI", system-ui, sans-serif;
  padding: 18px 24px 48px;
  overflow-wrap: anywhere;
}
#transcript { max-width: 1050px; margin: 0 auto; }
#view-voice-overlay {
  position: fixed;
  right: 18px;
  bottom: 18px;
  display: flex;
  gap: 2px;
  padding: 3px;
  border: 1px solid rgba(128,128,128,.45);
  border-radius: 6px;
  background: rgba(35,35,35,.58);
  backdrop-filter: blur(4px);
  z-index: 40;
}
.view-voice-button {
  width: 30px;
  height: 28px;
  border: 0;
  border-radius: 4px;
  color: #fff;
  background: transparent;
  font-size: 17px;
  line-height: 26px;
  text-align: center;
}
.virtual-spacer { width: 1px; pointer-events: none; }
.virtual-record { display: flow-root; }
#follow-toggle { cursor: pointer; font-weight: 700; }
#follow-toggle:hover { background: rgba(255,255,255,.18); }
#follow-toggle:active { background: rgba(255,255,255,.28); }
.view-voice-button[disabled] { opacity: .82; }
h1, h2, h3 { line-height: 1.25; margin-top: 1.45em; }
h2 { padding-bottom: .25em; border-bottom: 1px solid var(--quote); }
a { color: var(--link); }
blockquote {
  margin: .75em 0;
  padding: .15em 1em;
  border-left: 4px solid var(--quote);
  color: var(--text);
}
pre, code { font-family: "Cascadia Mono", Consolas, monospace; }
code { background: var(--code); border-radius: 3px; padding: .08em .3em; }
pre { background: var(--code); border-radius: 6px; padding: 12px; overflow: auto; }
pre code { padding: 0; background: transparent; }
details {
  margin: .75em 0;
  padding: .4em .7em;
  border: 1px solid var(--quote);
  border-radius: 6px;
  background: color-mix(in srgb, var(--panel) 65%, transparent);
}
summary { cursor: pointer; color: var(--muted); font-weight: 600; }
.word { border-radius: 2px; }
.word.active { background: var(--highlight); }
.word.paused {
  outline: 2px solid var(--highlight);
  outline-offset: 1px;
  animation: marker-blink 1s steps(1, end) infinite;
}
#find-popup {
  position: fixed;
  top: 8px;
  right: 18px;
  z-index: 1000;
  display: none;
  align-items: center;
  gap: 1px;
  padding: 4px 5px;
  border: 1px solid var(--quote);
  border-radius: 6px;
  background: var(--panel);
  box-shadow: 0 3px 14px rgba(0,0,0,.28);
}
#find-popup.open { display: flex; }
#find-input {
  width: 300px;
  height: 28px;
  box-sizing: border-box;
  border: 1px solid var(--quote);
  border-radius: 4px;
  padding: 3px 7px;
  color: var(--text);
  background: var(--page);
}
.find-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid transparent;
  border-radius: 4px;
  color: var(--text);
  background: transparent;
  cursor: default;
}
.find-button:hover {
  border-color: color-mix(in srgb, var(--quote) 75%, transparent);
  background: color-mix(in srgb, var(--text) 12%, transparent);
}
.find-button:active { background: color-mix(in srgb, var(--text) 20%, transparent); }
.find-button.enabled {
  border-color: var(--link);
  background: color-mix(in srgb, var(--link) 18%, transparent);
}
.find-button:disabled { opacity: .42; }
#find-voiced svg { width: 17px; height: 17px; fill: currentColor; }
#find-count { min-width: 62px; padding: 0 4px; color: var(--muted); text-align: center; white-space: nowrap; }
.word.find-match { box-shadow: inset 0 -2px 0 #c08a00; }
.word.find-current { background: #d99b22; color: #111; }
#live-end-marker {
  display: none;
  max-width: 1050px;
  margin-left: auto;
  margin-right: auto;
  width: 1em;
  height: .72em;
  margin-top: .25em;
  border: 2px solid var(--highlight);
  animation: marker-blink 1s steps(1, end) infinite;
}
@keyframes marker-blink { 50% { opacity: .2; } }
</style>
</head>
<body>
<div id="find-popup" role="search" aria-label="Find in transcript">
  <input id="find-input" type="text" spellcheck="false" aria-label="Find">
  <button type="button" id="find-case" class="find-button" title="Match case (Alt+C)">Aa</button>
  <button type="button" id="find-word" class="find-button" title="Match whole word (Alt+W)">ab</button>
  <button type="button" id="find-regex" class="find-button" title="Use regular expression (Alt+R)">.*</button>
  <button type="button" id="find-voiced" class="find-button enabled" title="Search voiced text only (Alt+V)">
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5.5c-2.2 0-3.3 1.6-4.5 2.4C6.2 8.8 4.8 9.3 3 10c1.7 4.2 5 6.5 9 6.5s7.3-2.3 9-6.5c-1.8-.7-3.2-1.2-4.5-2.1C15.3 7.1 14.2 5.5 12 5.5zm-6.1 5.1c2 .2 4 .4 6.1.4s4.1-.2 6.1-.4c-1.6 2.1-3.6 3.1-6.1 3.1s-4.5-1-6.1-3.1z"/></svg>
  </button>
  <span id="find-count">No results</span>
  <button type="button" id="find-prev" class="find-button" title="Previous match (Shift+Enter)">↑</button>
  <button type="button" id="find-next" class="find-button" title="Next match (Enter)">↓</button>
  <button type="button" id="find-close" class="find-button" title="Close (Escape)">×</button>
</div>
<main id="transcript"></main>
<div id="view-voice-overlay" aria-label="Transcript follow controls">
  <button type="button" class="view-voice-button" disabled title="Current view">👁️</button>
  <button type="button" id="follow-toggle" class="view-voice-button" title="Toggle follow speech">=</button>
  <button type="button" class="view-voice-button" disabled title="Current speech position">👄</button>
</div>
<div id="live-end-marker" aria-label="Next text position"></div>
<script nonce="agent-panel-speaker">
const transcript = document.getElementById('transcript');
const liveEndMarker = document.getElementById('live-end-marker');
const findPopup = document.getElementById('find-popup');
const findInput = document.getElementById('find-input');
const findCase = document.getElementById('find-case');
const findWord = document.getElementById('find-word');
const findRegex = document.getElementById('find-regex');
const findVoiced = document.getElementById('find-voiced');
const findCount = document.getElementById('find-count');
const findPrev = document.getElementById('find-prev');
const findNext = document.getElementById('find-next');
const findClose = document.getElementById('find-close');
const followToggle = document.getElementById('follow-toggle');
let words = [];
let lexicalWords = [];
let currentIndex = -1;
let currentEndIndex = -1;
let voiceMarkerIndex = -1;
let currentNode = -1;
let currentFragmentText = null;
let currentFragmentStart = -1;
let currentFragmentEnd = -1;
let currentBoundaryWordIndex = -1;
let fadeMs = 250;
let followSpeech = true;
let programmaticScrollUntil = 0;
let windowStartIndex = -1;
let windowEndIndex = -1;
let virtualShiftPending = false;
let latestPlaybackSequence = 0;
let latestSettingsSequence = 0;
const fadingAnimations = new WeakMap();
let knownNodeIds = new Set();
let displayWordsByRecord = new Map();
let lexicalWordsByRecord = new Map();
let segmentRangesByNode = new Map();
const reportedMappingFailures = new Set();
const reportedPlaybackFailures = new Set();
let findMatches = [];
let currentFindMatch = -1;
let findGeneration = 0;
let findSearchPending = false;
let findSlowTimer = 0;
let findCaseEnabled = false;
let findWordEnabled = false;
let findRegexEnabled = false;
let findVoicedEnabled = true;
let findCurrentWords = [];
let findInputTimer = 0;

function tokenize(text) {
  return (text || '').toLocaleLowerCase().match(/[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*/gu) || [];
}

function tokenizeDisplay(text) {
  return (text || '').toLocaleLowerCase().match(/[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]/gu) || [];
}

function isLexical(text) {
  return /^[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*$/u.test(text);
}

function wrapWords() {
  words = [];
  lexicalWords = [];
  const walker = document.createTreeWalker(transcript, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parent = node.parentElement;
      if (!parent || /^(SCRIPT|STYLE)$/.test(parent.tagName)) return NodeFilter.FILTER_REJECT;
      return node.nodeValue.trim() ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
    }
  });
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  const rx = /[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]/gu;
  for (const node of nodes) {
    const text = node.nodeValue;
    let match;
    let last = 0;
    const fragment = document.createDocumentFragment();
    while ((match = rx.exec(text)) !== null) {
      fragment.append(text.slice(last, match.index));
      const span = document.createElement('span');
      span.className = 'word';
      span.textContent = match[0];
      span.dataset.normalized = match[0].toLocaleLowerCase();
      span.dataset.index = String(words.length);
      span.dataset.lexical = isLexical(match[0]) ? '1' : '0';
      words.push(span);
      if (span.dataset.lexical === '1') lexicalWords.push(span);
      fragment.append(span);
      last = match.index + match[0].length;
    }
    fragment.append(text.slice(last));
    node.replaceWith(fragment);
  }
}

function replaceTranscriptWindow(
  html,
  preserve,
  nodeMap,
  startIndex = -1,
  endIndex = -1,
  topSpacerHeight = 0,
  bottomSpacerHeight = 0,
  anchorRecordNumber = null,
  anchorSourceId = null,
  anchorOffset = null,
  focusVirtualIndex = null,
  focusEdge = null) {
  clearFindHighlights();
  findCurrentWords = [];
  const nearBottom = document.documentElement.scrollHeight -
    (window.scrollY + window.innerHeight) < 80;
  const previousY = window.scrollY;
  const openDetails = preserve
    ? [...transcript.querySelectorAll('details')].map(x => x.open)
    : [];
  transcript.innerHTML =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +
    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  windowStartIndex = startIndex;
  windowEndIndex = endIndex;
  [...transcript.querySelectorAll('details')].forEach((item, index) => {
    if (index < openDetails.length) item.open = openDetails[index];
  });
  wrapWords();
  assignRecordScopes();
  assignNodeScopes(nodeMap || []);
  const measurements = [...transcript.querySelectorAll('.virtual-record')]
    .map(record => ({
      index:Number(record.dataset.virtualIndex || -1),
      height:record.getBoundingClientRect().height
    }))
    .filter(item => item.index >= 0 && item.height > 0);
  if (measurements.length) {
    chrome.webview.postMessage({type:'window-measured', measurements});
  }
  virtualShiftPending = false;
  if (anchorRecordNumber !== null && anchorOffset !== null) {
    const selector = '.record-anchor[data-jsonl-record="' +
      CSS.escape(String(anchorRecordNumber)) + '"][data-source-id="' +
      CSS.escape(String(anchorSourceId || '')) + '"]';
    const anchor = transcript.querySelector(selector);
    if (anchor) {
      const delta = anchor.getBoundingClientRect().top - Number(anchorOffset);
      programmaticScrollUntil = performance.now() + 500;
      window.scrollBy(0, delta);
    }
  } else if (focusVirtualIndex !== null) {
    const focusRecord = transcript.querySelector(
      '.virtual-record[data-virtual-index="' +
      CSS.escape(String(focusVirtualIndex)) + '"]');
    if (focusRecord) {
      programmaticScrollUntil = performance.now() + 2000;
      if (focusEdge === 'start') {
        window.scrollTo(0, 0);
      } else if (focusEdge === 'end') {
        window.scrollTo(0, document.documentElement.scrollHeight);
      } else {
        focusRecord.scrollIntoView({block:'center', behavior:'auto'});
      }
    }
  }
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  liveEndMarker.style.display = 'none';
  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
}

function replaceTranscript(html, preserve, nodeMap) {
  replaceTranscriptWindow(html, preserve, nodeMap, -1, -1, 0, 0);
}

function updateFollowToggle() {
  followToggle.textContent = followSpeech ? '=' : '≠';
  followToggle.title = followSpeech
    ? 'Following speech; click to stop following'
    : 'Not following speech; click to follow';
}

function setFollowSpeech(enabled, notify) {
  followSpeech = !!enabled;
  updateFollowToggle();
  if (notify) {
    chrome.webview.postMessage({type:'follow-changed', enabled:followSpeech});
  }
  if (followSpeech && currentIndex >= 0) {
    const target = words[currentIndex];
    if (target) {
      programmaticScrollUntil = performance.now() + 500;
      target.scrollIntoView({block:'center', behavior:'auto'});
    }
  }
}

function applySettings(highlight, duration, follow, dark) {
  document.documentElement.classList.toggle('dark', dark);
  document.documentElement.style.setProperty('--highlight', highlight);
  document.documentElement.style.setProperty('--fade-ms', duration + 'ms');
  fadeMs = duration;
  setFollowSpeech(follow, false);
}

function makeRecordKey(recordNumber, sourceId) {
  return sourceId + '\u0000' + recordNumber;
}

function appendRecordWord(map, key, word) {
  let collection = map.get(key);
  if (!collection) {
    collection = [];
    map.set(key, collection);
  }
  collection.push(word);
  return collection.length - 1;
}

function assignRecordScopes() {
  displayWordsByRecord = new Map();
  lexicalWordsByRecord = new Map();
  let recordNumber = '';
  let sourceId = '';
  const walker = document.createTreeWalker(
    transcript,
    NodeFilter.SHOW_ELEMENT);
  while (walker.nextNode()) {
    const element = walker.currentNode;
    if (element.classList.contains('record-anchor')) {
      recordNumber = element.dataset.jsonlRecord || '';
      sourceId = element.dataset.sourceId || '';
      continue;
    }
    if (!element.classList.contains('word')) continue;

    element.dataset.recordNumber = recordNumber;
    element.dataset.sourceId = sourceId;
    if (!recordNumber && !sourceId) continue;

    const key = makeRecordKey(recordNumber, sourceId);
    element.dataset.recordIndex = String(
      appendRecordWord(displayWordsByRecord, key, element));
    if (element.dataset.lexical === '1') {
      element.dataset.lexicalRecordIndex = String(
        appendRecordWord(lexicalWordsByRecord, key, element));
    }
  }
}

function findSequence(
  collection,
  target,
  startAt,
  requiredNodeId,
  requiredRecordNumber,
  requiredSourceId) {
  if (!target.length || !collection.length) return -1;
  const lastStart = collection.length - target.length;
  for (let i = Math.max(0, startAt); i <= lastStart; ++i) {
    let equal = true;
    for (let j = 0; j < target.length; ++j) {
      const candidate = collection[i + j];
      if (candidate.dataset.normalized !== target[j] ||
          (requiredNodeId !== null &&
           candidate.dataset.nodeId !== requiredNodeId) ||
          (requiredRecordNumber !== null &&
           candidate.dataset.recordNumber !== requiredRecordNumber) ||
          (requiredSourceId !== null &&
           candidate.dataset.sourceId !== requiredSourceId)) {
        equal = false;
        break;
      }
    }
    if (equal) return i;
  }
  return -1;
}

function markNodeRange(start, end, nodeId) {
  for (let index = start; index <= end; ++index) {
    words[index].dataset.nodeId = nodeId;
  }
}

function markCollectionRange(collection, start, end, nodeId) {
  for (let index = start; index <= end; ++index) {
    collection[index].dataset.nodeId = nodeId;
  }
}

function rememberSegmentRange(
  nodeId,
  start,
  end,
  displayTarget,
  lexicalTarget) {
  let ranges = segmentRangesByNode.get(nodeId);
  if (!ranges) {
    ranges = [];
    segmentRangesByNode.set(nodeId, ranges);
  }
  ranges.push({
    start,
    end,
    displayKey: displayTarget.join('\u0000'),
    lexicalKey: lexicalTarget.join('\u0000')
  });
}

function assignNodeScopes(nodeMap) {
  knownNodeIds = new Set();
  segmentRangesByNode = new Map();
  const displayCursors = new Map();
  const lexicalCursors = new Map();
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    knownNodeIds.add(nodeId);
    const recordNumber = String(
      item.RecordNumber ?? item.recordNumber ?? '');
    const sourceId = String(item.SourceId ?? item.sourceId ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const key = makeRecordKey(recordNumber, sourceId);
    const recordWords = displayWordsByRecord.get(key) || [];
    const recordLexicalWords = lexicalWordsByRecord.get(key) || [];
    let displayCursor = displayCursors.get(key) || 0;
    let lexicalCursor = lexicalCursors.get(key) || 0;
    let mappedAny = false;
    for (const segment of segments) {
      const displayTarget = tokenizeDisplay(segment);
      const lexicalTarget = tokenize(segment);
      if (!displayTarget.length && !lexicalTarget.length) continue;

      let start = findSequence(
        recordWords,
        displayTarget,
        displayCursor,
        null,
        null,
        null);
      if (start < 0 && displayCursor > 0) {
        start = findSequence(
          recordWords,
          displayTarget,
          0,
          null,
          null,
          null);
      }
      if (start >= 0) {
        const end = start + displayTarget.length - 1;
        markCollectionRange(recordWords, start, end, nodeId);
        const globalStart = Number(recordWords[start].dataset.index);
        const globalEnd = Number(recordWords[end].dataset.index);
        rememberSegmentRange(
          nodeId,
          globalStart,
          globalEnd,
          displayTarget,
          lexicalTarget);
        displayCursor = end + 1;
        displayCursors.set(key, displayCursor);
        while (lexicalCursor < recordLexicalWords.length &&
               Number(recordLexicalWords[lexicalCursor].dataset.index) <=
                 globalEnd) {
          ++lexicalCursor;
        }
        lexicalCursors.set(key, lexicalCursor);
        mappedAny = true;
        continue;
      }

      let lexicalStart = findSequence(
        recordLexicalWords,
        lexicalTarget,
        lexicalCursor,
        null,
        null,
        null);
      if (lexicalStart < 0 && lexicalCursor > 0) {
        lexicalStart = findSequence(
          recordLexicalWords,
          lexicalTarget,
          0,
          null,
          null,
          null);
      }
      if (lexicalStart >= 0) {
        const lexicalEnd = lexicalStart + lexicalTarget.length - 1;
        const tokenStart = Number(
          recordLexicalWords[lexicalStart].dataset.index);
        const tokenEnd = Number(
          recordLexicalWords[lexicalEnd].dataset.index);
        markNodeRange(tokenStart, tokenEnd, nodeId);
        rememberSegmentRange(
          nodeId,
          tokenStart,
          tokenEnd,
          displayTarget,
          lexicalTarget);
        lexicalCursor = lexicalEnd + 1;
        lexicalCursors.set(key, lexicalCursor);
        displayCursor = Number(
          recordLexicalWords[lexicalEnd].dataset.recordIndex) + 1;
        displayCursors.set(key, displayCursor);
        mappedAny = true;
        continue;
      }

      const failureKey = nodeId + ':' + recordNumber + ':' +
        sourceId + ':' + segment;
      if (!reportedMappingFailures.has(failureKey)) {
        reportedMappingFailures.add(failureKey);
        chrome.webview.postMessage({
          type: 'mapping-failure',
          nodeId: Number(nodeId),
          recordNumber: Number(recordNumber),
          sourceId,
          text: segment.slice(0, 240)
        });
      }
    }
    if (!mappedAny) continue;
  }
}

function chooseNearestRange(matches, nodeId) {
  if (!matches.length) return null;
  if (currentIndex < 0) return matches[0];
  if (nodeId < currentNode) {
    const before = matches.filter(match => match.start <= currentIndex);
    return before.length ? before[before.length - 1] : matches[0];
  }
  return matches.reduce((best, candidate) =>
    Math.abs(candidate.start - currentIndex) <
      Math.abs(best.start - currentIndex)
      ? candidate
      : best);
}

function collectRanges(collection, target, nodeKey, lexical) {
  const matches = [];
  if (!target.length || !collection.length) return matches;
  const lastStart = collection.length - target.length;
  for (let index = 0; index <= lastStart; ++index) {
    let equal = true;
    for (let offset = 0; offset < target.length; ++offset) {
      const candidate = collection[index + offset];
      if (candidate.dataset.normalized !== target[offset] ||
          candidate.dataset.nodeId !== nodeKey) {
        equal = false;
        break;
      }
    }
    if (!equal) continue;
    if (lexical) {
      matches.push({
        start: Number(collection[index].dataset.index),
        end: Number(collection[index + target.length - 1].dataset.index)
      });
    } else {
      matches.push({start: index, end: index + target.length - 1});
    }
  }
  return matches;
}

function findFragmentRange(text, nodeId) {
  const nodeKey = String(nodeId);
  const displayTarget = tokenizeDisplay(text);
  const lexicalTarget = tokenize(text);
  const displayKey = displayTarget.join('\u0000');
  const lexicalKey = lexicalTarget.join('\u0000');
  const mapped = segmentRangesByNode.get(nodeKey) || [];
  const matches = mapped.filter(range =>
    (displayKey && range.displayKey === displayKey) ||
    (lexicalKey && range.lexicalKey === lexicalKey));
  const mappedRange = chooseNearestRange(matches, nodeId);
  if (mappedRange) return mappedRange;
  if (knownNodeIds.has(nodeKey)) return null;

  const globalStart = findSequence(
    words,
    displayTarget,
    0,
    null,
    null,
    null);
  if (globalStart >= 0) {
    return {
      start: globalStart,
      end: globalStart + displayTarget.length - 1
    };
  }
  return null;
}

function openAncestors(element) {
  let opened = 0;
  let parent = element?.parentElement;
  while (parent) {
    if (parent.tagName === 'DETAILS' && !parent.open) {
      parent.open = true;
      ++opened;
    }
    parent = parent.parentElement;
  }
  return opened;
}

function reveal(element) {
  if (!followSpeech || !element) return;
  const rect = element.getBoundingClientRect();
  const topComfort = window.innerHeight * .22;
  const bottomComfort = window.innerHeight * .78;
  if (rect.top < topComfort || rect.bottom > bottomComfort) {
    programmaticScrollUntil = performance.now() + 500;
    element.scrollIntoView({block: 'center', behavior: 'auto'});
  }
}

function cancelFade(word) {
  const animation = fadingAnimations.get(word);
  if (animation) {
    animation.cancel();
    fadingAnimations.delete(word);
  }
}

function retireCurrentWord(useFade) {
  if (currentIndex < 0) return;
  const end = Math.max(currentIndex, currentEndIndex);
  const highlight = getComputedStyle(document.documentElement)
    .getPropertyValue('--highlight').trim();
  for (let index = currentIndex; index <= end; ++index) {
    const previous = words[index];
    if (!previous) continue;
    previous.classList.remove('active');
    cancelFade(previous);
    if (useFade && fadeMs > 0) {
      const animation = previous.animate(
        [
          {backgroundColor: highlight},
          {backgroundColor: 'transparent'}
        ],
        {duration: fadeMs, easing: 'linear'});
      fadingAnimations.set(previous, animation);
      animation.onfinish = () => fadingAnimations.delete(previous);
      animation.oncancel = () => fadingAnimations.delete(previous);
    }
  }
  currentIndex = -1;
  currentEndIndex = -1;
}

function clearMarkers() {
  if (currentIndex >= 0) {
    const end = Math.max(currentIndex, currentEndIndex);
    for (let index = currentIndex; index <= end; ++index) {
      words[index]?.classList.remove('paused');
    }
  }
  liveEndMarker.style.display = 'none';
}

function sequenceMatchesAt(index, target, limit) {
  if (index < 0 || index + target.length - 1 > limit) return false;
  for (let offset = 0; offset < target.length; ++offset) {
    if (words[index + offset].dataset.normalized !== target[offset]) {
      return false;
    }
  }
  return true;
}

function findBoundaryRange(fragmentRange, wordText, wordIndex, reset) {
  const target = tokenizeDisplay(wordText);
  if (!target.length) {
    const fallback = Math.min(
      fragmentRange.end,
      fragmentRange.start + Math.max(0, wordIndex));
    return {start: fallback, end: fallback};
  }

  if (!reset && currentBoundaryWordIndex === wordIndex &&
      sequenceMatchesAt(currentIndex, target, fragmentRange.end)) {
    return {start: currentIndex, end: currentIndex + target.length - 1};
  }

  const matches = [];
  for (let index = fragmentRange.start;
       index <= fragmentRange.end - target.length + 1;
       ++index) {
    if (sequenceMatchesAt(index, target, fragmentRange.end)) {
      matches.push({start: index, end: index + target.length - 1});
    }
  }
  if (!matches.length) {
    const fallback = Math.min(
      fragmentRange.end,
      fragmentRange.start + Math.max(0, wordIndex));
    return {start: fallback, end: fallback};
  }

  if (!reset && wordIndex > currentBoundaryWordIndex && currentIndex >= 0) {
    const after = matches.find(match => match.start > currentEndIndex);
    if (after) return after;
    const same = matches.find(match => match.start === currentIndex);
    if (same) return same;
  }

  const expected = Math.min(
    fragmentRange.end,
    fragmentRange.start + Math.max(0, wordIndex));
  return matches.reduce((best, candidate) =>
    Math.abs(candidate.start - expected) < Math.abs(best.start - expected)
      ? candidate
      : best);
}

function applyRangeClass(range, className) {
  for (let index = range.start; index <= range.end; ++index) {
    const word = words[index];
    if (!word) continue;
    cancelFade(word);
    word.classList.add(className);
  }
}

function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {
  setFollowSpeech(follow, false);
  clearMarkers();
  if (state === 'none' || state === 'waiting-end') {
    retireCurrentWord(true);
    return;
  }
  if (state === 'paused-end') {
    retireCurrentWord(true);
    liveEndMarker.style.display = 'block';
    reveal(liveEndMarker);
    return;
  }

  const fragmentChanged = currentFragmentText !== fragmentText ||
    currentNode !== nodeId || currentFragmentStart < 0;
  if (fragmentChanged) {
    const fragmentRange = findFragmentRange(fragmentText, nodeId);
    if (!fragmentRange) {
      const failureKey = String(nodeId) + ':' + (fragmentText || '');
      if (!reportedPlaybackFailures.has(failureKey)) {
        reportedPlaybackFailures.add(failureKey);
        chrome.webview.postMessage({
          type: 'playback-unmatched',
          nodeId,
          text: (fragmentText || '').slice(0, 240)
        });
      }
      currentNode = nodeId;
      if (followSpeech) {
        chrome.webview.postMessage({
          type:'window-for-node',
          nodeId
        });
      }
      return;
    }
    currentFragmentText = fragmentText;
    currentFragmentStart = fragmentRange.start;
    currentFragmentEnd = fragmentRange.end;
    currentBoundaryWordIndex = -1;
  }

  const range = findBoundaryRange(
    {start: currentFragmentStart, end: currentFragmentEnd},
    wordText,
    wordIndex,
    fragmentChanged || wordIndex < currentBoundaryWordIndex);
  const target = words[range.start];
  openAncestors(target);
  if (state === 'paused') {
    retireCurrentWord(false);
    applyRangeClass(range, 'paused');
  } else {
    if (currentIndex >= 0 &&
        (currentIndex !== range.start || currentEndIndex !== range.end)) {
      retireCurrentWord(true);
    }
    applyRangeClass(range, 'active');
  }
  currentIndex = range.start;
  currentEndIndex = range.end;
  voiceMarkerIndex = range.start;
  currentBoundaryWordIndex = wordIndex;
  currentNode = nodeId;
  reveal(target);
}


function clearFindHighlights() {
  for (const word of findCurrentWords) {
    word.classList.remove('find-current');
  }
  findCurrentWords = [];
}

function reportFind(action, extra = {}) {
  chrome.webview.postMessage({
    type: 'find-diagnostic',
    action,
    query: findInput.value,
    caseEnabled: findCaseEnabled,
    wordEnabled: findWordEnabled,
    regexEnabled: findRegexEnabled,
    voicedEnabled: findVoicedEnabled,
    matchCount: findMatches.length,
    currentMatch: currentFindMatch,
    ...extra
  });
}

function updateFindNavigationState() {
  const available = !findSearchPending && findMatches.length > 0;
  findPrev.disabled = !available;
  findNext.disabled = !available;
}

function cancelFindSearch(updateStatus) {
  ++findGeneration;
  if (findSearchPending) {
    chrome.webview.postMessage({type:'find-cancel'});
    findSearchPending = false;
    reportFind('csharp-search-cancelled');
  }
  if (findSlowTimer) {
    clearTimeout(findSlowTimer);
    findSlowTimer = 0;
  }
  if (updateStatus) findCount.textContent = 'Cancelled';
  updateFindNavigationState();
}

function normalizeFindMatch(match) {
  return {
    recordNumber: Number(match.RecordNumber ?? match.recordNumber ?? 0),
    sourceId: String(match.SourceId ?? match.sourceId ?? ''),
    startWordIndex: Number(match.StartWordIndex ?? match.startWordIndex ?? -1),
    endWordIndex: Number(match.EndWordIndex ?? match.endWordIndex ?? -1),
    nodeId: Number(match.NodeId ?? match.nodeId ?? 0),
    nodeWordIndex: Number(match.NodeWordIndex ?? match.nodeWordIndex ?? -1)
  };
}

async function showFindMatch(index, trigger = 'unknown') {
  if (!findMatches.length || findSearchPending) {
    reportFind('navigation-ignored', {trigger});
    return;
  }

  clearFindHighlights();
  currentFindMatch = (index + findMatches.length) % findMatches.length;
  const match = findMatches[currentFindMatch];
  const key = makeRecordKey(String(match.recordNumber), match.sourceId);
  const recordWords = displayWordsByRecord.get(key);
  if (!recordWords) {
    findCount.textContent = `${currentFindMatch + 1} of ${findMatches.length}`;
    chrome.webview.postMessage({
      type:'window-request',
      recordNumber:match.recordNumber,
      sourceId:match.sourceId,
      reason:'search',
      matchIndex:currentFindMatch
    });
    reportFind('window-requested', {trigger, targetIndex:currentFindMatch});
    return;
  }
  if (match.startWordIndex < 0 || match.startWordIndex >= recordWords.length) {
    reportFind('navigation-invalid-index', {
      trigger,
      targetIndex: match.startWordIndex,
      wordCount: recordWords.length
    });
    return;
  }
  const end = Math.min(match.endWordIndex, recordWords.length - 1);
  for (let wordIndex = match.startWordIndex; wordIndex <= end; ++wordIndex) {
    const word = recordWords[wordIndex];
    if (!word) continue;
    word.classList.add('find-current');
    findCurrentWords.push(word);
  }
  const target = recordWords[match.startWordIndex];
  const openedDetailsCount = openAncestors(target);
  programmaticScrollUntil = performance.now() + 500;
  target.scrollIntoView({block:'center', behavior:'auto'});
  if (followSpeech) setFollowSpeech(false, true);
  findCount.textContent = `${currentFindMatch + 1} of ${findMatches.length}`;
  reportFind('navigated', {
    trigger,
    targetIndex: currentFindMatch,
    openedDetailsCount
  });
}

function applyCSharpFindResults(data) {
  const requestId = Number(data.requestId ?? data.RequestId ?? 0);
  if (!findSearchPending || requestId !== findGeneration) return;
  findSearchPending = false;
  if (findSlowTimer) clearTimeout(findSlowTimer);
  findSlowTimer = 0;
  findMatches = (data.matches ?? data.Matches ?? []).map(normalizeFindMatch);
  if (!findMatches.length) {
    currentFindMatch = -1;
    findCount.textContent = 'No results';
    updateFindNavigationState();
    reportFind('completed', {
      matchCount: 0,
      elapsedMilliseconds: Number(
        data.elapsedMilliseconds ?? data.ElapsedMilliseconds ?? 0)
    });
    return;
  }
  currentFindMatch = 0;
  updateFindNavigationState();
  void showFindMatch(currentFindMatch, 'search-completed');
  reportFind('completed', {
    matchCount: findMatches.length,
    elapsedMilliseconds: Number(
      data.elapsedMilliseconds ?? data.ElapsedMilliseconds ?? 0)
  });
}

function getFindOrigin() {
  const selection = window.getSelection();
  if (selection && selection.rangeCount > 0 && !selection.isCollapsed) {
    const range = selection.getRangeAt(0);
    const node = range.endContainer.nodeType === Node.ELEMENT_NODE
      ? range.endContainer
      : range.endContainer.parentElement;
    const word = node?.closest?.('.word');
    if (word) {
      return {
        recordNumber:Number(word.dataset.recordNumber || 0),
        sourceId:word.dataset.sourceId || '',
        wordIndex:Number(word.dataset.recordIndex || -1)
      };
    }
  }
  const voiceWord = voiceMarkerIndex >= 0 ? words[voiceMarkerIndex] : null;
  if (voiceWord) {
    return {
      recordNumber:Number(voiceWord.dataset.recordNumber || 0),
      sourceId:voiceWord.dataset.sourceId || '',
      wordIndex:Number(voiceWord.dataset.recordIndex || -1)
    };
  }
  return {recordNumber:0, sourceId:'', wordIndex:-1};
}

function runFind() {
  const origin = getFindOrigin();
  if (followSpeech) setFollowSpeech(false, true);
  cancelFindSearch(false);
  clearFindHighlights();
  findMatches = [];
  currentFindMatch = -1;
  const query = findInput.value;
  if (!query) {
    findCount.textContent = 'No results';
    updateFindNavigationState();
    return;
  }
  const requestId = ++findGeneration;
  findSearchPending = true;
  findCount.textContent = 'Searching…';
  updateFindNavigationState();
  reportFind('csharp-search-started');
  chrome.webview.postMessage({
    type:'find-query',
    requestId,
    query,
    caseEnabled:findCaseEnabled,
    wordEnabled:findWordEnabled,
    regexEnabled:findRegexEnabled,
    voicedEnabled:findVoicedEnabled,
    originRecordNumber:origin.recordNumber,
    originSourceId:origin.sourceId,
    originWordIndex:origin.wordIndex
  });
  findSlowTimer = setTimeout(() => {
    if (!findSearchPending || requestId !== findGeneration) return;
    reportFind('slow-prompt');
    if (!confirm('The search is still running. Continue waiting?')) {
      cancelFindSearch(true);
    }
  }, 5000);
}

function focusFindInput() {
  findInput.focus({preventScroll:true});
  findInput.select();
}

function openFind() {
  findPopup.classList.add('open');
  focusFindInput();
  requestAnimationFrame(focusFindInput);
  setTimeout(focusFindInput, 0);
  setTimeout(focusFindInput, 50);
  reportFind('opened');
}

function closeFind() {
  if (findInputTimer) {
    clearTimeout(findInputTimer);
    findInputTimer = 0;
  }
  cancelFindSearch(false);
  clearFindHighlights();
  findMatches = [];
  currentFindMatch = -1;
  findPopup.classList.remove('open');
  reportFind('closed');
}

function toggleFindOption(button, setter) {
  setter();
  button.classList.toggle('enabled');
  runFind();
}

findInput.addEventListener('input', () => {
  if (findInputTimer) clearTimeout(findInputTimer);
  cancelFindSearch(false);
  findInputTimer = setTimeout(() => {
    findInputTimer = 0;
    runFind();
  }, 150);
});
findCase.addEventListener('click', () => toggleFindOption(findCase, () => findCaseEnabled = !findCaseEnabled));
findWord.addEventListener('click', () => toggleFindOption(findWord, () => findWordEnabled = !findWordEnabled));
findRegex.addEventListener('click', () => toggleFindOption(findRegex, () => findRegexEnabled = !findRegexEnabled));
findVoiced.addEventListener('click', () => toggleFindOption(findVoiced, () => findVoicedEnabled = !findVoicedEnabled));
findPrev.addEventListener('click', () => showFindMatch(currentFindMatch - 1, 'button-previous'));
findNext.addEventListener('click', () => showFindMatch(currentFindMatch + 1, 'button-next'));
findClose.addEventListener('click', closeFind);
updateFindNavigationState();

findPopup.addEventListener('keydown', event => {
  const lower = event.key.toLocaleLowerCase();
  if (event.altKey && !event.ctrlKey && !event.shiftKey &&
      (lower === 'c' || lower === 'w' || lower === 'r' || lower === 'v')) {
    event.preventDefault();
    event.stopPropagation();
    if (lower === 'c') findCase.click();
    else if (lower === 'w') findWord.click();
    else if (lower === 'r') findRegex.click();
    else findVoiced.click();
    focusFindInput();
    return;
  }
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    if (findSearchPending) cancelFindSearch(true);
    else closeFind();
    return;
  }
  if (event.key === 'Enter' && event.ctrlKey) {
    event.preventDefault();
    const match = findMatches[currentFindMatch];
    if (!match || match.nodeId <= 0 || match.nodeWordIndex < 0) {
      findCount.textContent = 'Not voiced';
      reportFind('seek-ignored');
      return;
    }
    reportFind('seek-requested', {
      trigger: event.shiftKey ? 'ctrl-shift-enter' : 'ctrl-enter'
    });
    chrome.webview.postMessage({
      type:'find-seek',
      nodeId:match.nodeId,
      nodeWordIndex:match.nodeWordIndex
    });
    return;
  }
  if (event.key === 'Enter') {
    event.preventDefault();
    void showFindMatch(
      currentFindMatch + (event.shiftKey ? -1 : 1),
      event.shiftKey ? 'shift-enter' : 'enter');
  }
});

followToggle.addEventListener('click', () => {
  setFollowSpeech(!followSpeech, true);
  if (followSpeech && currentNode >= 0 && currentIndex < 0) {
    chrome.webview.postMessage({type:'window-for-node', nodeId:currentNode});
  }
});
updateFollowToggle();

let scrollFollowTimer = 0;
let virtualShiftTimer = 0;
function firstVisibleVirtualRecord() {
  const records = transcript.querySelectorAll('.virtual-record');
  for (const record of records) {
    if (record.getBoundingClientRect().bottom >= 0) return record;
  }
  return records.length ? records[records.length - 1] : null;
}

function firstVisibleRecordAnchor() {
  const record = firstVisibleVirtualRecord();
  return record?.querySelector('.record-anchor') || null;
}

function requestVirtualShift(direction) {
  if (virtualShiftPending || windowStartIndex < 0 || windowEndIndex < 0) return;
  const visibleRecord = firstVisibleVirtualRecord();
  const anchor = firstVisibleRecordAnchor();
  if (!visibleRecord || !anchor) return;
  const visibleIndex = Number(visibleRecord.dataset.virtualIndex || -1);
  if (visibleIndex < 0) return;
  virtualShiftPending = true;
  const focalIndex = direction < 0
    ? Math.max(0, visibleIndex - 20)
    : visibleIndex + 20;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:direction < 0 ? 'scroll-up' : 'scroll-down',
    focalIndex,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorSourceId:anchor.dataset.sourceId || '',
    anchorOffset:anchor.getBoundingClientRect().top
  });
  setTimeout(() => { virtualShiftPending = false; }, 3000);
}

window.addEventListener('scroll', () => {
  const now = performance.now();
  if (followSpeech && now > programmaticScrollUntil) {
    if (scrollFollowTimer) clearTimeout(scrollFollowTimer);
    scrollFollowTimer = setTimeout(() => {
      scrollFollowTimer = 0;
      if (followSpeech && performance.now() > programmaticScrollUntil) {
        setFollowSpeech(false, true);
      }
    }, 120);
  }
  if (virtualShiftTimer) clearTimeout(virtualShiftTimer);
  virtualShiftTimer = setTimeout(() => {
    virtualShiftTimer = 0;
    const visibleRecord = firstVisibleVirtualRecord();
    const visibleIndex = Number(visibleRecord?.dataset.virtualIndex || -1);
    if (visibleIndex >= 0 && visibleIndex <= windowStartIndex + 20 &&
        windowStartIndex > 0) {
      requestVirtualShift(-1);
    } else if (visibleIndex >= windowEndIndex - 20) {
      requestVirtualShift(1);
    }
  }, 80);
}, {passive:true});


chrome.webview.addEventListener('message', event => {
  const data = event.data;
  if (!data) return;
  if (data.type === 'find-results') {
    applyCSharpFindResults(data);
    return;
  }
  if (data.type === 'window-ready') {
    const matchIndex = Number(data.matchIndex ?? -1);
    if (matchIndex >= 0 && matchIndex < findMatches.length) {
      void showFindMatch(matchIndex, 'window-ready');
    }
    return;
  }
  if (data.type === 'find-error') {
    const requestId = Number(data.requestId ?? data.RequestId ?? 0);
    if (requestId !== findGeneration) return;
    findSearchPending = false;
    if (findSlowTimer) clearTimeout(findSlowTimer);
    findSlowTimer = 0;
    const errorKind = data.errorKind ?? data.ErrorKind ?? 'search';
    const errorText = data.error ?? data.Error ?? '';
    findCount.textContent = errorKind === 'regex' ? 'Invalid regex' : 'Search failed';
    findCount.title = errorText;
    updateFindNavigationState();
    reportFind(errorKind === 'regex' ? 'invalid-regex' : 'search-failed', {error:errorText});
    return;
  }
  if (data.type === 'settings') {
    const sequence = Number(data.sequence || 0);
    if (sequence < latestSettingsSequence) return;
    latestSettingsSequence = sequence;
    applySettings(
      data.highlight,
      data.duration,
      data.follow,
      data.dark);
    return;
  }
  if (data.type !== 'playback') return;
  const sequence = Number(data.sequence || 0);
  if (sequence < latestPlaybackSequence) return;
  latestPlaybackSequence = sequence;
  setPlayback(
    data.state,
    data.fragmentText,
    data.wordIndex,
    data.wordText,
    data.nodeId,
    data.follow);
  chrome.webview.postMessage({
    type: 'playback-applied',
    sequence: data.sequence,
    nodeId: data.nodeId,
    wordIndex: data.wordIndex,
    wordText: data.wordText || '',
    fragmentText: data.fragmentText || '',
    state: data.state || '',
    rangeStart: currentIndex,
    rangeEnd: currentEndIndex,
    boundaryWordIndex: currentBoundaryWordIndex,
    boundaryTimestamp: data.boundaryTimestamp,
    javascriptTimestamp: String(performance.now())
  });
});

window.addEventListener('keydown', event => {
  if (!event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey &&
      (event.key === 'Home' || event.key === 'End') &&
      !findPopup.contains(document.activeElement)) {
    event.preventDefault();
    event.stopPropagation();
    setFollowSpeech(false, true);
    chrome.webview.postMessage({
      type:'window-edge',
      edge:event.key === 'Home' ? 'start' : 'end'
    });
    return;
  }
  if ((event.ctrlKey || event.metaKey) && !event.shiftKey &&
      event.key.toLocaleLowerCase() === 'f') {
    event.preventDefault();
    event.stopPropagation();
    openFind();
    return;
  }
  const lower = event.key.toLocaleLowerCase();
  const findOptionKey = findPopup.classList.contains('open') &&
    event.altKey && !event.ctrlKey && !event.shiftKey &&
    (lower === 'c' || lower === 'w' || lower === 'r');
  if (findOptionKey) return;
  if (event.ctrlKey || event.metaKey || event.shiftKey) return;
  if (event.key.length !== 1) return;
  if (findPopup.classList.contains('open') && !event.altKey) return;
  event.preventDefault();
  event.stopPropagation();
  chrome.webview.postMessage({type:'transport', key:event.key, alt:event.altKey});
}, true);
</script>
</body>
</html>
""";
  }
}
