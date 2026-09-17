from __future__ import annotations

import argparse
from pathlib import Path


SOURCE = Path("AgentPanelSpeaker/TranscriptView.cs")


def replace_once(text: str, label: str, old: str, new: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{label}: expected exactly one source match, found {count}")
  return text.replace(old, new, 1)


def build_patched_text(text: str) -> str:
  text = replace_once(
    text,
    "canonical playback refresh gate",
    """    if (!_initialized || _refreshInProgress)
    {
      return;
    }

    // A Core-backed word is resolved directly by the WebView when materialized.
""",
    """    if (!_initialized)
    {
      return;
    }

    // A Core-backed word is resolved directly by the WebView when materialized.
""")

  text = replace_once(
    text,
    "legacy playback refresh gate",
    """      PostPlaybackPosition(position);
      return;
    }

    // Non-canonical synthesized narration retains the pre-migration node path
""",
    """      PostPlaybackPosition(position);
      return;
    }

    if (_refreshInProgress)
    {
      return;
    }

    // Non-canonical synthesized narration retains the pre-migration node path
""")

  text = replace_once(
    text,
    "virtual record source state",
    """const openDisclosureOverrides = new Set();
let discardDisclosureStateOnNextReplacement = false;
""",
    """const openDisclosureOverrides = new Set();
let discardDisclosureStateOnNextReplacement = false;
const virtualRecordSourceHtml = new WeakMap();
""")

  text = replace_once(
    text,
    "virtual reconciliation helpers",
    """function wrapWords(nodeMap = null) {
  words = [];
  lexicalWords = [];
  ensureCoreOrdinalSpeechMaps();
  wrapWordsForRecordKeys(
    nodeMap === null ? null : nodeRecordKeys(nodeMap),
    false);
}

function structureDetailsKey(details) {
""",
    """function wrapWords(nodeMap = null) {
  words = [];
  lexicalWords = [];
  ensureCoreOrdinalSpeechMaps();
  wrapWordsForRecordKeys(
    nodeMap === null ? null : nodeRecordKeys(nodeMap),
    false);
}

function reindexWrappedWords() {
  words = Array.from(transcript.querySelectorAll('.word'));
  lexicalWords = words.filter(word => word.dataset.lexical === '1');
  for (let index = 0; index < words.length; ++index) {
    words[index].dataset.index = String(index);
  }
}

function virtualRecordUnitId(record) {
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
  for (const incoming of template.content.children) {
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

function structureDetailsKey(details) {
""")

  text = replace_once(
    text,
    "whole-window innerHTML replacement",
    """  transcript.innerHTML = exactAssignedHtml;
  resetPlaybackProjectionState();
""",
    """  reconcileTranscriptWindow(
    html,
    topSpacerHeight,
    bottomSpacerHeight);
  resetPlaybackProjectionState();
""")

  text = replace_once(
    text,
    "retained word reindex",
    """  phaseStarted = performance.now();
  wrapWords(nodeMap || []);
  wrapSpeechFragments();
  const wrapWordsMilliseconds = performance.now() - phaseStarted;
""",
    """  phaseStarted = performance.now();
  wrapWords(nodeMap || []);
  reindexWrappedWords();
  wrapSpeechFragments();
  const wrapWordsMilliseconds = performance.now() - phaseStarted;
""")
  return text


def main() -> int:
  parser = argparse.ArgumentParser()
  parser.add_argument("--check", action="store_true")
  args = parser.parse_args()

  original = SOURCE.read_text(encoding="utf-8")
  patched = build_patched_text(original)
  if args.check:
    if patched == original:
      raise RuntimeError("Issue #138 patch made no changes.")
    print("Issue #138 patch dry-run passed.")
    return 0

  with SOURCE.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(patched)
  print("Issue #138 production patch applied.")
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
