#!/usr/bin/env python3
"""Apply final issue #88 corrections after the temporary production applicator."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "AgentPanelSpeaker" / "SapiSpeechEngine.cs"

text = PATH.read_text(encoding="utf-8")


def replace_once(old: str, new: str, description: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected exactly one {description}; found {count}.")
  text = text.replace(old, new, 1)


# Every canonical word, including the first, must have an actual provider
# bookmark.  A synthetic time-zero first boundary is not provider evidence.
replace_once(
  '''        if (wordIndex != 0)
        {
          replacement.Add(new XElement(
            ns + "mark",
            new XAttribute("name", $"aps_{word.WordIndex}")));
        }
''',
  '''        replacement.Add(new XElement(
          ns + "mark",
          new XAttribute("name", $"aps_{word.WordIndex}")));
''',
  "conditional first-token bookmark block")

replace_once(
  '''    var raw = new List<SpeechWordBoundary>();
    SpeechMarkupWord first = words[0];
    raw.Add(new SpeechWordBoundary(
      TimeSpan.Zero,
      first.WordIndex,
      first.CharacterStart,
      first.CharacterLength,
      first.Text,
      Exact: true));
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
''',
  '''    var raw = new List<SpeechWordBoundary>();
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
''',
  "synthetic first-token boundary seed")

replace_once(
  '''        string synthesisText = GetBookmarkedSynthesisText(markup, words, wordIndex);
''',
  '''        string synthesisText = GetOwnedBookmarkedSynthesisText(markup, words, wordIndex);
''',
  "provenance bookmark synthesis call")

replace_once(
  '''  private static string GetBookmarkedSynthesisText(
    SpeechMarkup markup,
    IReadOnlyList<SpeechMarkupWord> words,
    int index)
''',
  '''  private static string GetOwnedBookmarkedSynthesisText(
    SpeechMarkup markup,
    IReadOnlyList<SpeechMarkupWord> words,
    int index)
''',
  "provenance bookmark synthesis helper")

# Tracking degradation belongs to the PCM player created from the same speech
# request.  Do not raise it synchronously while a SpeakCommand is still being
# processed: a CancelCommand may already be queued behind that synchronous
# synthesis.  Hold it with the active player and publish it only on an idle
# service cycle.  A cancel/replacement therefore discards stale ownership
# before it can move the transcript marker.
replace_once(
  '''    IReadOnlyList<SpeechWordBoundary> wordBoundaries =
      Array.Empty<SpeechWordBoundary>();
    int nextWordBoundary = 0;
''',
  '''    IReadOnlyList<SpeechWordBoundary> wordBoundaries =
      Array.Empty<SpeechWordBoundary>();
    SpeechTrackingDegradation? pendingTrackingDegradation = null;
    int nextWordBoundary = 0;
''',
  "service-loop tracking-degradation declaration")

replace_once(
  '''            ref player,
            ref wordBoundaries,
            ref nextWordBoundary,
            ref exiting);
''',
  '''            ref player,
            ref wordBoundaries,
            ref pendingTrackingDegradation,
            ref nextWordBoundary,
            ref exiting);
''',
  "ProcessCommand degradation argument")

replace_once(
  '''        if (player is not null)
        {
          TimeSpan position = player.Position;
''',
  '''        if (player is not null)
        {
          if (command is null && pendingTrackingDegradation is not null)
          {
            RaiseWordTrackingUnavailable(pendingTrackingDegradation);
            pendingTrackingDegradation = null;
          }
          TimeSpan position = player.Position;
''',
  "idle-cycle degradation publication")

replace_once(
  '''            wordBoundaries = Array.Empty<SpeechWordBoundary>();
            nextWordBoundary = 0;
            MarkAudioEnd();
''',
  '''            wordBoundaries = Array.Empty<SpeechWordBoundary>();
            pendingTrackingDegradation = null;
            nextWordBoundary = 0;
            MarkAudioEnd();
''',
  "completed-player degradation clear")

replace_once(
  '''        bool shouldComplete = player is not null || command is PlaybackCommand;
        CancelPlayer(ref player);
''',
  '''        bool shouldComplete = player is not null || command is PlaybackCommand;
        CancelPlayer(ref player);
        pendingTrackingDegradation = null;
''',
  "fault-path degradation clear")

replace_once(
  '''    ref WaveOutPlayer? player,
    ref IReadOnlyList<SpeechWordBoundary> wordBoundaries,
    ref int nextWordBoundary,
''',
  '''    ref WaveOutPlayer? player,
    ref IReadOnlyList<SpeechWordBoundary> wordBoundaries,
    ref SpeechTrackingDegradation? pendingTrackingDegradation,
    ref int nextWordBoundary,
''',
  "ProcessCommand degradation parameter")

replace_once(
  '''        if (speechBuffer.TrackingDegradation is not null)
        {
          RaiseWordTrackingUnavailable(speechBuffer.TrackingDegradation);
        }
        player = new WaveOutPlayer(speechBuffer.Wave);
        wordBoundaries = speechBuffer.WordBoundaries;
        nextWordBoundary = 0;
''',
  '''        player = new WaveOutPlayer(speechBuffer.Wave);
        wordBoundaries = speechBuffer.WordBoundaries;
        pendingTrackingDegradation = speechBuffer.TrackingDegradation;
        nextWordBoundary = 0;
''',
  "synchronous tracking-degradation callback")

replace_once(
  '''        player = new WaveOutPlayer(previewBuffer.Wave);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
''',
  '''        player = new WaveOutPlayer(previewBuffer.Wave);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        pendingTrackingDegradation = null;
        nextWordBoundary = 0;
''',
  "IPA-preview degradation clear")

replace_once(
  '''        player = StartWakeToneTest(wakeTest.WakeSettings);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
''',
  '''        player = StartWakeToneTest(wakeTest.WakeSettings);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        pendingTrackingDegradation = null;
        nextWordBoundary = 0;
''',
  "wake-test degradation clear")

replace_once(
  '''        CancelPlayer(ref player);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
        if (wasActive)
''',
  '''        CancelPlayer(ref player);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        pendingTrackingDegradation = null;
        nextWordBoundary = 0;
        if (wasActive)
''',
  "cancel-command degradation clear")

replace_once(
  '''      case DisposeCommand:
        CancelPlayer(ref player);
        exiting = true;
''',
  '''      case DisposeCommand:
        CancelPlayer(ref player);
        pendingTrackingDegradation = null;
        exiting = true;
''',
  "dispose-command degradation clear")

PATH.write_text(text, encoding="utf-8")
