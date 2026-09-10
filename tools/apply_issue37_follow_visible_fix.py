from pathlib import Path

virtual_path = Path('AgentPanelSpeaker/TranscriptVirtualDocument.cs')
virtual = virtual_path.read_text(encoding='utf-8')

old_signature = '''  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight)
'''
new_signature = '''  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight,
    int? protectedStartIndex = null,
    int? protectedEndIndex = null)
'''
if virtual.count(old_signature) != 1:
  raise SystemExit('CreateShiftedWindow signature anchor mismatch')
virtual = virtual.replace(old_signature, new_signature, 1)

old_normalization = '''    double normalizedViewportHeight = NormalizeViewportHeight(viewportHeight);
    double targetHeight = normalizedViewportHeight *
      MinimumWindowViewportHeights;
'''
new_normalization = '''    double normalizedViewportHeight = NormalizeViewportHeight(viewportHeight);
    (int protectedStart, int protectedEnd) = ResolveProtectedVisibleRange(
      focalIndex,
      left,
      right,
      direction,
      normalizedViewportHeight,
      protectedStartIndex,
      protectedEndIndex);
    double targetHeight = normalizedViewportHeight *
      MinimumWindowViewportHeights;
'''
# This text occurs only in CreateShiftedWindow; CreateWindow calculates its
# target directly from NormalizeViewportHeight(viewportHeight).
if virtual.count(old_normalization) != 1:
  raise SystemExit('CreateShiftedWindow normalization anchor mismatch')
virtual = virtual.replace(old_normalization, new_normalization, 1)

old_down_trim = '''      TrimToMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: false);
'''
new_down_trim = '''      TrimToMinimumHeight(
        ref left,
        ref right,
        protectedStart,
        protectedEnd,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: false);
'''
if virtual.count(old_down_trim) != 1:
  raise SystemExit('upward-shift trim anchor mismatch')
virtual = virtual.replace(old_down_trim, new_down_trim, 1)

old_up_trim = '''      TrimToMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: true);
'''
new_up_trim = '''      TrimToMinimumHeight(
        ref left,
        ref right,
        protectedStart,
        protectedEnd,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: true);
'''
if virtual.count(old_up_trim) != 1:
  raise SystemExit('downward-shift trim anchor mismatch')
virtual = virtual.replace(old_up_trim, new_up_trim, 1)

old_create_trim = '''    TrimToMinimumHeight(
      ref left,
      ref right,
      focalIndex,
      targetHeight,
      ref totalHeight,
      trimBothEdges: true);
'''
new_create_trim = '''    TrimToMinimumHeight(
      ref left,
      ref right,
      focalIndex,
      focalIndex,
      targetHeight,
      ref totalHeight,
      trimBothEdges: true);
'''
if virtual.count(old_create_trim) != 1:
  raise SystemExit('CreateWindow trim anchor mismatch')
virtual = virtual.replace(old_create_trim, new_create_trim, 1)

old_trim_signature = '''  private void TrimToMinimumHeight(
    ref int left,
    ref int right,
    int focalIndex,
    double targetHeight,
    ref double totalHeight,
    bool trimBothEdges,
    bool trimLeadingEdge = true)
'''
new_trim_signature = '''  private void TrimToMinimumHeight(
    ref int left,
    ref int right,
    int protectedStartIndex,
    int protectedEndIndex,
    double targetHeight,
    ref double totalHeight,
    bool trimBothEdges,
    bool trimLeadingEdge = true)
'''
if virtual.count(old_trim_signature) != 1:
  raise SystemExit('TrimToMinimumHeight signature anchor mismatch')
virtual = virtual.replace(old_trim_signature, new_trim_signature, 1)

virtual = virtual.replace(
  'if ((trimBothEdges || trimLeadingEdge) && left < focalIndex)',
  'if ((trimBothEdges || trimLeadingEdge) && left < protectedStartIndex)',
  1)
virtual = virtual.replace(
  'if ((trimBothEdges || !trimLeadingEdge) && right > focalIndex)',
  'if ((trimBothEdges || !trimLeadingEdge) && right > protectedEndIndex)',
  1)

helper_anchor = '''  private static double NormalizeViewportHeight(double viewportHeight)
'''
helper = '''  /// <summary>
  /// Resolves the atomic Core-unit range that trimming is not allowed to cross.
  /// Exact browser viewport indexes win when available.  Legacy/internal callers
  /// that supply only the directional focal get a conservative one-viewport
  /// opposite-side range so a potentially visible neighbouring unit is never
  /// discarded merely because the total materialized height remains large.
  /// </summary>
  private (int Start, int End) ResolveProtectedVisibleRange(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight,
    int? protectedStartIndex,
    int? protectedEndIndex)
  {
    if (protectedStartIndex is int requestedStart ||
        protectedEndIndex is int requestedEnd)
    {
      int start = Math.Clamp(
        protectedStartIndex ?? focalIndex,
        currentStartIndex,
        currentEndIndex);
      int end = Math.Clamp(
        protectedEndIndex ?? focalIndex,
        currentStartIndex,
        currentEndIndex);
      if (start > end)
      {
        (start, end) = (end, start);
      }
      return (
        Math.Min(start, focalIndex),
        Math.Max(end, focalIndex));
    }

    int conservativeStart = focalIndex;
    int conservativeEnd = focalIndex;
    double coveredHeight = 0.0;
    if (direction > 0)
    {
      for (int index = focalIndex - 1;
           index >= currentStartIndex && coveredHeight < viewportHeight;
           --index)
      {
        conservativeStart = index;
        coveredHeight += EffectiveHeight(index);
      }
    }
    else if (direction < 0)
    {
      for (int index = focalIndex + 1;
           index <= currentEndIndex && coveredHeight < viewportHeight;
           ++index)
      {
        conservativeEnd = index;
        coveredHeight += EffectiveHeight(index);
      }
    }
    return (conservativeStart, conservativeEnd);
  }

'''
if virtual.count(helper_anchor) != 1:
  raise SystemExit('protected-range helper insertion anchor mismatch')
virtual = virtual.replace(helper_anchor, helper + helper_anchor, 1)
virtual_path.write_text(virtual, encoding='utf-8')

view_path = Path('AgentPanelSpeaker/TranscriptView.cs')
view = view_path.read_text(encoding='utf-8')

old_initial = '''    return _pendingPosition is TranscriptPlaybackPosition position &&
      TryResolvePositionIndex(document, identities, position, out int index)
        ? index
        : Math.Max(0, document.Count - 1);
'''
new_initial = '''    return _settings.FollowSpeech &&
      _pendingPosition is TranscriptPlaybackPosition position &&
      TryResolvePositionIndex(document, identities, position, out int index)
        ? index
        : Math.Max(0, document.Count - 1);
'''
if view.count(old_initial) != 1:
  raise SystemExit('ResolveInitialWindowIndex anchor mismatch')
view = view.replace(old_initial, new_initial, 1)

old_render_anchor = '''      TranscriptPlaybackPosition? renderAnchor = null;
      int latestIndex = -1;
      if (_pendingPosition is TranscriptPlaybackPosition latestPosition &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            latestPosition,
            out latestIndex))
      {
        renderAnchor = latestPosition;
      }
      else if (_lastLocatedContentPosition is TranscriptPlaybackPosition located &&
          TryResolvePositionIndex(
            payload.Document,
            payload.Identities,
            located,
            out latestIndex))
      {
        renderAnchor = located;
      }
'''
new_render_anchor = '''      TranscriptPlaybackPosition? renderAnchor = null;
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
'''
if view.count(old_render_anchor) != 1:
  raise SystemExit('initial render-anchor anchor mismatch')
view = view.replace(old_render_anchor, new_render_anchor, 1)

old_receiver = '''          _ = RenderWindowForIndexAsync(
            validFocalIndex,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "anchorRecordNumber"),
            ReadOptionalString(root, "anchorSourceId"),
            ReadOptionalDouble(root, "anchorOffset"));
'''
new_receiver = '''          _ = RenderWindowForIndexCoreAsync(
            validFocalIndex,
            ReadOptionalString(root, "reason"),
            ReadOptionalInt32(root, "anchorRecordNumber"),
            ReadOptionalString(root, "anchorSourceId"),
            ReadOptionalDouble(root, "anchorOffset"),
            ReadOptionalInt32(root, "visibleStartIndex"),
            ReadOptionalInt32(root, "visibleEndIndex"));
'''
if view.count(old_receiver) != 1:
  raise SystemExit('window-shift receiver anchor mismatch')
view = view.replace(old_receiver, new_receiver, 1)

old_method = '''  private async Task RenderWindowForIndexAsync(
    int focalIndex,
    string reason,
    int? anchorRecordNumber,
    string anchorSourceId,
    double? anchorOffset)
  {
'''
new_method = '''  private Task RenderWindowForIndexAsync(
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
      protectedEndIndex: null);
  }

  private async Task RenderWindowForIndexCoreAsync(
    int focalIndex,
    string reason,
    int? anchorRecordNumber,
    string anchorSourceId,
    double? anchorOffset,
    int? protectedStartIndex,
    int? protectedEndIndex)
  {
'''
if view.count(old_method) != 1:
  raise SystemExit('RenderWindowForIndexAsync method anchor mismatch')
view = view.replace(old_method, new_method, 1)

old_shift_call = '''          _windowEndIndex,
          direction,
          GetVirtualViewportHeight());
'''
new_shift_call = '''          _windowEndIndex,
          direction,
          GetVirtualViewportHeight(),
          protectedStartIndex,
          protectedEndIndex);
'''
if view.count(old_shift_call) != 1:
  raise SystemExit('CreateShiftedWindow call anchor mismatch')
view = view.replace(old_shift_call, new_shift_call, 1)

old_js = '''function requestVirtualShift(direction, reason = null, referenceRecord = null) {
  if (virtualShiftPending || windowStartIndex < 0 || windowEndIndex < 0) return;
  const visibleRecord = referenceRecord || firstVisibleVirtualRecord(direction);
  const anchor = visibleRecord?.querySelector('.record-anchor') || null;
  if (!visibleRecord || !anchor) return;
  const visibleIndex = Number(visibleRecord.dataset.virtualIndex || -1);
  if (visibleIndex < 0) return;
  virtualShiftPending = true;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    viewportHeight:window.innerHeight,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorSourceId:anchor.dataset.sourceId || '',
    anchorOffset:anchor.getBoundingClientRect().top
  });
'''
new_js = '''function requestVirtualShift(direction, reason = null, referenceRecord = null) {
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
  const visibleStartIndex = visibleRecords.length
    ? Number(visibleRecords[0].dataset.virtualIndex || -1)
    : null;
  const visibleEndIndex = visibleRecords.length
    ? Number(visibleRecords[visibleRecords.length - 1].dataset.virtualIndex || -1)
    : null;
  virtualShiftPending = true;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    visibleStartIndex,
    visibleEndIndex,
    viewportHeight:window.innerHeight,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorSourceId:anchor.dataset.sourceId || '',
    anchorOffset:anchor.getBoundingClientRect().top
  });
'''
if view.count(old_js) != 1:
  raise SystemExit('requestVirtualShift browser anchor mismatch')
view = view.replace(old_js, new_js, 1)

view_path.write_text(view, encoding='utf-8')
