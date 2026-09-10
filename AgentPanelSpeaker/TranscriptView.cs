using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Renders the selected JSONL session as Markdown-derived HTML and tracks the
/// current speech position.
/// </summary>
internal sealed class TranscriptView : UserControl
{
  private const int GwlStyle = -16;
  private const int WsVisible = 0x10000000;
  private const int StartupCanonicalEndPercent = 98;
  private const int StartupRenderPercent = 99;
  private readonly WebView2 _webView = new();
  private readonly Label _loadingLabel = new();
  private readonly Label _failureLabel = new();
  private readonly System.Windows.Forms.Timer _refreshTimer = new();
  private readonly System.Windows.Forms.Timer _settingsApplyTimer = new();
  private readonly MarkdownPipeline _pipeline;
  private long _loadingLabelLastHandle;
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
  private int _startupProgressPhase;
  private int _startupProgressPercent;
  private CancellationTokenSource? _renderCancellation;
  private CancellationTokenSource? _searchIndexCancellation;
  private TranscriptSettings _settings = TranscriptSettings.Default;
  private TranscriptPlaybackPosition? _pendingPosition;
  private TranscriptPlaybackPosition? _lastLocatedContentPosition;
  private bool _settingsApplyPending;
  private long _settingsMessageSequence;
  private long _playbackMessageSequence;
  private TranscriptSearchIndex? _searchIndex;
  private TranscriptVirtualDocument? _virtualDocument;
  private IReadOnlyList<TranscriptNodeIdentity> _identities =
    Array.Empty<TranscriptNodeIdentity>();
  private int _windowStartIndex = -1;
  private int _windowEndIndex = -1;
  private double _browserViewportHeight;
  private bool _domPresentationMode;
  private int _layoutGeneration = 1;
  private Size _lastLayoutSize;
  private CancellationTokenSource? _findCancellation;
  private PendingFindRequest? _pendingFindRequest;
  private long _latestFindWindowNavigationGeneration;
  private readonly SemaphoreSlim _windowRenderGate = new(1, 1);

  private sealed record PendingFindRequest(
    long RequestId,
    string Query,
    bool CaseEnabled,
    bool WordEnabled,
    bool RegexEnabled,
    bool VoicedEnabled,
    bool HasSelectionOrigin,
    int OriginRecordNumber,
    string OriginSourceId,
    int OriginWordIndex);

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
    _loadingLabel.Name = "TranscriptLoadingLabel";
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

    _loadingLabel.HandleCreated += (_, _) =>
    {
      _loadingLabelLastHandle = _loadingLabel.Handle.ToInt64();
      LogLoadingLabelHandleLifecycle("handle-created", _loadingLabelLastHandle);
    };
    _loadingLabel.HandleDestroyed += (_, _) =>
    {
      LogLoadingLabelHandleLifecycle(
        "handle-destroyed",
        _loadingLabelLastHandle);
      _loadingLabelLastHandle = 0;
    };
    _loadingLabel.VisibleChanged += (_, _) =>
      LogViewState("loading-visible-changed", "VisibleChanged");
    _webView.VisibleChanged += (_, _) =>
      LogViewState("webview-visible-changed", "VisibleChanged");
    _failureLabel.VisibleChanged += (_, _) =>
      LogViewState("failure-visible-changed", "VisibleChanged");
    _loadingLabel.ParentChanged += (_, _) =>
      LogViewState("loading-parent-changed", "ParentChanged");
    _webView.ParentChanged += (_, _) =>
      LogViewState("webview-parent-changed", "ParentChanged");

    _webView.CoreWebView2InitializationCompleted +=
      WebViewInitializationCompleted;
    _refreshTimer.Interval = 250;
    _refreshTimer.Tick += RefreshTimerTick;
    _settingsApplyTimer.Interval = 100;
    _settingsApplyTimer.Tick += SettingsApplyTimerTick;
    _ = InitializeAsync();
  }


  /// <summary>
  /// Captures the currently rendered WebView2 surface for the theme-transition
  /// cover. Returns null until WebView2 is initialized and visible.
  /// </summary>
  internal async Task<Bitmap?> CapturePreviewBitmapAsync()
  {
    CoreWebView2? core = _webView.CoreWebView2;
    if (!_initialized ||
        _webView.IsDisposed ||
        !_webView.Visible ||
        core is null ||
        _webView.ClientSize.Width <= 0 ||
        _webView.ClientSize.Height <= 0)
    {
      return null;
    }

    using var stream = new MemoryStream();
    await core.CapturePreviewAsync(
      CoreWebView2CapturePreviewImageFormat.Png,
      stream);
    stream.Position = 0;
    using Image captured = Image.FromStream(stream);
    return new Bitmap(captured);
  }

  /// <summary>
  /// Returns the WebView2 client rectangle in screen coordinates.
  /// </summary>
  internal Rectangle GetWebViewScreenBounds()
  {
    return _webView.RectangleToScreen(_webView.ClientRectangle);
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
  /// Raised when Find reaches the end without another voiced result.
  /// </summary>
  public event EventHandler? FindSeekEndRequested;

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
    _lastLocatedContentPosition = null;
    CancelSearchIndexBuild();
    _searchIndex = null;
    _virtualDocument = null;
    _identities = Array.Empty<TranscriptNodeIdentity>();
    _windowStartIndex = -1;
    _windowEndIndex = -1;
    _domPresentationMode = false;
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
    CancelSearchIndexBuild();
    _searchIndex = null;
    _virtualDocument = null;
    _identities = Array.Empty<TranscriptNodeIdentity>();
    _windowStartIndex = -1;
    _windowEndIndex = -1;
    _domPresentationMode = false;
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
    LogViewState("apply-settings", "begin", requestedDark: dark);
    _settings = settings.Normalize();
    _virtualDocument?.SetShowRolledBackHistory(
      _settings.ShowRolledBackHistory);
    _dark = dark;
    Color page = dark
      ? Color.FromArgb(30, 32, 35)
      : Color.FromArgb(247, 247, 245);
    Color text = dark
      ? Color.FromArgb(217, 220, 225)
      : Color.FromArgb(36, 38, 41);
    LogLoadingLabelNativeState("apply-settings", "before-back-color");
    _loadingLabel.BackColor = page;
    LogLoadingLabelNativeState("apply-settings", "after-back-color");
    _loadingLabel.ForeColor = text;
    LogLoadingLabelNativeState("apply-settings", "after-fore-color");
    _failureLabel.BackColor = page;
    _failureLabel.ForeColor = text;
    if (_initialized)
    {
      QueueSettingsApply(immediate: false);
    }
    LogViewState("apply-settings", "end", requestedDark: dark);
  }

  /// <summary>
  /// Updates the filled or paused transcript marker through a low-latency,
  /// one-way WebView message.
  /// </summary>
  public void ShowPlaybackPosition(TranscriptPlaybackPosition position)
  {
    _pendingPosition = position;
    if (position.NodeId > 0 &&
        (position.State is TranscriptPlaybackState.Speaking or
          TranscriptPlaybackState.Paused))
    {
      _lastLocatedContentPosition = position;
    }
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
      showRolledBackHistory = settings.ShowRolledBackHistory,
      layoutGeneration = _layoutGeneration,
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
    LogViewState("apply-theme", "begin", requestedDark: dark);
    ApplySettings(_settings, dark);
    LogViewState("apply-theme", "end", requestedDark: dark);
  }

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _renderGeneration++;
      CancelActiveRender();
      CancelSearchIndexBuild();
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
    LogViewState("navigation-completed", "before-visibility-update");
    _failureLabel.Visible = false;
    _webView.Visible = true;
    LogViewState("navigation-completed", "after-visibility-update");
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

    // Record the exact file generation before preparation starts. If
    // preparation fails, the timer must not retry identical bytes in a tight
    // loop; a real file change or explicit forced refresh can retry.
    _lastWriteUtc = info.LastWriteTimeUtc;
    _lastLength = info.Length;

    var cancellation = new CancellationTokenSource();
    _renderCancellation = cancellation;
    _activeRenderGeneration = generation;
    _refreshInProgress = true;
    CancelSearchIndexBuild();
    _searchIndex = null;
    if (force)
    {
      _startupProgressPhase = 0;
      _startupProgressPercent = 0;
      ShowStartupProgress(1, "Preparing canonical transcript…", 0);
    }
    IProgress<TranscriptBuildProgress>? startupRecordProgress = force
      ? new Progress<TranscriptBuildProgress>(progress =>
      {
        if (generation != _renderGeneration ||
            !string.Equals(
              path,
              _sessionPath,
              StringComparison.OrdinalIgnoreCase))
        {
          return;
        }
        ShowStartupProgress(
          1,
          "Preparing canonical transcript…",
          ScaleStartupProgress(
            progress.Completed,
            progress.Total,
            0,
            StartupCanonicalEndPercent));
      })
      : null;
    DiagnosticLog.Write("transcript.render_started", new
    {
      path,
      force,
      info.Length
    });
    var renderTimer = Stopwatch.StartNew();
    string structureProbeId = $"{generation}:{Guid.NewGuid():N}";

    try
    {
      AgentSource source = _source;
      const bool includeRolledBackTurns = true;
      CancellationToken token = cancellation.Token;
      TranscriptRenderPayload payload = await Task.Run(() =>
      {
        TranscriptPresentationDomResult presentation = null!;
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
            token,
            includeRolledBackTurns,
            startupRecordProgress),
          () => presentation = TranscriptPresentationDomFormatter.Format(
            path,
            source,
            _pipeline,
            token));
        token.ThrowIfCancellationRequested();
        string html = presentation.Html;
        TranscriptStructureSnapshot rendererStructure =
          TranscriptStructureProbe.CaptureHtml(
            structureProbeId,
            "dom-model-html",
            html);
        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
          presentation.Units);
        document.SetShowRolledBackHistory(
          _settings.ShowRolledBackHistory);
        document.SetLayoutGeneration(_layoutGeneration);
        return new TranscriptRenderPayload(
          document,
          identities,
          html,
          rendererStructure,
          presentation.Nodes);
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
      TranscriptWindow window = payload.Document.CreateWindow(focalIndex, GetVirtualViewportHeight());
      TranscriptStructureSnapshot virtualStructure =
        TranscriptStructureProbe.CaptureHtml(
          structureProbeId,
          "initial-virtual-window-html",
          window.Html);
      DiagnosticLog.Write("transcript.initial_window_selected", new
      {
        focalIndex,
        window.StartIndex,
        window.EndIndex,
        recordCount = window.Records.Count,
        totalRecordCount = payload.Document.Count,
        htmlCharacters = window.Html.Length,
        window.TopSpacerHeight,
        window.BottomSpacerHeight,
        preparationMilliseconds
      });
      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: force ? focalIndex : null,
        structureProbeId: structureProbeId,
        expectedStructure: virtualStructure);
      if (force)
      {
        ShowStartupProgress(
          3,
          "Rendering visible transcript…",
          StartupRenderPercent);
      }
      long domStartMilliseconds = renderTimer.ElapsedMilliseconds;
      if (!await ExecuteAsync(script))
      {
        ShowLoading("Unable to load transcript view. See diagnostic log.");
        return;
      }
      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;
      _domPresentationMode = false;
      TranscriptStructureSnapshot? webViewStructure =
        await CaptureWebViewStructureAsync(structureProbeId);
      if (webViewStructure is not null)
      {
        TranscriptStructureProbe.Compare(virtualStructure, webViewStructure);
      }

      TranscriptPlaybackPosition? renderAnchor = null;
      int latestIndex = -1;
      if (_settings.FollowSpeech &&
          _pendingPosition is TranscriptPlaybackPosition latestPosition &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            latestPosition,
            out latestIndex))
      {
        renderAnchor = latestPosition;
      }
      else if (_settings.FollowSpeech &&
          _lastLocatedContentPosition is TranscriptPlaybackPosition located &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            located,
            out latestIndex))
      {
        renderAnchor = located;
      }

      if (renderAnchor is not null &&
          (latestIndex < _windowStartIndex || latestIndex > _windowEndIndex))
      {
        window = payload.Document.CreateWindow(latestIndex, GetVirtualViewportHeight());
        virtualStructure = TranscriptStructureProbe.CaptureHtml(
          structureProbeId,
          "virtual-window-html-positioned",
          window.Html);
        TranscriptStructureProbe.Compare(
          payload.RendererStructure,
          virtualStructure);
        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex,
              structureProbeId: structureProbeId,
              expectedStructure: virtualStructure)))
        {
          ShowLoading("Unable to position transcript at the voice marker. " +
            "See diagnostic log.");
          return;
        }
        _windowStartIndex = window.StartIndex;
        _windowEndIndex = window.EndIndex;
        webViewStructure = await CaptureWebViewStructureAsync(structureProbeId);
        if (webViewStructure is not null)
        {
          TranscriptStructureProbe.Compare(virtualStructure, webViewStructure);
        }
        focalIndex = latestIndex;
      }

      long domMilliseconds =
        renderTimer.ElapsedMilliseconds - domStartMilliseconds;
      _lastWriteUtc = info.LastWriteTimeUtc;
      _lastLength = info.Length;
      if (force)
      {
        ShowStartupProgress(
          3,
          "Rendering visible transcript…",
          100);
      }
      HideLoading();
      BeginDeferredSearchIndexBuild(
        path,
        generation,
        payload.SearchHtml,
        payload.Identities);
      _restoredFromSettings = false;
      QueueSettingsApply(immediate: true);
      if (_lastLocatedContentPosition is TranscriptPlaybackPosition locatedPosition &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            locatedPosition,
            out _))
      {
        PostPlaybackPosition(locatedPosition);
      }
      if (_pendingPosition is TranscriptPlaybackPosition pending &&
          pending != _lastLocatedContentPosition)
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

  /// <summary>
  /// Starts full-file search indexing only after the initial visible window is
  /// installed. Search readiness is deliberately independent of first paint.
  /// </summary>
  private void BeginDeferredSearchIndexBuild(
    string path,
    int generation,
    string html,
    IReadOnlyList<TranscriptNodeIdentity> identities)
  {
    var cancellation = new CancellationTokenSource();
    _searchIndexCancellation = cancellation;
    _ = BuildDeferredSearchIndexAsync(
      path,
      generation,
      html,
      identities,
      cancellation);
  }

  /// <summary>
  /// Builds the immutable full-session search corpus away from the UI thread,
  /// then attaches stable word IDs to whichever virtual window is current.
  /// </summary>
  private async Task BuildDeferredSearchIndexAsync(
    string path,
    int generation,
    string html,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    CancellationTokenSource cancellation)
  {
    var timer = Stopwatch.StartNew();
    try
    {
      TranscriptSearchIndex index = await Task.Run(
        () => TranscriptSearchIndex.Build(
          html,
          identities,
          cancellation.Token),
        cancellation.Token);
      cancellation.Token.ThrowIfCancellationRequested();
      if (!ReferenceEquals(_searchIndexCancellation, cancellation) ||
          generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      _searchIndex = index;
      await InstallCurrentWindowSearchMapsAsync(
        index,
        generation,
        path,
        cancellation.Token);
      cancellation.Token.ThrowIfCancellationRequested();
      if (!ReferenceEquals(_searchIndexCancellation, cancellation) ||
          generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      StartPendingFindRequest();
      DiagnosticLog.Write("transcript.search_index_completed", new
      {
        path,
        generation,
        elapsedMilliseconds = timer.ElapsedMilliseconds,
        firstRenderAlreadyVisible = !_loadingLabel.Visible
      });
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      DiagnosticLog.Write("transcript.search_index_cancelled", new
      {
        path,
        generation,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    catch (Exception exception)
    {
      DiagnosticLog.Write("transcript.search_index_failed", new
      {
        path,
        generation,
        exception = exception.ToString()
      });
      PendingFindRequest? request = _pendingFindRequest;
      if (request is not null)
      {
        _pendingFindRequest = null;
        PostMessage(new
        {
          type = "find-error",
          requestId = request.RequestId,
          errorKind = "search",
          error = exception.Message
        });
      }
    }
    finally
    {
      if (ReferenceEquals(_searchIndexCancellation, cancellation))
      {
        _searchIndexCancellation = null;
      }
      cancellation.Dispose();
    }
  }

  /// <summary>
  /// Installs search-owned stable word IDs into the current browser window
  /// without replacing its canonical Core HTML or moving the viewport.
  /// </summary>
  private async Task InstallCurrentWindowSearchMapsAsync(
    TranscriptSearchIndex index,
    int generation,
    string path,
    CancellationToken cancellationToken)
  {
    await _windowRenderGate.WaitAsync(cancellationToken);
    try
    {
      if (generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase) ||
          _virtualDocument is not TranscriptVirtualDocument document ||
          _windowStartIndex < 0 ||
          _windowEndIndex < _windowStartIndex)
      {
        return;
      }

      int start = Math.Clamp(_windowStartIndex, 0, document.Count - 1);
      int end = Math.Clamp(_windowEndIndex, start, document.Count - 1);
      TranscriptVirtualRecord[] records = document.Records
        .Skip(start)
        .Take(end - start + 1)
        .ToArray();
      IReadOnlyList<TranscriptRecordWordMap> wordMaps = index.GetWordMaps(records);
      await ExecuteAsync(
        "installSearchWordMaps(" + JsonSerializer.Serialize(wordMaps) + ");");
    }
    finally
    {
      _windowRenderGate.Release();
    }
  }

  private void CancelSearchIndexBuild()
  {
    CancellationTokenSource? cancellation = _searchIndexCancellation;
    _searchIndexCancellation = null;
    cancellation?.Cancel();
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

  private void ShowStartupProgress(
    int phase,
    string description,
    int percentage)
  {
    Debug.Assert(phase >= 1 && phase <= 3);
    Debug.Assert(!string.IsNullOrWhiteSpace(description));
    if (phase < _startupProgressPhase)
    {
      return;
    }

    _startupProgressPhase = phase;
    _startupProgressPercent = Math.Max(
      _startupProgressPercent,
      Math.Clamp(percentage, 0, 100));
    string name = string.IsNullOrWhiteSpace(_sessionDisplayName)
      ? Path.GetFileName(_sessionPath) ?? string.Empty
      : _sessionDisplayName;
    string text = description + Environment.NewLine +
      $"{_startupProgressPercent}%";
    if (!string.IsNullOrWhiteSpace(name))
    {
      text += Environment.NewLine + name;
    }
    ShowLoading(text);
  }

  private static int ScaleStartupProgress(
    int completed,
    int total,
    int startPercentage,
    int endPercentage)
  {
    if (total <= 0)
    {
      return startPercentage;
    }
    int boundedCompleted = Math.Clamp(completed, 0, total);
    int span = Math.Max(0, endPercentage - startPercentage);
    return startPercentage + (int)((long)span * boundedCompleted / total);
  }

  private void ShowLoading(string text)
  {
    LogViewState("show-loading", "before", requestedLoadingText: text);
    _loadingLabel.Text = text;
    _loadingLabel.Visible = true;
    _loadingLabel.BringToFront();
    LogViewState("show-loading", "after", requestedLoadingText: text);
  }

  private void HideLoading()
  {
    LogViewState("hide-loading", "before");
    _loadingLabel.Visible = false;
    LogViewState("hide-loading", "after");
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
      if (type == "lazy-word-materialized")
      {
        DiagnosticLog.Write("transcript.lazy_word_materialized", root.Clone());
        return;
      }
      if (type == "stable-word-map-failure")
      {
        DiagnosticLog.Write("transcript.stable_word_map_failure", new
        {
          key = ReadOptionalString(root, "key"),
          renderedWordCount = ReadOptionalInt32(root, "renderedWordCount"),
          mappedWordCount = ReadOptionalInt32(root, "mappedWordCount")
        });
        return;
      }
      if (type == "mapping-failure" || type == "playback-unmatched")
      {
        DiagnosticLog.Write($"transcript.{type}", root.Clone());
        return;
      }
      if (type is "mapping-node-summary" or
          "mapping-install-summary" or
          "fragment-range-miss")
      {
        DiagnosticLog.Write(
          $"transcript.{type.Replace('-', '_')}",
          root.Clone());
        return;
      }
      if (type is "structure-js-equivalent" or "structure-js-divergence")
      {
        DiagnosticLog.Write(
          type == "structure-js-equivalent"
            ? "transcript.structure_js_equivalent"
            : "transcript.structure_js_divergence",
          root.Clone());
        return;
      }
      if (type == "find-query")
      {
        HandleFindQuery(root);
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
          corpusWords = ReadOptionalInt32(root, "corpusWords"),
          firstWordId = ReadOptionalString(root, "firstWordId"),
          seekWordId = ReadOptionalString(root, "seekWordId"),
          expectedWordCount = ReadOptionalInt32(root, "expectedWordCount"),
          resolvedWordCount = ReadOptionalInt32(root, "resolvedWordCount"),
          navigationGeneration = ReadOptionalInt64(root, "navigationGeneration")
        });
        return;
      }
      if (type == "window-measured")
      {
        UpdateBrowserViewportHeight(ReadOptionalDouble(root, "viewportHeight"));
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
          int layoutGeneration = ReadOptionalInt32(
            root,
            "layoutGeneration") ?? _layoutGeneration;
          virtualDocument.UpdateMeasuredHeights(values, layoutGeneration);
        }
        return;
      }
      if (type == "window-shift")
      {
        UpdateBrowserViewportHeight(ReadOptionalDouble(root, "viewportHeight"));
        int? focalIndex = ReadOptionalInt32(root, "focalIndex");
        if (focalIndex is int validFocalIndex)
        {
          _ = RenderWindowForIndexCoreAsync(
            validFocalIndex,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "anchorRecordNumber"),
            ReadOptionalString(root, "anchorSourceId"),
            ReadOptionalDouble(root, "anchorOffset"),
            ReadOptionalInt32(root, "visibleStartIndex"),
            ReadOptionalInt32(root, "visibleEndIndex"),
            ReadOptionalInt32(root, "sourceStartIndex"),
            ReadOptionalInt32(root, "sourceEndIndex"));
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
          long? navigationGeneration = ReadOptionalInt64(
            root,
            "navigationGeneration");
          if (navigationGeneration is long generation)
          {
            _latestFindWindowNavigationGeneration = Math.Max(
              _latestFindWindowNavigationGeneration,
              generation);
          }
          _ = RenderWindowForRecordAsync(
            validRecordNumber,
            sourceId,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "matchIndex"),
            navigationGeneration);
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
            validNodeId > 0 &&
            nodeWordIndex is int validNodeWordIndex &&
            validNodeWordIndex >= 0)
        {
          FindSeekRequested?.Invoke(
            this,
            new FindSeekRequestedEventArgs(validNodeId, validNodeWordIndex));
          return;
        }

        long? wordId = ReadOptionalInt64(root, "wordId");
        TranscriptSearchIndex? searchIndex = _searchIndex;
        if (wordId is long validWordId &&
            validWordId > 0 &&
            searchIndex is not null &&
            searchIndex.TryResolveSpeechWord(
              validWordId,
              out long resolvedNodeId,
              out int resolvedNodeWordIndex))
        {
          FindSeekRequested?.Invoke(
            this,
            new FindSeekRequestedEventArgs(
              resolvedNodeId,
              resolvedNodeWordIndex));
        }
        return;
      }
      if (type == "find-seek-end")
      {
        FindSeekEndRequested?.Invoke(this, EventArgs.Empty);
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

  private void HandleFindQuery(JsonElement root)
  {
    long? requestId = ReadOptionalInt64(root, "requestId");
    string query = ReadOptionalString(root, "query");
    if (requestId is not long validRequestId || query.Length == 0)
    {
      return;
    }

    var request = new PendingFindRequest(
      validRequestId,
      query,
      ReadOptionalBoolean(root, "caseEnabled") == true,
      ReadOptionalBoolean(root, "wordEnabled") == true,
      ReadOptionalBoolean(root, "regexEnabled") == true,
      ReadOptionalBoolean(root, "voicedEnabled") != false,
      string.Equals(
        ReadOptionalString(root, "originKind"),
        "selection",
        StringComparison.Ordinal),
      ReadOptionalInt32(root, "originRecordNumber") ?? 0,
      ReadOptionalString(root, "originSourceId"),
      ReadOptionalInt32(root, "originWordIndex") ?? -1);

    CancelFindSearch();
    _pendingFindRequest = request;
    if (_searchIndex is null)
    {
      PostMessage(new
      {
        type = "find-waiting",
        requestId = request.RequestId
      });
      DiagnosticLog.Write("transcript.find_waiting_for_index", new
      {
        requestId = request.RequestId,
        request.Query
      });
      return;
    }

    StartPendingFindRequest();
  }

  private void StartPendingFindRequest()
  {
    PendingFindRequest? request = _pendingFindRequest;
    TranscriptSearchIndex? index = _searchIndex;
    if (request is null || index is null)
    {
      return;
    }

    _pendingFindRequest = null;
    _ = ExecuteFindQueryAsync(index, request);
  }

  private async Task ExecuteFindQueryAsync(
    TranscriptSearchIndex index,
    PendingFindRequest pending)
  {
    CancelFindSearch();
    var cancellation = new CancellationTokenSource();
    _findCancellation = cancellation;

    int originRecordNumber = pending.OriginRecordNumber;
    string originSourceId = pending.OriginSourceId;
    int originWordIndex = pending.OriginWordIndex;
    if (!pending.HasSelectionOrigin &&
        _pendingPosition is TranscriptPlaybackPosition voicePosition &&
        index.TryResolveVoiceOrigin(
          voicePosition.NodeId,
          voicePosition.WordIndex,
          out int voiceRecordNumber,
          out string voiceSourceId,
          out int voiceRecordWordIndex))
    {
      originRecordNumber = voiceRecordNumber;
      originSourceId = voiceSourceId;
      originWordIndex = voiceRecordWordIndex;
    }

    var request = new TranscriptSearchRequest(
      pending.RequestId,
      pending.Query,
      pending.CaseEnabled,
      pending.WordEnabled,
      pending.RegexEnabled,
      pending.VoicedEnabled);
    PostMessage(new
    {
      type = "find-started",
      requestId = pending.RequestId
    });
    var timer = Stopwatch.StartNew();
    try
    {
      IReadOnlyList<TranscriptSearchMatch> matches = await index.SearchAsync(
        request,
        cancellation.Token);
      TranscriptVirtualDocument? visibleDocument = _virtualDocument;
      if (visibleDocument is not null)
      {
        matches = matches.Where(match => visibleDocument.IsVisible(
          match.RecordNumber,
          match.SourceId)).ToArray();
      }
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
        requestId = pending.RequestId,
        matches,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
      DiagnosticLog.Write("transcript.find_csharp_completed", new
      {
        requestId = pending.RequestId,
        query = pending.Query,
        request.Regex,
        request.VoicedOnly,
        originKind = pending.HasSelectionOrigin ? "selection" : "voice",
        originRecordNumber,
        originSourceId,
        originWordIndex,
        matchCount = matches.Count,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      DiagnosticLog.Write("transcript.find_csharp_cancelled", new
      {
        requestId = pending.RequestId,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    catch (ArgumentException exception) when (request.Regex)
    {
      PostMessage(new
      {
        type = "find-error",
        requestId = pending.RequestId,
        errorKind = "regex",
        error = exception.Message
      });
    }
    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
    {
      PostMessage(new
      {
        type = "find-error",
        requestId = pending.RequestId,
        errorKind = "search",
        error = exception.Message
      });
      DiagnosticLog.Write("transcript.find_csharp_failed", new
      {
        requestId = pending.RequestId,
        query = pending.Query,
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
    _pendingFindRequest = null;
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
      '=' => Keys.Oemplus,
      _ => Keys.None
    };
  }

  private async Task<TranscriptStructureSnapshot?> CaptureWebViewStructureAsync(
    string probeId)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (!_initialized || _webView.IsDisposed || core is null)
      {
        return null;
      }
      string result = await core.ExecuteScriptAsync(
        TranscriptStructureProbe.BuildWebViewProbeScript());
      return TranscriptStructureProbe.CaptureWebViewResult(probeId, result);
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException or JsonException)
    {
      DiagnosticLog.Write("transcript.structure_probe_failed", new
      {
        probeId,
        stage = "webview-dom",
        exception = exception.ToString()
      });
      return null;
    }
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
    LogViewState("initialization-failure", "before");
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
    LogViewState("initialization-failure", "after");
  }

  /// <inheritdoc />
  protected override void OnLayout(LayoutEventArgs eventArgs)
  {
    base.OnLayout(eventArgs);
    LogViewState("layout", "after-base", affectedProperty: eventArgs.AffectedProperty);
  }

  /// <inheritdoc />
  protected override void OnSizeChanged(EventArgs eventArgs)
  {
    base.OnSizeChanged(eventArgs);
    Size currentSize = ClientSize;
    if (currentSize != _lastLayoutSize)
    {
      _lastLayoutSize = currentSize;
      _browserViewportHeight = 0.0;
      ++_layoutGeneration;
      _virtualDocument?.SetLayoutGeneration(_layoutGeneration);
      if (_initialized)
      {
        QueueSettingsApply(immediate: false);
      }
    }
    LogViewState("size-changed", "after-base");
  }

  private void LogViewState(
    string operation,
    string phase,
    bool? requestedDark = null,
    string? requestedLoadingText = null,
    string? affectedProperty = null)
  {
    if (IsDisposed)
    {
      return;
    }

    int ChildIndex(Control control)
    {
      return control.Parent == this && Controls.Contains(control)
        ? Controls.GetChildIndex(control)
        : -1;
    }

    static object BoundsOf(Control control) => new
    {
      control.Left,
      control.Top,
      control.Width,
      control.Height
    };

    DiagnosticLog.Write("transcript.view_state", new
    {
      operation,
      phase,
      requestedDark,
      requestedLoadingText,
      affectedProperty,
      initialized = _initialized,
      dark = _dark,
      refreshInProgress = _refreshInProgress,
      refreshPending = _refreshPending,
      renderGeneration = _renderGeneration,
      activeRenderGeneration = _activeRenderGeneration,
      sessionPath = _sessionPath,
      viewVisible = Visible,
      viewBounds = BoundsOf(this),
      loading = new
      {
        visible = _loadingLabel.Visible,
        text = _loadingLabel.Text,
        bounds = BoundsOf(_loadingLabel),
        childIndex = ChildIndex(_loadingLabel),
        parent = _loadingLabel.Parent?.GetType().FullName,
        handleCreated = _loadingLabel.IsHandleCreated,
        handle = _loadingLabel.IsHandleCreated ? _loadingLabel.Handle.ToInt64() : 0L,
        native = GetNativeWindowState(_loadingLabel)
      },
      webView = new
      {
        visible = _webView.Visible,
        bounds = BoundsOf(_webView),
        childIndex = ChildIndex(_webView),
        parent = _webView.Parent?.GetType().FullName,
        handleCreated = _webView.IsHandleCreated,
        handle = _webView.IsHandleCreated ? _webView.Handle.ToInt64() : 0L,
        coreReady = _webView.CoreWebView2 is not null
      },
      failure = new
      {
        visible = _failureLabel.Visible,
        bounds = BoundsOf(_failureLabel),
        childIndex = ChildIndex(_failureLabel)
      }
    });
  }

  private void LogLoadingLabelHandleLifecycle(string phase, long knownHandle)
  {
    DiagnosticLog.Write("transcript.loading_label_handle", new
    {
      phase,
      knownHandle,
      managedVisible = _loadingLabel.Visible,
      handleCreated = _loadingLabel.IsHandleCreated,
      currentHandle = _loadingLabel.IsHandleCreated
        ? _loadingLabel.Handle.ToInt64()
        : 0L,
      native = GetNativeWindowState(knownHandle),
      stack = Environment.StackTrace
    });
  }

  private void LogLoadingLabelNativeState(string operation, string phase)
  {
    DiagnosticLog.Write("transcript.loading_label_native", new
    {
      operation,
      phase,
      managedVisible = _loadingLabel.Visible,
      handleCreated = _loadingLabel.IsHandleCreated,
      handle = _loadingLabel.IsHandleCreated
        ? _loadingLabel.Handle.ToInt64()
        : 0L,
      native = GetNativeWindowState(_loadingLabel)
    });
  }

  private static object GetNativeWindowState(Control control)
  {
    return control.IsHandleCreated
      ? GetNativeWindowState(control.Handle.ToInt64())
      : new
      {
        handle = 0L,
        isWindow = false,
        isWindowVisible = false,
        style = 0U,
        wsVisible = false
      };
  }

  private static object GetNativeWindowState(long handleValue)
  {
    if (handleValue == 0)
    {
      return new
      {
        handle = 0L,
        isWindow = false,
        isWindowVisible = false,
        style = 0U,
        wsVisible = false
      };
    }

    IntPtr handle = new(handleValue);
    bool isWindow = IsWindow(handle);
    int styleValue = isWindow ? GetWindowLong(handle, GwlStyle) : 0;
    uint style = unchecked((uint)styleValue);
    return new
    {
      handle = handleValue,
      isWindow,
      isWindowVisible = isWindow && IsWindowVisible(handle),
      style,
      wsVisible = (style & unchecked((uint)WsVisible)) != 0
    };
  }

  #pragma warning disable SYSLIB1054
  [DllImport("user32.dll", EntryPoint = "IsWindow")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindow(IntPtr handle);

  [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindowVisible(IntPtr handle);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
  private static extern int GetWindowLong(IntPtr handle, int index);
  #pragma warning restore SYSLIB1054

  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    string SearchHtml,
    TranscriptStructureSnapshot RendererStructure,
    IReadOnlyList<TranscriptDomNode> DomNodes);


  private double GetVirtualViewportHeight()
  {
    if (double.IsFinite(_browserViewportHeight) && _browserViewportHeight > 0)
    {
      return _browserViewportHeight;
    }
    return _webView.ClientSize.Height > 0
      ? _webView.ClientSize.Height
      : TranscriptVirtualDocument.DefaultViewportHeight;
  }

  private void UpdateBrowserViewportHeight(double? viewportHeight)
  {
    if (viewportHeight is double value &&
        double.IsFinite(value) &&
        value > 0)
    {
      _browserViewportHeight = value;
    }
  }

  private int ResolveInitialWindowIndex(
    TranscriptVirtualDocument document,
    IReadOnlyList<TranscriptNodeIdentity> identities)
  {
    return _settings.FollowSpeech &&
      _pendingPosition is TranscriptPlaybackPosition position &&
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

  private string BuildReplaceDomScript(
    TranscriptWindow window,
    IReadOnlyList<TranscriptDomNode> domNodes,
    bool preserve,
    string? structureProbeId = null,
    TranscriptStructureSnapshot? expectedStructure = null)
  {
    var keys = window.Records
      .SelectMany(record => record.Identities.Count != 0
        ? record.Identities
        : new[]
        {
          new TranscriptVirtualIdentity(record.RecordNumber, record.SourceId)
        })
      .Select(identity => identity.SourceId + "\0" + identity.RecordNumber)
      .ToHashSet(StringComparer.Ordinal);
    IReadOnlyList<TranscriptNodeIdentity> identities = _identities
      .Where(identity => keys.Contains(
        identity.SourceId + "\0" + identity.RecordNumber))
      .ToArray();
    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex?.GetWordMaps(
      window.Records) ?? Array.Empty<TranscriptRecordWordMap>();
    return "replaceTranscriptDom(" +
      JsonSerializer.Serialize(domNodes) + "," +
      JsonSerializer.Serialize(preserve) + "," +
      JsonSerializer.Serialize(identities) + "," +
      JsonSerializer.Serialize(wordMaps) + "," +
      JsonSerializer.Serialize(expectedStructure?.Entries ??
        Array.Empty<TranscriptStructureEntry>()) + "," +
      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + ");";
  }

  private string BuildReplaceWindowScript(
    TranscriptWindow window,
    bool preserve,
    int? anchorRecordNumber = null,
    string? anchorSourceId = null,
    double? anchorOffset = null,
    int? focusVirtualIndex = null,
    string? focusEdge = null,
    string? structureProbeId = null,
    TranscriptStructureSnapshot? expectedStructure = null)
  {
    var keys = window.Records
      .SelectMany(record => record.Identities.Count != 0
        ? record.Identities
        : new[]
        {
          new TranscriptVirtualIdentity(record.RecordNumber, record.SourceId)
        })
      .Select(identity => identity.SourceId + "\0" + identity.RecordNumber)
      .ToHashSet(StringComparer.Ordinal);
    IReadOnlyList<TranscriptNodeIdentity> identities = _identities
      .Where(identity => keys.Contains(identity.SourceId + "\0" + identity.RecordNumber))
      .ToArray();
    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex?.GetWordMaps(
      window.Records) ?? Array.Empty<TranscriptRecordWordMap>();
    return "replaceTranscriptWindow(" +
      JsonSerializer.Serialize(window.Html) + "," +
      JsonSerializer.Serialize(preserve) + "," +
      JsonSerializer.Serialize(identities) + "," +
      JsonSerializer.Serialize(wordMaps) + "," +
      window.StartIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.EndIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.TopSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.BottomSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      JsonSerializer.Serialize(anchorRecordNumber) + "," +
      JsonSerializer.Serialize(anchorSourceId) + "," +
      JsonSerializer.Serialize(anchorOffset) + "," +
      JsonSerializer.Serialize(focusVirtualIndex) + "," +
      JsonSerializer.Serialize(focusEdge) + "," +
      JsonSerializer.Serialize(expectedStructure?.Entries ??
        Array.Empty<TranscriptStructureEntry>()) + "," +
      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + ");";
  }

  private async Task RenderWindowForRecordAsync(
    int recordNumber,
    string sourceId,
    string reason,
    int? matchIndex,
    long? navigationGeneration = null)
  {
    TranscriptVirtualDocument? document = _virtualDocument;
    if (document is null ||
        !document.TryGetIndex(recordNumber, sourceId, out int focalIndex))
    {
      return;
    }
    if (navigationGeneration is long requestedGeneration &&
        requestedGeneration != _latestFindWindowNavigationGeneration)
    {
      return;
    }

    await _windowRenderGate.WaitAsync();
    try
    {
      if (navigationGeneration is long currentGeneration &&
          currentGeneration != _latestFindWindowNavigationGeneration)
      {
        return;
      }
      if (focalIndex >= _windowStartIndex && focalIndex <= _windowEndIndex)
      {
        if (matchIndex is int existingMatch)
        {
          PostMessage(new
          {
            type = "window-ready",
            matchIndex = existingMatch,
            navigationGeneration
          });
        }
        return;
      }
      TranscriptWindow window = document.CreateWindow(focalIndex, GetVirtualViewportHeight());
      var timer = Stopwatch.StartNew();
      if (!await ExecuteAsync(BuildReplaceWindowScript(
            window,
            preserve: false,
            focusVirtualIndex: string.Equals(
              reason,
              "search",
              StringComparison.OrdinalIgnoreCase)
                ? null
                : focalIndex)))
      {
        return;
      }
      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;
      if (!string.Equals(reason, "search", StringComparison.OrdinalIgnoreCase) &&
          _pendingPosition is TranscriptPlaybackPosition pending)
      {
        PostPlaybackPosition(pending);
      }
      if (matchIndex is int currentMatch)
      {
        PostMessage(new
        {
          type = "window-ready",
          matchIndex = currentMatch,
          navigationGeneration
        });
      }
      DiagnosticLog.Write("transcript.window_rendered", new
      {
        reason,
        recordNumber,
        sourceId,
        navigationGeneration,
        window.StartIndex,
        window.EndIndex,
        recordCount = window.Records.Count,
        htmlCharacters = window.Html.Length,
        elapsedMilliseconds = timer.ElapsedMilliseconds
      });
    }
    finally
    {
      _windowRenderGate.Release();
    }
  }

  private async Task RenderWindowForEdgeAsync(string edge)
  {
    if (_domPresentationMode)
    {
      await ExecuteAsync(edge == "start"
        ? "window.scrollTo(0, 0);"
        : "window.scrollTo(0, document.documentElement.scrollHeight);");
      return;
    }
    TranscriptVirtualDocument? document = _virtualDocument;
    if (document is null || document.Count == 0)
    {
      return;
    }
    int focalIndex = edge == "start" ? 0 : document.Count - 1;
    TranscriptWindow window = document.CreateWindow(focalIndex, GetVirtualViewportHeight());
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
    // Keyboard Home/End is explicit manual navigation.  The WebView has already
    // disabled follow mode, so replaying the pending speech marker here would
    // countermand the user's chosen window and can restart window ping-pong.
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

  private Task RenderWindowForIndexAsync(
    int focalIndex,
    string reason,
    int? anchorRecordNumber,
    string anchorSourceId,
    double? anchorOffset)
  {
    return RenderWindowForIndexCoreAsync(
      focalIndex,
      reason,
      anchorRecordNumber,
      anchorSourceId,
      anchorOffset,
      protectedStartIndex: null,
      protectedEndIndex: null,
      sourceStartIndex: null,
      sourceEndIndex: null);
  }

  private async Task RenderWindowForIndexCoreAsync(
    int focalIndex,
    string reason,
    int? anchorRecordNumber,
    string anchorSourceId,
    double? anchorOffset,
    int? protectedStartIndex,
    int? protectedEndIndex,
    int? sourceStartIndex,
    int? sourceEndIndex)
  {
    await _windowRenderGate.WaitAsync();
    try
    {
      if (_domPresentationMode)
      {
        return;
      }
      TranscriptVirtualDocument? document = _virtualDocument;
      if (document is null)
      {
        return;
      }
      if (sourceStartIndex is int requestedStart &&
          sourceEndIndex is int requestedEnd &&
          (requestedStart != _windowStartIndex ||
           requestedEnd != _windowEndIndex))
      {
        DiagnosticLog.Write("transcript.window_shift_stale", new
        {
          reason,
          focalIndex,
          requestedStart,
          requestedEnd,
          currentStart = _windowStartIndex,
          currentEnd = _windowEndIndex
        });
        return;
      }

      int direction = reason.EndsWith("-up", StringComparison.OrdinalIgnoreCase)
        ? -1
        : reason.EndsWith("-down", StringComparison.OrdinalIgnoreCase)
          ? 1
          : 0;
      TranscriptWindow window = direction == 0
        ? document.CreateWindow(focalIndex, GetVirtualViewportHeight())
        : document.CreateShiftedWindow(
            focalIndex,
            _windowStartIndex,
            _windowEndIndex,
            direction,
            GetVirtualViewportHeight(),
            protectedStartIndex,
            protectedEndIndex);
      if (window.StartIndex == _windowStartIndex &&
          window.EndIndex == _windowEndIndex)
      {
        return;
      }

      var timer = Stopwatch.StartNew();
      if (!await ExecuteAsync(BuildReplaceWindowScript(
            window,
            preserve: false,
            anchorRecordNumber: anchorRecordNumber,
            anchorSourceId: anchorSourceId,
            anchorOffset: anchorOffset,
            focusVirtualIndex: focalIndex)))
      {
        return;
      }
      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;
      bool manualScroll =
        string.Equals(reason, "scroll-up", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "scroll-down", StringComparison.OrdinalIgnoreCase);
      if (!manualScroll &&
          _pendingPosition is TranscriptPlaybackPosition pending)
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
    finally
    {
      _windowRenderGate.Release();
    }
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
  padding: 3px;
  border: 1px solid rgba(128,128,128,.45);
  border-radius: 6px;
  background: rgba(35,35,35,.58);
  backdrop-filter: blur(4px);
  opacity: .5;
  transition: opacity 120ms ease-in-out;
  z-index: 40;
}
#view-voice-overlay:hover,
#view-voice-overlay:focus-within { opacity: 1; }
.view-voice-button {
  min-width: 76px;
  height: 32px;
  border: 0;
  border-radius: 4px;
  color: #fff;
  background: transparent;
  font-size: 17px;
  line-height: 30px;
  text-align: center;
}
.virtual-spacer { width: 1px; pointer-events: none; }
.virtual-record { display: flow-root; }
#follow-toggle { cursor: pointer; font-weight: 700; padding: 0 8px; }
#follow-toggle:hover { background: rgba(255,255,255,.18); }
#follow-toggle:active { background: rgba(255,255,255,.28); }
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
li.speech-list-item-active {
  background: var(--highlight);
  border-radius: 3px;
}
li.speech-list-item-paused {
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
#find-voiced svg, #find-seek-voiced svg { width: 17px; height: 17px; fill: currentColor; }
#find-count { min-width: 62px; padding: 0 4px; color: var(--muted); text-align: center; white-space: nowrap; }
.word.find-match { box-shadow: inset 0 -2px 0 #c08a00; }
.word.find-current { background: #d99b22; color: #111; }
#live-end-marker {
  display: none;
  box-sizing: border-box;
  width: fit-content;
  max-width: min(1050px, calc(100vw - 48px));
  min-height: 1.2em;
  margin: .5em auto 1em;
  padding: .25em .65em;
  border: 2px solid var(--highlight);
  color: var(--muted);
  text-align: center;
  white-space: nowrap;
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
  <button type="button" id="find-seek-voiced" class="find-button" title="Move voice cursor to this or the next voiced match (Ctrl+Shift+Enter)" aria-label="Move voice cursor to this or the next voiced match">
    <svg viewBox="0 0 28 24" aria-hidden="true">
      <path d="M1 11h9.2L7.1 7.9 8.5 6.5 14 12l-5.5 5.5-1.4-1.4 3.1-3.1H1v-2z"/>
      <path d="M21 5.5c-1.7 0-2.6 1.2-3.5 1.9-1 .7-2.1 1.1-3.5 1.6 1.3 3.3 3.9 5 7 5s5.7-1.7 7-5c-1.4-.5-2.5-.9-3.5-1.6-.9-.7-1.8-1.9-3.5-1.9zm-4.7 4c1.5.2 3.1.3 4.7.3s3.2-.1 4.7-.3c-1.2 1.6-2.8 2.4-4.7 2.4s-3.5-.8-4.7-2.4z"/>
    </svg>
  </button>
  <span id="find-count">No results</span>
  <button type="button" id="find-prev" class="find-button" title="Previous match (Shift+Enter)">↑</button>
  <button type="button" id="find-next" class="find-button" title="Next match (Enter)">↓</button>
  <button type="button" id="find-close" class="find-button" title="Close (Escape)">×</button>
</div>
<main id="transcript"></main>
<div id="view-voice-overlay" aria-label="Transcript follow control">
  <button type="button" id="follow-toggle" class="view-voice-button" title="Toggle follow speech (=)">👁️ = 👄</button>
</div>
<div id="live-end-marker" aria-label="End of transcript status" aria-live="polite"></div>
<script nonce="agent-panel-speaker">
const transcript = document.getElementById('transcript');
const liveEndMarker = document.getElementById('live-end-marker');
const findPopup = document.getElementById('find-popup');
const findInput = document.getElementById('find-input');
const findCase = document.getElementById('find-case');
const findWord = document.getElementById('find-word');
const findRegex = document.getElementById('find-regex');
const findVoiced = document.getElementById('find-voiced');
const findSeekVoiced = document.getElementById('find-seek-voiced');
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
let currentSpeechListItem = null;
let fadeMs = 250;
let followSpeech = true;
let showRolledBackHistory = false;
let layoutGeneration = 1;
let programmaticScrollUntil = 0;
let windowStartIndex = -1;
let windowEndIndex = -1;
let virtualShiftPending = false;
let latestPlaybackSequence = 0;
let latestSettingsSequence = 0;
const fadingAnimations = new WeakMap();
let knownNodeIds = new Set();
let displayWordsByRecord = new Map();
let displayWordsById = new Map();
let lexicalWordsByRecord = new Map();
let availableWordMapsByRecord = new Map();
let segmentRangesByNode = new Map();
let mappingGeneration = 0;
const reportedMappingFailures = new Set();
const reportedPlaybackFailures = new Set();
let findMatches = [];
let currentFindMatch = -1;
let findGeneration = 0;
let findNavigationGeneration = 0;
let findSearchPending = false;
let findSlowTimer = 0;
let findCaseEnabled = false;
let findWordEnabled = false;
let findRegexEnabled = false;
let findVoicedEnabled = true;
let findCurrentWords = [];
let findInputTimer = 0;

function tokenize(text) {
  return (text || '').toLocaleLowerCase().match(
    /[\p{L}\p{M}\p{N}_]+(?:['’\-][\p{L}\p{M}\p{N}_]+)*/gu) || [];
}

function tokenizeDisplay(text) {
  return (text || '').toLocaleLowerCase().match(
    /(?<![\p{L}\p{M}\p{N}_.])\d*\.\d+(?!\.\d)(?=[fFlL]|\b)|\.+|[\p{L}\p{M}\p{N}_]+(?:['’\-][\p{L}\p{M}\p{N}_]+)*|[^\s]/gu) || [];
}

function isLexical(text) {
  return /^[\p{L}\p{M}\p{N}_]+(?:['’\-][\p{L}\p{M}\p{N}_]+)*$/u.test(text);
}

function nodeRecordKeys(nodeMap) {
  const result = new Set();
  for (const item of nodeMap || []) {
    const recordNumber = String(
      item.RecordNumber ?? item.recordNumber ?? '');
    const sourceId = String(item.SourceId ?? item.sourceId ?? '');
    result.add(makeRecordKey(recordNumber, sourceId));
  }
  return result;
}

function setAvailableWordMaps(wordMap) {
  availableWordMapsByRecord = new Map();
  for (const record of wordMap || []) {
    const recordNumber = String(
      record.RecordNumber ?? record.recordNumber ?? '');
    const sourceId = String(record.SourceId ?? record.sourceId ?? '');
    availableWordMapsByRecord.set(
      makeRecordKey(recordNumber, sourceId), record);
  }
}

function ensureCoreOrdinalSpeechMaps() {
  for (const item of transcript.querySelectorAll('li[data-list-ordinal]')) {
    if (item.querySelector(':scope > .speech-ordinal-map')) continue;
    const ordinal = String(item.dataset.listOrdinal || '').trim();
    if (!/^-?\d+$/.test(ordinal)) continue;
    const marker = document.createElement('span');
    marker.className = 'speech-ordinal-map';
    marker.setAttribute('aria-hidden', 'true');
    marker.style.display = 'none';
    marker.textContent = ordinal + '. ';
    item.insertBefore(marker, item.firstChild);
  }
}

function wrapWordsForRecordKeys(recordKeys, reset) {
  if (reset) {
    words = [];
    lexicalWords = [];
  }
  let currentKey = '';
  const walker = document.createTreeWalker(
    transcript,
    NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  const nodes = [];
  while (walker.nextNode()) {
    const node = walker.currentNode;
    if (node.nodeType === Node.ELEMENT_NODE) {
      const element = node;
      if (element.classList.contains('record-anchor')) {
        currentKey = makeRecordKey(
          String(element.dataset.jsonlRecord || ''),
          element.dataset.sourceId || '');
      }
      continue;
    }
    const parent = node.parentElement;
    if (!parent ||
        /^(SCRIPT|STYLE)$/.test(parent.tagName) ||
        parent.closest('.word') ||
        (recordKeys !== null && !recordKeys.has(currentKey)) ||
        !node.nodeValue.trim()) {
      continue;
    }
    nodes.push(node);
  }

  const rx = /(?<![\p{L}\p{M}\p{N}_.])\d*\.\d+(?!\.\d)(?=[fFlL]|\b)|\.+|[\p{L}\p{M}\p{N}_]+(?:['’\-][\p{L}\p{M}\p{N}_]+)*|[^\s]/gu;
  for (const node of nodes) {
    const value = node.nodeValue;
    let match;
    let last = 0;
    rx.lastIndex = 0;
    const fragment = document.createDocumentFragment();
    while ((match = rx.exec(value)) !== null) {
      fragment.append(value.slice(last, match.index));
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
    fragment.append(value.slice(last));
    node.replaceWith(fragment);
  }
}

function wrapWords(nodeMap = null) {
  words = [];
  lexicalWords = [];
  ensureCoreOrdinalSpeechMaps();
  wrapWordsForRecordKeys(
    nodeMap === null ? null : nodeRecordKeys(nodeMap),
    false);
}

function structureDetailsKey(details) {
  const presentation = details.getAttribute('data-presentation-id');
  if (presentation) return 'presentation:' + presentation;
  const marker = details.querySelector('[data-aicore-unit-id]');
  if (marker) return 'core-unit:' + marker.getAttribute('data-aicore-unit-id');
  const summary = Array.from(details.children).find(
    child => child.tagName === 'SUMMARY');
  const summaryText = summary
    ? summary.textContent.trim().replace(/\s+/g, ' ')
    : '';
  return 'summary:' + summaryText;
}

function normalizeStructureEntries(entries) {
  const result = new Map();
  for (const entry of entries || []) {
    const recordNumber = Number(entry.RecordNumber ?? entry.recordNumber ?? 0);
    const sourceId = String(entry.SourceId ?? entry.sourceId ?? '');
    const turnId = String(entry.TurnId ?? entry.turnId ?? '');
    const detailsChain = Array.from(
      entry.DetailsChain ?? entry.detailsChain ?? [],
      value => String(value));
    result.set(sourceId + '\u0000' + recordNumber, {
      recordNumber,
      sourceId,
      turnId,
      detailsChain
    });
  }
  return result;
}

function captureStructureDom() {
  const detailsKeys = new Map(
    Array.from(transcript.querySelectorAll('details'))
      .map(details => [details, structureDetailsKey(details)]));
  const entries = [];
  for (const anchor of transcript.querySelectorAll('.record-anchor')) {
    const chain = [];
    let element = anchor.parentElement;
    while (element) {
      if (element.tagName === 'DETAILS') {
        chain.unshift(detailsKeys.get(element) || 'details:?');
      }
      element = element.parentElement;
    }
    const turn = anchor.closest('section.transcript-turn');
    entries.push({
      recordNumber: Number(anchor.getAttribute('data-jsonl-record') || 0),
      sourceId: anchor.getAttribute('data-source-id') || '',
      turnId: turn
        ? 'presentation:' + (turn.getAttribute('data-presentation-id') || '')
        : '',
      detailsChain: chain
    });
  }
  return {
    entries,
    detailsCount: transcript.querySelectorAll('details').length,
    turnCount: transcript.querySelectorAll('section.transcript-turn').length
  };
}

function compareStructureMaps(before, after) {
  const differences = [];
  for (const [key, left] of before) {
    const right = after.get(key);
    if (!right) {
      differences.push({
        recordNumber: left.recordNumber,
        sourceId: left.sourceId,
        kind: 'missing-record',
        beforeTurn: left.turnId,
        afterTurn: '',
        beforeDetails: left.detailsChain,
        afterDetails: []
      });
      continue;
    }
    const detailsChanged =
      left.detailsChain.length !== right.detailsChain.length ||
      left.detailsChain.some((value, index) => value !== right.detailsChain[index]);
    const turnChanged = left.turnId && right.turnId && left.turnId !== right.turnId;
    if (detailsChanged || turnChanged) {
      differences.push({
        recordNumber: left.recordNumber,
        sourceId: left.sourceId,
        kind: 'containment-changed',
        beforeTurn: left.turnId,
        afterTurn: right.turnId,
        beforeDetails: left.detailsChain,
        afterDetails: right.detailsChain
      });
    }
  }
  for (const [key, right] of after) {
    if (!before.has(key)) {
      differences.push({
        recordNumber: right.recordNumber,
        sourceId: right.sourceId,
        kind: 'unexpected-record',
        beforeTurn: '',
        afterTurn: right.turnId,
        beforeDetails: [],
        afterDetails: right.detailsChain
      });
    }
  }
  return differences;
}

function structureAnchorSelector(recordNumber, sourceId) {
  return '.record-anchor[data-jsonl-record="' +
    CSS.escape(String(recordNumber)) + '"][data-source-id="' +
    CSS.escape(String(sourceId || '')) + '"]';
}

function inputContextForRecord(html, recordNumber, sourceId) {
  const recordNeedle = 'data-jsonl-record="' + String(recordNumber) + '"';
  const sourceNeedle = 'data-source-id="' + String(sourceId || '') + '"';
  let index = html.indexOf(sourceNeedle);
  if (index < 0) index = html.indexOf(recordNeedle);
  if (index < 0) return '';
  const start = Math.max(0, index - 1800);
  const end = Math.min(html.length, index + 2200);
  return html.slice(start, end);
}

function domContextForRecord(recordNumber, sourceId) {
  const anchor = transcript.querySelector(
    structureAnchorSelector(recordNumber, sourceId));
  if (!anchor) return '';
  let element = anchor.parentElement;
  let selected = anchor;
  for (let depth = 0; element && depth < 4; depth++) {
    selected = element;
    if (element.tagName === 'DETAILS') break;
    element = element.parentElement;
  }
  const html = selected.outerHTML || '';
  return html.length <= 5000 ? html : html.slice(0, 5000);
}

function buildStructureDivergenceContexts(html, differences) {
  return (differences || []).slice(0, 20).map(diff => ({
    recordNumber: diff.recordNumber,
    sourceId: diff.sourceId,
    beforeDetails: diff.beforeDetails,
    afterDetails: diff.afterDetails,
    inputContext: inputContextForRecord(
      html,
      diff.recordNumber,
      diff.sourceId),
    domContext: domContextForRecord(diff.recordNumber, diff.sourceId)
  }));
}

function postStructureStage(
  probeId,
  stage,
  expectedMap,
  previousMap,
  previousStage,
  rawInputHtml = '',
  exactAssignedHtml = '',
  exactParsedHtml = '') {
  if (!probeId) return previousMap;
  const snapshot = captureStructureDom();
  const currentMap = normalizeStructureEntries(snapshot.entries);
  const expectedDifferences = compareStructureMaps(expectedMap, currentMap);
  const previousDifferences = compareStructureMaps(previousMap, currentMap);
  chrome.webview.postMessage({
    type: previousDifferences.length === 0
      ? 'structure-js-equivalent'
      : 'structure-js-divergence',
    probeId,
    stage,
    previousStage,
    anchorCount: snapshot.entries.length,
    detailsCount: snapshot.detailsCount,
    turnCount: snapshot.turnCount,
    expectedDifferenceCount: expectedDifferences.length,
    previousDifferenceCount: previousDifferences.length,
    differencesFromExpected: expectedDifferences,
    differencesFromPrevious: previousDifferences,
    divergenceContexts:
      previousDifferences.length === 0 || !rawInputHtml
        ? []
        : buildStructureDivergenceContexts(rawInputHtml, previousDifferences),
    exactAssignedHtml:
      previousDifferences.length === 0 ? '' : exactAssignedHtml,
    exactParsedHtml:
      previousDifferences.length === 0 ? '' : exactParsedHtml,
    exactAssignedHtmlLength:
      previousDifferences.length === 0 ? 0 : exactAssignedHtml.length,
    exactParsedHtmlLength:
      previousDifferences.length === 0 ? 0 : exactParsedHtml.length
  });
  return currentMap;
}

function buildTranscriptDomNode(spec) {
  if (!spec) return document.createDocumentFragment();
  const kind = String(spec.Kind ?? spec.kind ?? '');
  if (kind === 'text') {
    return document.createTextNode(String(spec.Text ?? spec.text ?? ''));
  }
  if (kind === 'html') {
    // Markdown parsing ends at the leaf boundary. Structural transcript nodes
    // never enter innerHTML/template parsing.
    const template = document.createElement('template');
    template.innerHTML = String(spec.Html ?? spec.html ?? '');
    return template.content.cloneNode(true);
  }
  if (kind !== 'element') return document.createDocumentFragment();

  const tag = String(spec.Tag ?? spec.tag ?? 'div');
  const element = document.createElement(tag);
  const attributes = spec.Attributes ?? spec.attributes ?? {};
  for (const [name, value] of Object.entries(attributes)) {
    element.setAttribute(name, String(value));
  }
  const children = spec.Children ?? spec.children ?? [];
  for (const child of children) {
    element.append(buildTranscriptDomNode(child));
  }
  return element;
}

function replaceTranscriptDom(
  domNodes,
  preserve,
  nodeMap,
  wordMap,
  expectedStructure = [],
  structureProbeId = '') {
  const expectedStructureMap = normalizeStructureEntries(expectedStructure);
  clearFindHighlights();
  findCurrentWords = [];
  const nearBottom = document.documentElement.scrollHeight -
    (window.scrollY + window.innerHeight) < 80;
  const previousY = window.scrollY;
  const openDetails = new Map();
  if (preserve) {
    for (const details of transcript.querySelectorAll('details')) {
      openDetails.set(structureDetailsKey(details), details.open);
    }
  }

  const fragment = document.createDocumentFragment();
  for (const spec of domNodes || []) {
    fragment.append(buildTranscriptDomNode(spec));
  }
  transcript.replaceChildren(fragment);
  applyRevisionVisibility(showRolledBackHistory);

  let currentStructureMap = postStructureStage(
    structureProbeId,
    'after-dom-construction',
    expectedStructureMap,
    expectedStructureMap,
    'dom-model-serialized-html');
  for (const details of transcript.querySelectorAll('details')) {
    const key = structureDetailsKey(details);
    if (openDetails.has(key)) details.open = openDetails.get(key);
  }
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-details-restore',
    expectedStructureMap,
    currentStructureMap,
    'after-dom-construction');

  setAvailableWordMaps(wordMap || []);
  wrapWords(nodeMap || []);
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-wrap-words',
    expectedStructureMap,
    currentStructureMap,
    'after-details-restore');
  assignRecordScopes();
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-record-scopes',
    expectedStructureMap,
    currentStructureMap,
    'after-wrap-words');
  assignNodeScopes(nodeMap || []);
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-node-scopes',
    expectedStructureMap,
    currentStructureMap,
    'after-record-scopes');
  assignStableWordScopes(wordMap || []);
  postMappingInstallSummary(nodeMap || []);
  postStructureStage(
    structureProbeId,
    'replace-dom-exit',
    expectedStructureMap,
    currentStructureMap,
    'after-node-scopes');

  windowStartIndex = 0;
  windowEndIndex = Number.MAX_SAFE_INTEGER;
  virtualShiftPending = false;
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  currentSpeechListItem = null;
  liveEndMarker.style.display = 'none';
  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
}

function replaceTranscriptWindow(
  html,
  preserve,
  nodeMap,
  wordMap,
  startIndex = -1,
  endIndex = -1,
  topSpacerHeight = 0,
  bottomSpacerHeight = 0,
  anchorRecordNumber = null,
  anchorSourceId = null,
  anchorOffset = null,
  focusVirtualIndex = null,
  focusEdge = null,
  expectedStructure = [],
  structureProbeId = '') {
  const expectedStructureMap = normalizeStructureEntries(expectedStructure);
  let previousStructureMap = expectedStructureMap;
  let previousStructureStage = 'virtual-window-html';
  clearFindHighlights();
  findCurrentWords = [];
  const nearBottom = document.documentElement.scrollHeight -
    (window.scrollY + window.innerHeight) < 80;
  const previousY = window.scrollY;
  const openDetails = preserve
    ? [...transcript.querySelectorAll('details')].map(x => x.open)
    : [];
  // Replacing spacer heights and materialized records can itself change
  // scrollY.  Mark that layout-induced movement as programmatic.  Genuine
  // wheel/touch/scroll-key/scrollbar input can explicitly override this guard.
  programmaticScrollUntil = Math.max(
    programmaticScrollUntil,
    performance.now() + VW_WINDOW_REPLACEMENT_SCROLL_GUARD_MS);
  const exactAssignedHtml =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +
    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  transcript.innerHTML = exactAssignedHtml;
  applyRevisionVisibility(showRolledBackHistory);
  const exactParsedHtml = transcript.innerHTML;
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-inner-html',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage,
    html,
    exactAssignedHtml,
    exactParsedHtml);
  previousStructureStage = 'after-inner-html';
  windowStartIndex = startIndex;
  windowEndIndex = endIndex;
  [...transcript.querySelectorAll('details')].forEach((item, index) => {
    if (index < openDetails.length) item.open = openDetails[index];
  });
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-details-restore',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-details-restore';
  setAvailableWordMaps(wordMap || []);
  wrapWords(nodeMap || []);
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-wrap-words',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-wrap-words';
  assignRecordScopes();
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-record-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-record-scopes';
  assignNodeScopes(nodeMap || []);
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-node-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-node-scopes';
  assignStableWordScopes(wordMap || []);
  postMappingInstallSummary(nodeMap || []);
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-stable-word-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-stable-word-scopes';
  const measurements = [...transcript.querySelectorAll('.virtual-record')]
    .map(record => ({
      index:Number(record.dataset.virtualIndex || -1),
      height:record.getBoundingClientRect().height
    }))
    .filter(item => item.index >= 0 && item.height > 0);
  if (measurements.length) {
    chrome.webview.postMessage({
      type:'window-measured',
      layoutGeneration,
      viewportHeight:window.innerHeight,
      measurements
    });
  }
  virtualShiftPending = false;
  function viewportIntersectsMaterializedContent() {
    return [...transcript.querySelectorAll('.virtual-record')].some(record => {
      const rect = record.getBoundingClientRect();
      return rect.bottom > 0 && rect.top < window.innerHeight;
    });
  }
  function focusRequestedVirtualRecord() {
    if (focusVirtualIndex === null) return false;
    const focusRecord = transcript.querySelector(
      '.virtual-record[data-virtual-index="' +
      CSS.escape(String(focusVirtualIndex)) + '"]');
    if (!focusRecord) return false;
    programmaticScrollUntil = performance.now() + 2000;
    if (focusEdge === 'start') {
      window.scrollTo(0, 0);
    } else if (focusEdge === 'end') {
      window.scrollTo(0, document.documentElement.scrollHeight);
    } else {
      focusRecord.scrollIntoView({block:'center', behavior:'auto'});
    }
    return true;
  }

  let restoredAnchor = false;
  if (anchorRecordNumber !== null && anchorOffset !== null) {
    const selector = '.record-anchor[data-jsonl-record="' +
      CSS.escape(String(anchorRecordNumber)) + '"][data-source-id="' +
      CSS.escape(String(anchorSourceId || '')) + '"]';
    const anchor = transcript.querySelector(selector);
    if (anchor) {
      const delta = anchor.getBoundingClientRect().top - Number(anchorOffset);
      programmaticScrollUntil = performance.now() + 500;
      window.scrollBy(0, delta);
      restoredAnchor = true;
    }
  }
  if ((!restoredAnchor || !viewportIntersectsMaterializedContent()) &&
      focusVirtualIndex !== null) {
    focusRequestedVirtualRecord();
  }
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  currentSpeechListItem = null;
  liveEndMarker.style.display = 'none';
  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
  postStructureStage(
    structureProbeId,
    'replace-window-exit',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
}

function replaceTranscript(html, preserve, nodeMap, wordMap = []) {
  replaceTranscriptWindow(
    html, preserve, nodeMap, wordMap, -1, -1, 0, 0);
}

function updateFollowToggle() {
  followToggle.textContent = followSpeech ? '👁️ = 👄' : '👁️ ≠ 👄';
  followToggle.title = followSpeech
    ? 'Following speech; click or press = to stop following'
    : 'Not following speech; click or press = to follow';
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
      programmaticScrollUntil = performance.now() + 1500;
      target.scrollIntoView({block:'center', behavior:'smooth'});
    }
  }
}

function applyRevisionVisibility(show) {
  showRolledBackHistory = !!show;
  for (const turn of transcript.querySelectorAll(
      'section.transcript-turn[data-revision-historical="true"]')) {
    turn.hidden = !showRolledBackHistory;
  }
}

function applySettings(
  highlight,
  duration,
  follow,
  showHistory,
  generation,
  dark) {
  document.documentElement.classList.toggle('dark', dark);
  document.documentElement.style.setProperty('--highlight', highlight);
  document.documentElement.style.setProperty('--fade-ms', duration + 'ms');
  fadeMs = duration;
  layoutGeneration = Number(generation || layoutGeneration);
  applyRevisionVisibility(showHistory);
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

function assignStableWordScopes(wordMap) {
  displayWordsById = new Map();
  const mapsByRecord = new Map();
  for (const record of wordMap || []) {
    const recordNumber = String(record.RecordNumber ?? record.recordNumber ?? '');
    const sourceId = String(record.SourceId ?? record.sourceId ?? '');
    mapsByRecord.set(
      makeRecordKey(recordNumber, sourceId),
      record.Words ?? record.words ?? []);
  }

  for (const [key, recordWords] of displayWordsByRecord) {
    const mappedWords = mapsByRecord.get(key) || [];
    if (mappedWords.length !== recordWords.length) {
      chrome.webview.postMessage({
        type:'stable-word-map-failure',
        key,
        renderedWordCount:recordWords.length,
        mappedWordCount:mappedWords.length
      });
    }
    const count = Math.min(recordWords.length, mappedWords.length);
    for (let index = 0; index < count; ++index) {
      const word = recordWords[index];
      const mapped = mappedWords[index];
      const wordId = String(mapped.WordId ?? mapped.wordId ?? '');
      if (!wordId) continue;
      word.dataset.wordId = wordId;
      word.id = 'word-' + wordId;
      displayWordsById.set(wordId, word);
      const nodeId = Number(mapped.NodeId ?? mapped.nodeId ?? 0);
      const nodeWordIndex = Number(
        mapped.NodeWordIndex ?? mapped.nodeWordIndex ?? -1);
      if (nodeId > 0 && nodeWordIndex >= 0) {
        word.dataset.nodeId = String(nodeId);
        word.dataset.nodeWordIndex = String(nodeWordIndex);
      }
    }
  }
}

function installSearchWordMaps(wordMap) {
  setAvailableWordMaps(wordMap || []);
  assignStableWordScopes(wordMap || []);
}

function materializeRecordWords(recordNumber, sourceId) {
  const key = makeRecordKey(String(recordNumber), String(sourceId || ''));
  if (displayWordsByRecord.has(key)) return true;
  if (!availableWordMapsByRecord.has(key)) return false;
  const selector = '.record-anchor[data-jsonl-record="' +
    CSS.escape(String(recordNumber)) + '"][data-source-id="' +
    CSS.escape(String(sourceId || '')) + '"]';
  if (!transcript.querySelector(selector)) return false;

  const beforeWordCount = words.length;
  const started = performance.now();
  wrapWordsForRecordKeys(new Set([key]), false);
  assignRecordScopes();
  assignStableWordScopes([...availableWordMapsByRecord.values()]);
  const materialized = displayWordsByRecord.has(key);
  chrome.webview.postMessage({
    type:'lazy-word-materialized',
    recordNumber:Number(recordNumber),
    sourceId:String(sourceId || ''),
    addedWordCount:words.length - beforeWordCount,
    totalWordCount:words.length,
    elapsedMilliseconds:Math.round(performance.now() - started),
    materialized
  });
  return materialized;
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

function diagnosticRanges(ranges) {
  return (ranges || []).slice(0, 64).map(range => ({
    start: range.start,
    end: range.end,
    displayKey: String(range.displayKey || '').slice(0, 1000),
    lexicalKey: String(range.lexicalKey || '').slice(0, 1000)
  }));
}

function postMappingInstallSummary(nodeMap) {
  const scopedCounts = new Map();
  const stableCounts = new Map();
  for (const word of words) {
    const nodeId = String(word.dataset.nodeId || '');
    if (!nodeId) continue;
    scopedCounts.set(nodeId, (scopedCounts.get(nodeId) || 0) + 1);
    if (word.dataset.wordId) {
      stableCounts.set(nodeId, (stableCounts.get(nodeId) || 0) + 1);
    }
  }
  const nodes = [];
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    const recordNumber = Number(
      item.RecordNumber ?? item.recordNumber ?? 0);
    const sourceId = String(item.SourceId ?? item.sourceId ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const ranges = segmentRangesByNode.get(nodeId) || [];
    nodes.push({
      nodeId: Number(nodeId),
      recordNumber,
      sourceId,
      segmentCount: segments.length,
      rangeCount: ranges.length,
      scopedWordCount: scopedCounts.get(nodeId) || 0,
      stableWordCount: stableCounts.get(nodeId) || 0
    });
  }
  chrome.webview.postMessage({
    type: 'mapping-install-summary',
    mappingGeneration,
    nodeCount: nodes.length,
    totalWordCount: words.length,
    nodes
  });
}

function postFragmentRangeMiss(
  text,
  nodeId,
  nodeKey,
  displayKey,
  lexicalKey,
  mapped,
  knownNode) {
  const nodeWords = words.filter(word => word.dataset.nodeId === nodeKey);
  chrome.webview.postMessage({
    type: 'fragment-range-miss',
    mappingGeneration,
    nodeId,
    knownNode,
    text: String(text || '').slice(0, 500),
    displayKey: String(displayKey || '').slice(0, 1000),
    lexicalKey: String(lexicalKey || '').slice(0, 1000),
    storedRangeCount: mapped.length,
    storedRanges: diagnosticRanges(mapped),
    currentNode,
    currentIndex,
    currentEndIndex,
    currentFragmentText: String(currentFragmentText || '').slice(0, 500),
    currentFragmentStart,
    currentFragmentEnd,
    currentBoundaryWordIndex,
    nodeWordCount: nodeWords.length,
    nodeWordSample: nodeWords.slice(0, 80).map(word => ({
      index: Number(word.dataset.index ?? -1),
      normalized: word.dataset.normalized || '',
      recordNumber: word.dataset.recordNumber || '',
      sourceId: word.dataset.sourceId || '',
      recordIndex: word.dataset.recordIndex || '',
      wordId: word.dataset.wordId || ''
    }))
  });
}

function assignNodeScopes(nodeMap) {
  ++mappingGeneration;
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
        continue;
      }

      const failureKey = nodeId + ':' + recordNumber + ':' +
        sourceId + ':' + segment;
      if (!reportedMappingFailures.has(failureKey)) {
        reportedMappingFailures.add(failureKey);
        chrome.webview.postMessage({
          type: 'mapping-failure',
          mappingGeneration,
          nodeId: Number(nodeId),
          recordNumber: Number(recordNumber),
          sourceId,
          text: segment.slice(0, 500),
          displayKey: displayTarget.join('\u0000').slice(0, 1000),
          lexicalKey: lexicalTarget.join('\u0000').slice(0, 1000),
          displayCursor,
          lexicalCursor,
          recordWordCount: recordWords.length,
          recordLexicalWordCount: recordLexicalWords.length,
          recordWordSample: recordWords.slice(0, 80)
            .map(word => word.dataset.normalized || ''),
          recordLexicalWordSample: recordLexicalWords.slice(0, 80)
            .map(word => word.dataset.normalized || '')
        });
      }
    }
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
  const knownNode = knownNodeIds.has(nodeKey);
  if (knownNode) {
    postFragmentRangeMiss(
      text, nodeId, nodeKey, displayKey, lexicalKey, mapped, true);
    return null;
  }

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
  postFragmentRangeMiss(
    text, nodeId, nodeKey, displayKey, lexicalKey, mapped, false);
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

const VW_MIN_VIEWPORT_HEIGHTS = 5;
const VW_EDGE_TRIGGER_VIEWPORTS = 2;
const VW_SCROLL_DIRECTION_EPSILON_PX = 1;
const VW_SHIFT_PENDING_TIMEOUT_MS = 3000;
let lastVoiceVirtualIndex = -1;
let lastVoiceLocalY = Number.NaN;

function materializedVirtualRecords() {
  return [...transcript.querySelectorAll('.virtual-record')];
}

function materializedWindowBounds() {
  const records = materializedVirtualRecords();
  if (!records.length) return null;
  return {
    records,
    first:records[0].getBoundingClientRect(),
    last:records[records.length - 1].getBoundingClientRect()
  };
}

function maybePrefetchVoiceCursor(element) {
  if (!followSpeech || !element || virtualShiftPending) return;
  const record = element.closest('.virtual-record');
  const bounds = materializedWindowBounds();
  if (!record || !bounds) return;

  const virtualIndex = Number(record.dataset.virtualIndex || -1);
  const recordRect = record.getBoundingClientRect();
  const cursorRect = element.getBoundingClientRect();
  const localY = cursorRect.top - recordRect.top;
  let direction = 0;
  if (lastVoiceVirtualIndex >= 0) {
    if (virtualIndex > lastVoiceVirtualIndex) direction = 1;
    else if (virtualIndex < lastVoiceVirtualIndex) direction = -1;
    else if (Number.isFinite(lastVoiceLocalY)) {
      if (localY > lastVoiceLocalY + VW_SCROLL_DIRECTION_EPSILON_PX) direction = 1;
      else if (localY < lastVoiceLocalY - VW_SCROLL_DIRECTION_EPSILON_PX) direction = -1;
    }
  }
  lastVoiceVirtualIndex = virtualIndex;
  lastVoiceLocalY = localY;

  const triggerDistance = window.innerHeight * VW_EDGE_TRIGGER_VIEWPORTS;
  const nearTop = cursorRect.top - bounds.first.top <= triggerDistance;
  const nearBottom = bounds.last.bottom - cursorRect.bottom <= triggerDistance;
  const canMoveUp = windowStartIndex > 0;
  const bottomSpacer = transcript.querySelector(
    '.virtual-spacer[data-virtual-spacer="bottom"]');
  const canMoveDown = (bottomSpacer?.getBoundingClientRect().height ?? 0) > 0;

  if (nearTop && nearBottom) {
    if (direction < 0 && canMoveUp) {
      requestVirtualShift(-1, 'playback-up', record);
    } else if (direction > 0 && canMoveDown) {
      requestVirtualShift(1, 'playback-down', record);
    } else if (canMoveDown &&
               bounds.last.bottom - cursorRect.bottom <=
                 cursorRect.top - bounds.first.top) {
      requestVirtualShift(1, 'playback-down', record);
    } else if (canMoveUp) {
      requestVirtualShift(-1, 'playback-up', record);
    }
    return;
  }
  if (nearBottom && canMoveDown && direction >= 0) {
    requestVirtualShift(1, 'playback-down', record);
  } else if (nearTop && canMoveUp && direction <= 0) {
    requestVirtualShift(-1, 'playback-up', record);
  }
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

function ordinalListItemForWord(word) {
  const ordinal = word?.closest('.speech-ordinal-map');
  return ordinal ? ordinal.closest('li') : null;
}

function clearSpeechListItemHighlight() {
  if (!currentSpeechListItem) return;
  currentSpeechListItem.classList.remove(
    'speech-list-item-active',
    'speech-list-item-paused');
  currentSpeechListItem = null;
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
  clearSpeechListItemHighlight();
  if (state === 'none') {
    retireCurrentWord(true);
    return;
  }
  if (state === 'waiting-end' || state === 'paused-end') {
    retireCurrentWord(true);
    liveEndMarker.textContent = state === 'waiting-end'
      ? 'Waiting for new text...'
      : 'Press play to wait for more text.';
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
  const listItem = ordinalListItemForWord(target);
  openAncestors(listItem || target);
  if (state === 'paused') {
    retireCurrentWord(false);
    if (listItem) {
      listItem.classList.add('speech-list-item-paused');
      currentSpeechListItem = listItem;
    } else {
      applyRangeClass(range, 'paused');
    }
  } else {
    if (currentIndex >= 0 &&
        (currentIndex !== range.start || currentEndIndex !== range.end)) {
      retireCurrentWord(true);
    }
    if (listItem) {
      listItem.classList.add('speech-list-item-active');
      currentSpeechListItem = listItem;
    } else {
      applyRangeClass(range, 'active');
    }
  }
  currentIndex = range.start;
  currentEndIndex = range.end;
  voiceMarkerIndex = range.start;
  currentBoundaryWordIndex = wordIndex;
  currentNode = nodeId;
  reveal(listItem || target);
  maybePrefetchVoiceCursor(target);
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
  findSeekVoiced.disabled = !available;
}

function cancelFindSearch(updateStatus) {
  ++findGeneration;
  ++findNavigationGeneration;
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
    fileOrdinal: Number(match.FileOrdinal ?? match.fileOrdinal ?? 0),
    recordNumber: Number(match.RecordNumber ?? match.recordNumber ?? 0),
    sourceId: String(match.SourceId ?? match.sourceId ?? ''),
    startWordIndex: Number(match.StartWordIndex ?? match.startWordIndex ?? -1),
    endWordIndex: Number(match.EndWordIndex ?? match.endWordIndex ?? -1),
    wordIds: (match.WordIds ?? match.wordIds ?? []).map(value => String(value)),
    seekWordId: String(match.SeekWordId ?? match.seekWordId ?? ''),
    nodeId: Number(match.NodeId ?? match.nodeId ?? 0),
    nodeWordIndex: Number(match.NodeWordIndex ?? match.nodeWordIndex ?? -1)
  };
}

async function showFindMatch(
  index,
  trigger = 'unknown',
  requestedNavigationGeneration = null) {
  if (!findMatches.length || findSearchPending) {
    reportFind('navigation-ignored', {trigger});
    return;
  }

  const navigationGeneration = requestedNavigationGeneration === null
    ? ++findNavigationGeneration
    : Number(requestedNavigationGeneration);
  if (navigationGeneration !== findNavigationGeneration) {
    reportFind('navigation-stale', {trigger, navigationGeneration});
    return;
  }

  clearFindHighlights();
  liveEndMarker.style.display = 'none';
  currentFindMatch = (index + findMatches.length) % findMatches.length;
  const match = findMatches[currentFindMatch];
  if (followSpeech) setFollowSpeech(false, true);
  const key = makeRecordKey(String(match.recordNumber), match.sourceId);
  let recordWords = displayWordsByRecord.get(key);
  if (!recordWords &&
      materializeRecordWords(match.recordNumber, match.sourceId)) {
    recordWords = displayWordsByRecord.get(key);
  }
  if (!recordWords) {
    findCount.textContent = `${match.fileOrdinal} of ${findMatches.length}`;
    chrome.webview.postMessage({
      type:'window-request',
      recordNumber:match.recordNumber,
      sourceId:match.sourceId,
      reason:'search',
      matchIndex:currentFindMatch,
      navigationGeneration
    });
    reportFind('window-requested', {trigger, targetIndex:currentFindMatch});
    return;
  }
  const matchedWords = match.wordIds
    .map(wordId => displayWordsById.get(String(wordId)))
    .filter(word => !!word);
  if (!match.wordIds.length || matchedWords.length !== match.wordIds.length) {
    reportFind('navigation-word-id-missing', {
      trigger,
      expectedWordCount:match.wordIds.length,
      resolvedWordCount:matchedWords.length,
      firstWordId:match.wordIds.length ? match.wordIds[0] : ''
    });
    return;
  }
  for (const word of matchedWords) {
    word.classList.add('find-current');
    findCurrentWords.push(word);
  }
  const target = matchedWords[0];
  const openedDetailsCount = openAncestors(target);
  programmaticScrollUntil = performance.now() + 1500;
  target.scrollIntoView({block:'center', behavior:'smooth'});
  findCount.textContent = `${match.fileOrdinal} of ${findMatches.length}`;
  reportFind('navigated', {
    trigger,
    targetIndex: currentFindMatch,
    openedDetailsCount
  });
}

function startFindSlowTimer(requestId) {
  if (!findSearchPending || requestId !== findGeneration) return;
  if (findSlowTimer) clearTimeout(findSlowTimer);
  findSlowTimer = setTimeout(() => {
    if (!findSearchPending || requestId !== findGeneration) return;
    reportFind('slow-prompt');
    if (!confirm('The search is still running. Continue waiting?')) {
      cancelFindSearch(true);
    }
  }, 5000);
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
        kind:'selection',
        recordNumber:Number(word.dataset.recordNumber || 0),
        sourceId:word.dataset.sourceId || '',
        wordIndex:Number(word.dataset.recordIndex || -1)
      };
    }
  }
  return {kind:'voice', recordNumber:0, sourceId:'', wordIndex:-1};
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
    originKind:origin.kind,
    originRecordNumber:origin.recordNumber,
    originSourceId:origin.sourceId,
    originWordIndex:origin.wordIndex
  });
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

  if (findMatches.length > 0 && currentFindMatch >= 0) {
    updateFindNavigationState();
    void showFindMatch(currentFindMatch, 'reopened');
  } else if (findInput.value && !findSearchPending) {
    runFind();
  } else {
    updateFindNavigationState();
  }
  reportFind('opened');
}

function closeFind() {
  if (findInputTimer) {
    clearTimeout(findInputTimer);
    findInputTimer = 0;
  }
  cancelFindSearch(false);
  clearFindHighlights();
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

function isVoicedFindMatch(match) {
  return !!match && Number(match.seekWordId || 0) > 0;
}

function postFindSeek(match, trigger) {
  reportFind('seek-requested', {
    trigger,
    targetMatch: currentFindMatch,
    fileOrdinal: match.fileOrdinal,
    seekWordId:match.seekWordId
  });
  chrome.webview.postMessage({
    type:'find-seek',
    nodeId:Number(match.nodeId),
    nodeWordIndex:Number(match.nodeWordIndex),
    wordId:Number(match.seekWordId)
  });
}

async function seekCurrentOrNextVoiced(trigger) {
  if (!findMatches.length || findSearchPending || currentFindMatch < 0) {
    findCount.textContent = 'No results';
    reportFind('seek-ignored', {trigger, reason:'no-results'});
    return;
  }

  let targetIndex = currentFindMatch;
  if (!isVoicedFindMatch(findMatches[targetIndex])) {
    targetIndex = -1;
    for (let candidate = currentFindMatch + 1;
         candidate < findMatches.length;
         ++candidate) {
      if (isVoicedFindMatch(findMatches[candidate])) {
        targetIndex = candidate;
        break;
      }
    }
    if (targetIndex < 0) {
      clearFindHighlights();
      currentFindMatch = findMatches.length - 1;
      findCount.textContent = 'End';
      liveEndMarker.textContent = 'Press play to wait for more text.';
      liveEndMarker.style.display = 'block';
      programmaticScrollUntil = performance.now() + 1500;
      liveEndMarker.scrollIntoView({block:'center', behavior:'smooth'});
      if (followSpeech) setFollowSpeech(false, true);
      chrome.webview.postMessage({type:'find-seek-end'});
      reportFind('seek-end', {trigger, reason:'no-later-voiced-result'});
      return;
    }
    await showFindMatch(targetIndex, trigger + '-next-voiced');
  }

  const match = findMatches[currentFindMatch];
  if (!isVoicedFindMatch(match)) {
    findCount.textContent = 'Not voiced';
    reportFind('seek-ignored', {trigger, reason:'target-not-voiced'});
    return;
  }
  postFindSeek(match, trigger);
}

findSeekVoiced.addEventListener('click', () => {
  void seekCurrentOrNextVoiced('button-seek-voiced');
});
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
    if (event.shiftKey) {
      void seekCurrentOrNextVoiced('ctrl-shift-enter');
      return;
    }
    const match = findMatches[currentFindMatch];
    if (!isVoicedFindMatch(match)) {
      findCount.textContent = 'Not voiced';
      reportFind('seek-ignored', {
        trigger:'ctrl-enter',
        reason:'target-not-voiced'
      });
      return;
    }
    postFindSeek(match, 'ctrl-enter');
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

const VW_USER_SCROLL_INTENT_MS = 1200;
const VW_WINDOW_REPLACEMENT_SCROLL_GUARD_MS = 300;
const VW_SCROLL_KEYS = new Set([
  'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '
]);
let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;
let userScrollIntentUntil = 0;
let userScrollIntentDirection = 0;
let lastTouchY = Number.NaN;

function markUserScrollIntent(direction = 0) {
  userScrollIntentUntil = performance.now() + VW_USER_SCROLL_INTENT_MS;
  userScrollIntentDirection = Math.sign(Number(direction) || 0);
}

function isEditableScrollTarget(target) {
  return target instanceof Element &&
    (target.matches('input,textarea,select') || target.isContentEditable);
}

window.addEventListener('wheel', event => {
  markUserScrollIntent(event.deltaY);
}, {
  passive:true,
  capture:true
});
window.addEventListener('touchstart', event => {
  lastTouchY = event.touches.length > 0
    ? event.touches[0].clientY
    : Number.NaN;
  markUserScrollIntent();
}, {
  passive:true,
  capture:true
});
window.addEventListener('touchmove', event => {
  const currentTouchY = event.touches.length > 0
    ? event.touches[0].clientY
    : Number.NaN;
  const direction = Number.isFinite(lastTouchY) &&
    Number.isFinite(currentTouchY)
      ? lastTouchY - currentTouchY
      : 0;
  lastTouchY = currentTouchY;
  markUserScrollIntent(direction);
}, {
  passive:true,
  capture:true
});
window.addEventListener('keydown', event => {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||
      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {
    return;
  }
  let direction = 0;
  if (event.key === 'ArrowUp' || event.key === 'PageUp' ||
      event.key === 'Home') {
    direction = -1;
  } else if (event.key === 'ArrowDown' || event.key === 'PageDown' ||
             event.key === 'End') {
    direction = 1;
  } else if (event.key === ' ') {
    direction = event.shiftKey ? -1 : 1;
  }
  markUserScrollIntent(direction);
}, {capture:true});
window.addEventListener('pointerdown', event => {
  if (event.button !== 0) return;
  const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
  if (scrollbarWidth > 0 && event.clientX >= document.documentElement.clientWidth) {
    markUserScrollIntent();
  }
}, {capture:true});

function firstVisibleVirtualRecord(direction = 0) {
  const records = materializedVirtualRecords();
  const visible = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  });
  if (visible.length) {
    return direction > 0 ? visible[visible.length - 1] : visible[0];
  }
  if (!records.length) return null;
  const firstRect = records[0].getBoundingClientRect();
  const lastRect = records[records.length - 1].getBoundingClientRect();
  if (direction < 0 && firstRect.top >= window.innerHeight) return records[0];
  if (direction > 0 && lastRect.bottom <= 0) return records[records.length - 1];
  return null;
}

function requestVirtualShift(direction, reason = null, referenceRecord = null) {
  if (virtualShiftPending || windowStartIndex < 0 || windowEndIndex < 0) return;
  const visibleRecords = materializedVirtualRecords().filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  });
  const visibleRecord = referenceRecord || firstVisibleVirtualRecord(direction);
  const anchor = visibleRecord?.querySelector('.record-anchor') || null;
  if (!visibleRecord || !anchor) return;
  const visibleIndex = Number(visibleRecord.dataset.virtualIndex || -1);
  if (visibleIndex < 0) return;
  const visibleRange = visibleRecords.length
    ? {
        visibleStartIndex:Number(
          visibleRecords[0].dataset.virtualIndex || -1),
        visibleEndIndex:Number(
          visibleRecords[visibleRecords.length - 1].dataset.virtualIndex || -1)
      }
    : {};
  virtualShiftPending = true;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    sourceStartIndex:windowStartIndex,
    sourceEndIndex:windowEndIndex,
    ...visibleRange,
    viewportHeight:window.innerHeight,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorSourceId:anchor.dataset.sourceId || '',
    anchorOffset:anchor.getBoundingClientRect().top
  });
  setTimeout(
    () => { virtualShiftPending = false; },
    VW_SHIFT_PENDING_TIMEOUT_MS);
}

function maybeRequestManualVirtualShift(direction) {
  if (direction === 0 || virtualShiftPending) return;
  const bounds = materializedWindowBounds();
  if (!bounds) return;
  const triggerDistance = window.innerHeight * VW_EDGE_TRIGGER_VIEWPORTS;
  if (direction < 0 && windowStartIndex > 0 &&
      -bounds.first.top <= triggerDistance) {
    requestVirtualShift(-1, 'scroll-up');
    return;
  }
  const bottomSpacer = transcript.querySelector(
    '.virtual-spacer[data-virtual-spacer="bottom"]');
  const hasContentBelow =
    (bottomSpacer?.getBoundingClientRect().height ?? 0) > 0;
  if (direction > 0 && hasContentBelow &&
      bounds.last.bottom - window.innerHeight <= triggerDistance) {
    requestVirtualShift(1, 'scroll-down');
  }
}

window.addEventListener('scroll', () => {
  const now = performance.now();
  const currentY = window.scrollY;
  const delta = currentY - lastManualScrollY;
  lastManualScrollY = currentY;
  if (Math.abs(delta) <= VW_SCROLL_DIRECTION_EPSILON_PX) return;

  const direction = delta > 0 ? 1 : -1;
  const explicitUserIntent = now <= userScrollIntentUntil;
  if (now <= programmaticScrollUntil) {
    // A physical input can override a programmatic scroll only when the
    // resulting movement agrees with that input. Window replacement and
    // anchor restoration can move scrollY in the opposite direction while the
    // earlier user-intent timer is still alive; that movement is not a second
    // user gesture and must not start a competing virtual-window shift.
    if (!explicitUserIntent ||
        (userScrollIntentDirection !== 0 &&
         direction !== userScrollIntentDirection)) {
      return;
    }
    programmaticScrollUntil = 0;
  }

  if (followSpeech) setFollowSpeech(false, true);
  if (virtualShiftFrame) cancelAnimationFrame(virtualShiftFrame);
  virtualShiftFrame = requestAnimationFrame(() => {
    virtualShiftFrame = 0;
    if (performance.now() <= programmaticScrollUntil) return;
    maybeRequestManualVirtualShift(direction);
  });
}, {passive:true});


chrome.webview.addEventListener('message', event => {
  const data = event.data;
  if (!data) return;
  if (data.type === 'find-waiting') {
    const requestId = Number(data.requestId ?? data.RequestId ?? 0);
    if (!findSearchPending || requestId !== findGeneration) return;
    findCount.textContent = 'Waiting for transcript…';
    reportFind('waiting-for-index');
    return;
  }
  if (data.type === 'find-started') {
    const requestId = Number(data.requestId ?? data.RequestId ?? 0);
    if (!findSearchPending || requestId !== findGeneration) return;
    findCount.textContent = 'Searching…';
    startFindSlowTimer(requestId);
    reportFind('search-started');
    return;
  }
  if (data.type === 'find-results') {
    applyCSharpFindResults(data);
    return;
  }
  if (data.type === 'window-ready') {
    const matchIndex = Number(data.matchIndex ?? -1);
    const navigationGeneration = Number(data.navigationGeneration ?? 0);
    if (navigationGeneration !== findNavigationGeneration) {
      reportFind('window-ready-stale', {
        targetIndex:matchIndex,
        navigationGeneration
      });
      return;
    }
    if (matchIndex >= 0 && matchIndex < findMatches.length) {
      void showFindMatch(
        matchIndex,
        'window-ready',
        navigationGeneration);
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
      data.showRolledBackHistory,
      data.layoutGeneration,
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
    (lower === 'c' || lower === 'w' || lower === 'r' || lower === 'v');
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
