using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
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
  private readonly MarkdownPipeline _pipeline;
  private string? _sessionPath;
  private AgentSource _source;
  private DateTime _lastWriteUtc;
  private long _lastLength = -1;
  private bool _initialized;
  private bool _dark;
  private bool _refreshInProgress;
  private bool _refreshPending;
  private bool _refreshPendingForce;
  private int _renderGeneration;
  private TranscriptSettings _settings = TranscriptSettings.Default;
  private TranscriptPlaybackPosition? _pendingPosition;

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
    _loadingLabel.Text = "Loading transcript view…";
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
    _ = InitializeAsync();
  }

  /// <summary>
  /// Raised when the rendered page receives a transport hotkey.
  /// </summary>
  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;

  /// <summary>
  /// Selects a transcript source and immediately renders its current content.
  /// </summary>
  public void SelectSession(string path, AgentSource source)
  {
    _pendingPosition = null;
    _sessionPath = path;
    _source = source;
    _lastWriteUtc = DateTime.MinValue;
    _lastLength = -1;
    _renderGeneration++;
    ShowLoading("Loading transcript view…");
    _refreshTimer.Start();
    QueueRefresh(force: true);
  }

  /// <summary>
  /// Clears the rendered page when no transcript is selected.
  /// </summary>
  public void ClearSession()
  {
    _pendingPosition = null;
    _sessionPath = null;
    _lastWriteUtc = DateTime.MinValue;
    _lastLength = -1;
    _renderGeneration++;
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
      Color colour = _settings.GetHighlightColour(_dark);
      _ = ExecuteAsync(
        $"applySettings({JsonSerializer.Serialize(ToCss(colour))}," +
        $"{_settings.FadeMilliseconds}," +
        $"{JsonSerializer.Serialize(_settings.FollowSpeech)}," +
        $"{JsonSerializer.Serialize(_dark)});");
      if (_pendingPosition is not null)
      {
        ShowPlaybackPosition(_pendingPosition);
      }
    }
  }

  /// <summary>
  /// Updates the filled or paused transcript marker.
  /// </summary>
  public void ShowPlaybackPosition(TranscriptPlaybackPosition position)
  {
    _pendingPosition = position;
    if (!_initialized)
    {
      return;
    }

    PostPlaybackPosition(position);
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
      _refreshTimer.Stop();
      _refreshTimer.Dispose();
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
    if (_refreshInProgress)
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

    _refreshInProgress = true;
    if (force)
    {
      ShowLoading("Loading transcript view…");
    }
    DiagnosticLog.Write("transcript.render_started", new
    {
      path,
      force,
      info.Length
    });

    try
    {
      AgentSource source = _source;
      TranscriptRenderPayload payload = await Task.Run(() =>
      {
        string markdown = TranscriptMarkdownFormatter.Format(path, source);
        string html = Markdown.ToHtml(markdown, _pipeline);
        IReadOnlyList<TranscriptNodeIdentity> identities =
          TranscriptNodeIdentityMap.Build(path, source);
        return new TranscriptRenderPayload(html, identities);
      });

      if (generation != _renderGeneration ||
          !string.Equals(path, _sessionPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      string script = "replaceTranscript(" +
        JsonSerializer.Serialize(payload.Html) + "," +
        JsonSerializer.Serialize(!force) + "," +
        JsonSerializer.Serialize(payload.Identities) + ");";
      if (_pendingPosition is TranscriptPlaybackPosition pending)
      {
        script += BuildPlaybackScript(pending, ToScriptState(pending.State));
      }
      if (!await ExecuteAsync(script))
      {
        ShowLoading("Unable to load transcript view. See diagnostic log.");
        return;
      }
      _lastWriteUtc = info.LastWriteTimeUtc;
      _lastLength = info.Length;
      HideLoading();
      DiagnosticLog.Write("transcript.render_completed", new
      {
        path,
        force,
        identityCount = payload.Identities.Count
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

  private void PostPlaybackPosition(TranscriptPlaybackPosition position)
  {
    CoreWebView2? core = _webView.CoreWebView2;
    if (!_initialized || _webView.IsDisposed || core is null)
    {
      return;
    }

    try
    {
      core.PostWebMessageAsJson(JsonSerializer.Serialize(new
      {
        type = "playback",
        state = ToScriptState(position.State),
        fragmentText = position.FragmentText,
        wordIndex = position.WordIndex,
        wordText = position.Word,
        nodeId = position.NodeId,
        follow = _settings.FollowSpeech
      }));
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException)
    {
      DiagnosticLog.Write("transcript.playback_message_failed", new
      {
        exception = exception.ToString()
      });
    }
  }


  private string BuildPlaybackScript(
    TranscriptPlaybackPosition position,
    string state)
  {
    return "setPlayback(" +
      JsonSerializer.Serialize(state) + "," +
      JsonSerializer.Serialize(position.FragmentText) + "," +
      position.WordIndex + "," +
      JsonSerializer.Serialize(position.Word) + "," +
      position.NodeId + "," +
      JsonSerializer.Serialize(_settings.FollowSpeech) + ");";
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

  private static Keys KeyNameToKeys(string key)
  {
    return key.ToLowerInvariant() switch
    {
      "u" => Keys.U,
      "h" => Keys.H,
      "j" => Keys.J,
      "k" => Keys.K,
      "l" => Keys.L,
      ";" => Keys.OemSemicolon,
      "o" => Keys.O,
      "'" => Keys.OemQuotes,
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
      content="default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline'; script-src 'nonce-agent-panel-speaker'; object-src 'none'; frame-src 'none'; base-uri 'none'">
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
.word { border-radius: 2px; transition: background-color var(--fade-ms) linear; }
.word.active { background: var(--highlight); transition: none; }
.word.fading { background: transparent; }
.word.paused {
  outline: 2px solid var(--highlight);
  outline-offset: 1px;
  animation: marker-blink 1s steps(1, end) infinite;
}
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
<main id="transcript"></main>
<div id="live-end-marker" aria-label="Next text position"></div>
<script nonce="agent-panel-speaker">
const transcript = document.getElementById('transcript');
const liveEndMarker = document.getElementById('live-end-marker');
let words = [];
let lexicalWords = [];
let currentIndex = -1;
let currentEndIndex = -1;
let currentNode = -1;
let currentFragmentText = null;
let currentFragmentStart = -1;
let currentFragmentEnd = -1;
let currentBoundaryWordIndex = -1;
let fadeMs = 250;
let followSpeech = true;
let knownNodeIds = new Set();
const reportedMappingFailures = new Set();
const reportedPlaybackFailures = new Set();

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

function assignRecordScopes() {
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
    } else if (element.classList.contains('word')) {
      element.dataset.recordNumber = recordNumber;
      element.dataset.sourceId = sourceId;
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

function assignNodeScopes(nodeMap) {
  knownNodeIds = new Set();
  const displayCursors = new Map();
  const lexicalCursors = new Map();
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    knownNodeIds.add(nodeId);
    const recordNumber = String(
      item.RecordNumber ?? item.recordNumber ?? '');
    const sourceId = String(item.SourceId ?? item.sourceId ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const recordKey = sourceId + '\u0000' + recordNumber;
    let displayCursor = displayCursors.get(recordKey) || 0;
    let lexicalCursor = lexicalCursors.get(recordKey) || 0;
    let mappedAny = false;
    for (const segment of segments) {
      const displayTarget = tokenizeDisplay(segment);
      let start = findSequence(
        words,
        displayTarget,
        displayCursor,
        null,
        recordNumber || null,
        sourceId || null);
      if (start < 0 && displayCursor > 0) {
        start = findSequence(
          words,
          displayTarget,
          0,
          null,
          recordNumber || null,
          sourceId || null);
      }
      if (start >= 0) {
        const end = start + displayTarget.length - 1;
        markNodeRange(start, end, nodeId);
        displayCursor = end + 1;
        displayCursors.set(recordKey, displayCursor);
        while (lexicalCursor < lexicalWords.length &&
               Number(lexicalWords[lexicalCursor].dataset.index) <= end) {
          ++lexicalCursor;
        }
        lexicalCursors.set(recordKey, lexicalCursor);
        mappedAny = true;
        continue;
      }

      const lexicalTarget = tokenize(segment);
      let lexicalStart = findSequence(
        lexicalWords,
        lexicalTarget,
        lexicalCursor,
        null,
        recordNumber || null,
        sourceId || null);
      if (lexicalStart < 0 && lexicalCursor > 0) {
        lexicalStart = findSequence(
          lexicalWords,
          lexicalTarget,
          0,
          null,
          recordNumber || null,
          sourceId || null);
      }
      if (lexicalStart >= 0) {
        const lexicalEnd = lexicalStart + lexicalTarget.length - 1;
        const tokenStart = Number(lexicalWords[lexicalStart].dataset.index);
        const tokenEnd = Number(lexicalWords[lexicalEnd].dataset.index);
        markNodeRange(tokenStart, tokenEnd, nodeId);
        lexicalCursor = lexicalEnd + 1;
        lexicalCursors.set(recordKey, lexicalCursor);
        displayCursor = tokenEnd + 1;
        displayCursors.set(recordKey, displayCursor);
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
  let range = chooseNearestRange(
    collectRanges(words, displayTarget, nodeKey, false),
    nodeId);
  if (range) return range;

  const lexicalTarget = tokenize(text);
  range = chooseNearestRange(
    collectRanges(lexicalWords, lexicalTarget, nodeKey, true),
    nodeId);
  if (range) return range;
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
  let parent = element?.parentElement;
  while (parent) {
    if (parent.tagName === 'DETAILS') parent.open = true;
    parent = parent.parentElement;
  }
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

function retireCurrentWord(useFade) {
  if (currentIndex < 0) return;
  const end = Math.max(currentIndex, currentEndIndex);
  for (let index = currentIndex; index <= end; ++index) {
    const previous = words[index];
    if (!previous) continue;
    previous.classList.remove('active');
    if (useFade && fadeMs > 0) {
      previous.style.backgroundColor = 'var(--highlight)';
      requestAnimationFrame(() => {
        previous.classList.add('fading');
        setTimeout(() => {
          previous.classList.remove('fading');
          previous.style.backgroundColor = '';
        }, fadeMs);
      });
    }
  }
  currentIndex = -1;
  currentEndIndex = -1;
}

function clearMarkers() {
  for (const word of words) word.classList.remove('paused');
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
    words[index]?.classList.add(className);
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
  currentBoundaryWordIndex = wordIndex;
  currentNode = nodeId;
  reveal(target);
}

chrome.webview.addEventListener('message', event => {
  const data = event.data;
  if (!data || data.type !== 'playback') return;
  setPlayback(
    data.state,
    data.fragmentText,
    data.wordIndex,
    data.wordText,
    data.nodeId,
    data.follow);
});

window.addEventListener('keydown', event => {
  if (event.ctrlKey || event.metaKey || event.shiftKey) return;
  const keys = new Set(['u','h','j','k','l',';','o',"'"]);
  if (!keys.has(event.key.toLocaleLowerCase())) return;
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
