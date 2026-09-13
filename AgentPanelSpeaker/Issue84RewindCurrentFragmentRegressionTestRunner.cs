using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #84 rewind-current-fragment navigation.
/// </summary>
internal static class Issue84RewindCurrentFragmentRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #84 navigation regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("rewind-current-fragment/paused-mid-fragment-restarts-current",
        TestPausedMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent),
      ("rewind-current-fragment/paused-first-word-moves-previous",
        TestPausedFirstWordMovesPrevious),
      ("rewind-current-fragment/active-first-word-moves-previous",
        TestActiveFirstWordMovesPrevious)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #84 rewind-current-fragment regression suite: {tests.Length} tests");
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
        Console.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #84 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #84 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestPausedMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(2002, out _),
      "Could not place the paused cursor inside the current fragment.");

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused rewind reported no destination.");
    Require(text == "For local code files",
      "Paused rewind moved to the preceding fragment instead of the current fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused rewind did not move the marker to word zero of the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 1 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Paused rewind did not retain word-zero as the pending resume position.");
  }

  private static void TestActiveMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetField(speech, "_pendingHistoryIndex", null);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetField(speech, "_activeHistoryIndex", 1);
    SetField(speech, "_activeWordIndex", 2);
    SetField(speech, "_activeKind", ParseActiveKind(speech, "History"));
    SetField(speech, "_isPaused", false);

    Require(speech.TryRewindSentence(out string text),
      "Active rewind reported no destination.");
    Require(text == "For local code files",
      "Active rewind moved to the preceding fragment instead of the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 1 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Active rewind did not queue word zero of the current fragment.");
  }

  private static void TestPausedFirstWordMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(2001, out _),
      "Could not place the paused cursor on the first word of the current fragment.");

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused first-word rewind reported no previous fragment.");
    Require(text == "In practice For web research",
      "Paused first-word rewind did not move to the previous fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "In",
      "Paused first-word rewind did not move to word zero of the previous fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 0 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Paused first-word rewind did not retain the previous fragment at word zero.");
  }

  private static void TestActiveFirstWordMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    SetField(speech, "_pendingHistoryIndex", null);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetField(speech, "_activeHistoryIndex", 1);
    SetField(speech, "_activeWordIndex", 0);
    SetField(speech, "_activeKind", ParseActiveKind(speech, "History"));
    SetField(speech, "_isPaused", false);

    Require(speech.TryRewindSentence(out string text),
      "Active first-word rewind reported no previous fragment.");
    Require(text == "In practice For web research",
      "Active first-word rewind did not move to the previous fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 0 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Active first-word rewind did not queue the previous fragment at word zero.");
  }

  private static SpeechService CreateSpeech()
  {
    var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          10,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "In practice For web research",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(1001, "In", 0, 2),
            new SpeechFragmentWord(1002, "practice", 3, 8),
            new SpeechFragmentWord(1003, "For", 12, 3),
            new SpeechFragmentWord(1004, "web", 16, 3),
            new SpeechFragmentWord(1005, "research", 20, 8)
          }),
        new SpeechFragment(
          10,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "For local code files",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(2001, "For", 0, 3),
            new SpeechFragmentWord(2002, "local", 4, 5),
            new SpeechFragmentWord(2003, "code", 10, 4),
            new SpeechFragmentWord(2004, "files", 15, 5)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);
    return speech;
  }

  private static object ParseActiveKind(SpeechService speech, string value)
  {
    FieldInfo field = GetField(speech, "_activeKind");
    return Enum.Parse(field.FieldType, value);
  }

  private static int ReadInt(SpeechService speech, string name)
  {
    object? value = GetField(speech, name).GetValue(speech);
    return value switch
    {
      int number => number,
      null => -1,
      _ => throw new InvalidOperationException($"{name} is not an integer field.")
    };
  }

  private static void SetField(SpeechService speech, string name, object? value)
  {
    GetField(speech, name).SetValue(speech, value);
  }

  private static FieldInfo GetField(SpeechService speech, string name)
  {
    return speech.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"SpeechService field {name} is missing.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
