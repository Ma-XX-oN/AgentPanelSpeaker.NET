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
  updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
  if count != 1:
    raise RuntimeError(f'{path}: expected one regex replacement, found {count}')
  write(path, updated)


# Current seekability is expressed in the same transcript-global Core word-ID
# coordinate space as click, playback, and direct speech seeking.
replace_once(
  'AgentPanelSpeaker/SpeechService.cs',
  '''/// <summary>\n/// Identifies one contiguous node-global word range that is currently eligible\n/// for speech and direct transcript seeking.\n/// </summary>\ninternal sealed record SeekableTranscriptWordRange(\n  long NodeId,\n  int StartNodeWordIndex,\n  int WordCount);\n''',
  '''/// <summary>\n/// Identifies one contiguous Core word-ID range currently eligible for speech\n/// and direct transcript seeking.\n/// </summary>\ninternal sealed record SeekableTranscriptWordRange(\n  long StartWordId,\n  int WordCount);\n''')

replace_regex(
  'AgentPanelSpeaker/SpeechService.cs',
  r'''  /// <summary>\n  /// Returns the node-global word ranges that are eligible for speech under\n  /// the current profile, fence, and rolled-back-history policies\.\n  /// </summary>\n  public IReadOnlyList<SeekableTranscriptWordRange>\n    GetSeekableTranscriptWordRanges\(\)\n  \{.*?\n  \}\n\n  /// <summary>\n  /// Moves the paused playback marker to one immutable Core transcript word ID''',
  '''  /// <summary>\n  /// Returns contiguous Core word-ID ranges currently eligible for speech under\n  /// the current profile, fence, and rolled-back-history policies.\n  /// </summary>\n  public IReadOnlyList<SeekableTranscriptWordRange>\n    GetSeekableTranscriptWordRanges()\n  {\n    lock (_sync)\n    {\n      ThrowIfDisposed();\n      var ranges = new List<SeekableTranscriptWordRange>();\n      foreach (SpeechFragment fragment in _history)\n      {\n        IReadOnlyList<SpeechFragmentWord>? words = fragment.TranscriptWords;\n        if (words is null || words.Count == 0 ||\n            !TryGetEligibleProfileLocked(fragment, out _, out _))\n        {\n          continue;\n        }\n\n        foreach (SpeechFragmentWord word in words)\n        {\n          if (word.Id < 1)\n          {\n            throw new InvalidDataException(\n              "Core-backed speech history contains an invalid word ID.");\n          }\n          if (ranges.Count != 0)\n          {\n            SeekableTranscriptWordRange previous = ranges[^1];\n            if (previous.StartWordId + previous.WordCount == word.Id)\n            {\n              ranges[^1] = previous with\n              {\n                WordCount = checked(previous.WordCount + 1)\n              };\n              continue;\n            }\n          }\n          ranges.Add(new SeekableTranscriptWordRange(word.Id, 1));\n        }\n      }\n      return ranges.ToArray();\n    }\n  }\n\n  /// <summary>\n  /// Moves the paused playback marker to one immutable Core transcript word ID''')

replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''  /// Publishes the authoritative currently seekable node-global speech ranges\n  /// used by the Ctrl+click transcript affordance.\n''',
  '''  /// Publishes the authoritative currently seekable Core word-ID ranges used\n  /// by the Ctrl+click transcript affordance.\n''')

# Eligibility visuals live in one stylesheet rule. Policy changes rewrite that
# rule; they do not add/remove classes or attributes on every word node.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''body.voice-pointer-select-mode\n  .word.voice-selectable:not(.voice-excluded) {\n  outline: 1px solid var(--link);\n  outline-offset: 1px;\n  cursor: pointer;\n}\n''',
  '''/* Ctrl+click eligibility is installed centrally by refreshVoicePolicyCss(). */\n''')

replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''let seekableVoiceRanges = [];\nconst openDisclosureOverrides = new Set();\n''',
  '''let voicePolicyRanges = [];\nconst voicePolicyStyle = document.createElement('style');\nvoicePolicyStyle.id = 'voice-policy-style';\ndocument.head.append(voicePolicyStyle);\nconst openDisclosureOverrides = new Set();\n''')

# Replace legacy per-word voice class stamping with centralized Core-ID policy.
replace_regex(
  'AgentPanelSpeaker/TranscriptView.cs',
  r'''function markVoiceSelectableWords\(.*?\nfunction lexicalWordsCanJoin''',
  r'''function normalizeVoicePolicyRanges(ranges) {
  const normalized = (ranges || []).map(range => ({
    startWordId:Number(range.StartWordId ?? range.startWordId ?? 0),
    wordCount:Number(range.WordCount ?? range.wordCount ?? 0)
  })).filter(range =>
    Number.isSafeInteger(range.startWordId) && range.startWordId > 0 &&
    Number.isSafeInteger(range.wordCount) && range.wordCount > 0)
    .sort((left, right) => left.startWordId - right.startWordId);
  const merged = [];
  for (const range of normalized) {
    const previous = merged.at(-1);
    if (previous &&
        previous.startWordId + previous.wordCount >= range.startWordId) {
      const end = Math.max(
        previous.startWordId + previous.wordCount,
        range.startWordId + range.wordCount);
      previous.wordCount = end - previous.startWordId;
    } else {
      merged.push({...range});
    }
  }
  return merged;
}

function isVoiceWordEligible(element) {
  const wordId = canonicalWordId(element);
  if (wordId <= 0) return false;
  let low = 0;
  let high = voicePolicyRanges.length - 1;
  while (low <= high) {
    const middle = (low + high) >> 1;
    const range = voicePolicyRanges[middle];
    if (wordId < range.startWordId) {
      high = middle - 1;
      continue;
    }
    const end = range.startWordId + range.wordCount;
    if (wordId >= end) {
      low = middle + 1;
      continue;
    }
    return true;
  }
  return false;
}

function refreshVoicePolicyCss() {
  const selectors = [];
  for (const owner of transcript.querySelectorAll('[id^="word-"]')) {
    if (!isVoiceWordEligible(owner)) continue;
    selectors.push('#' + CSS.escape(owner.id));
  }
  voicePolicyStyle.textContent = selectors.length === 0
    ? ''
    : 'body.voice-pointer-select-mode :is(' + selectors.join(',') + ') {' +
      'outline:1px solid var(--link);outline-offset:1px;cursor:pointer;}';
}

function setVoicePolicy(ranges) {
  voicePolicyRanges = normalizeVoicePolicyRanges(ranges);
  refreshVoicePolicyCss();
}

function isSeekableVoiceWord(word) {
  return isVoiceWordEligible(word);
}

// Legacy node/text mapping still exists for playback until issue #76 removes it.
// These hooks intentionally do not stamp eligibility identity onto word nodes.
function markVoiceSelectableWords() {}
function markVoiceSelectableWordsByGlobalRange() {}
function markAlignedVoiceSelectableWords() {}

function lexicalWordsCanJoin''')

# The original aligned mapping helper is declared later than lexicalWordsCanJoin;
# remove that second declaration so it cannot reintroduce eligibility classes.
replace_regex(
  'AgentPanelSpeaker/TranscriptView.cs',
  r'''function markAlignedVoiceSelectableWords\(.*?\n\}\n\nfunction assignNodeScopes''',
  '''function assignNodeScopes''')

# Every materialization refreshes one centralized stylesheet rule after the
# legacy playback scope install, rather than mutating each word's policy class.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''  applyVoiceEligibilityClasses();\n}\n\nfunction chooseNearestRange''',
  '''  refreshVoicePolicyCss();\n}\n\nfunction chooseNearestRange''')

replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''  if (data.type === 'seekable-voice-ranges') {\n    setSeekableVoiceRanges(data.ranges ?? data.Ranges ?? []);\n    return;\n  }\n''',
  '''  if (data.type === 'seekable-voice-ranges') {\n    setVoicePolicy(data.ranges ?? data.Ranges ?? []);\n    return;\n  }\n''')

# Numeric boundary script has already replaced the click selector with Core IDs;
# require current centralized policy before posting the immutable handle.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''  const word = event.target.closest('[id^="word-"]');\n  if (!word || !transcript.contains(word)) return;\n  const wordId = canonicalWordId(word);\n''',
  '''  const word = event.target.closest('[id^="word-"]');\n  if (!word || !transcript.contains(word) || !isVoiceWordEligible(word)) return;\n  const wordId = canonicalWordId(word);\n''')
