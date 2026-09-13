from pathlib import Path
import re
import sys

TEST_FILE = Path("AgentPanelSpeaker/Issue84RewindCurrentFragmentRegressionTestRunner.cs")
PRODUCTION_FILE = Path("AgentPanelSpeaker/SpeechService.cs")


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def replace_method(text: str, old_name: str, replacement: str) -> str:
  pattern = re.compile(
    rf"^  private static void {re.escape(old_name)}\(\)\n  \{{.*?^  \}}\n",
    re.MULTILINE | re.DOTALL)
  updated, count = pattern.subn(replacement.rstrip() + "\n", text, count=1)
  if count != 1:
    raise RuntimeError(f"method {old_name}: expected one match, found {count}")
  return updated


def update_tests() -> None:
  text = TEST_FILE.read_text(encoding="utf-8")
  replacements = {
    '("rewind-current-fragment/active-first-word-pending-grace-restarts-current",\n        TestActiveFirstWordPendingGraceRestartsCurrent),':
      '("rewind-current-fragment/active-first-word-pending-grace-moves-previous",\n        TestActiveFirstWordPendingGraceMovesPrevious),',
    '("rewind-current-fragment/active-first-word-running-grace-restarts-current",\n        TestActiveFirstWordRunningGraceRestartsCurrent),':
      '("rewind-current-fragment/active-first-word-running-grace-moves-previous",\n        TestActiveFirstWordRunningGraceMovesPrevious),',
    '("rewind-current-fragment/active-first-word-grace-is-consumed-by-rewind",\n        TestActiveFirstWordGraceIsConsumedByRewind),':
      '("rewind-current-fragment/active-later-word-running-grace-moves-previous",\n        TestActiveLaterWordRunningGraceMovesPrevious),',
    '("rewind-current-fragment/rewind-restart-does-not-rearm-grace",\n        TestRewindRestartDoesNotRearmGrace),':
      '("rewind-current-fragment/rewind-destination-arms-new-grace",\n        TestRewindDestinationArmsNewGrace),',
    '("rewind-current-fragment/active-first-word-expired-grace-moves-previous",\n        TestActiveFirstWordExpiredGraceMovesPrevious),':
      '("rewind-current-fragment/active-first-word-expired-grace-restarts-current",\n        TestActiveFirstWordExpiredGraceRestartsCurrent),'
  }
  for old, new in replacements.items():
    text = replace_once(text, old, new, old.split("\n", 1)[0])

  text = replace_method(text, "TestActiveFirstWordPendingGraceRestartsCurrent", r'''  private static void TestActiveFirstWordPendingGraceMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      null);

    Require(speech.TryRewindSentence(out string text),
      "Pre-boundary rewind reported no destination.");
    Require(text == "For web research",
      "A fragment that had not yet reached its first real word boundary did not move to the previous fragment.");
  }
''')

  text = replace_method(text, "TestActiveFirstWordRunningGraceRestartsCurrent", r'''  private static void TestActiveFirstWordRunningGraceMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    Require(speech.TryRewindSentence(out string text),
      "First-word running-grace rewind reported no destination.");
    Require(text == "For web research",
      "J inside the first-word reaction window did not move to the previous fragment.");
  }
''')

  text = replace_method(text, "TestActiveFirstWordGraceIsConsumedByRewind", r'''  private static void TestActiveLaterWordRunningGraceMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 2);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    Require(speech.TryRewindSentence(out string text),
      "Later-word rewind inside grace reported no destination.");
    Require(text == "For web research",
      "The reaction timeout stopped mattering as soon as playback advanced beyond word zero.");
  }
''')

  text = replace_method(text, "TestRewindRestartDoesNotRearmGrace", r'''  private static void TestRewindDestinationArmsNewGrace()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    Require(speech.TryRewindSentence(out string text),
      "Rewind inside grace reported no destination.");
    Require(text == "For web research",
      "Rewind inside grace did not select the previous fragment.");
    InvokePlaybackStartPreparation(speech, CurrentFragmentIndex - 1, 0);
    Require(ReadBool(speech, "_rewindCurrentFragmentGracePending"),
      "A newly selected previous fragment did not prepare its own reaction window.");
    Require(ReadNullableLong(
        speech,
        "_rewindCurrentFragmentGraceStartedTimestamp") is null,
      "A newly selected fragment started its timer before a real word boundary.");
  }
''')

  text = replace_method(text, "TestActiveFirstWordExpiredGraceMovesPrevious", r'''  private static void TestActiveFirstWordExpiredGraceRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency);

    Require(speech.TryRewindSentence(out string text),
      "Expired-grace rewind reported no destination.");
    Require(text == "For local code files",
      "J after the reaction timeout did not restart the current fragment.");
  }
''')

  text = replace_method(text, "TestNamedGracePeriod", r'''  private static void TestNamedGracePeriod()
  {
    FieldInfo millisecondsField = typeof(SpeechService).GetField(
      "RewindCurrentFragmentGraceMilliseconds",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Named RewindCurrentFragmentGraceMilliseconds constant is missing.");
    Require(millisecondsField.GetRawConstantValue() is int milliseconds &&
        milliseconds == 1000,
      "Named rewind grace duration is not the grep-friendly 1000 ms constant.");

    FieldInfo periodField = typeof(SpeechService).GetField(
      "RewindCurrentFragmentGracePeriod",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Named RewindCurrentFragmentGracePeriod field is missing.");
    Require(periodField.GetValue(null) is TimeSpan period &&
        period == TimeSpan.FromMilliseconds(milliseconds),
      "RewindCurrentFragmentGracePeriod is not derived from the millisecond constant.");
  }
''')

  TEST_FILE.write_text(text, encoding="utf-8")
  print("Issue #84 corrected rewind-timeout regressions staged.")


def update_production() -> None:
  text = PRODUCTION_FILE.read_text(encoding="utf-8")

  text = replace_once(
    text,
    "  private static readonly TimeSpan RewindCurrentFragmentGracePeriod =\n    TimeSpan.FromSeconds(1);",
    "  // Human reaction window after the first spoken word of a fragment begins.\n  // Keep the millisecond value explicit so transport tuning is easy to grep.\n  private const int RewindCurrentFragmentGraceMilliseconds = 1000;\n  private static readonly TimeSpan RewindCurrentFragmentGracePeriod =\n    TimeSpan.FromMilliseconds(RewindCurrentFragmentGraceMilliseconds);",
    "grep-friendly grace constant")

  suppression_block = '''  // PreviousSentence is the only owner of this one-shot suppression. A
  // J-selected history target must not arm a fresh first-word grace window
  // when its audio starts, or repeated J presses can remain on one fragment.
  private int? _rewindGraceSuppressedHistoryIndex;
'''
  text = replace_once(text, suppression_block, "", "obsolete grace suppression field")

  reset_line = "      _rewindGraceSuppressedHistoryIndex = null;\n"
  reset_count = text.count(reset_line)
  if reset_count != 4:
    raise RuntimeError(
      f"obsolete grace suppression resets: expected 4 matches, found {reset_count}")
  text = text.replace(reset_line, "")

  try_rewind = re.compile(
    r"^  public bool TryRewindSentence\(out string text\)\n  \{.*?^  \}\n",
    re.MULTILINE | re.DOTALL)
  new_try_rewind = r'''  public bool TryRewindSentence(out string text)
  {
    lock (_sync)
    {
      int anchor = GetNavigationAnchorLocked();
      bool restartCurrent = false;
      if (anchor >= 0 && anchor < _history.Count)
      {
        if (_pendingHistoryIndex is int pendingHistoryIndex)
        {
          // A queued navigation target owns the cursor while the old utterance
          // is cancelling. Word zero means another immediate J goes backward.
          restartCurrent = pendingHistoryIndex == anchor &&
            _pendingHistoryWordIndex > 0;
        }
        else if (_isPaused)
        {
          // The reaction timeout is about actual speech, never a paused marker.
          restartCurrent = _activeHistoryIndex == anchor &&
            _activeWordIndex > 0;
        }
        else if (_activeKind == ActiveSpeechKind.History &&
                 _activeHistoryIndex == anchor)
        {
          // While the human reaction window is active, J means "previous".
          // After it expires, J means "restart this fragment" regardless of
          // which later word the speech engine has already reached.
          restartCurrent =
            !IsRewindCurrentFragmentGraceActiveLocked(anchor);
        }
      }

      int candidate = restartCurrent
        ? anchor
        : FindPreviousEligibleLocked(
          anchor >= _history.Count ? _history.Count - 1 : anchor - 1);
      ClearRewindCurrentFragmentGraceLocked();
      LogNavigationLocked("rewind-sentence", anchor, candidate);
      return RestartCandidateLocked(candidate, out text);
    }
  }
'''
  text, count = try_rewind.subn(new_try_rewind, text, count=1)
  if count != 1:
    raise RuntimeError(f"TryRewindSentence: expected one match, found {count}")

  grace_active = re.compile(
    r"^  private bool IsRewindCurrentFragmentGraceActiveLocked\(int anchor\)\n  \{.*?^  \}\n",
    re.MULTILINE | re.DOTALL)
  new_grace_active = r'''  private bool IsRewindCurrentFragmentGraceActiveLocked(int anchor)
  {
    if (_activeKind != ActiveSpeechKind.History ||
        _isPaused ||
        _activeHistoryIndex != anchor)
    {
      return false;
    }
    if (_rewindCurrentFragmentGracePending)
    {
      return true;
    }
    // The timeout starts at the real first-word boundary and intentionally
    // remains meaningful after the engine advances to later words.
    return _rewindCurrentFragmentGraceStartedTimestamp is long started &&
      Stopwatch.GetElapsedTime(started) < RewindCurrentFragmentGracePeriod;
  }
'''
  text, count = grace_active.subn(new_grace_active, text, count=1)
  if count != 1:
    raise RuntimeError(
      f"IsRewindCurrentFragmentGraceActiveLocked: expected one match, found {count}")

  prepare = re.compile(
    r"^  private void PrepareRewindCurrentFragmentGraceLocked\(\n    int historyIndex,\n    int startWordIndex\)\n  \{.*?^  \}\n",
    re.MULTILINE | re.DOTALL)
  new_prepare = r'''  private void PrepareRewindCurrentFragmentGraceLocked(int startWordIndex)
  {
    // Starting at fragment-relative word zero prepares the human reaction
    // window. The clock itself starts only at the real engine word boundary.
    _rewindCurrentFragmentGracePending = startWordIndex == 0;
    _rewindCurrentFragmentGraceStartedTimestamp = null;
  }
'''
  text, count = prepare.subn(new_prepare, text, count=1)
  if count != 1:
    raise RuntimeError(
      f"PrepareRewindCurrentFragmentGraceLocked: expected one match, found {count}")

  text = replace_once(
    text,
    "    PrepareRewindCurrentFragmentGraceLocked(\n      _activeHistoryIndex,\n      boundedWordIndex);",
    "    PrepareRewindCurrentFragmentGraceLocked(boundedWordIndex);",
    "history-start grace preparation")
  text = replace_once(
    text,
    "    PrepareRewindCurrentFragmentGraceLocked(\n      _activeHistoryIndex,\n      _activeWordIndex);",
    "    PrepareRewindCurrentFragmentGraceLocked(_activeWordIndex);",
    "current-word restart grace preparation")

  resume_old = '''        else if (hasActiveUtterance)
        {
          _engine.Resume();
          if (_activeKind == ActiveSpeechKind.History &&
              _activeWordIndex == 0)
          {
            StartRewindCurrentFragmentGraceLocked();
          }
          else
          {
            ClearRewindCurrentFragmentGraceLocked();
          }
          ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
        }
'''
  resume_new = '''        else if (hasActiveUtterance)
        {
          _engine.Resume();
          // Resuming an already-started fragment is not a new first-word
          // boundary, so it must not manufacture a fresh reaction window.
          ClearRewindCurrentFragmentGraceLocked();
          ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
        }
'''
  text = replace_once(text, resume_old, resume_new, "pause-resume grace handling")

  PRODUCTION_FILE.write_text(text, encoding="utf-8")
  print("Issue #84 corrected rewind-timeout production fix staged.")


if len(sys.argv) != 2 or sys.argv[1] not in {"test", "production"}:
  raise SystemExit("usage: issue84-rewind-grace-semantics.py test|production")

if sys.argv[1] == "test":
  update_tests()
else:
  update_production()
