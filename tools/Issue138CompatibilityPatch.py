from pathlib import Path
import argparse

SOURCE = Path("AgentPanelSpeaker/TranscriptView.cs")
TEST = Path("AgentPanelSpeaker/Issue138LiveTailDomPreservationRegressionTestRunner.cs")


def replace_once(text: str, label: str, old: str, new: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def patch_source(text: str) -> str:
  old = """function virtualRecordUnitId(record) {
  const marker = record.querySelector(
    '.aicore-structural-unit[data-aicore-unit-id]');
  const unitId = marker?.getAttribute('data-aicore-unit-id') || '';
  if (!unitId) {
    throw new Error('Virtual transcript record is missing a Core unit ID.');
  }
  return unitId;
}

function createVirtualSpacer(edge, height) {
  const spacer = document.createElement('div');
  spacer.className = 'virtual-spacer';
  spacer.dataset.virtualSpacer = edge;
  spacer.style.height = Math.max(0, Number(height) || 0) + 'px';
  return spacer;
}

function reconcileTranscriptWindow(html, topSpacerHeight, bottomSpacerHeight) {
  const template = document.createElement('template');
  template.innerHTML = html;
  for (const node of template.content.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent.trim()) continue;
    if (node.nodeType !== Node.ELEMENT_NODE ||
        !node.classList.contains('virtual-record')) {
      throw new Error(
        'Virtual transcript window contains a non-record top-level node.');
    }
  }

  const incomingRecords = Array.from(template.content.children);
  const existingByUnitId = new Map();
  for (const child of transcript.children) {
    if (!child.classList.contains('virtual-record')) continue;
    const unitId = virtualRecordUnitId(child);
    if (existingByUnitId.has(unitId)) {
      throw new Error('Duplicate materialized Core unit ID: ' + unitId);
    }
    existingByUnitId.set(unitId, child);
  }

  const fragment = document.createDocumentFragment();
  fragment.append(createVirtualSpacer('top', topSpacerHeight));
  const incomingUnitIds = new Set();
  for (const incoming of incomingRecords) {
    const unitId = virtualRecordUnitId(incoming);
    if (incomingUnitIds.has(unitId)) {
      throw new Error('Duplicate incoming Core unit ID: ' + unitId);
    }
    incomingUnitIds.add(unitId);
    const sourceHtml = incoming.innerHTML;
    const existing = existingByUnitId.get(unitId);
    if (existing && virtualRecordSourceHtml.get(existing) === sourceHtml) {
      const virtualIndex = incoming.getAttribute('data-virtual-index');
      if (virtualIndex === null) {
        throw new Error('Virtual transcript record is missing its index.');
      }
      existing.setAttribute('data-virtual-index', virtualIndex);
      fragment.append(existing);
      continue;
    }

    virtualRecordSourceHtml.set(incoming, sourceHtml);
    fragment.append(incoming);
  }
  fragment.append(createVirtualSpacer('bottom', bottomSpacerHeight));
  transcript.replaceChildren(fragment);
}
"""
  new = """function virtualRecordUnitId(record) {
  const marker = record.querySelector(
    '.aicore-structural-unit[data-aicore-unit-id]');
  return marker?.getAttribute('data-aicore-unit-id') || '';
}

function seedVirtualRecordSourceHtml() {
  for (const record of transcript.children) {
    if (!record.classList.contains('virtual-record')) continue;
    if (!virtualRecordUnitId(record)) continue;
    virtualRecordSourceHtml.set(record, record.innerHTML);
  }
}

function createVirtualSpacer(edge, height) {
  const spacer = document.createElement('div');
  spacer.className = 'virtual-spacer';
  spacer.dataset.virtualSpacer = edge;
  spacer.style.height = Math.max(0, Number(height) || 0) + 'px';
  return spacer;
}

function reconcileTranscriptWindow(html, topSpacerHeight, bottomSpacerHeight) {
  const template = document.createElement('template');
  template.innerHTML = html;
  for (const node of template.content.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent.trim()) continue;
    if (node.nodeType !== Node.ELEMENT_NODE ||
        !node.classList.contains('virtual-record')) {
      throw new Error(
        'Virtual transcript window contains a non-record top-level node.');
    }
  }

  const incomingRecords = Array.from(template.content.children);
  const existingRecords = Array.from(transcript.children)
    .filter(child => child.classList.contains('virtual-record'));
  if (incomingRecords.some(record => !virtualRecordUnitId(record)) ||
      existingRecords.some(record => !virtualRecordUnitId(record))) {
    return false;
  }

  const existingByUnitId = new Map();
  for (const child of existingRecords) {
    const unitId = virtualRecordUnitId(child);
    if (existingByUnitId.has(unitId)) {
      throw new Error('Duplicate materialized Core unit ID: ' + unitId);
    }
    existingByUnitId.set(unitId, child);
  }

  const fragment = document.createDocumentFragment();
  fragment.append(createVirtualSpacer('top', topSpacerHeight));
  const incomingUnitIds = new Set();
  for (const incoming of incomingRecords) {
    const unitId = virtualRecordUnitId(incoming);
    if (incomingUnitIds.has(unitId)) {
      throw new Error('Duplicate incoming Core unit ID: ' + unitId);
    }
    incomingUnitIds.add(unitId);
    const sourceHtml = incoming.innerHTML;
    const existing = existingByUnitId.get(unitId);
    if (existing && virtualRecordSourceHtml.get(existing) === sourceHtml) {
      const virtualIndex = incoming.getAttribute('data-virtual-index');
      if (virtualIndex === null) {
        throw new Error('Virtual transcript record is missing its index.');
      }
      existing.setAttribute('data-virtual-index', virtualIndex);
      fragment.append(existing);
      continue;
    }

    virtualRecordSourceHtml.set(incoming, sourceHtml);
    fragment.append(incoming);
  }
  fragment.append(createVirtualSpacer('bottom', bottomSpacerHeight));
  transcript.replaceChildren(fragment);
  return true;
}
"""
  text = replace_once(text, "Core-only keyed reconciliation", old, new)
  text = replace_once(
    text,
    "legacy full-replacement compatibility path",
    """  reconcileTranscriptWindow(
    html,
    topSpacerHeight,
    bottomSpacerHeight);
  resetPlaybackProjectionState();
""",
    """  if (!reconcileTranscriptWindow(
      html,
      topSpacerHeight,
      bottomSpacerHeight)) {
    transcript.innerHTML = exactAssignedHtml;
    seedVirtualRecordSourceHtml();
  }
  resetPlaybackProjectionState();
""")
  return text


def patch_test(text: str) -> str:
  text = replace_once(
    text,
    "register legacy compatibility regression",
    """      (\"live-tail-dom/changed-record-is-replaced-and-tail-remains-unique\",
        TestChangedRecordIsReplacedAndTailRemainsUnique),
      (\"live-tail-dom/playback-message-continues-during-refresh\",
        TestPlaybackMessageContinuesDuringRefresh)
""",
    """      (\"live-tail-dom/changed-record-is-replaced-and-tail-remains-unique\",
        TestChangedRecordIsReplacedAndTailRemainsUnique),
      (\"live-tail-dom/legacy-window-retains-full-replacement-path\",
        TestLegacyWindowRetainsFullReplacementPath),
      (\"live-tail-dom/playback-message-continues-during-refresh\",
        TestPlaybackMessageContinuesDuringRefresh)
""")
  insertion = """  /// <summary>
  /// Virtual windows without Core unit identity retain the established full
  /// replacement path.  They must render correctly without inventing a keyed
  /// reconciliation identity from record numbers or visible text.
  /// </summary>
  private static void TestLegacyWindowRetainsFullReplacementPath()
  {
    using var host = CreateOffscreenHost();
    using var view = CreateInitializedView(host);
    WebView2 webView = ReadField<WebView2>(view, \"_webView\");

    JsonElement result = ExecuteJsonProbe(webView, \"\"\"
(() => {
  const first =
    '<section class=\"virtual-record\" data-virtual-index=\"2\">' +
    '<span class=\"record-anchor\" data-jsonl-record=\"3\"></span>' +
    '<p id=\"legacy-value\">before</p></section>';
  const changed =
    '<section class=\"virtual-record\" data-virtual-index=\"2\">' +
    '<span class=\"record-anchor\" data-jsonl-record=\"3\"></span>' +
    '<p id=\"legacy-value\">after</p></section>';

  replaceTranscriptWindow(first, false, [], 2, 2, 100, 100);
  const before = document.querySelector(
    '.virtual-record[data-virtual-index=\"2\"]');
  replaceTranscriptWindow(changed, false, [], 2, 2, 100, 100);
  const after = document.querySelector(
    '.virtual-record[data-virtual-index=\"2\"]');
  return JSON.stringify({
    firstRendered: before !== null,
    changedRendered: after !== null,
    changedWasReplaced: before !== after,
    textAfterChange: document.getElementById('legacy-value')?.textContent || ''
  });
})()
\"\"\");

    Require(result.GetProperty(\"firstRendered\").GetBoolean(),
      \"Legacy virtual record was not installed by the compatibility path.\");
    Require(result.GetProperty(\"changedRendered\").GetBoolean(),
      \"Changed legacy virtual record disappeared during full replacement.\");
    Require(result.GetProperty(\"changedWasReplaced\").GetBoolean(),
      \"Legacy virtual record was incorrectly treated as Core-keyed content.\");
    Require(string.Equals(
        result.GetProperty(\"textAfterChange\").GetString(),
        \"after\",
        StringComparison.Ordinal),
      \"Legacy full replacement did not install changed content.\");
  }

"""
  text = replace_once(
    text,
    "insert legacy compatibility regression",
    """  /// <summary>
  /// Canonical word-boundary updates are independent of transcript preparation.
""",
    insertion + """  /// <summary>
  /// Canonical word-boundary updates are independent of transcript preparation.
""")
  return text


def main() -> int:
  parser = argparse.ArgumentParser()
  parser.add_argument("--check", action="store_true")
  args = parser.parse_args()

  source_original = SOURCE.read_text(encoding="utf-8")
  test_original = TEST.read_text(encoding="utf-8")
  source_patched = patch_source(source_original)
  test_patched = patch_test(test_original)
  if source_patched == source_original or test_patched == test_original:
    raise RuntimeError("Issue #138 compatibility patch made no changes.")

  if args.check:
    print("Issue #138 compatibility patch dry-run passed.")
    return 0

  with SOURCE.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(source_patched)
  with TEST.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(test_patched)
  print("Issue #138 compatibility patch applied.")
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
