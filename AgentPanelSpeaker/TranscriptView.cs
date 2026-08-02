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
    if (_initialized && !_refreshInProgress)
    {
      PostPlaybackPosition(position);
    }
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
        return new TranscriptRenderPayload(html, identities);
      }, token);

      long preparationMilliseconds = renderTimer.ElapsedMilliseconds;
      cancellation.Token.ThrowIfCancellationRequested();
      if (generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      string script = "replaceTranscript(" +
        JsonSerializer.Serialize(payload.Html) + "," +
        JsonSerializer.Serialize(!force) + "," +
        JsonSerializer.Serialize(payload.Identities) + ");";
      long domStartMilliseconds = renderTimer.ElapsedMilliseconds;
      if (!await ExecuteAsync(script))
      {
        ShowLoading("Unable to load transcript view. See diagnostic log.");
        return;
      }
      long domMilliseconds =
        renderTimer.ElapsedMilliseconds - domStartMilliseconds;
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
          error = ReadOptionalString(root, "error")
        });
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
    string Html,
    IReadOnlyList<TranscriptNodeIdentity> Identities);

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
let findWorker = null;
let findGeneration = 0;
let findSlowTimer = 0;
let findCaseEnabled = false;
let findWordEnabled = false;
let findRegexEnabled = false;
let findVoicedEnabled = true;
let findCorpusAll = null;
let findCorpusVoiced = null;
const findHighlightedWords = new Set();
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

function replaceTranscript(html, preserve, nodeMap) {
  cancelFindSearch(false);
  findCorpusAll = null;
  findCorpusVoiced = null;
  findHighlightedWords.clear();
  findCurrentWords = [];
  const nearBottom = document.documentElement.scrollHeight -
    (window.scrollY + window.innerHeight) < 80;
  const previousY = window.scrollY;
  const openDetails = preserve
    ? [...transcript.querySelectorAll('details')].map(x => x.open)
    : [];
  transcript.innerHTML = html;
  [...transcript.querySelectorAll('details')].forEach((item, index) => {
    if (index < openDetails.length) item.open = openDetails[index];
  });
  wrapWords();
  assignRecordScopes();
  assignNodeScopes(nodeMap || []);
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

function applySettings(highlight, duration, follow, dark) {
  document.documentElement.classList.toggle('dark', dark);
  document.documentElement.style.setProperty('--highlight', highlight);
  document.documentElement.style.setProperty('--fade-ms', duration + 'ms');
  fadeMs = duration;
  followSpeech = follow;
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

function nextAnimationFrame() {
  return new Promise(resolve => requestAnimationFrame(resolve));
}

function reveal(element) {
  if (!followSpeech || !element) return;
  const rect = element.getBoundingClientRect();
  const topComfort = window.innerHeight * .22;
  const bottomComfort = window.innerHeight * .78;
  if (rect.top < topComfort || rect.bottom > bottomComfort) {
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
  followSpeech = follow;
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
  for (const word of findHighlightedWords) {
    word.classList.remove('find-match');
  }
  findHighlightedWords.clear();
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
  const available = !findWorker && findMatches.length > 0;
  findPrev.disabled = !available;
  findNext.disabled = !available;
}

function cancelFindSearch(updateStatus) {
  ++findGeneration;
  if (findWorker) {
    findWorker.terminate();
    findWorker = null;
    reportFind('worker-cancelled');
  }
  if (findSlowTimer) {
    clearTimeout(findSlowTimer);
    findSlowTimer = 0;
  }
  if (updateStatus) findCount.textContent = 'Cancelled';
  updateFindNavigationState();
}

async function buildFindCorpus(voicedOnly, generation) {
  const cached = voicedOnly ? findCorpusVoiced : findCorpusAll;
  if (cached) return cached;
  const started = performance.now();
  const pieces = [];
  const ranges = [];
  const nodeWordCounts = new Map();
  let textLength = 0;
  const chunkSize = 1500;
  for (let index = 0; index < words.length; ++index) {
    if (generation !== findGeneration) return null;
    const word = words[index];
    if (voicedOnly && !word.dataset.nodeId) continue;
    const value = word.textContent || '';
    if (pieces.length) {
      pieces.push(' ');
      ++textLength;
    }
    const start = textLength;
    pieces.push(value);
    textLength += value.length;
    const nodeId = word.dataset.nodeId || '';
    let nodeWordIndex = -1;
    if (nodeId && word.dataset.lexical === '1') {
      nodeWordIndex = nodeWordCounts.get(nodeId) || 0;
      nodeWordCounts.set(nodeId, nodeWordIndex + 1);
    }
    ranges.push({
      start,
      end: textLength,
      word,
      wordIndex: index,
      nodeId,
      nodeWordIndex
    });
    if (index > 0 && index % chunkSize === 0) {
      findCount.textContent = 'Indexing…';
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  if (generation !== findGeneration) return null;
  const text = pieces.join('');
  const corpus = {text, ranges};
  if (voicedOnly) findCorpusVoiced = corpus;
  else findCorpusAll = corpus;
  reportFind('corpus-built', {
    corpusLength: text.length,
    corpusWords: ranges.length,
    elapsedMilliseconds: Math.round(performance.now() - started)
  });
  return corpus;
}

function escapeRegex(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function firstRangeEndingAfter(ranges, position) {
  let low = 0;
  let high = ranges.length;
  while (low < high) {
    const middle = (low + high) >>> 1;
    if (ranges[middle].end <= position) low = middle + 1;
    else high = middle;
  }
  return low;
}

function mapFindMatches(rawMatches, corpus) {
  const mapped = [];
  for (const raw of rawMatches) {
    const end = raw.start + Math.max(1, raw.length);
    const touched = [];
    for (let index = firstRangeEndingAfter(corpus.ranges, raw.start);
         index < corpus.ranges.length && corpus.ranges[index].start < end;
         ++index) {
      touched.push(corpus.ranges[index]);
    }
    if (!touched.length) continue;
    const firstLexical = touched.find(item => item.nodeWordIndex >= 0);
    mapped.push({
      words: touched.map(item => item.word),
      nodeId: firstLexical?.nodeId || '',
      nodeWordIndex: firstLexical?.nodeWordIndex ?? -1,
      startWordIndex: touched[0].wordIndex
    });
  }
  return mapped;
}

async function applyFindMatches(matches, mapElapsedMilliseconds) {
  let stageStarted = performance.now();
  clearFindHighlights();
  reportFind('results-cleared', {
    elapsedMilliseconds: Math.round(performance.now() - stageStarted)
  });
  await nextAnimationFrame();
  reportFind('after-clear-frame');

  findMatches = matches;
  stageStarted = performance.now();
  let highlightedWordCount = 0;
  for (const match of findMatches) {
    for (const word of match.words) {
      word.classList.add('find-match');
      findHighlightedWords.add(word);
      ++highlightedWordCount;
    }
  }
  reportFind('match-classes-applied', {
    elapsedMilliseconds: Math.round(performance.now() - stageStarted),
    highlightedWordCount
  });
  await nextAnimationFrame();
  reportFind('after-match-classes-frame');

  if (!findMatches.length) {
    currentFindMatch = -1;
    findCount.textContent = 'No results';
    updateFindNavigationState();
    reportFind('completed', {
      matchCount: 0,
      mapElapsedMilliseconds
    });
    return;
  }
  const marker = voiceMarkerIndex;
  currentFindMatch = findMatches.findIndex(match =>
    match.startWordIndex > marker);
  if (currentFindMatch < 0) currentFindMatch = 0;
  updateFindNavigationState();
  await showFindMatch(currentFindMatch, 'search-completed');
  reportFind('completed', {
    matchCount: findMatches.length,
    mapElapsedMilliseconds
  });
}

async function showFindMatch(index, trigger = 'unknown') {
  if (!findMatches.length || findWorker) {
    reportFind('navigation-ignored', {trigger});
    return;
  }

  let stageStarted = performance.now();
  for (const word of findCurrentWords) {
    word.classList.remove('find-current');
  }
  reportFind('current-class-cleared', {
    trigger,
    elapsedMilliseconds: Math.round(performance.now() - stageStarted),
    wordCount: findCurrentWords.length
  });
  await nextAnimationFrame();
  reportFind('after-current-clear-frame', {trigger});

  currentFindMatch = (index + findMatches.length) % findMatches.length;
  const match = findMatches[currentFindMatch];
  findCurrentWords = match.words;
  stageStarted = performance.now();
  for (const word of findCurrentWords) word.classList.add('find-current');
  reportFind('current-class-applied', {
    trigger,
    elapsedMilliseconds: Math.round(performance.now() - stageStarted),
    wordCount: findCurrentWords.length
  });
  await nextAnimationFrame();
  reportFind('after-current-class-frame', {trigger});

  const target = match.words[0];
  stageStarted = performance.now();
  const openedDetailsCount = openAncestors(target);
  reportFind('ancestors-opened', {
    trigger,
    elapsedMilliseconds: Math.round(performance.now() - stageStarted),
    openedDetailsCount
  });
  await nextAnimationFrame();
  reportFind('after-ancestors-frame', {trigger, openedDetailsCount});

  stageStarted = performance.now();
  target?.scrollIntoView({block:'center', behavior:'auto'});
  reportFind('scroll-requested', {
    trigger,
    elapsedMilliseconds: Math.round(performance.now() - stageStarted)
  });
  await nextAnimationFrame();
  reportFind('after-scroll-frame', {trigger});

  findCount.textContent = `${currentFindMatch + 1} of ${findMatches.length}`;
  reportFind('navigated', {trigger, targetIndex: currentFindMatch});
}

async function runFind() {
  cancelFindSearch(false);
  clearFindHighlights();
  findMatches = [];
  currentFindMatch = -1;
  updateFindNavigationState();
  const query = findInput.value;
  if (!query) {
    findCount.textContent = 'No results';
    return;
  }
  const generation = ++findGeneration;
  findCount.textContent = 'Indexing…';
  updateFindNavigationState();
  const corpus = await buildFindCorpus(findVoicedEnabled, generation);
  if (!corpus || generation !== findGeneration) return;
  const source = findRegexEnabled ? query : escapeRegex(query);
  const pattern = findWordEnabled
    ? `(?<![\\p{L}\\p{N}_])(?:${source})(?![\\p{L}\\p{N}_])`
    : source;
  const workerSource = `onmessage=e=>{try{const r=new RegExp(e.data.pattern,e.data.flags);const a=[];let m;while((m=r.exec(e.data.text))!==null){a.push({start:m.index,length:m[0].length});if(m[0].length===0)r.lastIndex++;}postMessage({matches:a});}catch(error){postMessage({error:String(error.message||error)});}};`;
  const workerUrl = URL.createObjectURL(
    new Blob([workerSource], {type:'text/javascript'}));
  findWorker = new Worker(workerUrl);
  URL.revokeObjectURL(workerUrl);
  findCount.textContent = 'Searching…';
  updateFindNavigationState();
  reportFind('started', {corpusLength: corpus.text.length});
  findWorker.onmessage = event => {
    if (generation !== findGeneration) return;
    if (findSlowTimer) clearTimeout(findSlowTimer);
    findSlowTimer = 0;
    findWorker?.terminate();
    findWorker = null;
    if (event.data.error) {
      findCount.textContent = 'Invalid regex';
      updateFindNavigationState();
      reportFind('invalid-regex', {error: event.data.error});
      return;
    }
    const mapStarted = performance.now();
    const mappedMatches = mapFindMatches(event.data.matches || [], corpus);
    const mapElapsedMilliseconds = Math.round(performance.now() - mapStarted);
    reportFind('results-mapped', {
      rawMatchCount: (event.data.matches || []).length,
      mappedMatchCount: mappedMatches.length,
      elapsedMilliseconds: mapElapsedMilliseconds
    });
    void applyFindMatches(mappedMatches, mapElapsedMilliseconds);
  };
  findWorker.postMessage({
    text: corpus.text,
    pattern,
    flags: findCaseEnabled ? 'gu' : 'giu'
  });
  findSlowTimer = setTimeout(() => {
    if (!findWorker || generation !== findGeneration) return;
    reportFind('slow-prompt');
    if (!confirm('The regular-expression search is still running. Continue waiting?')) {
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
  transcript.focus();
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
    if (findWorker) cancelFindSearch(true);
    else closeFind();
    return;
  }
  if (event.key === 'Enter' && event.ctrlKey) {
    event.preventDefault();
    const match = findMatches[currentFindMatch];
    if (!match || !match.nodeId || match.nodeWordIndex < 0) {
      findCount.textContent = 'Not voiced';
      reportFind('seek-ignored');
      return;
    }
    reportFind('seek-requested', {trigger: event.shiftKey ? 'ctrl-shift-enter' : 'ctrl-enter'});
    chrome.webview.postMessage({
      type:'find-seek',
      nodeId:Number(match.nodeId),
      nodeWordIndex:match.nodeWordIndex
    });
    return;
  }
  if (event.key === 'Enter') {
    event.preventDefault();
    showFindMatch(
      currentFindMatch + (event.shiftKey ? -1 : 1),
      event.shiftKey ? 'shift-enter' : 'enter');
  }
});

chrome.webview.addEventListener('message', event => {
  const data = event.data;
  if (!data) return;
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
