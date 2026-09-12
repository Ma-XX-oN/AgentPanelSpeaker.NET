from pathlib import Path
import re


def read(path: str) -> str:
  return Path(path).read_text(encoding='utf-8')


def write(path: str, text: str) -> None:
  Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(path: str, old: str, new: str) -> None:
  text = read(path)
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one replacement target, found {count}')
  write(path, text.replace(old, new))


def replace_regex(path: str, pattern: str, replacement: str) -> None:
  text = read(path)
  updated, count = re.subn(
    pattern,
    lambda _: replacement,
    text,
    count=1,
    flags=re.S)
  if count != 1:
    raise RuntimeError(f'{path}: expected one regex replacement, found {count}')
  write(path, updated)


# Display projection now owns a retained Core session so virtualization can ask
# Core which unit owns an off-window playback word. No consumer word->unit map.
formatter = r'''using Markdig;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Obtains completed canonical HTML units directly from AIConversationCore for
/// transcript display and virtualization.
/// </summary>
internal static class TranscriptPresentationDomFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptPresentationDomFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Projects one selected provider JSONL session through a retained Core
  /// session and returns Core-rendered HTML units without rebuilding semantic
  /// HTML in C#.
  /// </summary>
  public static TranscriptPresentationDomResult Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(pipeline);

    IReadOnlyList<string> jsonLines = ReadJsonLines(path, cancellationToken);
    if (jsonLines.Count == 0)
    {
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        string.Empty,
        Array.Empty<CanonicalHtmlUnitProjection>(),
        null);
    }

    AIConversationCoreRetainedSession? retained = null;
    try
    {
      retained = CoreClient.CreateRetainedSession(
        source,
        jsonLines,
        new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));
      cancellationToken.ThrowIfCancellationRequested();
      AIConversationProjection projection = retained.Projection;

      CanonicalHtmlUnitProjection[] units = projection.HtmlUnits ??
        throw new InvalidOperationException(
          "AIConversationCore projection omitted canonical HTML units.");
      foreach (CanonicalHtmlUnitProjection unit in units)
      {
        if (!unit.Atomic ||
            !string.Equals(unit.Kind, "turn", StringComparison.Ordinal))
        {
          throw new InvalidOperationException(
            "AIConversationCore returned an unsupported HTML virtualization unit.");
        }
      }

      string html = string.Concat(units.Select(unit => unit.Html));
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        html,
        units,
        retained.Id);
    }
    catch
    {
      if (retained is not null)
      {
        CoreClient.CloseRetainedSession(retained.Id);
      }
      throw;
    }
  }

  /// <summary>
  /// Resolves one transcript-global Core word in the retained display session.
  /// </summary>
  internal static AIConversationCoreWordLocation? LocateRetainedWord(
    string sessionId,
    long wordId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    if (wordId < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(wordId));
    }
    return CoreClient.LocateRetainedWord(
      sessionId,
      wordId,
      new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));
  }

  /// <summary>
  /// Releases one retained display session when its rendered projection is no
  /// longer the active transcript inventory.
  /// </summary>
  internal static void CloseRetainedSession(string? sessionId)
  {
    if (!string.IsNullOrWhiteSpace(sessionId))
    {
      CoreClient.CloseRetainedSession(sessionId);
    }
  }

  private static IReadOnlyList<string> ReadJsonLines(
    string path,
    CancellationToken cancellationToken)
  {
    var jsonLines = new List<string>();
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is string line)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }
      using JsonDocument document = JsonDocument.Parse(line);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new JsonException("A transcript JSONL record must be an object.");
      }
      jsonLines.Add(line);
    }
    return jsonLines;
  }
}

/// <summary>
/// Completed Core HTML plus its retained Core session identity. The legacy
/// DOM-node slot remains only for API compatibility while callers migrate to
/// Core-owned units.
/// </summary>
internal sealed record TranscriptPresentationDomResult(
  IReadOnlyList<TranscriptDomNode> Nodes,
  string Html,
  IReadOnlyList<CanonicalHtmlUnitProjection> Units,
  string? CoreSessionId = null);

/// <summary>
/// Legacy browser-DOM instruction shape retained for compatibility. Production
/// transcript rendering no longer constructs semantic nodes through this type.
/// </summary>
internal sealed record TranscriptDomNode(
  string Kind,
  string? Tag,
  IReadOnlyDictionary<string, string>? Attributes,
  string? Text,
  string? Html,
  IReadOnlyList<TranscriptDomNode>? Children);
'''
write('AgentPanelSpeaker/TranscriptPresentationDomFormatter.cs', formatter)

path = 'AgentPanelSpeaker/TranscriptView.cs'

replace_once(
  path,
  '''  private TranscriptVirtualDocument? _virtualDocument;\n  private IReadOnlyList<TranscriptNodeIdentity> _identities =\n''',
  '''  private TranscriptVirtualDocument? _virtualDocument;\n  private string? _coreSessionId;\n  private IReadOnlyList<TranscriptNodeIdentity> _identities =\n''')

replace_once(
  path,
  '''    _lastLength = -1;\n    _renderGeneration++;\n    CancelActiveRender();\n    ShowLoading(GetLoadingText());\n''',
  '''    _lastLength = -1;\n    _renderGeneration++;\n    CancelActiveRender();\n    ReleaseCoreSession();\n    ShowLoading(GetLoadingText());\n''')

replace_once(
  path,
  '''    _lastLength = -1;\n    _renderGeneration++;\n    CancelActiveRender();\n    _refreshTimer.Stop();\n''',
  '''    _lastLength = -1;\n    _renderGeneration++;\n    CancelActiveRender();\n    ReleaseCoreSession();\n    _refreshTimer.Stop();\n''')

replace_once(
  path,
  '''      _renderGeneration++;\n      CancelActiveRender();\n      CancelSearchIndexBuild();\n      _refreshTimer.Stop();\n''',
  '''      _renderGeneration++;\n      CancelActiveRender();\n      CancelSearchIndexBuild();\n      ReleaseCoreSession();\n      _refreshTimer.Stop();\n''')

replace_regex(
  path,
  r'''  /// <summary>\n  /// Updates the filled or paused transcript marker through a low-latency,\n  /// one-way WebView message\.\n  /// </summary>\n  public void ShowPlaybackPosition\(TranscriptPlaybackPosition position\)\n  \{.*?\n  \}\n\n  private void QueueSettingsApply''',
  '''  /// <summary>\n  /// Updates the filled or paused transcript marker through a low-latency,\n  /// one-way WebView message. Core-backed transcript positions cross this\n  /// boundary only as immutable Core word IDs.\n  /// </summary>\n  public void ShowPlaybackPosition(TranscriptPlaybackPosition position)\n  {\n    _pendingPosition = position;\n    bool contentPosition =\n      position.State is TranscriptPlaybackState.Speaking or\n        TranscriptPlaybackState.Paused;\n    if (contentPosition &&\n        (position.WordId is > 0 || position.NodeId > 0))\n    {\n      _lastLocatedContentPosition = position;\n    }\n    if (!_initialized || _refreshInProgress)\n    {\n      return;\n    }\n\n    // A Core-backed word is resolved directly by the WebView when materialized.\n    // If absent, the WebView requests window-for-word and the host asks Core for\n    // the containing unit. Never fall back to NodeId/text for this path.\n    if (position.WordId is > 0)\n    {\n      PostPlaybackPosition(position);\n      return;\n    }\n\n    // Non-canonical synthesized narration retains the pre-migration node path\n    // until issue #76 completes its separate impact analysis.\n    TranscriptNodeIdentity? identity = _identities.FirstOrDefault(\n      item => item.NodeId == position.NodeId);\n    if (_settings.FollowSpeech && identity is not null &&\n        _virtualDocument is TranscriptVirtualDocument document &&\n        document.TryGetIndex(identity.RecordNumber, out int index) &&\n        (index < _windowStartIndex || index > _windowEndIndex))\n    {\n      _ = RenderWindowForRecordAsync(\n        identity.RecordNumber,\n        "playback-position",\n        matchIndex: null);\n      return;\n    }\n\n    PostPlaybackPosition(position);\n  }\n\n  private void QueueSettingsApply''')

replace_once(
  path,
  '''      position.BoundaryTimestamp,\n      postedTimestamp = Stopwatch.GetTimestamp()\n''',
  '''      position.BoundaryTimestamp,\n      position.WordId,\n      postedTimestamp = Stopwatch.GetTimestamp()\n''')

replace_once(
  path,
  '''      nodeId = position.NodeId,\n      characterPosition = position.CharacterPosition,\n''',
  '''      nodeId = position.NodeId,\n      wordId = position.WordId,\n      characterPosition = position.CharacterPosition,\n''')

# Retain/adopt the exact Core session that produced the active HTML units.
replace_once(
  path,
  '''    var renderTimer = Stopwatch.StartNew();\n    string structureProbeId = $"{generation}:{Guid.NewGuid():N}";\n\n    try\n''',
  '''    var renderTimer = Stopwatch.StartNew();\n    string structureProbeId = $"{generation}:{Guid.NewGuid():N}";\n    string? preparedCoreSessionId = null;\n    bool coreSessionAdopted = false;\n\n    try\n''')

replace_once(
  path,
  '''        token.ThrowIfCancellationRequested();\n        string html = presentation.Html;\n        TranscriptStructureSnapshot rendererStructure =\n          TranscriptStructureProbe.CaptureHtml(\n            structureProbeId,\n            "dom-model-html",\n            html);\n        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(\n          presentation.Units);\n        document.SetShowRolledBackHistory(\n          _settings.ShowRolledBackHistory);\n        document.SetLayoutGeneration(_layoutGeneration);\n        return new TranscriptRenderPayload(\n          document,\n          identities,\n          html,\n          rendererStructure,\n          presentation.Nodes);\n''',
  '''        try\n        {\n          token.ThrowIfCancellationRequested();\n          string html = presentation.Html;\n          TranscriptStructureSnapshot rendererStructure =\n            TranscriptStructureProbe.CaptureHtml(\n              structureProbeId,\n              "dom-model-html",\n              html);\n          TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(\n            presentation.Units);\n          document.SetShowRolledBackHistory(\n            _settings.ShowRolledBackHistory);\n          document.SetLayoutGeneration(_layoutGeneration);\n          return new TranscriptRenderPayload(\n            document,\n            identities,\n            html,\n            rendererStructure,\n            presentation.Nodes,\n            presentation.CoreSessionId);\n        }\n        catch\n        {\n          TranscriptPresentationDomFormatter.CloseRetainedSession(\n            presentation.CoreSessionId);\n          throw;\n        }\n''')

replace_once(
  path,
  '''      }, token);\n\n      long preparationMilliseconds = renderTimer.ElapsedMilliseconds;\n      cancellation.Token.ThrowIfCancellationRequested();\n''',
  '''      }, token);\n\n      preparedCoreSessionId = payload.CoreSessionId;\n      long preparationMilliseconds = renderTimer.ElapsedMilliseconds;\n      cancellation.Token.ThrowIfCancellationRequested();\n''')

replace_once(
  path,
  '''      _virtualDocument = payload.Document;\n      _identities = payload.Identities;\n''',
  '''      ReplaceCoreSession(preparedCoreSessionId);\n      coreSessionAdopted = true;\n      _virtualDocument = payload.Document;\n      _identities = payload.Identities;\n''')

# Core-backed playback must not use the legacy NodeId pre-positioning path on
# refresh. The browser will request the exact word if its owner is off-window.
replace_once(
  path,
  '''      if (_settings.FollowSpeech &&\n          _pendingPosition is TranscriptPlaybackPosition latestPosition &&\n          TryResolvePositionIndex(\n''',
  '''      if (_settings.FollowSpeech &&\n          _pendingPosition is TranscriptPlaybackPosition latestPosition &&\n          latestPosition.WordId is null &&\n          TryResolvePositionIndex(\n''')

replace_once(
  path,
  '''      else if (_settings.FollowSpeech &&\n          _lastLocatedContentPosition is TranscriptPlaybackPosition located &&\n          TryResolvePositionIndex(\n''',
  '''      else if (_settings.FollowSpeech &&\n          _lastLocatedContentPosition is TranscriptPlaybackPosition located &&\n          located.WordId is null &&\n          TryResolvePositionIndex(\n''')

replace_once(
  path,
  '''      if (_lastLocatedContentPosition is TranscriptPlaybackPosition locatedPosition &&\n          TryResolvePositionIndex(\n            payload.Document,\n            payload.Identities,\n            locatedPosition,\n            out _))\n      {\n        PostPlaybackPosition(locatedPosition);\n      }\n''',
  '''      if (_lastLocatedContentPosition is TranscriptPlaybackPosition locatedPosition &&\n          (locatedPosition.WordId is > 0 ||\n           TryResolvePositionIndex(\n             payload.Document,\n             payload.Identities,\n             locatedPosition,\n             out _)))\n      {\n        PostPlaybackPosition(locatedPosition);\n      }\n''')

replace_once(
  path,
  '''    finally\n    {\n      if (ReferenceEquals(_renderCancellation, cancellation))\n''',
  '''    finally\n    {\n      if (!coreSessionAdopted)\n      {\n        TranscriptPresentationDomFormatter.CloseRetainedSession(\n          preparedCoreSessionId);\n      }\n      if (ReferenceEquals(_renderCancellation, cancellation))\n''')

# Host receives only the Core word ID for off-window playback and asks the
# retained Core session which unit owns it.
replace_once(
  path,
  '''      if (type == "window-for-node")\n      {\n''',
  '''      if (type == "window-for-word")\n      {\n        long? wordId = ReadOptionalInt64(root, "wordId");\n        if (wordId is > 0)\n        {\n          _ = RenderWindowForWordAsync(wordId.Value, "playback-word");\n        }\n        return;\n      }\n      if (type == "window-for-node")\n      {\n''')

render_word_method = r'''  /// <summary>
  /// Materializes the Core-owned virtual unit containing one off-window
  /// transcript word. Core is the only word-to-unit resolver.
  /// </summary>
  private async Task RenderWindowForWordAsync(long wordId, string reason)
  {
    if (wordId < 1 ||
        _virtualDocument is not TranscriptVirtualDocument document ||
        string.IsNullOrWhiteSpace(_coreSessionId))
    {
      return;
    }

    string sessionId = _coreSessionId;
    AIConversationCoreWordLocation? location;
    try
    {
      location = await Task.Run(() =>
        TranscriptPresentationDomFormatter.LocateRetainedWord(
          sessionId,
          wordId));
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ArgumentException)
    {
      DiagnosticLog.Write("transcript.playback_word_lookup_failed", new
      {
        wordId,
        reason,
        exception = exception.ToString()
      });
      return;
    }

    if (!ReferenceEquals(document, _virtualDocument) ||
        !string.Equals(sessionId, _coreSessionId, StringComparison.Ordinal))
    {
      return;
    }
    if (location is null)
    {
      DiagnosticLog.Write("transcript.playback_word_missing", new
      {
        wordId,
        reason
      });
      return;
    }
    if (!document.TryGetUnitIndex(location.Unit.Id, out int index))
    {
      DiagnosticLog.Write("transcript.playback_word_unit_missing", new
      {
        wordId,
        unitId = location.Unit.Id,
        reason
      });
      return;
    }
    if (index >= _windowStartIndex && index <= _windowEndIndex)
    {
      DiagnosticLog.Write("transcript.playback_word_dom_missing", new
      {
        wordId,
        unitId = location.Unit.Id,
        index,
        reason
      });
      return;
    }

    await RenderWindowForIndexAsync(
      index,
      reason,
      anchorRecordNumber: null,
      anchorOffset: null);
  }

  private void ReplaceCoreSession(string? sessionId)
  {
    if (string.Equals(_coreSessionId, sessionId, StringComparison.Ordinal))
    {
      return;
    }
    string? previous = _coreSessionId;
    _coreSessionId = sessionId;
    TranscriptPresentationDomFormatter.CloseRetainedSession(previous);
  }

  private void ReleaseCoreSession()
  {
    ReplaceCoreSession(null);
  }

'''
replace_once(
  path,
  '''  private Task RenderWindowForNodeAsync(long nodeId, string reason)\n''',
  render_word_method + '''  private Task RenderWindowForNodeAsync(long nodeId, string reason)\n''')

replace_once(
  path,
  '''  private sealed record TranscriptRenderPayload(\n    TranscriptVirtualDocument Document,\n    IReadOnlyList<TranscriptNodeIdentity> Identities,\n    string SearchHtml,\n    TranscriptStructureSnapshot RendererStructure,\n    IReadOnlyList<TranscriptDomNode> DomNodes);\n''',
  '''  private sealed record TranscriptRenderPayload(\n    TranscriptVirtualDocument Document,\n    IReadOnlyList<TranscriptNodeIdentity> Identities,\n    string SearchHtml,\n    TranscriptStructureSnapshot RendererStructure,\n    IReadOnlyList<TranscriptDomNode> DomNodes,\n    string? CoreSessionId);\n''')

# Browser styling applies directly to Core word owners/pieces, not dynamically
# reconstructed eligibility classes.
replace_once(
  path,
  '''.word { border-radius: 2px; }\n.word.active { background: var(--highlight); }\n.word.paused {\n''',
  '''.word, [id^="word-"], [data-word-id] { border-radius: 2px; }\n.word.active, [id^="word-"].active, [data-word-id].active {\n  background: var(--highlight);\n}\n.word.paused, [id^="word-"].paused, [data-word-id].paused {\n''')

replace_once(
  path,
  '''let currentBoundaryWordIndex = -1;\nlet currentSpeechListItem = null;\nlet fadeMs = 250;\n''',
  '''let currentBoundaryWordIndex = -1;\nlet currentSpeechListItem = null;\nlet currentCanonicalPlaybackWordId = 0;\nlet currentCanonicalPlaybackElements = [];\nlet requestedPlaybackWordId = 0;\nlet fadeMs = 250;\n''')

# Follow mode uses the canonical playback owner when available.
replace_once(
  path,
  '''  if (followSpeech && currentIndex >= 0) {\n    const target = words[currentIndex];\n    if (target) {\n      programmaticScrollUntil = performance.now() + 1500;\n      target.scrollIntoView({block:'center', behavior:'smooth'});\n    }\n  }\n''',
  '''  if (followSpeech) {\n    const target = currentCanonicalPlaybackElements[0] ||\n      (currentIndex >= 0 ? words[currentIndex] : null);\n    if (target) {\n      programmaticScrollUntil = performance.now() + 1500;\n      target.scrollIntoView({block:'center', behavior:'smooth'});\n    } else if (currentCanonicalPlaybackWordId > 0) {\n      requestCanonicalPlaybackWindow(currentCanonicalPlaybackWordId);\n    }\n  }\n''')

helpers = r'''function canonicalPlaybackElements(wordId) {
  const id = Number(wordId);
  if (!Number.isSafeInteger(id) || id < 1) return [];
  const result = [];
  const owner = document.getElementById('word-' + id);
  if (owner) result.push(owner);
  const selector = '[data-word-id="' + CSS.escape(String(id)) + '"]';
  for (const piece of transcript.querySelectorAll(selector)) {
    if (!result.includes(piece)) result.push(piece);
  }
  return result;
}

function retireCanonicalPlayback(useFade) {
  if (!currentCanonicalPlaybackElements.length) {
    currentCanonicalPlaybackWordId = 0;
    return;
  }
  const highlight = getComputedStyle(document.documentElement)
    .getPropertyValue('--highlight').trim();
  for (const element of currentCanonicalPlaybackElements) {
    element.classList.remove('active', 'paused');
    cancelFade(element);
    if (useFade && fadeMs > 0) {
      const animation = element.animate(
        [
          {backgroundColor: highlight},
          {backgroundColor: 'transparent'}
        ],
        {duration: fadeMs, easing: 'linear'});
      fadingAnimations.set(element, animation);
      animation.onfinish = () => fadingAnimations.delete(element);
      animation.oncancel = () => fadingAnimations.delete(element);
    }
  }
  currentCanonicalPlaybackElements = [];
  currentCanonicalPlaybackWordId = 0;
}

function requestCanonicalPlaybackWindow(wordId) {
  const id = Number(wordId);
  if (!followSpeech || !Number.isSafeInteger(id) || id < 1 ||
      requestedPlaybackWordId === id) {
    return;
  }
  requestedPlaybackWordId = id;
  chrome.webview.postMessage({type:'window-for-word', wordId:id});
}

function setCanonicalPlayback(state, wordId) {
  retireCurrentWord(true);
  retireCanonicalPlayback(true);
  currentCanonicalPlaybackWordId = wordId;
  const elements = canonicalPlaybackElements(wordId);
  if (!elements.length) {
    requestCanonicalPlaybackWindow(wordId);
    return;
  }
  requestedPlaybackWordId = 0;
  currentCanonicalPlaybackElements = elements;
  const className = state === 'paused' ? 'paused' : 'active';
  for (const element of elements) {
    cancelFade(element);
    element.classList.add(className);
  }
  const target = elements[0];
  maybePrefetchVoiceCursor(target);
  reveal(target);
}

'''
replace_once(
  path,
  '''function restoreRetainedPlaybackProjection() {\n''',
  helpers + '''function restoreRetainedPlaybackProjection() {\n''')

replace_once(
  path,
  '''    retainedPlayback.wordText,\n    retainedPlayback.nodeId,\n    false);\n''',
  '''    retainedPlayback.wordText,\n    retainedPlayback.nodeId,\n    false,\n    retainedPlayback.wordId);\n''')

replace_once(
  path,
  '''function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {\n''',
  '''function setPlayback(\n  state,\n  fragmentText,\n  wordIndex,\n  wordText,\n  nodeId,\n  follow,\n  wordId = null) {\n''')

replace_once(
  path,
  '''  if (state === 'none') {\n    retireCurrentWord(true);\n    return;\n  }\n''',
  '''  if (state === 'none') {\n    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n    return;\n  }\n''')

replace_once(
  path,
  '''  if (state === 'waiting-end' || state === 'paused-end') {\n    retireCurrentWord(true);\n''',
  '''  if (state === 'waiting-end' || state === 'paused-end') {\n    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n''')

replace_once(
  path,
  '''  const fragmentChanged = currentFragmentText !== fragmentText ||\n''',
  '''  const canonicalWordIdValue = Number(wordId ?? 0);\n  if (Number.isSafeInteger(canonicalWordIdValue) && canonicalWordIdValue > 0) {\n    setCanonicalPlayback(state, canonicalWordIdValue);\n    return;\n  }\n\n  // Legacy non-canonical narration path. Core-backed playback returned above\n  // and is never allowed to fall back to node/text matching.\n  retireCanonicalPlayback(true);\n  const fragmentChanged = currentFragmentText !== fragmentText ||\n''')

# A rematerialized window may satisfy a previously requested Core word. Allow a
# fresh request if the replaced window still does not contain it.
text = read(path)
needle = '  restoreRetainedPlaybackProjection();'
count = text.count(needle)
if count != 2:
  raise RuntimeError(f'{path}: expected two retained playback restore sites, found {count}')
text = text.replace(
  needle,
  '  requestedPlaybackWordId = 0;\n  restoreRetainedPlaybackProjection();')
write(path, text)

replace_once(
  path,
  '''  retainedPlayback = {\n    state:data.state,\n    fragmentText:data.fragmentText,\n    wordIndex:data.wordIndex,\n    wordText:data.wordText,\n    nodeId:data.nodeId\n  };\n''',
  '''  retainedPlayback = {\n    state:data.state,\n    fragmentText:data.fragmentText,\n    wordIndex:data.wordIndex,\n    wordText:data.wordText,\n    nodeId:data.nodeId,\n    wordId:data.wordId\n  };\n''')

replace_once(
  path,
  '''    data.wordText,\n    data.nodeId,\n    data.follow);\n''',
  '''    data.wordText,\n    data.nodeId,\n    data.follow,\n    data.wordId);\n''')

replace_once(
  path,
  '''    nodeId: data.nodeId,\n    wordIndex: data.wordIndex,\n''',
  '''    nodeId: data.nodeId,\n    wordId: data.wordId,\n    wordIndex: data.wordIndex,\n''')

replace_once(
  path,
  '''followToggle.addEventListener('click', () => {\n  setFollowSpeech(!followSpeech, true);\n  if (followSpeech && currentNode >= 0 && currentIndex < 0) {\n    chrome.webview.postMessage({type:'window-for-node', nodeId:currentNode});\n  }\n});\n''',
  '''followToggle.addEventListener('click', () => {\n  setFollowSpeech(!followSpeech, true);\n  if (followSpeech && currentCanonicalPlaybackWordId > 0 &&\n      canonicalPlaybackElements(currentCanonicalPlaybackWordId).length === 0) {\n    requestCanonicalPlaybackWindow(currentCanonicalPlaybackWordId);\n  } else if (followSpeech && currentNode >= 0 && currentIndex < 0) {\n    chrome.webview.postMessage({type:'window-for-node', nodeId:currentNode});\n  }\n});\n''')
