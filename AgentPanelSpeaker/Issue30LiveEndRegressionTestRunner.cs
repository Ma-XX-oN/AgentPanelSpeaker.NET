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
    using SpeechService speech = CreateSpeechService();
    var positions = new List<TranscriptPlaybackPosition>();
    speech.PlaybackPositionChanged += position =>
    {
      lock (positions)
      {
        positions.Add(position);
      }
    };

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
  /// Reproduces the installed-app failure: while the final fragment is still
  /// speaking, skip forward one sentence. With no later eligible fragment,
  /// public navigation moves to the paused live end and cancels the utterance.
  /// Completion must then publish PausedAtLiveEnd so the WebView can render
  /// "Press play to wait for more text.".
  /// </summary>
  private static void TestSkipForwardWhileSpeakingPausesAtEnd()
  {
    using SpeechService speech = CreateSpeechService();
    var positions = new List<TranscriptPlaybackPosition>();
    speech.PlaybackPositionChanged += position =>
    {
      lock (positions)
      {
        positions.Add(position);
      }
    };

    speech.BeginLiveSession();
    speech.SpeakLive(new SpeechFragment(
      7,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      string.Join(' ', Enumerable.Repeat(
        "skip-forward-live-end-regression-probe",
        80))));

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

  private static SpeechService CreateSpeechService()
  {
    var speech = new SpeechService();
    InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault()
      ?? throw new InvalidOperationException(
        "No installed voice is available for the live-end regression.");
    var profile = new SpeechProfileSettings(voice.Name, 10, 0)
    {
      Volume = 0
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
