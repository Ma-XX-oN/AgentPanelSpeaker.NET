from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{label}: expected exactly one replacement sentinel, found {count}")
  return text.replace(old, new, 1)


def patch(path_name: str, replacements: list[tuple[str, str, str]]) -> None:
  path = Path(path_name)
  text = path.read_text(encoding="utf-8")
  for old, new, label in replacements:
    text = replace_once(text, old, new, label)
  path.write_text(text, encoding="utf-8")


patch(
  "AgentPanelSpeaker/SpeechService.cs",
  [
    (
      """  private int? _pendingHistoryIndex;\n  private int _pendingHistoryWordIndex;""",
      """  private int? _pendingHistoryIndex;\n  // Zero-based offset within the pending SpeechFragment word collection.\n  // This is NOT an AIConversationCore canonical WordId.\n  private int _pendingHistoryWordIndex;""",
      "pending fragment-relative word offset comment",
    ),
    (
      """  private string _activeTranscriptText = string.Empty;\n  private int _activeWordIndex;\n  private int _activeWordBaseIndex;""",
      """  private string _activeTranscriptText = string.Empty;\n  // Zero-based offset within the active SpeechFragment word collection.\n  // This is NOT an AIConversationCore canonical WordId.\n  private int _activeWordIndex;\n  // Fragment-relative offset represented by engine WordIndex zero when an\n  // utterance starts in the middle of a retained SpeechFragment.\n  private int _activeWordBaseIndex;""",
      "active fragment-relative word offset comments",
    ),
    (
      """  private bool _rewindCurrentFragmentGracePending;\n  private long? _rewindCurrentFragmentGraceStartedTimestamp;""",
      """  private bool _rewindCurrentFragmentGracePending;\n  private long? _rewindCurrentFragmentGraceStartedTimestamp;\n  // PreviousSentence is the only owner of this one-shot suppression. A\n  // J-selected history target must not arm a fresh first-word grace window\n  // when its audio starts, or repeated J presses can remain on one fragment.\n  private int? _rewindGraceSuppressedHistoryIndex;""",
      "rewind suppression state",
    ),
    (
      """      _pendingHistoryIndex = null;\n      _pendingHistoryWordIndex = 0;\n      _pendingUntracked = null;\n      ClearProcessingTimeAnnouncementLocked();\n      _pauseBeforeNextHistory = false;\n      _activeHistoryIndex = -1;\n      _nextHistoryIndex = 0;""",
      """      _pendingHistoryIndex = null;\n      _pendingHistoryWordIndex = 0;\n      _rewindGraceSuppressedHistoryIndex = null;\n      _pendingUntracked = null;\n      ClearProcessingTimeAnnouncementLocked();\n      _pauseBeforeNextHistory = false;\n      _activeHistoryIndex = -1;\n      _nextHistoryIndex = 0;""",
      "begin-live-session suppression reset",
    ),
    (
      """      _pendingHistoryIndex = null;\n      _pendingHistoryWordIndex = 0;\n      _pendingUntracked = null;\n      ClearProcessingTimeAnnouncementLocked();\n      _pauseBeforeNextHistory = false;\n      _activeHistoryIndex = -1;\n      _lastFenceActivity = null;\n      _history.Clear();""",
      """      _pendingHistoryIndex = null;\n      _pendingHistoryWordIndex = 0;\n      _rewindGraceSuppressedHistoryIndex = null;\n      _pendingUntracked = null;\n      ClearProcessingTimeAnnouncementLocked();\n      _pauseBeforeNextHistory = false;\n      _activeHistoryIndex = -1;\n      _lastFenceActivity = null;\n      _history.Clear();""",
      "load-history suppression reset",
    ),
    (
      """      int candidate = restartCurrent\n        ? anchor\n        : FindPreviousEligibleLocked(\n          anchor >= _history.Count ? _history.Count - 1 : anchor - 1);\n      LogNavigationLocked(\"rewind-sentence\", anchor, candidate);\n      return RestartCandidateLocked(candidate, out text);""",
      """      int candidate = restartCurrent\n        ? anchor\n        : FindPreviousEligibleLocked(\n          anchor >= _history.Count ? _history.Count - 1 : anchor - 1);\n      if (candidate >= 0)\n      {\n        // PreviousSentence owns both the destination and its 500 ms policy.\n        // Consuming grace here makes a second immediate J move backward, while\n        // the one-shot target prevents this J-selected start from re-arming it.\n        ClearRewindCurrentFragmentGraceLocked();\n        _rewindGraceSuppressedHistoryIndex = candidate;\n      }\n      LogNavigationLocked(\"rewind-sentence\", anchor, candidate);\n      return RestartCandidateLocked(candidate, out text);""",
      "PreviousSentence consumes and suppresses grace",
    ),
    (
      """  /// <summary>\n  /// Prepares first-word rewind grace for a new history playback start.\n  /// The timer itself begins only when reading reaches word zero.\n  /// </summary>\n  private void PrepareRewindCurrentFragmentGraceLocked(int startWordIndex)\n  {\n    _rewindCurrentFragmentGracePending = startWordIndex == 0;\n    _rewindCurrentFragmentGraceStartedTimestamp = null;\n  }""",
      """  /// <summary>\n  /// Prepares first-word rewind grace for a new history playback start.\n  /// The timer itself begins only when a real engine boundary reaches\n  /// fragment-relative word index zero. A PreviousSentence-selected target\n  /// consumes its one-shot suppression instead of re-arming grace.\n  /// </summary>\n  private void PrepareRewindCurrentFragmentGraceLocked(\n    int historyIndex,\n    int startWordIndex)\n  {\n    bool suppress = _rewindGraceSuppressedHistoryIndex is int suppressed &&\n      suppressed == historyIndex;\n    // Suppression belongs to exactly one attempted history start. If another\n    // command redirected playback first, it must not leak into a later start.\n    _rewindGraceSuppressedHistoryIndex = null;\n    _rewindCurrentFragmentGracePending = startWordIndex == 0 && !suppress;\n    _rewindCurrentFragmentGraceStartedTimestamp = null;\n  }""",
      "grace preparation ownership",
    ),
    (
      """      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n      if (_rewindCurrentFragmentGracePending)""",
      """      // Both indexes here are fragment-relative offsets. The globally\n      // unique Core WordId is resolved separately from TranscriptWords.\n      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n      if (_rewindCurrentFragmentGracePending)""",
      "engine-boundary coordinate comment",
    ),
    (
      """    PrepareRewindCurrentFragmentGraceLocked(boundedWordIndex);\n    SetActiveKindLocked(ActiveSpeechKind.History);\n    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);""",
      """    PrepareRewindCurrentFragmentGraceLocked(\n      _activeHistoryIndex,\n      boundedWordIndex);\n    SetActiveKindLocked(ActiveSpeechKind.History);""",
      "history start removes synthetic cursor",
    ),
    (
      """    PrepareRewindCurrentFragmentGraceLocked(_activeWordIndex);\n    SpeakConfiguredLocked(\n      remaining,\n      _activeProfile,\n      _activePauseAfter);\n    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);""",
      """    PrepareRewindCurrentFragmentGraceLocked(\n      _activeHistoryIndex,\n      _activeWordIndex);\n    SpeakConfiguredLocked(\n      remaining,\n      _activeProfile,\n      _activePauseAfter);""",
      "long-pause restart removes synthetic cursor",
    ),
    (
      """    _pauseBeforeNextHistory = false;\n    _pendingHistoryIndex = null;\n    _pendingHistoryWordIndex = 0;\n    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _nextHistoryIndex = _history.Count;\n    _lastFenceActivity = null;\n    _activeHistoryIndex = -1;""",
      """    _pauseBeforeNextHistory = false;\n    _pendingHistoryIndex = null;\n    _pendingHistoryWordIndex = 0;\n    _rewindGraceSuppressedHistoryIndex = null;\n    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _nextHistoryIndex = _history.Count;\n    _lastFenceActivity = null;\n    _activeHistoryIndex = -1;""",
      "paused-live-end suppression reset",
    ),
    (
      """    _pauseBeforeNextHistory = false;\n    _pendingHistoryIndex = null;\n    _pendingHistoryWordIndex = 0;\n    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _nextHistoryIndex = _history.Count;\n    _lastFenceActivity = null;\n    SetPausedLocked(false);""",
      """    _pauseBeforeNextHistory = false;\n    _pendingHistoryIndex = null;\n    _pendingHistoryWordIndex = 0;\n    _rewindGraceSuppressedHistoryIndex = null;\n    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _nextHistoryIndex = _history.Count;\n    _lastFenceActivity = null;\n    SetPausedLocked(false);""",
      "live-end suppression reset",
    ),
  ],
)

patch(
  "AgentPanelSpeaker/SpeechWordBoundary.cs",
  [
    (
      """/// <summary>\n/// Identifies one spoken word and its start position in the playback buffer.\n/// </summary>""",
      """/// <summary>\n/// Identifies one spoken token and its start position in the playback buffer.\n/// <c>WordIndex</c> is zero-based within the current engine utterance;\n/// SpeechService translates it into a SpeechFragment-relative offset. It is\n/// never an AIConversationCore canonical WordId.\n/// </summary>""",
      "speech engine WordIndex coordinate comment",
    ),
  ],
)

patch(
  "AgentPanelSpeaker/TranscriptPlaybackPosition.cs",
  [
    (
      """/// <summary>\n/// Describes the transcript marker corresponding to monitored playback.\n/// </summary>""",
      """/// <summary>\n/// Describes the transcript marker corresponding to monitored playback.\n/// <c>WordIndex</c> is the zero-based offset inside the active SpeechFragment.\n/// <c>WordId</c> is the globally unique AIConversationCore canonical word\n/// identity and does not reset at fragment boundaries.\n/// </summary>""",
      "playback local-vs-canonical coordinate comment",
    ),
  ],
)

patch(
  "AgentPanelSpeaker/SpeechFragment.cs",
  [
    (
      """/// <summary>\n/// Maps one immutable Core transcript word into one app-owned speech fragment.\n/// </summary>\ninternal sealed record SpeechFragmentWord(\n  long Id,\n  string Text,\n  int CharacterStart,\n  int CharacterLength);""",
      """/// <summary>\n/// Maps one immutable Core transcript word into one app-owned speech fragment.\n/// </summary>\n/// <param name=\"Id\">\n/// Globally unique AIConversationCore canonical WordId. It does not reset at\n/// speech-fragment boundaries.\n/// </param>\n/// <param name=\"Text\">Canonical word text carried into speech.</param>\n/// <param name=\"CharacterStart\">\n/// Zero-based character offset inside this SpeechFragment's text.\n/// </param>\n/// <param name=\"CharacterLength\">Character length inside the fragment.</param>\ninternal sealed record SpeechFragmentWord(\n  long Id,\n  string Text,\n  int CharacterStart,\n  int CharacterLength);""",
      "SpeechFragmentWord canonical identity comment",
    ),
  ],
)

# The old issue #35 fixture predates the canonical-word migration. Production
# retained history is Core-backed, so exercising an active transition without
# TranscriptWords accidentally tests the removed synthetic marker rather than
# the real engine-boundary cursor. Make that fixture match production identity.
patch(
  "AgentPanelSpeaker/Issue35SpeechTransitionRegressionTestRunner.cs",
  [
    (
      '        "HistoricalRevision" or "historicalRevision" => historical,\n        _ when parameter.HasDefaultValue => parameter.DefaultValue,',
      '        "HistoricalRevision" or "historicalRevision" => historical,\n        "TranscriptWords" or "transcriptWords" => BuildTranscriptWords(nodeId, text),\n        _ when parameter.HasDefaultValue => parameter.DefaultValue,',
      "issue35 canonical transcript-word fixture",
    ),
    (
      """    return (SpeechFragment)constructor.Invoke(arguments);\n  }\n\n  private static SpeechService CreateSpeechService(int rate)""",
      """    return (SpeechFragment)constructor.Invoke(arguments);\n  }\n\n  /// <summary>\n  /// Gives the transition fixture the same canonical-word shape as production\n  /// retained history. IDs are unique within this synthetic test transcript;\n  /// character offsets remain local to the containing SpeechFragment.\n  /// </summary>\n  private static IReadOnlyList<SpeechFragmentWord> BuildTranscriptWords(\n    long nodeId,\n    string text)\n  {\n    return SpeechTokenization.Matches(text)\n      .Cast<System.Text.RegularExpressions.Match>()\n      .Select((match, index) => new SpeechFragmentWord(\n        checked(nodeId * 1000 + index + 1),\n        match.Value,\n        match.Index,\n        match.Length))\n      .ToArray();\n  }\n\n  private static SpeechService CreateSpeechService(int rate)""",
      "issue35 canonical transcript-word helper",
    ),
  ],
)

# Semantic preflight: this repair must not add WebView-side rewind state.
transcript_view = Path("AgentPanelSpeaker/TranscriptView.cs").read_text(encoding="utf-8")
for forbidden in (
  "rewindCurrentFragmentGrace",
  "rewindGraceSuppressed",
  "RewindCurrentFragmentGracePeriod",
):
  if forbidden in transcript_view:
    raise RuntimeError(
      f"WebView/TranscriptView unexpectedly owns rewind state: {forbidden}")

print("Issue #84 C#-only ownership repair staged successfully.")
