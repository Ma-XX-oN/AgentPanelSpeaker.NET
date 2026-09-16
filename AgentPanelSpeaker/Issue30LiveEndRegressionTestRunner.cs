namespace AgentPanelSpeaker;

/// <summary>
/// Reproduces issue #30 through the public monitored-playback navigation API.
/// </summary>
internal static class Issue30LiveEndRegressionTestRunner
{
  private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

  /// <summary>
  /// Runs live-end playback regressions.
  /// </summary>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("live-end/natural-completion-waits", TestNaturalCompletionWaits),
      ("live-end/skip-to-existing-then-natural-end-waits",
        TestSkipToExistingThenNaturalEndWaits),
      ("live-end/paused-skip-to-existing-then-natural-end-waits",
        TestPausedSkipToExistingThenNaturalEndWaits),
      ("live-end/skip-forward-while-speaking-pauses-at-end",
        TestSkipForwardWhileSpeakingPausesAtEnd)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #30 live-end regression suite: {tests.Length} tests");
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #30 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #30 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Guards the ordinary no-navigation path: consuming the current history
  /// while unpaused must leave playback waiting for appended content.
  /// </summary>
  private static void TestNaturalCompletionWaits()
  {
    using SpeechService speech = CreateSpeechService(rate: 10);
    var positions = CapturePositions(speech);

    speech.BeginLiveSession();
    speech.SpeakLive(new SpeechFragment(
      1,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      "Natural completion live end probe."));

    Require(
      SpinWait.SpinUntil(
        () => !speech.IsSpeaking,
        CompletionTimeout),
      "Natural playback did not complete within the regression timeout.");

    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(
      final.State == TranscriptPlaybackState.WaitingAtLiveEnd,
      $"Natural completion ended in {final.State}, expected WaitingAtLiveEnd.");
  }

  /// <summary>
  /// Covers the user-corrected sequence: skip forward while speech is active
  /// to another existing fragment, not past the final fragment, then allow the
  /// retained history to finish naturally.  The terminal state must still be
  /// WaitingAtLiveEnd.
  /// </summary>
  private static void TestSkipToExistingThenNaturalEndWaits()
  {
    using SpeechService speech = CreateSpeechService(rate: 10);
    var positions = CapturePositions(speech);

    speech.BeginLiveSession();
    SeedSkipToExistingHistory(speech);
    Require(speech.IsSpeaking,
      "The skip-to-existing source fragment did not enter active speech.");

    bool moved = speech.TryForwardSentence(out string text);
    Require(moved && text == "Skip destination fragment.",
      "Forward navigation did not land on the existing destination fragment.");

    Require(
      SpinWait.SpinUntil(
        () => !speech.IsSpeaking,
        CompletionTimeout),
      "Skip-to-existing playback did not naturally reach live end.");

    Require(!speech.IsPaused,
      "Unpaused skip-to-existing playback unexpectedly ended paused.");
    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(
      final.State == TranscriptPlaybackState.WaitingAtLiveEnd,
      "Skip-to-existing natural completion ended in " +
      $"{final.State}, expected WaitingAtLiveEnd.");
  }

  /// <summary>
  /// Covers the pause-restoration form of the same corrected sequence.  A
  /// paused skip to an existing destination must retain that destination while
  /// cancellation completes; after resuming, normal playback must consume the
  /// remaining history and report WaitingAtLiveEnd.
  /// </summary>
  private static void TestPausedSkipToExistingThenNaturalEndWaits()
  {
    using SpeechService speech = CreateSpeechService(rate: 10);
    var positions = CapturePositions(speech);

    speech.BeginLiveSession();
    SeedSkipToExistingHistory(speech);
    Require(speech.IsSpeaking,
      "The paused skip-to-existing source fragment did not enter active speech.");

    Require(
      speech.TogglePause() == PauseToggleResult.Paused,
      "Could not pause active playback before the skip-to-existing probe.");
    bool moved = speech.TryForwardSentence(out string text);
    Require(moved && text == "Skip destination fragment.",
      "Paused forward navigation did not retain the existing destination.");

    Require(
      SpinWait.SpinUntil(
        () => !speech.IsSpeaking && speech.IsPaused,
        CompletionTimeout),
      "Paused skip cancellation did not settle at the retained destination.");

    TranscriptPlaybackPosition paused = LastPosition(positions);
    Require(
      paused.State == TranscriptPlaybackState.Paused && paused.NodeId == 11,
      "Paused skip-to-existing cancellation lost its destination; " +
      $"state={paused.State}, node={paused.NodeId}.");

    Require(
      speech.TogglePause() == PauseToggleResult.Resumed,
      "Could not resume the retained skip-to-existing destination.");
    Require(
      SpinWait.SpinUntil(
        () => !speech.IsSpeaking,
        CompletionTimeout),
      "Resumed skip-to-existing playback did not naturally reach live end.");

    Require(!speech.IsPaused,
      "Resumed skip-to-existing playback unexpectedly ended paused.");
    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(
      final.State == TranscriptPlaybackState.WaitingAtLiveEnd,
      "Paused skip-to-existing sequence ended in " +
      $"{final.State}, expected WaitingAtLiveEnd.");
  }

  /// <summary>
  /// Guards the distinct skip-past-end defect.  This is intentionally not the
  /// user-observed sequence: while the final fragment is still speaking, skip
  /// forward with no later eligible fragment.  Public navigation moves to the
  /// paused live end and completion must publish PausedAtLiveEnd.
  /// </summary>
  private static void TestSkipForwardWhileSpeakingPausesAtEnd()
  {
    using SpeechService speech = CreateSpeechService(rate: -10);
    var positions = CapturePositions(speech);

    speech.BeginLiveSession();
    speech.SpeakLive(new SpeechFragment(
      7,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      string.Join(' ', Enumerable.Repeat(
        "skip forward live end regression probe",
        100))));

    Require(speech.IsSpeaking,
      "The regression fragment did not enter active monitored speech.");

    bool moved = speech.TryForwardSentence(out string text);
    Require(!moved && text.Length == 0,
      "Skipping forward past the final fragment unexpectedly found more speech.");

    Require(
      SpinWait.SpinUntil(
        () => !speech.IsSpeaking && speech.IsPaused,
        CompletionTimeout),
      "Skip-forward cancellation did not settle into paused playback.");

    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(
      final.State == TranscriptPlaybackState.PausedAtLiveEnd,
      "Skipping forward past the final fragment while speech was active " +
      $"ended in {final.State}, expected PausedAtLiveEnd.");
  }

  private static List<TranscriptPlaybackPosition> CapturePositions(
    SpeechService speech)
  {
    var positions = new List<TranscriptPlaybackPosition>();
    speech.PlaybackPositionChanged += position =>
    {
      lock (positions)
      {
        positions.Add(position);
      }
    };
    return positions;
  }

  private static void SeedSkipToExistingHistory(SpeechService speech)
  {
    speech.SpeakLive(new SpeechFragment(
      10,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      string.Join(' ', Enumerable.Repeat(
        "active source fragment",
        100))));
    speech.SpeakLive(new SpeechFragment(
      11,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      "Skip destination fragment."));
    speech.SpeakLive(new SpeechFragment(
      12,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      "Final retained fragment."));
  }

  private static SpeechService CreateSpeechService(int rate)
  {
    var speech = new SpeechService();
    InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault()
      ?? throw new InvalidOperationException(
        "No installed voice is available for the live-end regression.");
    var profile = new SpeechProfileSettings(voice.Name, rate, 0)
    {
      // Volume zero means Not Spoken, so keep this audible-but-minimal. The
      // regression immediately cancels the long skip-forward utterance.
      Volume = 1
    };
    speech.SetPolicyProviders(
      _ => profile,
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default with { Enabled = false });
    return speech;
  }

  private static TranscriptPlaybackPosition LastPosition(
    List<TranscriptPlaybackPosition> positions)
  {
    lock (positions)
    {
      if (positions.Count == 0)
      {
        throw new InvalidOperationException(
          "SpeechService emitted no transcript playback positions.");
      }
      return positions[^1];
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
