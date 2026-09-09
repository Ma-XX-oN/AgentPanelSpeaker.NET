from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

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
  let anchorRestored = false;
  if (anchorRecordNumber !== null && anchorOffset !== null) {
    const selector = '.record-anchor[data-jsonl-record="' +
      CSS.escape(String(anchorRecordNumber)) + '"][data-source-id="' +
      CSS.escape(String(anchorSourceId || '')) + '"]';
    const anchor = transcript.querySelector(selector);
    if (anchor) {
      const delta = anchor.getBoundingClientRect().top - Number(anchorOffset);
      programmaticScrollUntil = performance.now() + 500;
      window.scrollBy(0, delta);
      anchorRestored = true;
    }
  }
  // A manual shift can intentionally move far enough that its old anchor is
  // no longer part of the new window.  In that case the anchor is not a valid
  // restoration target; focus the requested materialized unit instead of
  // leaving the viewport stranded inside a synthetic spacer.
  if (!anchorRestored && focusVirtualIndex !== null) {
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
if text.count(old_restore) != 1:
  raise SystemExit(f"expected one anchor restoration block, found {text.count(old_restore)}")
text = text.replace(old_restore, new_restore, 1)

old_virtual = '''let scrollFollowTimer = 0;
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
    // Window replacement, playback reveal, and anchor restoration all scroll
    // programmatically.  Those scroll events must not be reinterpreted as
    // manual edge navigation or they can bounce the virtual window away from
    // the playback target and back indefinitely.
    if (performance.now() <= programmaticScrollUntil) return;
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
'''
new_virtual = '''let scrollFollowTimer = 0;
let virtualShiftTimer = 0;
let previousScrollY = window.scrollY;
let manualScrollDirection = 0;
const virtualPrefetchViewportFactor = 1.5;

function firstVisibleVirtualRecord() {
  const records = transcript.querySelectorAll('.virtual-record');
  for (const record of records) {
    const rect = record.getBoundingClientRect();
    if (rect.bottom > 0 && rect.top < window.innerHeight) return record;
  }
  return null;
}

function firstVisibleRecordAnchor() {
  const record = firstVisibleVirtualRecord();
  return record?.querySelector('.record-anchor') || null;
}

function requestVirtualShift(direction) {
  if (virtualShiftPending || windowStartIndex < 0 || windowEndIndex < 0) return;
  const anchor = firstVisibleRecordAnchor();
  const focalIndex = direction < 0
    ? Math.max(0, windowStartIndex - 1)
    : windowEndIndex + 1;
  virtualShiftPending = true;
  const message = {
    type:'window-shift',
    reason:direction < 0 ? 'scroll-up' : 'scroll-down',
    focalIndex
  };
  if (anchor) {
    message.anchorRecordNumber = Number(anchor.dataset.jsonlRecord || 0);
    message.anchorSourceId = anchor.dataset.sourceId || '';
    message.anchorOffset = anchor.getBoundingClientRect().top;
  }
  chrome.webview.postMessage(message);
  setTimeout(() => { virtualShiftPending = false; }, 3000);
}

function maybePrefetchVirtualWindow(direction) {
  if (!direction || virtualShiftPending ||
      windowStartIndex < 0 || windowEndIndex < 0) return;
  const records = [...transcript.querySelectorAll('.virtual-record')];
  if (!records.length) return;
  const firstRect = records[0].getBoundingClientRect();
  const lastRect = records[records.length - 1].getBoundingClientRect();
  const viewportAboveRecords = firstRect.top >= window.innerHeight;
  const viewportBelowRecords = lastRect.bottom <= 0;
  const prefetchDistance = Math.max(
    240,
    window.innerHeight * virtualPrefetchViewportFactor);

  if (direction < 0 && windowStartIndex > 0) {
    const approachingTop = viewportAboveRecords ||
      (!viewportBelowRecords && firstRect.top >= -prefetchDistance);
    if (approachingTop) requestVirtualShift(-1);
    return;
  }

  if (direction > 0) {
    const bottomSpacer = transcript.querySelector(
      '.virtual-spacer[data-virtual-spacer="bottom"]');
    const hasUnloadedBottom =
      (bottomSpacer?.getBoundingClientRect().height || 0) > 0;
    const approachingBottom = viewportBelowRecords ||
      (!viewportAboveRecords &&
       lastRect.bottom <= window.innerHeight + prefetchDistance);
    if (hasUnloadedBottom && approachingBottom) requestVirtualShift(1);
  }
}

window.addEventListener('scroll', () => {
  const now = performance.now();
  const currentScrollY = window.scrollY;
  const scrollDelta = currentScrollY - previousScrollY;
  previousScrollY = currentScrollY;
  if (now > programmaticScrollUntil) {
    if (Math.abs(scrollDelta) >= 1) {
      manualScrollDirection = scrollDelta < 0 ? -1 : 1;
    }
  } else {
    manualScrollDirection = 0;
  }

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
    // Window replacement, playback reveal, and anchor restoration all scroll
    // programmatically.  Those scroll events must not be reinterpreted as
    // manual edge navigation or they can bounce the virtual window away from
    // the playback target and back indefinitely.
    if (performance.now() <= programmaticScrollUntil) return;
    maybePrefetchVirtualWindow(manualScrollDirection);
  }, 80);
}, {passive:true});
'''
if text.count(old_virtual) != 1:
  raise SystemExit(f"expected one virtual-scroll block, found {text.count(old_virtual)}")
text = text.replace(old_virtual, new_virtual, 1)

old_render = '''    if (!await ExecuteAsync(BuildReplaceWindowScript(
          window,
          preserve: false,
          anchorRecordNumber: anchorRecordNumber,
          anchorSourceId: anchorSourceId,
          anchorOffset: anchorOffset)))
'''
new_render = '''    if (!await ExecuteAsync(BuildReplaceWindowScript(
          window,
          preserve: false,
          anchorRecordNumber: anchorRecordNumber,
          anchorSourceId: anchorSourceId,
          anchorOffset: anchorOffset,
          focusVirtualIndex: focalIndex)))
'''
if text.count(old_render) != 1:
  raise SystemExit(f"expected one manual-window render call, found {text.count(old_render)}")
text = text.replace(old_render, new_render, 1)

path.write_text(text, encoding="utf-8")
