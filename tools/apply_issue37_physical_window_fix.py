from pathlib import Path
import re


def replace_once(text, old, new, label):
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


# ---------------------------------------------------------------------------
# TranscriptVirtualDocument: size and move windows by physical height.
# ---------------------------------------------------------------------------
virtual_path = Path("AgentPanelSpeaker/TranscriptVirtualDocument.cs")
virtual = virtual_path.read_text(encoding="utf-8")

virtual = replace_once(
  virtual,
  '''  private const int RegionRecordCount = 20;\n  private const int LoadedRegionRadius = 2;\n  private const int MaximumHtmlCharacters = 1_000_000;\n  private const double MinimumEstimatedHeight = 72.0;\n''',
  '''  internal const double MinimumWindowViewportHeights = 5.0;\n  internal const double EdgeTriggerViewportHeights = 2.0;\n  internal const double DefaultViewportHeight = 700.0;\n  private const double MinimumEstimatedHeight = 72.0;\n''',
  "virtual-window constants")

create_pattern = re.compile(
  r'''  public TranscriptWindow CreateWindow\(int focalIndex\)\n  \{.*?\n  \}\n\n  private double SumHeights''',
  re.S)
create_replacement = '''  /// <summary>
  /// Creates the smallest practical contiguous window around one atomic Core
  /// unit that covers at least the configured physical viewport-height floor.
  /// </summary>
  public TranscriptWindow CreateWindow(int focalIndex)
  {
    return CreateWindow(focalIndex, DefaultViewportHeight);
  }

  /// <summary>
  /// Creates a physically sized virtual window around one atomic Core unit.
  /// Record counts and HTML byte counts do not control the materialized span.
  /// </summary>
  public TranscriptWindow CreateWindow(int focalIndex, double viewportHeight)
  {
    if (_records.Length == 0)
    {
      return EmptyWindow();
    }

    focalIndex = ResolveVisibleFocalIndex(
      Math.Clamp(focalIndex, 0, _records.Length - 1));
    double targetHeight = NormalizeViewportHeight(viewportHeight) *
      MinimumWindowViewportHeights;
    int left = focalIndex;
    int right = focalIndex;
    double totalHeight = EffectiveHeight(focalIndex);
    double leftBufferHeight = 0.0;
    double rightBufferHeight = 0.0;

    while (totalHeight < targetHeight &&
           (left > 0 || right < _records.Length - 1))
    {
      bool canGrowLeft = left > 0;
      bool canGrowRight = right < _records.Length - 1;
      bool growLeft = canGrowLeft &&
        (!canGrowRight || leftBufferHeight <= rightBufferHeight);
      if (growLeft)
      {
        --left;
        double addedHeight = EffectiveHeight(left);
        totalHeight += addedHeight;
        leftBufferHeight += addedHeight;
      }
      else
      {
        ++right;
        double addedHeight = EffectiveHeight(right);
        totalHeight += addedHeight;
        rightBufferHeight += addedHeight;
      }
    }

    TrimToMinimumHeight(
      ref left,
      ref right,
      focalIndex,
      targetHeight,
      ref totalHeight,
      trimBothEdges: true);
    return BuildWindow(left, right);
  }

  /// <summary>
  /// Slides an existing physical window in one direction.  The newly exposed
  /// edge is extended by the edge-trigger depth and the opposite edge is then
  /// trimmed as far as possible without dropping below the five-viewport floor
  /// or removing the focal atomic Core unit.
  /// </summary>
  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight)
  {
    if (_records.Length == 0)
    {
      return EmptyWindow();
    }
    if (direction == 0)
    {
      return CreateWindow(focalIndex, viewportHeight);
    }

    focalIndex = ResolveVisibleFocalIndex(
      Math.Clamp(focalIndex, 0, _records.Length - 1));
    int left = Math.Clamp(currentStartIndex, 0, _records.Length - 1);
    int right = Math.Clamp(currentEndIndex, 0, _records.Length - 1);
    if (left > right || focalIndex < left || focalIndex > right)
    {
      return CreateWindow(focalIndex, viewportHeight);
    }

    double normalizedViewportHeight = NormalizeViewportHeight(viewportHeight);
    double targetHeight = normalizedViewportHeight *
      MinimumWindowViewportHeights;
    double extensionHeight = normalizedViewportHeight *
      EdgeTriggerViewportHeights;
    double totalHeight = SumHeights(left, right + 1);
    double addedHeight = 0.0;

    if (direction < 0)
    {
      while (left > 0 && addedHeight < extensionHeight)
      {
        --left;
        double height = EffectiveHeight(left);
        totalHeight += height;
        addedHeight += height;
      }
      EnsureMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight);
      TrimToMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: false);
    }
    else
    {
      while (right < _records.Length - 1 && addedHeight < extensionHeight)
      {
        ++right;
        double height = EffectiveHeight(right);
        totalHeight += height;
        addedHeight += height;
      }
      EnsureMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight);
      TrimToMinimumHeight(
        ref left,
        ref right,
        focalIndex,
        targetHeight,
        ref totalHeight,
        trimBothEdges: false,
        trimLeadingEdge: true);
    }

    return BuildWindow(left, right);
  }

  private TranscriptWindow EmptyWindow()
  {
    return new TranscriptWindow(
      string.Empty,
      0,
      -1,
      0,
      0,
      Array.Empty<TranscriptVirtualRecord>());
  }

  private TranscriptWindow BuildWindow(int left, int right)
  {
    TranscriptVirtualRecord[] records = _records[left..(right + 1)];
    string windowHtml = string.Concat(records.Select((record, offset) =>
      "<section class=\\\"virtual-record\\\" data-virtual-index=\\\"" +
      (left + offset).ToString(CultureInfo.InvariantCulture) + "\\\">" +
      record.Html + "</section>"));
    return new TranscriptWindow(
      windowHtml,
      left,
      right,
      SumHeights(0, left),
      SumHeights(right + 1, _records.Length),
      records);
  }

  private void EnsureMinimumHeight(
    ref int left,
    ref int right,
    int focalIndex,
    double targetHeight,
    ref double totalHeight)
  {
    double leftBufferHeight = SumHeights(left, focalIndex);
    double rightBufferHeight = SumHeights(focalIndex + 1, right + 1);
    while (totalHeight < targetHeight &&
           (left > 0 || right < _records.Length - 1))
    {
      bool canGrowLeft = left > 0;
      bool canGrowRight = right < _records.Length - 1;
      bool growLeft = canGrowLeft &&
        (!canGrowRight || leftBufferHeight <= rightBufferHeight);
      if (growLeft)
      {
        --left;
        double height = EffectiveHeight(left);
        totalHeight += height;
        leftBufferHeight += height;
      }
      else
      {
        ++right;
        double height = EffectiveHeight(right);
        totalHeight += height;
        rightBufferHeight += height;
      }
    }
  }

  private void TrimToMinimumHeight(
    ref int left,
    ref int right,
    int focalIndex,
    double targetHeight,
    ref double totalHeight,
    bool trimBothEdges,
    bool trimLeadingEdge = true)
  {
    bool changed;
    do
    {
      changed = false;
      if ((trimBothEdges || trimLeadingEdge) && left < focalIndex)
      {
        double height = EffectiveHeight(left);
        if (totalHeight - height >= targetHeight)
        {
          totalHeight -= height;
          ++left;
          changed = true;
        }
      }
      if ((trimBothEdges || !trimLeadingEdge) && right > focalIndex)
      {
        double height = EffectiveHeight(right);
        if (totalHeight - height >= targetHeight)
        {
          totalHeight -= height;
          --right;
          changed = true;
        }
      }
    }
    while (changed);
  }

  private static double NormalizeViewportHeight(double viewportHeight)
  {
    return double.IsFinite(viewportHeight) && viewportHeight > 0
      ? viewportHeight
      : DefaultViewportHeight;
  }

  private double SumHeights'''
virtual, count = create_pattern.subn(create_replacement, virtual, count=1)
if count != 1:
  raise SystemExit(f"CreateWindow replacement: expected one match, found {count}")
virtual_path.write_text(virtual, encoding="utf-8")


# ---------------------------------------------------------------------------
# TranscriptView: feed real viewport geometry to selection and drive manual /
# playback prefetch from physical positions rather than record bands.
# ---------------------------------------------------------------------------
view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
view = view_path.read_text(encoding="utf-8")

view = replace_once(
  view,
  '''  private int _windowStartIndex = -1;\n  private int _windowEndIndex = -1;\n  private bool _domPresentationMode;\n''',
  '''  private int _windowStartIndex = -1;\n  private int _windowEndIndex = -1;\n  private double _browserViewportHeight;\n  private bool _domPresentationMode;\n''',
  "browser viewport field")

view = replace_once(
  view,
  '''      _lastLayoutSize = currentSize;\n      ++_layoutGeneration;\n      _virtualDocument?.SetLayoutGeneration(_layoutGeneration);\n''',
  '''      _lastLayoutSize = currentSize;\n      _browserViewportHeight = 0.0;\n      ++_layoutGeneration;\n      _virtualDocument?.SetLayoutGeneration(_layoutGeneration);\n''',
  "layout viewport invalidation")

view = view.replace(
  "payload.Document.CreateWindow(focalIndex);",
  "payload.Document.CreateWindow(focalIndex, GetVirtualViewportHeight());")
view = view.replace(
  "payload.Document.CreateWindow(latestIndex);",
  "payload.Document.CreateWindow(latestIndex, GetVirtualViewportHeight());")
view = view.replace(
  "document.CreateWindow(focalIndex);",
  "document.CreateWindow(focalIndex, GetVirtualViewportHeight());")

viewport_helper_anchor = '''  private int ResolveInitialWindowIndex(\n'''
viewport_helper = '''  private double GetVirtualViewportHeight()
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

'''
view = replace_once(
  view,
  viewport_helper_anchor,
  viewport_helper + viewport_helper_anchor,
  "viewport helper insertion")

view = replace_once(
  view,
  '''      if (type == "window-measured")\n      {\n        TranscriptVirtualDocument? virtualDocument = _virtualDocument;\n''',
  '''      if (type == "window-measured")\n      {\n        UpdateBrowserViewportHeight(ReadOptionalDouble(root, "viewportHeight"));\n        TranscriptVirtualDocument? virtualDocument = _virtualDocument;\n''',
  "window measured viewport")

view = replace_once(
  view,
  '''      if (type == "window-shift")\n      {\n        int? focalIndex = ReadOptionalInt32(root, "focalIndex");\n''',
  '''      if (type == "window-shift")\n      {\n        UpdateBrowserViewportHeight(ReadOptionalDouble(root, "viewportHeight"));\n        int? focalIndex = ReadOptionalInt32(root, "focalIndex");\n''',
  "window shift viewport")

old_render_selection = '''    TranscriptWindow window = document.CreateWindow(focalIndex, GetVirtualViewportHeight());
    if (window.StartIndex == _windowStartIndex && window.EndIndex == _windowEndIndex)
'''
new_render_selection = '''    int direction = reason.EndsWith("-up", StringComparison.OrdinalIgnoreCase)
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
          GetVirtualViewportHeight());
    if (window.StartIndex == _windowStartIndex && window.EndIndex == _windowEndIndex)
'''
# This exact fragment occurs only in RenderWindowForIndexAsync after the global
# CreateWindow call-site replacement above.
view = replace_once(
  view,
  old_render_selection,
  new_render_selection,
  "directional physical selection")

view = replace_once(
  view,
  '''          anchorRecordNumber: anchorRecordNumber,\n          anchorSourceId: anchorSourceId,\n          anchorOffset: anchorOffset)))\n''',
  '''          anchorRecordNumber: anchorRecordNumber,\n          anchorSourceId: anchorSourceId,\n          anchorOffset: anchorOffset,\n          focusVirtualIndex: focalIndex)))\n''',
  "spacer fallback focus")

view = replace_once(
  view,
  '''      type:'window-measured',\n      layoutGeneration,\n      measurements\n''',
  '''      type:'window-measured',\n      layoutGeneration,\n      viewportHeight:window.innerHeight,\n      measurements\n''',
  "measurement viewport message")

# Replace anchor restoration with a post-condition: after replacement the
# viewport must intersect materialized content, otherwise focus the requested
# atomic unit as a safe fallback.
old_restore = '''  virtualShiftPending = false;
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
'''
new_restore = '''  virtualShiftPending = false;
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
'''
view = replace_once(view, old_restore, new_restore, "spacer-safe restoration")

# Add voice-cursor prefetch immediately before playback/reveal logic.  Movement
# is tracked by virtual index plus local Y within one atomic record, so a tall
# turn still has a meaningful playback direction.
reveal_anchor = '''function reveal(element) {\n'''
voice_prefetch = '''const VW_MIN_VIEWPORT_HEIGHTS = 5;
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

'''
view = replace_once(
  view,
  reveal_anchor,
  voice_prefetch + reveal_anchor,
  "voice prefetch helpers")

view = replace_once(
  view,
  '''  reveal(listItem || target);\n}\n\n\nfunction clearFindHighlights''',
  '''  reveal(listItem || target);\n  maybePrefetchVoiceCursor(target);\n}\n\n\nfunction clearFindHighlights''',
  "voice prefetch invocation")

# Replace the record-band manual-scroll controller with physical edge geometry
# plus actual scroll direction.  No turn count participates in the trigger.
scroll_pattern = re.compile(
  r'''let scrollFollowTimer = 0;\nlet virtualShiftTimer = 0;\nfunction firstVisibleVirtualRecord\(\) \{.*?\n\}, \{passive:true\}\);''',
  re.S)
scroll_replacement = '''let scrollFollowTimer = 0;
let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;

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
  if (followSpeech && now > programmaticScrollUntil) {
    if (scrollFollowTimer) clearTimeout(scrollFollowTimer);
    scrollFollowTimer = setTimeout(() => {
      scrollFollowTimer = 0;
      if (followSpeech && performance.now() > programmaticScrollUntil) {
        setFollowSpeech(false, true);
      }
    }, 120);
  }

  const currentY = window.scrollY;
  if (now <= programmaticScrollUntil) {
    lastManualScrollY = currentY;
    return;
  }
  const delta = currentY - lastManualScrollY;
  lastManualScrollY = currentY;
  if (Math.abs(delta) <= VW_SCROLL_DIRECTION_EPSILON_PX) return;
  const direction = delta > 0 ? 1 : -1;
  if (virtualShiftFrame) cancelAnimationFrame(virtualShiftFrame);
  virtualShiftFrame = requestAnimationFrame(() => {
    virtualShiftFrame = 0;
    if (performance.now() <= programmaticScrollUntil) return;
    maybeRequestManualVirtualShift(direction);
  });
}, {passive:true});'''
view, count = scroll_pattern.subn(scroll_replacement, view, count=1)
if count != 1:
  raise SystemExit(f"manual scroll controller: expected one match, found {count}")

view_path.write_text(view, encoding="utf-8")
