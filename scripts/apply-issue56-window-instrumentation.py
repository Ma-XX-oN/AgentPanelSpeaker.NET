from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

def replace_once(old: str, new: str, label: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise SystemExit(f'{label}: expected exactly one match, found {count}')
  text = text.replace(old, new, 1)

replace_once(
'''  private long _latestFindWindowNavigationGeneration;\n  private readonly SemaphoreSlim _windowRenderGate = new(1, 1);\n\n  private sealed record PendingFindRequest(\n''',
'''  private long _latestFindWindowNavigationGeneration;\n  private long _windowRenderTransactionSequence;\n  private readonly SemaphoreSlim _windowRenderGate = new(1, 1);\n\n  private sealed class WindowScriptBuildMetrics\n  {\n    public int NodeCount { get; set; }\n    public int WordMapCount { get; set; }\n    public int WordCount { get; set; }\n    public long IdentityMilliseconds { get; set; }\n    public long WordMapMilliseconds { get; set; }\n    public long SerializationMilliseconds { get; set; }\n  }\n\n  private sealed record PendingFindRequest(\n''',
'field/metrics class')

replace_once(
'''      if (type == "lazy-word-materialized")\n      {\n        DiagnosticLog.Write("transcript.lazy_word_materialized", root.Clone());\n        return;\n      }\n''',
'''      if (type == "window-transaction-diagnostic")\n      {\n        DiagnosticLog.Write("transcript.window_browser_timing", root.Clone());\n        return;\n      }\n      if (type == "window-shift-followup")\n      {\n        DiagnosticLog.Write("transcript.window_shift_followup", root.Clone());\n        return;\n      }\n      if (type == "lazy-word-materialized")\n      {\n        DiagnosticLog.Write("transcript.lazy_word_materialized", root.Clone());\n        return;\n      }\n''',
'web diagnostic handlers')

replace_once(
'''            ReadOptionalInt32(root, "sourceStartIndex"),\n            ReadOptionalInt32(root, "sourceEndIndex"));\n''',
'''            ReadOptionalInt32(root, "sourceStartIndex"),\n            ReadOptionalInt32(root, "sourceEndIndex"),\n            ReadOptionalInt64(root, "requestSequence"));\n''',
'window-shift request sequence')

old_signature = '''  private string BuildReplaceWindowScript(\n    TranscriptWindow window,\n    bool preserve,\n    int? anchorRecordNumber = null,\n    string? anchorSourceId = null,\n    double? anchorOffset = null,\n    int? focusVirtualIndex = null,\n    string? focusEdge = null,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null)\n  {\n    var keys = window.Records\n'''
new_signature = '''  private string BuildReplaceWindowScript(\n    TranscriptWindow window,\n    bool preserve,\n    int? anchorRecordNumber = null,\n    string? anchorSourceId = null,\n    double? anchorOffset = null,\n    int? focusVirtualIndex = null,\n    string? focusEdge = null,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null,\n    WindowScriptBuildMetrics? metrics = null,\n    long? transactionId = null,\n    string? renderReason = null,\n    long? requestSequence = null)\n  {\n    var identityTimer = Stopwatch.StartNew();\n    var keys = window.Records\n'''
replace_once(old_signature, new_signature, 'BuildReplaceWindowScript signature')

replace_once(
'''    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n      .Where(identity => keys.Contains(identity.SourceId + "\\0" + identity.RecordNumber))\n      .ToArray();\n    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex?.GetWordMaps(\n      window.Records) ?? Array.Empty<TranscriptRecordWordMap>();\n    return "replaceTranscriptWindow(" +\n''',
'''    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n      .Where(identity => keys.Contains(identity.SourceId + "\\0" + identity.RecordNumber))\n      .ToArray();\n    if (metrics is not null)\n    {\n      metrics.NodeCount = identities.Count;\n      metrics.IdentityMilliseconds = identityTimer.ElapsedMilliseconds;\n    }\n\n    var wordMapTimer = Stopwatch.StartNew();\n    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex?.GetWordMaps(\n      window.Records) ?? Array.Empty<TranscriptRecordWordMap>();\n    if (metrics is not null)\n    {\n      metrics.WordMapCount = wordMaps.Count;\n      metrics.WordCount = wordMaps.Sum(record => record.Words.Count);\n      metrics.WordMapMilliseconds = wordMapTimer.ElapsedMilliseconds;\n    }\n\n    long effectiveTransactionId = transactionId ??\n      Interlocked.Increment(ref _windowRenderTransactionSequence);\n    bool searchIndexAvailable = _searchIndex is not null;\n    var serializationTimer = Stopwatch.StartNew();\n    string script = "replaceTranscriptWindow(" +\n''',
'BuildReplaceWindowScript preparation')

replace_once(
'''      JsonSerializer.Serialize(expectedStructure?.Entries ??\n        Array.Empty<TranscriptStructureEntry>()) + "," +\n      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + ");";\n  }\n\n  private async Task RenderWindowForRecordAsync(\n''',
'''      JsonSerializer.Serialize(expectedStructure?.Entries ??\n        Array.Empty<TranscriptStructureEntry>()) + "," +\n      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + "," +\n      effectiveTransactionId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +\n      JsonSerializer.Serialize(renderReason ?? string.Empty) + "," +\n      (requestSequence ?? 0L).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +\n      JsonSerializer.Serialize(searchIndexAvailable) + ");";\n    if (metrics is not null)\n    {\n      metrics.SerializationMilliseconds = serializationTimer.ElapsedMilliseconds;\n    }\n    return script;\n  }\n\n  private async Task RenderWindowForRecordAsync(\n''',
'BuildReplaceWindowScript tail')

replace_once(
'''    int? protectedEndIndex,\n    int? sourceStartIndex,\n    int? sourceEndIndex)\n  {\n    await _windowRenderGate.WaitAsync();\n    try\n''',
'''    int? protectedEndIndex,\n    int? sourceStartIndex,\n    int? sourceEndIndex,\n    long? requestSequence = null)\n  {\n    var gateTimer = Stopwatch.StartNew();\n    await _windowRenderGate.WaitAsync();\n    long gateWaitMilliseconds = gateTimer.ElapsedMilliseconds;\n    try\n''',
'RenderWindowForIndexCoreAsync signature/gate')

replace_once(
'''      int direction = reason.EndsWith("-up", StringComparison.OrdinalIgnoreCase)\n        ? -1\n        : reason.EndsWith("-down", StringComparison.OrdinalIgnoreCase)\n          ? 1\n          : 0;\n      TranscriptWindow window = direction == 0\n        ? document.CreateWindow(focalIndex, GetVirtualViewportHeight())\n        : document.CreateShiftedWindow(\n            focalIndex,\n            _windowStartIndex,\n            _windowEndIndex,\n            direction,\n            GetVirtualViewportHeight(),\n            protectedStartIndex,\n            protectedEndIndex);\n''',
'''      int direction = reason.EndsWith("-up", StringComparison.OrdinalIgnoreCase)\n        ? -1\n        : reason.EndsWith("-down", StringComparison.OrdinalIgnoreCase)\n          ? 1\n          : 0;\n      var windowBuildTimer = Stopwatch.StartNew();\n      TranscriptWindow window = direction == 0\n        ? document.CreateWindow(focalIndex, GetVirtualViewportHeight())\n        : document.CreateShiftedWindow(\n            focalIndex,\n            _windowStartIndex,\n            _windowEndIndex,\n            direction,\n            GetVirtualViewportHeight(),\n            protectedStartIndex,\n            protectedEndIndex);\n      long windowBuildMilliseconds = windowBuildTimer.ElapsedMilliseconds;\n''',
'window build timing')

replace_once(
'''      var timer = Stopwatch.StartNew();\n      if (!await ExecuteAsync(BuildReplaceWindowScript(\n            window,\n            preserve: false,\n            anchorRecordNumber: anchorRecordNumber,\n            anchorSourceId: anchorSourceId,\n            anchorOffset: anchorOffset,\n            focusVirtualIndex: focalIndex)))\n      {\n        return;\n      }\n''',
'''      var timer = Stopwatch.StartNew();\n      long transactionId = Interlocked.Increment(\n        ref _windowRenderTransactionSequence);\n      var scriptMetrics = new WindowScriptBuildMetrics();\n      var scriptBuildTimer = Stopwatch.StartNew();\n      string replacementScript = BuildReplaceWindowScript(\n        window,\n        preserve: false,\n        anchorRecordNumber: anchorRecordNumber,\n        anchorSourceId: anchorSourceId,\n        anchorOffset: anchorOffset,\n        focusVirtualIndex: focalIndex,\n        metrics: scriptMetrics,\n        transactionId: transactionId,\n        renderReason: reason,\n        requestSequence: requestSequence);\n      long scriptBuildMilliseconds = scriptBuildTimer.ElapsedMilliseconds;\n      var executeTimer = Stopwatch.StartNew();\n      if (!await ExecuteAsync(replacementScript))\n      {\n        return;\n      }\n      long executeMilliseconds = executeTimer.ElapsedMilliseconds;\n''',
'instrumented execute')

replace_once(
'''      DiagnosticLog.Write("transcript.window_rendered", new\n      {\n        reason,\n        focalIndex,\n        window.StartIndex,\n        window.EndIndex,\n        recordCount = window.Records.Count,\n        htmlCharacters = window.Html.Length,\n        elapsedMilliseconds = timer.ElapsedMilliseconds\n      });\n''',
'''      DiagnosticLog.Write("transcript.window_rendered", new\n      {\n        transactionId,\n        requestSequence = requestSequence ?? 0L,\n        reason,\n        focalIndex,\n        window.StartIndex,\n        window.EndIndex,\n        recordCount = window.Records.Count,\n        nodeCount = scriptMetrics.NodeCount,\n        wordMapCount = scriptMetrics.WordMapCount,\n        wordCount = scriptMetrics.WordCount,\n        searchIndexAvailable = _searchIndex is not null,\n        htmlCharacters = window.Html.Length,\n        gateWaitMilliseconds,\n        windowBuildMilliseconds,\n        identityMilliseconds = scriptMetrics.IdentityMilliseconds,\n        wordMapMilliseconds = scriptMetrics.WordMapMilliseconds,\n        serializationMilliseconds = scriptMetrics.SerializationMilliseconds,\n        scriptBuildMilliseconds,\n        executeMilliseconds,\n        elapsedMilliseconds = timer.ElapsedMilliseconds\n      });\n''',
'C# transaction summary')

replace_once(
'''  focusEdge = null,\n  expectedStructure = [],\n  structureProbeId = '') {\n  const expectedStructureMap = normalizeStructureEntries(expectedStructure);\n''',
'''  focusEdge = null,\n  expectedStructure = [],\n  structureProbeId = '',\n  transactionId = 0,\n  renderReason = '',\n  requestSequence = 0,\n  searchIndexAvailable = false) {\n  const diagnosticStarted = performance.now();\n  const captureGeometry = () => {\n    const topSpacer = transcript.querySelector(\n      '.virtual-spacer[data-virtual-spacer="top"]');\n    const bottomSpacer = transcript.querySelector(\n      '.virtual-spacer[data-virtual-spacer="bottom"]');\n    const visible = [...transcript.querySelectorAll('.virtual-record')]\n      .filter(record => {\n        const rect = record.getBoundingClientRect();\n        return rect.bottom > 0 && rect.top < window.innerHeight;\n      });\n    return {\n      scrollY:Number(window.scrollY),\n      viewportHeight:Number(window.innerHeight),\n      documentHeight:Number(document.documentElement.scrollHeight),\n      topSpacerHeight:Number(topSpacer?.getBoundingClientRect().height ?? 0),\n      bottomSpacerHeight:Number(bottomSpacer?.getBoundingClientRect().height ?? 0),\n      visibleStartIndex:visible.length\n        ? Number(visible[0].dataset.virtualIndex || -1)\n        : -1,\n      visibleEndIndex:visible.length\n        ? Number(visible[visible.length - 1].dataset.virtualIndex || -1)\n        : -1\n    };\n  };\n  const beforeGeometry = captureGeometry();\n  const expectedStructureMap = normalizeStructureEntries(expectedStructure);\n''',
'JS signature and geometry')

replace_once(
'''  const exactAssignedHtml =\n    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +\n    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +\n    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +\n    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';\n  transcript.innerHTML = exactAssignedHtml;\n  applyRevisionVisibility(showRolledBackHistory);\n''',
'''  const exactAssignedHtml =\n    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +\n    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +\n    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +\n    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';\n  let phaseStarted = performance.now();\n  transcript.innerHTML = exactAssignedHtml;\n  applyRevisionVisibility(showRolledBackHistory);\n  const innerHtmlMilliseconds = performance.now() - phaseStarted;\n''',
'innerHTML timing')

replace_once(
'''  setAvailableWordMaps(wordMap || []);\n  wrapWords(nodeMap || []);\n  previousStructureMap = postStructureStage(\n''',
'''  setAvailableWordMaps(wordMap || []);\n  phaseStarted = performance.now();\n  wrapWords(nodeMap || []);\n  const wrapWordsMilliseconds = performance.now() - phaseStarted;\n  previousStructureMap = postStructureStage(\n''',
'wrap timing')

replace_once(
'''  previousStructureStage = 'after-wrap-words';\n  assignRecordScopes();\n  previousStructureMap = postStructureStage(\n''',
'''  previousStructureStage = 'after-wrap-words';\n  phaseStarted = performance.now();\n  assignRecordScopes();\n  const recordScopesMilliseconds = performance.now() - phaseStarted;\n  previousStructureMap = postStructureStage(\n''',
'record timing')

replace_once(
'''  previousStructureStage = 'after-record-scopes';\n  assignNodeScopes(nodeMap || []);\n  previousStructureMap = postStructureStage(\n''',
'''  previousStructureStage = 'after-record-scopes';\n  phaseStarted = performance.now();\n  assignNodeScopes(nodeMap || []);\n  const nodeScopesMilliseconds = performance.now() - phaseStarted;\n  previousStructureMap = postStructureStage(\n''',
'node timing')

replace_once(
'''  previousStructureStage = 'after-node-scopes';\n  assignStableWordScopes(wordMap || []);\n  postMappingInstallSummary(nodeMap || []);\n  previousStructureMap = postStructureStage(\n''',
'''  previousStructureStage = 'after-node-scopes';\n  phaseStarted = performance.now();\n  assignStableWordScopes(wordMap || []);\n  const stableWordScopesMilliseconds = performance.now() - phaseStarted;\n  phaseStarted = performance.now();\n  postMappingInstallSummary(nodeMap || []);\n  const mappingSummaryMilliseconds = performance.now() - phaseStarted;\n  previousStructureMap = postStructureStage(\n''',
'stable/mapping timing')

replace_once(
'''  const measurements = [...transcript.querySelectorAll('.virtual-record')]\n    .map(record => ({\n      index:Number(record.dataset.virtualIndex || -1),\n      height:record.getBoundingClientRect().height\n    }))\n    .filter(item => item.index >= 0 && item.height > 0);\n  if (measurements.length) {\n''',
'''  phaseStarted = performance.now();\n  const measurements = [...transcript.querySelectorAll('.virtual-record')]\n    .map(record => ({\n      index:Number(record.dataset.virtualIndex || -1),\n      height:record.getBoundingClientRect().height\n    }))\n    .filter(item => item.index >= 0 && item.height > 0);\n  const measurementMilliseconds = performance.now() - phaseStarted;\n  if (measurements.length) {\n''',
'measurement timing')

replace_once(
'''  let restoredAnchor = false;\n  if (anchorRecordNumber !== null && anchorOffset !== null) {\n''',
'''  phaseStarted = performance.now();\n  let restoredAnchor = false;\n  if (anchorRecordNumber !== null && anchorOffset !== null) {\n''',
'anchor timing start')

replace_once(
'''  if (preserve) {\n    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);\n    else window.scrollTo(0, previousY);\n  }\n  postStructureStage(\n    structureProbeId,\n    'replace-window-exit',\n    expectedStructureMap,\n    previousStructureMap,\n    previousStructureStage);\n}\n''',
'''  if (preserve) {\n    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);\n    else window.scrollTo(0, previousY);\n  }\n  const anchorRestoreMilliseconds = performance.now() - phaseStarted;\n  postStructureStage(\n    structureProbeId,\n    'replace-window-exit',\n    expectedStructureMap,\n    previousStructureMap,\n    previousStructureStage);\n  const afterGeometry = captureGeometry();\n  const totalMilliseconds = performance.now() - diagnosticStarted;\n  const diagnostic = {\n    type:'window-transaction-diagnostic',\n    transactionId:Number(transactionId || 0),\n    requestSequence:Number(requestSequence || 0),\n    reason:String(renderReason || ''),\n    searchIndexAvailable:!!searchIndexAvailable,\n    startIndex:Number(startIndex),\n    endIndex:Number(endIndex),\n    recordCount:document.querySelectorAll('.virtual-record').length,\n    nodeCount:Array.isArray(nodeMap) ? nodeMap.length : 0,\n    wordMapCount:Array.isArray(wordMap) ? wordMap.length : 0,\n    wordCount:words.length,\n    beforeScrollY:beforeGeometry.scrollY,\n    beforeViewportHeight:beforeGeometry.viewportHeight,\n    beforeDocumentHeight:beforeGeometry.documentHeight,\n    beforeTopSpacerHeight:beforeGeometry.topSpacerHeight,\n    beforeBottomSpacerHeight:beforeGeometry.bottomSpacerHeight,\n    beforeVisibleStartIndex:beforeGeometry.visibleStartIndex,\n    beforeVisibleEndIndex:beforeGeometry.visibleEndIndex,\n    afterScrollY:afterGeometry.scrollY,\n    afterViewportHeight:afterGeometry.viewportHeight,\n    afterDocumentHeight:afterGeometry.documentHeight,\n    afterTopSpacerHeight:afterGeometry.topSpacerHeight,\n    afterBottomSpacerHeight:afterGeometry.bottomSpacerHeight,\n    afterVisibleStartIndex:afterGeometry.visibleStartIndex,\n    afterVisibleEndIndex:afterGeometry.visibleEndIndex,\n    innerHtmlMilliseconds:Math.round(innerHtmlMilliseconds),\n    wrapWordsMilliseconds:Math.round(wrapWordsMilliseconds),\n    recordScopesMilliseconds:Math.round(recordScopesMilliseconds),\n    nodeScopesMilliseconds:Math.round(nodeScopesMilliseconds),\n    stableWordScopesMilliseconds:Math.round(stableWordScopesMilliseconds),\n    mappingSummaryMilliseconds:Math.round(mappingSummaryMilliseconds),\n    measurementMilliseconds:Math.round(measurementMilliseconds),\n    anchorRestoreMilliseconds:Math.round(anchorRestoreMilliseconds),\n    totalMilliseconds:Math.round(totalMilliseconds)\n  };\n  chrome.webview.postMessage(diagnostic);\n  lastWindowTransactionDiagnostic = {\n    transactionId:diagnostic.transactionId,\n    requestSequence:diagnostic.requestSequence,\n    reason:diagnostic.reason,\n    completedAt:performance.now(),\n    afterVisibleStartIndex:diagnostic.afterVisibleStartIndex,\n    afterVisibleEndIndex:diagnostic.afterVisibleEndIndex\n  };\n}\n''',
'JS diagnostic tail')

replace_once(
'''let userScrollIntentDirection = 0;\nlet lastTouchY = Number.NaN;\n\nfunction markUserScrollIntent(direction = 0) {\n''',
'''let userScrollIntentDirection = 0;\nlet lastTouchY = Number.NaN;\nlet windowShiftRequestSequence = 0;\nlet lastWindowTransactionDiagnostic = null;\n\nfunction markUserScrollIntent(direction = 0) {\n''',
'JS transaction globals')

replace_once(
'''  virtualShiftPending = true;\n  chrome.webview.postMessage({\n    type:'window-shift',\n    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),\n''',
'''  virtualShiftPending = true;\n  const requestSequence = ++windowShiftRequestSequence;\n  const requestReason = reason || (direction < 0 ? 'scroll-up' : 'scroll-down');\n  const requestedAt = performance.now();\n  if (lastWindowTransactionDiagnostic &&\n      requestedAt - lastWindowTransactionDiagnostic.completedAt <= 300) {\n    chrome.webview.postMessage({\n      type:'window-shift-followup',\n      requestSequence,\n      reason:requestReason,\n      previousTransactionId:lastWindowTransactionDiagnostic.transactionId,\n      previousRequestSequence:lastWindowTransactionDiagnostic.requestSequence,\n      previousReason:lastWindowTransactionDiagnostic.reason,\n      elapsedSinceReplacementMilliseconds:Math.round(\n        requestedAt - lastWindowTransactionDiagnostic.completedAt),\n      sourceStartIndex:windowStartIndex,\n      sourceEndIndex:windowEndIndex,\n      visibleStartIndex:visibleRange.visibleStartIndex ?? -1,\n      visibleEndIndex:visibleRange.visibleEndIndex ?? -1,\n      scrollY:Number(window.scrollY),\n      viewportHeight:Number(window.innerHeight)\n    });\n  }\n  chrome.webview.postMessage({\n    type:'window-shift',\n    requestSequence,\n    reason:requestReason,\n''',
'request correlation')

path.write_text(text, encoding='utf-8')
