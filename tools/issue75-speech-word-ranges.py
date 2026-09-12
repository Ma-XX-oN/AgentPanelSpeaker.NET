from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  target = Path(path)
  text = target.read_text(encoding='utf-8')
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one replacement target, found {count}')
  target.write_text(text.replace(old, new), encoding='utf-8', newline='\n')


def replace_between(path: str, start: str, end: str, replacement: str) -> None:
  target = Path(path)
  text = target.read_text(encoding='utf-8')
  start_index = text.find(start)
  if start_index < 0:
    raise RuntimeError(f'{path}: start marker not found: {start!r}')
  end_index = text.find(end, start_index)
  if end_index < 0:
    raise RuntimeError(f'{path}: end marker not found: {end!r}')
  target.write_text(
    text[:start_index] + replacement + text[end_index:],
    encoding='utf-8',
    newline='\n')


# One Core-backed transcript word carries both the immutable identity and its
# exact character range in the app-owned utterance text. WordIds remains a
# derived compatibility view during the #75/#76 migration; identity is stored
# only once in TranscriptWords.
replace_once(
  'AgentPanelSpeaker/SpeechFragment.cs',
  '''internal sealed record SpeechFragment(\n  long NodeId,\n  ContentCategory Category,\n  SpeechFragmentKind Kind,\n  string Text,\n  string FenceType = "",\n  int FenceBlockId = -1,\n  int FenceLineIndex = -1,\n  int FenceLineCount = 0,\n  bool PauseAfter = false,\n  DateTimeOffset? NodeTimestampUtc = null,\n  bool StartsUserTurn = false,\n  string? RevisionStatus = null,\n  int? RevisionDepth = null,\n  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false,\n  IReadOnlyList<long>? WordIds = null);\n''',
  '''/// <summary>\n/// Maps one immutable Core transcript word into one app-owned speech fragment.\n/// </summary>\ninternal sealed record SpeechFragmentWord(\n  long Id,\n  string Text,\n  int CharacterStart,\n  int CharacterLength);\n\ninternal sealed record SpeechFragment(\n  long NodeId,\n  ContentCategory Category,\n  SpeechFragmentKind Kind,\n  string Text,\n  string FenceType = "",\n  int FenceBlockId = -1,\n  int FenceLineIndex = -1,\n  int FenceLineCount = 0,\n  bool PauseAfter = false,\n  DateTimeOffset? NodeTimestampUtc = null,\n  bool StartsUserTurn = false,\n  string? RevisionStatus = null,\n  int? RevisionDepth = null,\n  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false,\n  IReadOnlyList<SpeechFragmentWord>? TranscriptWords = null)\n{\n  /// <summary>\n  /// Temporary migration view of the Core IDs carried by TranscriptWords.\n  /// </summary>\n  public IReadOnlyList<long>? WordIds => TranscriptWords?\n    .Select(static word => word.Id)\n    .ToArray();\n}\n''')

# ProcessNode now carries exact Core word ranges rather than a parallel ID list.
replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''    IReadOnlyList<SpeechTextPart> parts;\n    IReadOnlyList<IReadOnlyList<long>?> partWordIds;\n    if (node.CanonicalWords is { Count: > 0 } canonicalWords)\n    {\n      (parts, partWordIds) = BuildCanonicalSpeechParts(canonicalWords);\n    }\n    else\n    {\n      parts = TextCleaner.ParseForSpeech(node.Text);\n      partWordIds = Enumerable\n        .Repeat<IReadOnlyList<long>?>(null, parts.Count)\n        .ToArray();\n    }\n''',
  '''    IReadOnlyList<SpeechTextPart> parts;\n    IReadOnlyList<IReadOnlyList<SpeechFragmentWord>?> partTranscriptWords;\n    if (node.CanonicalWords is { Count: > 0 } canonicalWords)\n    {\n      (parts, partTranscriptWords) = BuildCanonicalSpeechParts(canonicalWords);\n    }\n    else\n    {\n      parts = TextCleaner.ParseForSpeech(node.Text);\n      partTranscriptWords = Enumerable\n        .Repeat<IReadOnlyList<SpeechFragmentWord>?>(null, parts.Count)\n        .ToArray();\n    }\n''')
replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''      IReadOnlyList<long>? canonicalPartWordIds = partWordIds[partIndex];\n      if (canonicalPartWordIds is not null)\n''',
  '''      IReadOnlyList<SpeechFragmentWord>? canonicalPartWords =\n        partTranscriptWords[partIndex];\n      if (canonicalPartWords is not null)\n''')
replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''          HistoricalRevision: node.HistoricalRevision,\n          WordIds: canonicalPartWordIds));\n''',
  '''          HistoricalRevision: node.HistoricalRevision,\n          TranscriptWords: canonicalPartWords));\n''')

canonical_parts_start = '''  /// <summary>\n  /// Builds transcript-backed speech parts directly from the authoritative Core\n'''
canonical_parts_end = '''  private static string FenceType(CanonicalSpeechWordProjection word)\n'''
canonical_parts = r'''  /// <summary>
  /// Builds transcript-backed speech parts directly from the authoritative Core
  /// word stream. Core separators preserve word adjacency; APS never retokenizes
  /// these words to decide identity. Ordered-list ordinals such as `1.` remain
  /// one canonical word and begin their own list-item part after a line break.
  /// </summary>
  private static (
    IReadOnlyList<SpeechTextPart> Parts,
    IReadOnlyList<IReadOnlyList<SpeechFragmentWord>?> TranscriptWords)
      BuildCanonicalSpeechParts(
        IReadOnlyList<CanonicalSpeechWordProjection> words)
  {
    var groups = new List<List<CanonicalSpeechWordProjection>>();
    var current = new List<CanonicalSpeechWordProjection>();
    string currentFence = string.Empty;

    foreach (CanonicalSpeechWordProjection word in words)
    {
      string fence = FenceType(word);
      bool fenceChanged = current.Count != 0 &&
        !string.Equals(fence, currentFence, StringComparison.OrdinalIgnoreCase);
      bool newFenceLine = current.Count != 0 &&
        fence.Length != 0 &&
        word.SeparatorBefore.Contains('\n');
      bool newOrderedItem = current.Count != 0 &&
        fence.Length == 0 &&
        word.SeparatorBefore.Contains('\n') &&
        IsOrderedListOrdinal(word.Text);
      if (fenceChanged || newFenceLine || newOrderedItem)
      {
        groups.Add(current);
        current = new List<CanonicalSpeechWordProjection>();
      }
      if (current.Count == 0)
      {
        currentFence = fence;
      }
      current.Add(word);
    }
    if (current.Count != 0)
    {
      groups.Add(current);
    }

    var parts = new List<SpeechTextPart>();
    var transcriptWords = new List<IReadOnlyList<SpeechFragmentWord>?>();
    int fenceLineIndex = 0;
    int fenceLineCount = groups.Count(group => FenceType(group[0]).Length != 0);
    foreach (List<CanonicalSpeechWordProjection> group in groups)
    {
      string fenceType = FenceType(group[0]);
      if (fenceType.Length != 0)
      {
        (string line, SpeechFragmentWord[] mappedWords) =
          BuildCanonicalFragment(group, preserveWhitespace: true);
        if (line.Length != 0)
        {
          parts.Add(new SpeechTextPart(
            SpeechFragmentKind.FencedCodeLine,
            line,
            fenceType,
            FenceBlockId: 0,
            FenceLineIndex: fenceLineIndex++,
            FenceLineCount: fenceLineCount,
            PauseAfter: true,
            SpeechTextStyle.Main));
          transcriptWords.Add(mappedWords);
        }
        continue;
      }

      AddCanonicalProseParts(group, parts, transcriptWords);
    }
    return (parts, transcriptWords);
  }

  /// <summary>
  /// Splits one Core prose/list-item word run at canonical sentence punctuation.
  /// Structural ordered-list ordinals contain their own dot and are therefore
  /// not punctuation tokens here.
  /// </summary>
  private static void AddCanonicalProseParts(
    IReadOnlyList<CanonicalSpeechWordProjection> words,
    ICollection<SpeechTextPart> parts,
    ICollection<IReadOnlyList<SpeechFragmentWord>?> transcriptWords)
  {
    int start = 0;
    for (int index = 0; index < words.Count; ++index)
    {
      if (words[index].Text is not ("." or "?" or "!"))
      {
        continue;
      }
      int end = index + 1;
      while (end < words.Count &&
             words[end].SeparatorBefore.Length == 0 &&
             words[end].Text is "\"" or "'" or ")" or "]" or "}")
      {
        ++end;
      }
      AddCanonicalProsePart(
        words,
        start,
        end,
        parts,
        transcriptWords,
        pauseAfter: false);
      start = end;
      index = end - 1;
    }
    if (start < words.Count)
    {
      AddCanonicalProsePart(
        words,
        start,
        words.Count,
        parts,
        transcriptWords,
        pauseAfter: true);
    }
    else if (parts.Count != 0 && parts.Last().PauseAfter is false)
    {
      SpeechTextPart last = parts.Last();
      parts.Remove(last);
      parts.Add(last with { PauseAfter = true });
    }
  }

  private static void AddCanonicalProsePart(
    IReadOnlyList<CanonicalSpeechWordProjection> words,
    int start,
    int end,
    ICollection<SpeechTextPart> parts,
    ICollection<IReadOnlyList<SpeechFragmentWord>?> transcriptWords,
    bool pauseAfter)
  {
    CanonicalSpeechWordProjection[] slice = words
      .Skip(start)
      .Take(end - start)
      .ToArray();
    if (slice.Length == 0)
    {
      return;
    }
    (string text, SpeechFragmentWord[] mappedWords) =
      BuildCanonicalFragment(slice, preserveWhitespace: false);
    parts.Add(new SpeechTextPart(
      SpeechFragmentKind.Prose,
      text,
      string.Empty,
      FenceBlockId: -1,
      FenceLineIndex: -1,
      FenceLineCount: 0,
      PauseAfter: pauseAfter,
      SpeechTextStyle.Main));
    transcriptWords.Add(mappedWords);
  }

  /// <summary>
  /// Reconstructs one app-owned utterance from Core words while recording each
  /// authoritative word's exact character range in that utterance.  A non-empty
  /// Core separator either remains exact (code) or becomes one speech space
  /// (prose); no tokenizer or visible-text search participates.
  /// </summary>
  private static (string Text, SpeechFragmentWord[] Words)
    BuildCanonicalFragment(
      IReadOnlyList<CanonicalSpeechWordProjection> words,
      bool preserveWhitespace)
  {
    var text = new StringBuilder();
    var mappedWords = new List<SpeechFragmentWord>(words.Count);
    for (int index = 0; index < words.Count; ++index)
    {
      CanonicalSpeechWordProjection word = words[index];
      if (index != 0 && word.SeparatorBefore.Length != 0)
      {
        text.Append(preserveWhitespace ? word.SeparatorBefore : " ");
      }
      int characterStart = text.Length;
      text.Append(word.Text);
      mappedWords.Add(new SpeechFragmentWord(
        word.Id,
        word.Text,
        characterStart,
        word.Text.Length));
    }
    return (text.ToString(), mappedWords.ToArray());
  }

'''
replace_between(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  canonical_parts_start,
  canonical_parts_end,
  canonical_parts)

# Canonical direct seek searches only the fragment's Core-backed word records.
seek_start = '''  /// <summary>\n  /// Moves the paused playback marker to one immutable Core transcript word ID\n'''
seek_end = '''  /// <summary>\n  /// Legacy node/ordinal seek retained only until issue #76 removes the old\n'''
seek_method = r'''  /// <summary>
  /// Moves the paused playback marker to one immutable Core transcript word ID
  /// when its containing fragment is currently eligible for speech.
  /// </summary>
  public bool TrySeekToTranscriptWord(long wordId, out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (wordId < 1)
      {
        text = string.Empty;
        return false;
      }

      for (int index = 0; index < _history.Count; ++index)
      {
        SpeechFragment fragment = _history[index];
        IReadOnlyList<SpeechFragmentWord>? words = fragment.TranscriptWords;
        if (words is null)
        {
          continue;
        }
        int localWordIndex = -1;
        for (int candidate = 0; candidate < words.Count; ++candidate)
        {
          if (words[candidate].Id == wordId)
          {
            localWordIndex = candidate;
            break;
          }
        }
        if (localWordIndex < 0)
        {
          continue;
        }
        if (!TryGetEligibleProfileLocked(fragment, out _, out _))
        {
          text = string.Empty;
          return false;
        }

        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
        _pendingUntracked = null;
        ClearProcessingTimeAnnouncementLocked();
        _pendingHistoryIndex = index;
        _pendingHistoryWordIndex = localWordIndex;
        _nextHistoryIndex = index;
        _lastFenceActivity = null;
        SetPausedLocked(true);
        SetPausedNavigationPositionLocked(index, localWordIndex);
        if (hadActiveSpeech)
        {
          RequestPauseRestoreAfterCancellationLocked(
            "seek-canonical-transcript-word");
          _engine.Cancel();
        }
        text = fragment.Text;
        return true;
      }

      text = string.Empty;
      return false;
    }
  }

'''
replace_between('AgentPanelSpeaker/SpeechService.cs', seek_start, seek_end, seek_method)

# Start history playback from the Core-backed character range when available.
replace_once(
  'AgentPanelSpeaker/SpeechService.cs',
  '''      StartHistorySpeechLocked(\n        fragment.Text,\n        profile,\n''',
  '''      StartHistorySpeechLocked(\n        fragment,\n        profile,\n''')
start_history_start = '''  /// <summary>\n  /// Starts one history fragment at a selected token while preserving the\n'''
start_history_end = '''  /// <summary>\n  /// Starts one prompt and restores idle state when configuration fails.\n'''
start_history_method = r'''  /// <summary>
  /// Starts one history fragment at a selected canonical/synthetic word while
  /// preserving the full transcript text used by the marker and search model.
  /// </summary>
  private void StartHistorySpeechLocked(
    SpeechFragment fragment,
    SpeechProfileSettings profile,
    bool pauseAfter,
    int wordIndex,
    bool pauseBefore = false)
  {
    int wordCount = GetFragmentWordCount(fragment);
    int boundedWordIndex = wordCount == 0
      ? 0
      : Math.Clamp(wordIndex, 0, wordCount - 1);
    int characterStart = GetFragmentWordCharacterPosition(
      fragment,
      boundedWordIndex);
    string spokenText = fragment.Text[characterStart..];

    _activeTranscriptText = fragment.Text;
    _activeWordIndex = boundedWordIndex;
    _activeWordBaseIndex = boundedWordIndex;
    _activeCharacterBaseOffset = characterStart;
    _activeCharacterPosition = characterStart;
    _activeWord = GetFragmentWordText(
      fragment,
      boundedWordIndex,
      FirstWord(spokenText));
    _activeCharacterCount = GetFragmentWordCharacterCount(
      fragment,
      boundedWordIndex,
      _activeWord.Length);
    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
    _activeProfile = profile.Normalize();
    _activePauseAfter = pauseAfter;
    _pauseStartedUtc = null;
    SetActiveKindLocked(ActiveSpeechKind.History);
    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
    try
    {
      SpeakConfiguredLocked(
        spokenText,
        profile,
        pauseAfter,
        pauseBefore);
    }
    catch
    {
      _activeHistoryIndex = -1;
      SetActiveKindLocked(ActiveSpeechKind.None);
      throw;
    }
  }

'''
replace_between(
  'AgentPanelSpeaker/SpeechService.cs',
  start_history_start,
  start_history_end,
  start_history_method)

# Restart uses the same canonical range instead of re-tokenizing Core text.
restart_start = '''  /// <summary>\n  /// Restarts a long-paused history utterance at the current word boundary.\n'''
restart_end = '''  private void ReportPlaybackPositionLocked(TranscriptPlaybackState state)\n'''
restart_method = r'''  /// <summary>
  /// Restarts a long-paused history utterance at the current word boundary.
  /// </summary>
  private void RestartCurrentWordLocked()
  {
    if (_activeProfile is null ||
        _activeTranscriptText.Length == 0 ||
        _activeHistoryIndex < 0 ||
        _activeHistoryIndex >= _history.Count)
    {
      _engine.Resume();
      return;
    }
    SpeechFragment fragment = _history[_activeHistoryIndex];
    int start = GetFragmentWordCharacterPosition(fragment, _activeWordIndex);
    string remaining = _activeTranscriptText[start..];
    _activeWordBaseIndex = _activeWordIndex;
    _activeCharacterBaseOffset = start;
    _activeCharacterPosition = start;
    _activeWord = GetFragmentWordText(
      fragment,
      _activeWordIndex,
      FirstWord(remaining));
    _activeCharacterCount = GetFragmentWordCharacterCount(
      fragment,
      _activeWordIndex,
      _activeWord.Length);
    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
    SpeakConfiguredLocked(
      remaining,
      _activeProfile,
      _activePauseAfter);
    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
  }

'''
replace_between(
  'AgentPanelSpeaker/SpeechService.cs',
  restart_start,
  restart_end,
  restart_method)

# SAPI word events map by exact Core-backed character overlap for transcript
# fragments. A miss is ignored and diagnosed; it never falls back to token index.
boundary_start = '''  /// <summary>\n  /// Advances the rendered transcript marker at one audio word boundary.\n'''
boundary_end = '''  /// <summary>\n  /// Advances serialized playback after one prompt completes or is cancelled.\n'''
boundary_method = r'''  /// <summary>
  /// Advances the rendered transcript marker at one audio word boundary.
  /// </summary>
  private void EngineWordBoundary(SpeechWordBoundary boundary)
  {
    lock (_sync)
    {
      if (_disposed ||
          _activeKind != ActiveSpeechKind.History ||
          _activeHistoryIndex < 0 ||
          _activeHistoryIndex >= _history.Count)
      {
        return;
      }
      SpeechFragment fragment = _history[_activeHistoryIndex];
      int absoluteCharacterPosition = checked(
        _activeCharacterBaseOffset + Math.Max(0, boundary.CharacterPosition));
      if (boundary.CharacterCount == 0 &&
          absoluteCharacterPosition >= _activeTranscriptText.Length)
      {
        DiagnosticLog.Write("speech.word_boundary_terminal_ignored", new
        {
          fragment.NodeId,
          activeHistoryIndex = _activeHistoryIndex,
          activeFragmentText = _activeTranscriptText,
          boundary.WordIndex,
          boundary.CharacterPosition,
          boundary.CharacterCount,
          boundary.Text,
          boundary.AudioPosition,
          characterBaseOffset = _activeCharacterBaseOffset,
          absoluteCharacterPosition,
          retainedWordIndex = _activeWordIndex
        });
        return;
      }

      int mappedWordIndex = GetFragmentWordIndexForBoundary(
        fragment,
        absoluteCharacterPosition,
        boundary.CharacterCount,
        _activeWordBaseIndex + boundary.WordIndex);
      if (mappedWordIndex < 0)
      {
        DiagnosticLog.Write("speech.canonical_word_boundary_miss", new
        {
          fragment.NodeId,
          activeHistoryIndex = _activeHistoryIndex,
          boundary.WordIndex,
          boundary.CharacterPosition,
          boundary.CharacterCount,
          boundary.Text,
          characterBaseOffset = _activeCharacterBaseOffset,
          absoluteCharacterPosition
        });
        return;
      }

      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);
      _activeWord = GetFragmentWordText(
        fragment,
        _activeWordIndex,
        boundary.Text);
      if (fragment.TranscriptWords is { Count: > 0 } transcriptWords)
      {
        SpeechFragmentWord word = transcriptWords[_activeWordIndex];
        _activeCharacterPosition = word.CharacterStart;
        _activeCharacterCount = word.CharacterLength;
      }
      else
      {
        _activeCharacterPosition = Math.Max(
          _activeCharacterPosition,
          absoluteCharacterPosition);
        _activeCharacterCount = Math.Max(0, boundary.CharacterCount);
      }
      _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
      DiagnosticLog.Write("speech.word_boundary", new
      {
        fragment.NodeId,
        activeHistoryIndex = _activeHistoryIndex,
        activeFragmentText = _activeTranscriptText,
        boundary.WordIndex,
        boundary.CharacterPosition,
        boundary.CharacterCount,
        boundary.Text,
        boundary.AudioPosition,
        boundary.Exact,
        characterBaseOffset = _activeCharacterBaseOffset,
        wordBaseIndex = _activeWordBaseIndex,
        absoluteCharacterPosition,
        mappedWordIndex = _activeWordIndex,
        mappedWord = _activeWord,
        timestamp = _activeBoundaryTimestamp
      });
      ReportPlaybackPositionLocked(
        _isPaused
          ? TranscriptPlaybackState.Paused
          : TranscriptPlaybackState.Speaking);
    }
  }

'''
replace_between(
  'AgentPanelSpeaker/SpeechService.cs',
  boundary_start,
  boundary_end,
  boundary_method)

# Active word ID comes from the same range record used for position/text.
replace_once(
  'AgentPanelSpeaker/SpeechService.cs',
  '''    IReadOnlyList<long>? wordIds = _history[_activeHistoryIndex].WordIds;\n    return wordIds is not null &&\n      _activeWordIndex >= 0 && _activeWordIndex < wordIds.Count\n        ? wordIds[_activeWordIndex]\n        : null;\n''',
  '''    IReadOnlyList<SpeechFragmentWord>? words =\n      _history[_activeHistoryIndex].TranscriptWords;\n    return words is not null &&\n      _activeWordIndex >= 0 && _activeWordIndex < words.Count\n        ? words[_activeWordIndex].Id\n        : null;\n''')

# Add the explicit canonical/synthetic word helpers before the old synthetic-only
# token helper. TranscriptWords != null is a semantic mode, not a fallback probe.
helper_marker = '''  private static int GetTokenIndexForBoundary(\n'''
helpers = r'''  private static int GetFragmentWordCount(SpeechFragment fragment)
  {
    return fragment.TranscriptWords is { } transcriptWords
      ? transcriptWords.Count
      : SpeechTokenization.Matches(fragment.Text).Count;
  }

  private static int GetFragmentWordCharacterPosition(
    SpeechFragment fragment,
    int wordIndex)
  {
    if (fragment.TranscriptWords is { } transcriptWords)
    {
      if (transcriptWords.Count == 0)
      {
        return 0;
      }
      int bounded = Math.Clamp(wordIndex, 0, transcriptWords.Count - 1);
      return transcriptWords[bounded].CharacterStart;
    }
    return GetWordCharacterPosition(fragment.Text, wordIndex);
  }

  private static int GetFragmentWordCharacterCount(
    SpeechFragment fragment,
    int wordIndex,
    int syntheticFallback)
  {
    if (fragment.TranscriptWords is { } transcriptWords)
    {
      if (transcriptWords.Count == 0)
      {
        return 0;
      }
      int bounded = Math.Clamp(wordIndex, 0, transcriptWords.Count - 1);
      return transcriptWords[bounded].CharacterLength;
    }
    return syntheticFallback;
  }

  private static string GetFragmentWordText(
    SpeechFragment fragment,
    int wordIndex,
    string syntheticFallback)
  {
    if (fragment.TranscriptWords is { } transcriptWords)
    {
      if (transcriptWords.Count == 0)
      {
        return string.Empty;
      }
      int bounded = Math.Clamp(wordIndex, 0, transcriptWords.Count - 1);
      return transcriptWords[bounded].Text;
    }
    return GetTokenAtIndex(fragment.Text, wordIndex, syntheticFallback);
  }

  private static int GetFragmentWordIndexForBoundary(
    SpeechFragment fragment,
    int characterPosition,
    int characterCount,
    int syntheticFallbackWordIndex)
  {
    if (fragment.TranscriptWords is { } transcriptWords)
    {
      if (transcriptWords.Count == 0)
      {
        return -1;
      }
      int boundedPosition = Math.Clamp(
        characterPosition,
        0,
        fragment.Text.Length);
      int boundaryEnd = Math.Clamp(
        checked(boundedPosition + Math.Max(1, characterCount)),
        boundedPosition,
        fragment.Text.Length);
      for (int index = 0; index < transcriptWords.Count; ++index)
      {
        SpeechFragmentWord word = transcriptWords[index];
        int wordEnd = checked(word.CharacterStart + word.CharacterLength);
        if (word.CharacterStart < boundaryEnd && wordEnd > boundedPosition)
        {
          return index;
        }
      }
      return -1;
    }
    return GetTokenIndexForBoundary(
      fragment.Text,
      characterPosition,
      characterCount,
      syntheticFallbackWordIndex);
  }

'''
replace_once(
  'AgentPanelSpeaker/SpeechService.cs',
  helper_marker,
  helpers + helper_marker)

# Paused marker positioning also uses the exact Core range for transcript-backed
# fragments.
paused_start = '''  /// <summary>\n  /// Moves the transcript marker to a selected history fragment without\n'''
paused_end = '''  /// <summary>\n  /// Returns the current navigation anchor.\n'''
paused_method = r'''  /// <summary>
  /// Moves the transcript marker to a selected history fragment without
  /// starting its audio.
  /// </summary>
  private void SetPausedNavigationPositionLocked(
    int index,
    int wordIndex = 0)
  {
    SpeechFragment fragment = _history[index];
    int wordCount = GetFragmentWordCount(fragment);
    int boundedWordIndex = wordCount == 0
      ? 0
      : Math.Clamp(wordIndex, 0, wordCount - 1);
    _activeHistoryIndex = index;
    _activeTranscriptText = fragment.Text;
    _activeWordIndex = boundedWordIndex;
    _activeWordBaseIndex = 0;
    _activeCharacterBaseOffset = 0;
    _activeCharacterPosition = GetFragmentWordCharacterPosition(
      fragment,
      boundedWordIndex);
    _activeWord = GetFragmentWordText(
      fragment,
      boundedWordIndex,
      FirstWord(fragment.Text));
    _activeCharacterCount = GetFragmentWordCharacterCount(
      fragment,
      boundedWordIndex,
      _activeWord.Length);
    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
    ReportPlaybackPositionLocked(TranscriptPlaybackState.Paused);
    DiagnosticLog.Write("speech.paused_navigation", new
    {
      historyIndex = index,
      fragment.NodeId,
      fragment.Text,
      wordIndex = _activeWordIndex,
      word = _activeWord,
      characterPosition = _activeCharacterPosition,
      characterCount = _activeCharacterCount
    });
  }

'''
replace_between(
  'AgentPanelSpeaker/SpeechService.cs',
  paused_start,
  paused_end,
  paused_method)

# Strengthen the permanent ordered-list test: range 0 is the whole `1.` ordinal,
# and range 1 begins exactly at `Item`.
replace_once(
  'AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs',
  '''      long[] actualIds = fragment.WordIds?.ToArray() ?? Array.Empty<long>();\n      Require(actualIds.SequenceEqual(expectedIds),\n        "Production ordered-list history did not carry Core ordinal/body IDs.");\n\n      using var speech = new SpeechService();\n''',
  '''      long[] actualIds = fragment.WordIds?.ToArray() ?? Array.Empty<long>();\n      Require(actualIds.SequenceEqual(expectedIds),\n        "Production ordered-list history did not carry Core ordinal/body IDs.");\n      IReadOnlyList<SpeechFragmentWord> mappedWords = fragment.TranscriptWords ??\n        throw new InvalidOperationException(\n          "Production ordered-list fragment omitted Core word ranges.");\n      Require(mappedWords.Count == 2,\n        $"Expected two ordered-list Core ranges, got {mappedWords.Count}.");\n      Require(mappedWords[0].Text == "1." &&\n          mappedWords[0].CharacterStart == 0 &&\n          mappedWords[0].CharacterLength == 2,\n        "Ordered-list ordinal is not one exact Core-backed speech range.");\n      Require(mappedWords[1].Text == "Item" &&\n          mappedWords[1].CharacterStart == 3 &&\n          mappedWords[1].CharacterLength == 4,\n        "Ordered-list body word range is not exact.");\n\n      using var speech = new SpeechService();\n''')

# The direct decimal fixture must create the same range model rather than only an
# ID list. The test helper is allowed to tokenize its synthetic fixture; production
# Core-backed history is not.
replace_once(
  'AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs',
  '''        "WordIds" => wordIds,\n        _ when parameter.HasDefaultValue => parameter.DefaultValue,\n''',
  '''        "TranscriptWords" => BuildTestTranscriptWords(text, wordIds),\n        _ when parameter.HasDefaultValue => parameter.DefaultValue,\n''')
replace_once(
  'AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs',
  '''  /// <summary>\n  /// The real monitor/history path must carry the same immutable Core word IDs\n''',
  r'''  private static IReadOnlyList<SpeechFragmentWord> BuildTestTranscriptWords(
    string text,
    IReadOnlyList<long> wordIds)
  {
    MatchCollection matches = SpeechTokenization.Matches(text);
    Require(matches.Count == wordIds.Count,
      "Synthetic test fixture word count does not match supplied IDs.");
    return matches.Cast<Match>()
      .Select((match, index) => new SpeechFragmentWord(
        wordIds[index],
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
  }

  /// <summary>
  /// The real monitor/history path must carry the same immutable Core word IDs
''')
