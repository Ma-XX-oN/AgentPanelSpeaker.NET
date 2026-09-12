from pathlib import Path

path = Path("AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")
old = '''      Require(
        SpinWait.SpinUntil(() => speech.IsSpeaking, TimeSpan.FromSeconds(3)),
        "User Context never entered active speech.");
'''
new = '''      Require(
        SpinWait.SpinUntil(
          () =>
          {
            lock (positions)
            {
              return positions.Any(position =>
                position.State == TranscriptPlaybackState.Speaking &&
                position.NodeId == 9001 &&
                position.WordIndex >= 2);
            }
          },
          TimeSpan.FromSeconds(30)),
        "User Context never emitted active provider word boundaries.");
'''
if text.count(old) != 1:
  raise RuntimeError(
    f"Expected one IsSpeaking active gate, found {text.count(old)}.")
text = text.replace(old, new, 1)
old_comment = '''  /// The source is deliberately far too long to complete inside the transition
  /// timeout, so reaching the User destination proves cancellation rather than
  /// eventual natural completion.
'''
new_comment = '''  /// The test waits for provider word-boundary advancement before toggling, so
  /// it cannot mistake SpeechService's pre-render active state for real playback.
  /// The source is deliberately far too long to complete inside the transition
  /// timeout, so reaching the User destination proves cancellation rather than
  /// eventual natural completion.
'''
if text.count(old_comment) != 1:
  raise RuntimeError(
    f"Expected one active-test comment anchor, found {text.count(old_comment)}.")
text = text.replace(old_comment, new_comment, 1)
path.write_text(text, encoding="utf-8")
print("Corrected User Context active RED gate to provider boundaries.")
