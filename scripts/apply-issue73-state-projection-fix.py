from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

def one(old, new, label):
  global text
  count = text.count(old)
  if count != 1:
    raise SystemExit(f'{label}: expected exactly 1 occurrence, found {count}')
  text = text.replace(old, new, 1)

# Reset retained browser-side playback only when the selected transcript changes.
one('''      _ = ExecuteAsync("resetDisclosureOpenOverrides();");''', '''      _ = ExecuteAsync(
        "resetDisclosureOpenOverrides(); resetRetainedPlayback();");''', 'select-session reset')
one('''        "resetDisclosureOpenOverrides(); replaceTranscript('', false, []);");''', '''        "resetDisclosureOpenOverrides(); resetRetainedPlayback(); " +
        "replaceTranscript('', false, []);");''', 'clear-session reset')

one('''let latestPlaybackSequence = 0;
let latestSettingsSequence = 0;''', '''let latestPlaybackSequence = 0;
let retainedPlayback = null;
let latestSettingsSequence = 0;''', 'retained playback declaration')

one('''const openDisclosureOverrides = new Set();
const programmaticDisclosureStates = new WeakMap();
let discardDisclosureStateOnNextReplacement = false;''', '''const openDisclosureOverrides = new Set();
let discardDisclosureStateOnNextReplacement = false;''', 'remove cause tracking')

old = '''function resetDisclosureOpenOverrides() {
  openDisclosureOverrides.clear();
  discardDisclosureStateOnNextReplacement = true;
}

function setDisclosureOpenProgrammatically(details, open) {
  const requested = !!open;
  if (!details || details.open === requested) return;
  programmaticDisclosureStates.set(details, requested);
  details.open = requested;
}

transcript.addEventListener('toggle', event => {
  const details = event.target;
  if (!(details instanceof HTMLDetailsElement)) return;
  const expected = programmaticDisclosureStates.get(details);
  if (expected !== undefined && expected === details.open) {
    programmaticDisclosureStates.delete(details);
    return;
  }
  const key = structureDetailsKey(details);
  if (!key) return;
  if (details.open) openDisclosureOverrides.add(key);
  else openDisclosureOverrides.delete(key);
}, true);'''
new = '''function resetDisclosureOpenOverrides() {
  openDisclosureOverrides.clear();
  discardDisclosureStateOnNextReplacement = true;
}

function rememberDisclosureState(details) {
  if (!(details instanceof HTMLDetailsElement)) return;
  const key = structureDetailsKey(details);
  if (!key) return;
  if (details.open) openDisclosureOverrides.add(key);
  else openDisclosureOverrides.delete(key);
}

function setDisclosureOpenProgrammatically(details, open) {
  if (!(details instanceof HTMLDetailsElement)) return;
  const requested = !!open;
  const key = structureDetailsKey(details);
  if (key) {
    if (requested) openDisclosureOverrides.add(key);
    else openDisclosureOverrides.delete(key);
  }
  if (details.open !== requested) details.open = requested;
}

transcript.addEventListener('toggle', event => {
  rememberDisclosureState(event.target);
}, true);'''
one(old, new, 'cause-independent disclosure state')

# Full-DOM replacement must remember the actual disclosure state regardless of
# why it became open/closed, even when scroll preservation is not requested.
one('''  const openDetails = new Map();
  if (preserve) {
    for (const details of transcript.querySelectorAll('details')) {
      openDetails.set(structureDetailsKey(details), details.open);
    }
  }

  const fragment = document.createDocumentFragment();''', '''  const openDetails = new Map();
  if (!discardDisclosureStateOnNextReplacement) {
    for (const details of transcript.querySelectorAll('details')) {
      const key = structureDetailsKey(details);
      if (preserve) openDetails.set(key, details.open);
      rememberDisclosureState(details);
    }
  }

  const fragment = document.createDocumentFragment();''', 'DOM disclosure capture')

# Once the old DOM is gone, its positional marker indices are invalid.  Reset
# only the ephemeral projection, not the retained playback state.
one('''  transcript.replaceChildren(fragment);
  applyRevisionVisibility(showRolledBackHistory);''', '''  transcript.replaceChildren(fragment);
  resetPlaybackProjectionState();
  applyRevisionVisibility(showRolledBackHistory);''', 'DOM projection reset')

# Restore retained playback after word/node identities have been rebuilt.
one('''  postMappingInstallSummary(nodeMap || []);
  postStructureStage(
    structureProbeId,
    'replace-dom-exit',''', '''  postMappingInstallSummary(nodeMap || []);
  restoreRetainedPlaybackProjection();
  postStructureStage(
    structureProbeId,
    'replace-dom-exit',''', 'DOM playback restore')

# Remove the obsolete end-of-replacement reset from replaceTranscriptDom.
one('''  windowStartIndex = 0;
  windowEndIndex = Number.MAX_SAFE_INTEGER;
  virtualShiftPending = false;
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  currentSpeechListItem = null;
  liveEndMarker.style.display = 'none';
  if (preserve) {''', '''  windowStartIndex = 0;
  windowEndIndex = Number.MAX_SAFE_INTEGER;
  virtualShiftPending = false;
  if (preserve) {''', 'remove DOM late reset')

# Virtual window capture writes the actual current state to persistent state.
one('''      const key = structureDetailsKey(details);
      localDetailsState.set(key, details.open);
      if (!details.open) openDisclosureOverrides.delete(key);''', '''      const key = structureDetailsKey(details);
      localDetailsState.set(key, details.open);
      rememberDisclosureState(details);''', 'window disclosure capture')

one('''  transcript.innerHTML = exactAssignedHtml;
  applyRevisionVisibility(showRolledBackHistory);''', '''  transcript.innerHTML = exactAssignedHtml;
  resetPlaybackProjectionState();
  applyRevisionVisibility(showRolledBackHistory);''', 'window projection reset')

# Restore the marker before measurement/anchor restoration so details opened by
# that marker are part of the materialized layout, but suppress viewport follow.
one('''  postMappingInstallSummary(nodeMap || []);
  const mappingSummaryMilliseconds = performance.now() - phaseStarted;''', '''  postMappingInstallSummary(nodeMap || []);
  restoreRetainedPlaybackProjection();
  const mappingSummaryMilliseconds = performance.now() - phaseStarted;''', 'window playback restore')

# Remove obsolete late reset in replaceTranscriptWindow.
one('''  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  currentSpeechListItem = null;
  liveEndMarker.style.display = 'none';
  if (preserve) {''', '''  if (preserve) {''', 'remove window late reset')

# Add retained/projection helpers immediately before the existing setPlayback
# function. Function declarations are hoisted, so replacements can call these.
one('''function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {''', '''function resetRetainedPlayback() {
  retainedPlayback = null;
  resetPlaybackProjectionState();
}

function resetPlaybackProjectionState() {
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  currentSpeechListItem = null;
  liveEndMarker.style.display = 'none';
}

function restoreRetainedPlaybackProjection() {
  if (!retainedPlayback) return;
  const preservedFollow = followSpeech;
  setPlayback(
    retainedPlayback.state,
    retainedPlayback.fragmentText,
    retainedPlayback.wordIndex,
    retainedPlayback.wordText,
    retainedPlayback.nodeId,
    false);
  // Projection restoration must never change the user's follow setting.
  setFollowSpeech(preservedFollow, false);
}

function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {''', 'projection helpers')

# Retain the authoritative playback payload independently from ephemeral DOM.
one('''  latestPlaybackSequence = sequence;
  setPlayback(
    data.state,''', '''  latestPlaybackSequence = sequence;
  retainedPlayback = {
    state:data.state,
    fragmentText:data.fragmentText,
    wordIndex:data.wordIndex,
    wordText:data.wordText,
    nodeId:data.nodeId
  };
  setPlayback(
    data.state,''', 'retain playback message')

path.write_text(text, encoding='utf-8')
