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
  private readonly Label _failureLabel = new();
  private readonly System.Windows.Forms.Timer _refreshTimer = new();
  private readonly MarkdownPipeline _pipeline;
  private string? _sessionPath;
  private AgentSource _source;
  private DateTime _lastWriteUtc;
  private long _lastLength = -1;
  private bool _initialized;
  private bool _dark;
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
    _failureLabel.Dock = DockStyle.Fill;
    _failureLabel.TextAlign = ContentAlignment.MiddleCenter;
    _failureLabel.Visible = false;
    Controls.Add(_webView);
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
    _refreshTimer.Start();
    RefreshTranscript(force: true);
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
    _refreshTimer.Stop();
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
    RefreshTranscript(force: true);
    if (_pendingPosition is not null)
    {
      ShowPlaybackPosition(_pendingPosition);
    }
  }

  private void RefreshTimerTick(object? sender, EventArgs eventArgs)
  {
    RefreshTranscript(force: false);
  }

  private void RefreshTranscript(bool force)
  {
    string? path = _sessionPath;
    if (!_initialized || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      return;
    }

    try
    {
      var info = new FileInfo(path);
      if (!force && info.LastWriteTimeUtc == _lastWriteUtc &&
          info.Length == _lastLength)
      {
        return;
      }
      _lastWriteUtc = info.LastWriteTimeUtc;
      _lastLength = info.Length;
      string markdown = TranscriptMarkdownFormatter.Format(path, _source);
      string html = Markdown.ToHtml(markdown, _pipeline);
      IReadOnlyList<TranscriptNodeIdentity> identities =
        TranscriptNodeIdentityMap.Build(path, _source);
      string script = "replaceTranscript(" +
        JsonSerializer.Serialize(html) + "," +
        JsonSerializer.Serialize(!force) + "," +
        JsonSerializer.Serialize(identities) + ");";
      if (_pendingPosition is TranscriptPlaybackPosition pending)
      {
        script += BuildPlaybackScript(pending, ToScriptState(pending.State));
      }
      _ = ExecuteAsync(script);
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or
      JsonException or InvalidDataException)
    {
      DiagnosticLog.Write("transcript.render_failed", new
      {
        path,
        exception = exception.ToString()
      });
    }
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
      if (!root.TryGetProperty("type", out JsonElement typeElement) ||
          typeElement.GetString() != "transport" ||
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

  private async Task ExecuteAsync(string script)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (_initialized && !_webView.IsDisposed && core is not null)
      {
        await core.ExecuteScriptAsync(script);
      }
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException)
    {
      DiagnosticLog.Write("transcript.script_failed", new
      {
        exception = exception.ToString()
      });
    }
  }

  private void ShowInitializationFailure(Exception exception)
  {
    DiagnosticLog.Write("transcript.webview_unavailable", new
    {
      exception = exception.ToString()
    });
    _initialized = false;
    _webView.Visible = false;
    _failureLabel.Text =
      "The Microsoft Edge WebView2 Runtime is required to render the " +
      "transcript. The remaining AgentPanelSpeaker features are still " +
      "available.";
    _failureLabel.Visible = true;
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
let currentIndex = -1;
let currentNode = -1;
let currentFragmentText = null;
let currentFragmentStart = -1;
let fadeMs = 250;
let followSpeech = true;
let knownNodeIds = new Set();

function tokenize(text) {
  return (text || '').toLocaleLowerCase().match(/[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*/gu) || [];
}

function wrapWords() {
  words = [];
  const walker = document.createTreeWalker(transcript, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parent = node.parentElement;
      if (!parent || /^(SCRIPT|STYLE)$/.test(parent.tagName)) return NodeFilter.FILTER_REJECT;
      return node.nodeValue.trim() ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
    }
  });
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  const rx = /[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*/gu;
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
      words.push(span);
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
  assignNodeScopes(nodeMap || []);
  currentIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
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

function findSequence(target, startAt, requiredNodeId) {
  if (!target.length || !words.length) return -1;
  const lastStart = words.length - target.length;
  for (let i = Math.max(0, startAt); i <= lastStart; ++i) {
    let equal = true;
    for (let j = 0; j < target.length; ++j) {
      const candidate = words[i + j];
      if (candidate.dataset.normalized !== target[j] ||
          (requiredNodeId !== null &&
           candidate.dataset.nodeId !== requiredNodeId)) {
        equal = false;
        break;
      }
    }
    if (equal) return i;
  }
  return -1;
}

function assignNodeScopes(nodeMap) {
  knownNodeIds = new Set();
  let cursor = 0;
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    knownNodeIds.add(nodeId);
    const segments = item.Segments ?? item.segments ?? [];
    let mappedAny = false;
    for (const segment of segments) {
      const target = tokenize(segment);
      if (!target.length) continue;
      const start = findSequence(target, cursor, null);
      if (start < 0) continue;
      for (let offset = 0; offset < target.length; ++offset) {
        words[start + offset].dataset.nodeId = nodeId;
      }
      cursor = start + target.length;
      mappedAny = true;
    }
    if (!mappedAny) {
      continue;
    }
  }
}

function chooseNearestMatch(matches, nodeId) {
  if (!matches.length) return -1;
  if (currentIndex < 0) return matches[0];
  if (nodeId < currentNode) {
    const before = matches.filter(index => index <= currentIndex);
    return before.length ? before[before.length - 1] : matches[0];
  }
  return matches.reduce((best, candidate) =>
    Math.abs(candidate - currentIndex) < Math.abs(best - currentIndex)
      ? candidate
      : best);
}

function findFragmentStart(text, nodeId) {
  const target = tokenize(text);
  if (!target.length || !words.length) return -1;
  const nodeKey = String(nodeId);
  const scopedMatches = [];
  const globalMatches = [];
  const lastStart = words.length - target.length;
  for (let i = 0; i <= lastStart; ++i) {
    let equal = true;
    let scoped = true;
    for (let j = 0; j < target.length; ++j) {
      const candidate = words[i + j];
      if (candidate.dataset.normalized !== target[j]) {
        equal = false;
        break;
      }
      if (candidate.dataset.nodeId !== nodeKey) scoped = false;
    }
    if (equal) {
      globalMatches.push(i);
      if (scoped) scopedMatches.push(i);
    }
  }
  const scoped = chooseNearestMatch(scopedMatches, nodeId);
  if (scoped >= 0) return scoped;
  if (knownNodeIds.has(nodeKey)) return -1;
  return chooseNearestMatch(globalMatches, nodeId);
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
  if (currentIndex < 0 || !words[currentIndex]) return;
  const previous = words[currentIndex];
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
  currentIndex = -1;
}

function clearMarkers() {
  for (const word of words) word.classList.remove('paused');
  liveEndMarker.style.display = 'none';
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
  let start = currentFragmentStart;
  if (currentFragmentText !== fragmentText || currentNode !== nodeId ||
      start < 0) {
    start = findFragmentStart(fragmentText, nodeId);
    if (start < 0) return;
    currentFragmentText = fragmentText;
    currentFragmentStart = start;
  }
  const targetIndex = Math.min(words.length - 1, start + Math.max(0, wordIndex));
  const target = words[targetIndex];
  openAncestors(target);
  if (state === 'paused') {
    retireCurrentWord(false);
    target.classList.add('paused');
  } else {
    if (currentIndex >= 0 && currentIndex !== targetIndex) {
      retireCurrentWord(true);
    }
    target.classList.add('active');
  }
  currentIndex = targetIndex;
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
