from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def replace_section(text: str, start: str, end: str, replacement: str, label: str) -> str:
  start_index = text.find(start)
  if start_index < 0:
    raise RuntimeError(f"{label}: start marker not found")
  end_index = text.find(end, start_index)
  if end_index < 0:
    raise RuntimeError(f"{label}: end marker not found")
  return text[:start_index] + replacement + text[end_index:]


virtual_path = Path("AgentPanelSpeaker/TranscriptVirtualDocument.cs")
virtual = virtual_path.read_text(encoding="utf-8")
virtual = replace_once(
  virtual,
  '''  internal const double MinimumWindowViewportHeights = 5.0;
  internal const double EdgeTriggerViewportHeights = 2.0;
  internal const double DefaultViewportHeight = 700.0;''',
  '''  internal const double MinimumWindowViewportHeights = 5.0;
  internal const double EdgeTriggerViewportHeights = 2.0;
  internal const double ShiftPrefetchViewportHeights = 4.0;
  internal const double DefaultViewportHeight = 700.0;''',
  "virtual-window constants")
virtual = replace_once(
  virtual,
  '''    double extensionHeight = normalizedViewportHeight *
      EdgeTriggerViewportHeights;''',
  '''    double extensionHeight = normalizedViewportHeight *
      ShiftPrefetchViewportHeights;''',
  "directional shift prefetch depth")
virtual_path.write_text(virtual, encoding="utf-8")


view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
view = view_path.read_text(encoding="utf-8")
view = replace_once(
  view,
  '''  private const int StartupCanonicalEndPercent = 89;
  private const int StartupSearchPercent = 94;
  private const int StartupRenderPercent = 99;''',
  '''  private const int StartupCanonicalEndPercent = 98;
  private const int StartupRenderPercent = 99;''',
  "startup percentage constants")
view = replace_once(
  view,
  '''  private CancellationTokenSource? _renderCancellation;
  private TranscriptSettings _settings = TranscriptSettings.Default;''',
  '''  private CancellationTokenSource? _renderCancellation;
  private CancellationTokenSource? _searchIndexCancellation;
  private TranscriptSettings _settings = TranscriptSettings.Default;''',
  "search cancellation field")
if view.count("_findWindowRenderGate") != 3:
  raise RuntimeError(
    f"window render gate rename: expected 3 matches, found {view.count('_findWindowRenderGate')}")
view = view.replace("_findWindowRenderGate", "_windowRenderGate")
view = replace_once(
  view,
  '''    _pendingPosition = null;
    _lastLocatedContentPosition = null;
    _searchIndex = null;''',
  '''    _pendingPosition = null;
    _lastLocatedContentPosition = null;
    CancelSearchIndexBuild();
    _searchIndex = null;''',
  "select-session search cancellation")
view = replace_once(
  view,
  '''  public void ClearSession()
  {
    _pendingPosition = null;
    _searchIndex = null;''',
  '''  public void ClearSession()
  {
    _pendingPosition = null;
    CancelSearchIndexBuild();
    _searchIndex = null;''',
  "clear-session search cancellation")
view = replace_once(
  view,
  '''      _renderGeneration++;
      CancelActiveRender();
      _refreshTimer.Stop();''',
  '''      _renderGeneration++;
      CancelActiveRender();
      CancelSearchIndexBuild();
      _refreshTimer.Stop();''',
  "dispose search cancellation")
view = replace_once(
  view,
  '''    _renderCancellation = cancellation;
    _activeRenderGeneration = generation;
    _refreshInProgress = true;
    if (force)''',
  '''    _renderCancellation = cancellation;
    _activeRenderGeneration = generation;
    _refreshInProgress = true;
    CancelSearchIndexBuild();
    _searchIndex = null;
    if (force)''',
  "refresh search reset")
view = replace_section(
  view,
  '''    IProgress<int>? startupPhaseProgress = force
      ? new Progress<int>(phase =>''',
  '''    DiagnosticLog.Write("transcript.render_started", new''',
  '''    DiagnosticLog.Write("transcript.render_started", new''',
  "remove blocking-search startup phase")
view = replace_once(
  view,
  '''        token.ThrowIfCancellationRequested();
        startupPhaseProgress?.Report(2);
        string html = presentation.Html;''',
  '''        token.ThrowIfCancellationRequested();
        string html = presentation.Html;''',
  "remove search phase report")
view = replace_once(
  view,
  '''        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
          html,
          identities,
          token);
        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
          presentation.Units);''',
  '''        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
          presentation.Units);''',
  "defer search-index construction")
view = replace_once(
  view,
  '''        return new TranscriptRenderPayload(
          document,
          identities,
          searchIndex,
          rendererStructure,
          presentation.Nodes);''',
  '''        return new TranscriptRenderPayload(
          document,
          identities,
          html,
          rendererStructure,
          presentation.Nodes);''',
  "carry HTML for deferred search")
view = replace_once(
  view,
  '''      _virtualDocument = payload.Document;
      _identities = payload.Identities;
      _searchIndex = payload.SearchIndex;
      int focalIndex = ResolveInitialWindowIndex(payload.Document, payload.Identities);''',
  '''      _virtualDocument = payload.Document;
      _identities = payload.Identities;
      int focalIndex = ResolveInitialWindowIndex(payload.Document, payload.Identities);''',
  "do not require search before initial window")
view = replace_once(
  view,
  '''      long domMilliseconds =
        renderTimer.ElapsedMilliseconds - domStartMilliseconds;
      StartPendingFindRequest();
      _lastWriteUtc = info.LastWriteTimeUtc;''',
  '''      long domMilliseconds =
        renderTimer.ElapsedMilliseconds - domStartMilliseconds;
      _lastWriteUtc = info.LastWriteTimeUtc;''',
  "defer pending find until index ready")
view = replace_once(
  view,
  '''      HideLoading();
      _restoredFromSettings = false;
      QueueSettingsApply(immediate: true);''',
  '''      HideLoading();
      BeginDeferredSearchIndexBuild(
        path,
        generation,
        payload.SearchHtml,
        payload.Identities);
      _restoredFromSettings = false;
      QueueSettingsApply(immediate: true);''',
  "start deferred search after first visible render")

search_methods = r'''  /// <summary>
  /// Starts full-file search indexing only after the initial visible window is
  /// installed.  Search readiness is deliberately independent of first paint.
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

'''
view = replace_once(
  view,
  '''  private void CancelActiveRender()
  {''',
  search_methods + '''  private void CancelActiveRender()
  {''',
  "deferred search methods")
view = replace_once(
  view,
  '''  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    TranscriptSearchIndex SearchIndex,
    TranscriptStructureSnapshot RendererStructure,''',
  '''  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    string SearchHtml,
    TranscriptStructureSnapshot RendererStructure,''',
  "render payload deferred search HTML")
view = replace_once(
  view,
  '''            ReadOptionalInt32(root, "visibleStartIndex"),
            ReadOptionalInt32(root, "visibleEndIndex"));''',
  '''            ReadOptionalInt32(root, "visibleStartIndex"),
            ReadOptionalInt32(root, "visibleEndIndex"),
            ReadOptionalInt32(root, "sourceStartIndex"),
            ReadOptionalInt32(root, "sourceEndIndex"));''',
  "window-shift source range")
view = replace_once(
  view,
  '''      protectedStartIndex: null,
      protectedEndIndex: null);''',
  '''      protectedStartIndex: null,
      protectedEndIndex: null,
      sourceStartIndex: null,
      sourceEndIndex: null);''',
  "index wrapper source range")

start_marker = '''  private async Task RenderWindowForIndexCoreAsync(
    int focalIndex,'''
end_marker = '''  private Task RenderWindowForNodeAsync(long nodeId, string reason)'''
new_method = r'''  private async Task RenderWindowForIndexCoreAsync(
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

'''
view = replace_section(
  view,
  start_marker,
  end_marker,
  new_method,
  "serialized stale-aware index render")
view = replace_once(
  view,
  '''    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    ...visibleRange,''',
  '''    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    sourceStartIndex:windowStartIndex,
    sourceEndIndex:windowEndIndex,
    ...visibleRange,''',
  "browser source window generation")
view = replace_once(
  view,
  '''function setAvailableWordMaps(wordMap) {
  availableWordMapsByRecord = new Map();
  for (const record of wordMap || []) {
    const recordNumber = String(record.RecordNumber ?? record.recordNumber ?? '');
    const sourceId = String(record.SourceId ?? record.sourceId ?? '');
    availableWordMapsByRecord.set(
      makeRecordKey(recordNumber, sourceId), record);
  }
}

function ensureCoreOrdinalSpeechMaps()''',
  '''function setAvailableWordMaps(wordMap) {
  availableWordMapsByRecord = new Map();
  for (const record of wordMap || []) {
    const recordNumber = String(record.RecordNumber ?? record.recordNumber ?? '');
    const sourceId = String(record.SourceId ?? record.sourceId ?? '');
    availableWordMapsByRecord.set(
      makeRecordKey(recordNumber, sourceId), record);
  }
}

function installSearchWordMaps(wordMap) {
  setAvailableWordMaps(wordMap || []);
  assignStableWordScopes(wordMap || []);
}

function ensureCoreOrdinalSpeechMaps()''',
  "install deferred search maps in existing DOM")
view_path.write_text(view, encoding="utf-8")
