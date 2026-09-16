using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #84 structural rewind navigation.
/// </summary>
internal static class Issue84RewindCurrentFragmentRegressionTestRunner
{
  private const int CurrentFragmentIndex = 2;

  /// <summary>
  /// Runs the issue #84 navigation regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("rewind-current-fragment/canonical-structural-boundaries",
        TestCanonicalStructuralBoundaries),
      ("rewind-current-fragment/paused-mid-fragment-restarts-current",
        TestPausedMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent),
      ("rewind-current-fragment/pending-word-zero-overrides-stale-active-word",
        TestPendingWordZeroOverridesStaleActiveWord),
      ("rewind-current-fragment/active-first-word-pending-grace-moves-previous",
        TestActiveFirstWordPendingGraceMovesPrevious),
      ("rewind-current-fragment/active-first-word-running-grace-moves-previous",
        TestActiveFirstWordRunningGraceMovesPrevious),
      ("rewind-current-fragment/active-later-word-running-grace-moves-previous",
        TestActiveLaterWordRunningGraceMovesPrevious),
      ("rewind-current-fragment/rewind-destination-arms-new-grace",
        TestRewindDestinationArmsNewGrace),
      ("rewind-current-fragment/active-first-word-expired-grace-restarts-current",
        TestActiveFirstWordExpiredGraceRestartsCurrent),
      ("rewind-current-fragment/paused-first-word-moves-previous",
        TestPausedFirstWordMovesPrevious),
      ("rewind-current-fragment/later-word-start-does-not-arm-grace",
        TestLaterWordStartDoesNotArmGrace),
      ("rewind-current-fragment/named-grace-period",
        TestNamedGracePeriod),
      ("rewind-current-fragment/history-start-has-no-synthetic-speaking-position",
        TestHistoryStartHasNoSyntheticSpeakingPosition)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #84 structural rewind regression suite: {tests.Length} tests");
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

  private static void TestCanonicalStructuralBoundaries()
  {
    CanonicalSpeechWordProjection[] words =
    {
      Word(1, "In", "", true),
      Word(2, "practice", " ", false),
      Word(3, ":", "", false),
      Word(4, "For", "\n\n", true),
      Word(5, "web", " ", false),
      Word(6, "research", " ", false),
      Word(7, ".", "", false),
      Word(8, "For", "\n", true),
      Word(9, "local", " ", false),
      Word(10, "code", " ", false),
      Word(11, "files", " ", false),
      Word(12, ".", "", false),
      Word(13, "Soft", "\n\n", true),
      Word(14, "line", " ", false),
      Word(15, "wrap", "\n", false),
      Word(16, "continues", " ", false),
      Word(17, ".", "", false)
    };

    MethodInfo method = typeof(JsonlSessionMonitor).GetMethod(
      "BuildCanonicalSpeechParts",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Canonical speech-part builder is missing.");
    object result = method.Invoke(null, new object[] { words }) ??
      throw new InvalidOperationException(
        "Canonical speech-part builder returned null.");
    FieldInfo item1 = result.GetType().GetField("Item1") ??
      throw new InvalidOperationException(
        "Canonical speech-part result has no Item1 field.");
    var parts = item1.GetValue(result) as IReadOnlyList<SpeechTextPart> ??
      throw new InvalidOperationException(
        "Canonical speech-part result did not contain speech parts.");
    string[] actual = parts.Select(part => part.Text).ToArray();
    string[] expected =
    {
      "In practice:",
      "For web research.",
      "For local code files.",
      "Soft line wrap continues."
    };
    Require(actual.SequenceEqual(expected),
      "Core structural navigation boundaries were not preserved as independent speech fragments.");
  }

  private static void TestPausedMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(3002, out _),
      "Could not place the paused cursor inside the current fragment.");

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused rewind reported no destination.");
    Require(text == "For local code files",
      "Paused rewind moved away from the current fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused rewind did not move the marker to word zero of the current fragment.");
  }

  private static void TestActiveMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 2);

    Require(speech.TryRewindSentence(out string text),
      "Active rewind reported no destination.");
    Require(text == "For local code files",
      "Active rewind moved away from the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == CurrentFragmentIndex &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Active rewind did not queue word zero of the current fragment.");
  }

  /// <summary>
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

  private static void TestActiveFirstWordPendingGraceMovesPrevious()
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

  private static void TestActiveFirstWordRunningGraceMovesPrevious()
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

  /// <summary>
  /// The first J inside grace may restart the current fragment, but that J
  /// consumes the grace. A second immediate J must therefore move backward.
  /// </summary>
  private static void TestActiveLaterWordRunningGraceMovesPrevious()
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

  /// <summary>
  /// Starting audio because J selected a destination must not arm another
  /// first-word grace window for that same J-selected destination.
  /// </summary>
  private static void TestRewindDestinationArmsNewGrace()
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

  private static void TestActiveFirstWordExpiredGraceRestartsCurrent()
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

  private static void TestPausedFirstWordMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(3001, out _),
      "Could not place the paused cursor on the first word of the current fragment.");
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused first-word rewind reported no previous fragment.");
    Require(text == "For web research",
      "Paused first-word rewind did not move to the immediately preceding structural fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused first-word rewind did not land at word zero of the previous fragment.");
  }

  private static void TestLaterWordStartDoesNotArmGrace()
  {
    using SpeechService speech = CreateSpeech();
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());
    InvokePlaybackStartPreparation(speech, CurrentFragmentIndex, 1);

    Require(ReadBool(speech, "_rewindCurrentFragmentGracePending") is false,
      "Playback starting after word zero incorrectly armed the rewind grace window.");
    Require(ReadNullableLong(
        speech,
        "_rewindCurrentFragmentGraceStartedTimestamp") is null,
      "Playback starting after word zero retained a rewind grace timestamp.");
  }

  private static void TestNamedGracePeriod()
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
        period == TimeSpan.FromMilliseconds(1000),
      "RewindCurrentFragmentGracePeriod is not derived from the millisecond constant.");
  }

  /// <summary>
  /// A history start must not impersonate the real voice cursor. The rendered
  /// speaking position is published by EngineWordBoundary after audio reaches
  /// a boundary, not speculatively before the engine speaks.
  /// </summary>
  private static void TestHistoryStartHasNoSyntheticSpeakingPosition()
  {
    MethodInfo report = GetPrivateMethod(
      typeof(SpeechService),
      "ReportPlaybackPositionLocked");
    foreach (string methodName in new[]
    {
      "StartHistorySpeechLocked",
      "RestartCurrentWordLocked"
    })
    {
      MethodInfo method = GetPrivateMethod(typeof(SpeechService), methodName);
      Require(!CallsDirectly(method, report),
        $"{methodName} still publishes a synthetic pre-boundary playback position.");
    }
  }

  /// <summary>
  /// Detects a direct private-method call by its IL metadata token.
  /// </summary>
  private static bool CallsDirectly(MethodInfo caller, MethodInfo callee)
  {
    byte[] il = caller.GetMethodBody()?.GetILAsByteArray() ??
      throw new InvalidOperationException(
        $"Could not inspect IL for {caller.Name}.");
    int targetToken = callee.MetadataToken;
    for (int index = 0; index + sizeof(int) < il.Length; ++index)
    {
      // 0x28 is the IL 'call' opcode used for a direct private-method call.
      if (il[index] == 0x28 &&
          BitConverter.ToInt32(il, index + 1) == targetToken)
      {
        return true;
      }
    }
    return false;
  }

  private static MethodInfo GetPrivateMethod(Type type, string name)
  {
    return type.GetMethod(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"{type.Name}.{name} is missing.");
  }

  private static CanonicalSpeechWordProjection Word(
    long id,
    string text,
    string separator,
    bool navigationBoundaryBefore)
  {
    string json = JsonSerializer.Serialize(new
    {
      id,
      text,
      separator_before = separator,
      groups = Array.Empty<string>(),
      provenance = (object?)null,
      navigation_boundary_before = navigationBoundaryBefore
    });
    return JsonSerializer.Deserialize<CanonicalSpeechWordProjection>(json) ??
      throw new InvalidOperationException(
        "Could not deserialize canonical speech-word fixture.");
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
        Fragment(10, 1000, "In practice", "In", "practice"),
        Fragment(10, 2000, "For web research", "For", "web", "research"),
        Fragment(10, 3000, "For local code files", "For", "local", "code", "files")
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);
    return speech;
  }

  private static SpeechFragment Fragment(
    long nodeId,
    long wordIdBase,
    string text,
    params string[] words)
  {
    var mapped = new List<SpeechFragmentWord>();
    int searchStart = 0;
    for (int index = 0; index < words.Length; ++index)
    {
      int start = text.IndexOf(
        words[index],
        searchStart,
        StringComparison.Ordinal);
      if (start < 0)
      {
        throw new InvalidOperationException(
          $"Fixture word {words[index]} was not found in {text}.");
      }
      mapped.Add(new SpeechFragmentWord(
        wordIdBase + index + 1,
        words[index],
        start,
        words[index].Length));
      searchStart = start + words[index].Length;
    }
    return new SpeechFragment(
      nodeId,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      text,
      TranscriptWords: mapped.ToArray());
  }

  private static void SetActivePosition(
    SpeechService speech,
    int historyIndex,
    int wordIndex)
  {
    SetField(speech, "_pendingHistoryIndex", null);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetField(speech, "_activeHistoryIndex", historyIndex);
    SetField(speech, "_activeWordIndex", wordIndex);
    SetField(speech, "_activeWordBaseIndex", wordIndex);
    SetField(speech, "_activeKind", ParseActiveKind(speech, "History"));
    SetField(speech, "_isPaused", false);
  }

  private static void InvokePlaybackStartPreparation(
    SpeechService speech,
    int historyIndex,
    int wordIndex)
  {
    MethodInfo method = speech.GetType().GetMethod(
      "PrepareRewindCurrentFragmentGraceLocked",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Playback-start rewind-grace preparation method is missing.");
    ParameterInfo[] parameters = method.GetParameters();
    object[] arguments = parameters.Length switch
    {
      1 => new object[] { wordIndex },
      2 => new object[] { historyIndex, wordIndex },
      _ => throw new InvalidOperationException(
        "Unexpected playback-start rewind-grace preparation signature.")
    };
    method.Invoke(speech, arguments);
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

  private static bool ReadBool(SpeechService speech, string name)
  {
    return GetField(speech, name).GetValue(speech) is bool value
      ? value
      : throw new InvalidOperationException($"{name} is not a Boolean field.");
  }

  private static long? ReadNullableLong(SpeechService speech, string name)
  {
    object? value = GetField(speech, name).GetValue(speech);
    return value switch
    {
      long number => number,
      null => null,
      _ => throw new InvalidOperationException($"{name} is not a nullable long field.")
    };
  }

  private static void SetFieldIfPresent(
    SpeechService speech,
    string name,
    object? value)
  {
    FieldInfo? field = speech.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic);
    field?.SetValue(speech, value);
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
