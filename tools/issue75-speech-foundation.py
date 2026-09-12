from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  target = Path(path)
  text = target.read_text(encoding='utf-8')
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one target, found {count}')
  target.write_text(text.replace(old, new), encoding='utf-8', newline='\n')


replace_once(
  'AgentPanelSpeaker/CanonicalHtmlUnitProjection.cs',
  '''  [property: JsonPropertyName("separator_before")] string SeparatorBefore,\n  [property: JsonPropertyName("provenance")]\n    CanonicalSpeechWordProvenanceProjection? Provenance);\n''',
  '''  [property: JsonPropertyName("separator_before")] string SeparatorBefore,\n  [property: JsonPropertyName("groups")] string[] Groups,\n  [property: JsonPropertyName("provenance")]\n    CanonicalSpeechWordProvenanceProjection? Provenance);\n''')

replace_once(
  'AgentPanelSpeaker/SpeechFragment.cs',
  '''  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false);\n''',
  '''  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false,\n  IReadOnlyList<long>? WordIds = null);\n''')

replace_once(
  'AgentPanelSpeaker/TranscriptPlaybackPosition.cs',
  '''  int CharacterPosition,\n  int CharacterCount,\n  long BoundaryTimestamp);\n''',
  '''  int CharacterPosition,\n  int CharacterCount,\n  long BoundaryTimestamp,\n  long? WordId = null);\n''')

path = Path('AgentPanelSpeaker/SpeechService.cs')
text = path.read_text(encoding='utf-8')
old = '''  /// <summary>\n  /// Moves the paused playback marker to one node-global lexical word when the\n  /// fragment containing that word is currently eligible for speech.\n  /// </summary>\n  public bool TrySeekToTranscriptWord(\n    long nodeId,\n    int nodeWordIndex,\n    out string text)\n'''
new = '''  /// <summary>\n  /// Moves the paused playback marker to one immutable Core transcript word ID\n  /// when its containing fragment is currently eligible for speech.\n  /// </summary>\n  public bool TrySeekToTranscriptWord(long wordId, out string text)\n  {\n    lock (_sync)\n    {\n      ThrowIfDisposed();\n      if (wordId < 1)\n      {\n        text = string.Empty;\n        return false;\n      }\n\n      for (int index = 0; index < _history.Count; ++index)\n      {\n        SpeechFragment fragment = _history[index];\n        IReadOnlyList<long>? wordIds = fragment.WordIds;\n        if (wordIds is null)\n        {\n          continue;\n        }\n        int localWordIndex = -1;\n        for (int candidate = 0; candidate < wordIds.Count; ++candidate)\n        {\n          if (wordIds[candidate] == wordId)\n          {\n            localWordIndex = candidate;\n            break;\n          }\n        }\n        if (localWordIndex < 0)\n        {\n          continue;\n        }\n        if (!TryGetEligibleProfileLocked(fragment, out _, out _))\n        {\n          text = string.Empty;\n          return false;\n        }\n\n        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;\n        _pendingUntracked = null;\n        ClearProcessingTimeAnnouncementLocked();\n        _pendingHistoryIndex = index;\n        _pendingHistoryWordIndex = localWordIndex;\n        _nextHistoryIndex = index;\n        _lastFenceActivity = null;\n        SetPausedLocked(true);\n        SetPausedNavigationPositionLocked(index, localWordIndex);\n        if (hadActiveSpeech)\n        {\n          RequestPauseRestoreAfterCancellationLocked(\n            "seek-canonical-transcript-word");\n          _engine.Cancel();\n        }\n        text = fragment.Text;\n        return true;\n      }\n\n      text = string.Empty;\n      return false;\n    }\n  }\n\n  /// <summary>\n  /// Legacy node/ordinal seek retained only until issue #76 removes the old\n  /// browser mapping path. New transcript interaction must use Core word IDs.\n  /// </summary>\n  public bool TrySeekToTranscriptWord(\n    long nodeId,\n    int nodeWordIndex,\n    out string text)\n'''
if text.count(old) != 1:
  raise RuntimeError('SpeechService seek insertion target mismatch')
text = text.replace(old, new)

old = '''      _activeCharacterCount,\n      _activeBoundaryTimestamp));\n  }\n\n\n  private static int GetTokenIndexForBoundary(\n'''
new = '''      _activeCharacterCount,\n      _activeBoundaryTimestamp,\n      GetActiveWordIdLocked()));\n  }\n\n  /// <summary>\n  /// Resolves the current speech token index to its immutable Core word ID.\n  /// App-synthesized narration intentionally has no transcript word identity.\n  /// </summary>\n  private long? GetActiveWordIdLocked()\n  {\n    if (_activeHistoryIndex < 0 || _activeHistoryIndex >= _history.Count)\n    {\n      return null;\n    }\n    IReadOnlyList<long>? wordIds = _history[_activeHistoryIndex].WordIds;\n    return wordIds is not null &&\n      _activeWordIndex >= 0 && _activeWordIndex < wordIds.Count\n        ? wordIds[_activeWordIndex]\n        : null;\n  }\n\n\n  private static int GetTokenIndexForBoundary(\n'''
if text.count(old) != 1:
  raise RuntimeError('SpeechService playback-report target mismatch')
text = text.replace(old, new)
path.write_text(text, encoding='utf-8', newline='\n')
