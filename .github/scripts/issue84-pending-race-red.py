from pathlib import Path

path = Path("AgentPanelSpeaker/Issue84RewindCurrentFragmentRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-first-word-pending-grace-restarts-current",
'''
new = '''      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent),
      ("rewind-current-fragment/pending-word-zero-overrides-stale-active-word",
        TestPendingWordZeroOverridesStaleActiveWord),
      ("rewind-current-fragment/active-first-word-pending-grace-restarts-current",
'''
if text.count(old) != 1:
  raise RuntimeError(f"test-list sentinel count was {text.count(old)}, expected 1")
text = text.replace(old, new, 1)

old = '''  private static void TestActiveFirstWordPendingGraceRestartsCurrent()
  {
'''
new = '''  /// <summary>
  /// After a mid-fragment J has queued word zero, that pending navigation
  /// cursor is authoritative. The engine may still report the old active word
  /// until cancellation completes; a second immediate J must not use that
  /// stale active offset to restart the same fragment again.
  /// </summary>
  private static void TestPendingWordZeroOverridesStaleActiveWord()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 2);
    SetField(speech, "_pendingHistoryIndex", CurrentFragmentIndex);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      null);

    Require(speech.TryRewindSentence(out string text),
      "Second immediate rewind reported no destination.");
    Require(text == "For web research",
      "Pending word zero did not override the stale active mid-fragment word.");
  }

  private static void TestActiveFirstWordPendingGraceRestartsCurrent()
  {
'''
if text.count(old) != 1:
  raise RuntimeError(f"test-method sentinel count was {text.count(old)}, expected 1")
text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
print("Issue #84 pending-vs-active repeated-J RED staged.")
