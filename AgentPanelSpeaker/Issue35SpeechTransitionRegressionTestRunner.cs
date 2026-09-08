using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies issue #35 playback transitions when retained historical revisions
/// become ineligible without rebuilding speech history.
/// </summary>
internal static class Issue35SpeechTransitionRegressionTestRunner
{
  private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

  /// <summary>
  /// Runs focused rolled-back-history speech regressions.
  /// </summary>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("rolled-back-speech/core-metadata-on-fragment", TestCoreMetadataOnFragment),
      ("rolled-back-speech/in-place-policy-entry-point", TestPolicyEntryPoint),
      ("rolled-back-speech/paused-skips-hidden-run", TestPausedSkipsHiddenRun),
      ("rolled-back-speech/paused-no-destination-live-end", TestPausedNoDestinationLiveEnd),
      ("rolled-back-speech/active-cancel-move-resume", TestActiveCancelMoveResume),
      ("rolled-back-speech/active-no-destination-waits", TestActiveNoDestinationWaits),
      ("rolled-back-speech/normal-prefix-pause", TestNormalPrefixPause)
    };

    int failures = 0;
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

    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} rolled-back speech regressions passed."
      : $"FAIL: {failures}/{tests.Length} rolled-back speech regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestCoreMetadataOnFragment()
  {
    RequireProperty<string?>("RevisionStatus");
    RequireProperty<int?>("RevisionDepth");
    RequireProperty<bool>("ProjectionVisible");
    RequireProperty<bool>("RevisionHistoryControlled");
    RequireProperty<bool>("HistoricalRevision");
  }

  private static void TestPolicyEntryPoint()
  {
    Require(ToggleMethod() is not null,
      "SpeechService has no in-place rolled-back-history policy entry point.");
  }

  private static void TestPausedSkipsHiddenRun()
  {
    using SpeechService speech = CreateSpeechService(rate: 10);
    List<TranscriptPlaybackPosition> positions = CapturePositions(speech);
    SetShowHistory(speech, true);
    speech.LoadHistory(
      new[]
      {
        Fragment(10, "Historical one.", true, "original", 0),
        Fragment(11, "Historical two.", true, "superseded", 1),
        Fragment(12, "Visible destination.", false, "edited", 2)
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    TranscriptPlaybackPosition before = LastPosition(positions);
    Require(before.State == TranscriptPlaybackState.Paused && before.NodeId == 10,
      $"History-on paused cursor began at {before.State}/{before.NodeId}, expected Paused/10.");

    SetShowHistory(speech, false);
    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(speech.IsPaused &&
            final.State == TranscriptPlaybackState.Paused &&
            final.NodeId == 12,
      $"Paused hidden run landed at {final.State}/{final.NodeId}, expected Paused/12.");
  }

  private static void TestPausedNoDestinationLiveEnd()
  {
    using SpeechService speech = CreateSpeechService(rate: 10);
    List<TranscriptPlaybackPosition> positions = CapturePositions(speech);
    SetShowHistory(speech, true);
    speech.LoadHistory(
      new[]
      {
        Fragment(20, "Historical one.", true, "original", 0),
        Fragment(21, "Historical tail.", true, "superseded", 1)
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    SetShowHistory(speech, false);
    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(speech.IsPaused && final.State == TranscriptPlaybackState.PausedAtLiveEnd,
      $"Paused hidden tail ended in {final.State}, expected PausedAtLiveEnd.");
  }

  private static void TestActiveCancelMoveResume()
  {
    using SpeechService speech = CreateSpeechService(rate: -10);
    List<TranscriptPlaybackPosition> positions = CapturePositions(speech);
    SetShowHistory(speech, true);
    speech.LoadHistory(
      new[]
      {
        Fragment(
          30,
          string.Join(' ', Enumerable.Repeat("historical active source", 80)),
          true,
          "original",
          0),
        Fragment(31, "Historical skipped.", true, "superseded", 1),
        Fragment(32, "Visible resumed destination.", false, "edited", 2)
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    Require(speech.TogglePause() == PauseToggleResult.Resumed,
      "Could not start historical source speech.");
    Require(SpinWait.SpinUntil(() => speech.IsSpeaking, TimeSpan.FromSeconds(3)),
      "Historical source never entered active speech.");
    int before;
    lock (positions)
    {
      before = positions.Count;
    }

    SetShowHistory(speech, false);
    Require(SpinWait.SpinUntil(
        () =>
        {
          lock (positions)
          {
            return positions.Skip(before).Any(position =>
              position.State == TranscriptPlaybackState.Speaking &&
              position.NodeId == 32);
          }
        },
        CompletionTimeout),
      "Active hidden source did not resume at visible node 32.");

    TranscriptPlaybackPosition[] transition;
    lock (positions)
    {
      transition = positions.Skip(before).ToArray();
    }
    Require(!transition.Any(position =>
        position.State == TranscriptPlaybackState.Speaking &&
        position.NodeId is 30 or 31),
      "Active hidden transition restarted or spoke historical content.");
  }

  private static void TestActiveNoDestinationWaits()
  {
    using SpeechService speech = CreateSpeechService(rate: -10);
    List<TranscriptPlaybackPosition> positions = CapturePositions(speech);
    SetShowHistory(speech, true);
    speech.LoadHistory(
      new[]
      {
        Fragment(
          40,
          string.Join(' ', Enumerable.Repeat("historical terminal source", 80)),
          true,
          "original",
          0)
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    Require(speech.TogglePause() == PauseToggleResult.Resumed,
      "Could not start terminal historical source.");
    Require(SpinWait.SpinUntil(() => speech.IsSpeaking, TimeSpan.FromSeconds(3)),
      "Terminal historical source never entered active speech.");
    SetShowHistory(speech, false);
    Require(SpinWait.SpinUntil(() => !speech.IsSpeaking, CompletionTimeout),
      "Terminal historical cancellation did not settle.");

    TranscriptPlaybackPosition final = LastPosition(positions);
    Require(!speech.IsPaused && final.State == TranscriptPlaybackState.WaitingAtLiveEnd,
      $"Active hidden terminal source ended in {final.State}, expected WaitingAtLiveEnd.");
  }

  private static void TestNormalPrefixPause()
  {
    MethodInfo build = typeof(SpeechSapiXmlBuilder).GetMethods(
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
      .Where(method => method.Name == "Build")
      .FirstOrDefault(method => method.GetParameters().Any(parameter =>
        string.Equals(parameter.Name, "pauseBefore", StringComparison.OrdinalIgnoreCase)))
      ?? throw new InvalidOperationException(
        "SpeechSapiXmlBuilder has no prefix form of the normal 250 ms structural pause.");

    object?[] arguments = build.GetParameters().Select(parameter => parameter.Name switch
    {
      "text" => "Destination.",
      "pitchSetting" => 0,
      "spelledWords" => Array.Empty<string>(),
      "pronunciations" => PronunciationRuleSet.Parse(string.Empty),
      "pauseAfter" => false,
      "pauseBefore" => true,
      _ when parameter.HasDefaultValue => parameter.DefaultValue,
      _ => throw new InvalidOperationException(
        $"Unexpected SpeechSapiXmlBuilder.Build parameter {parameter.Name}.")
    }).ToArray();
    SpeechMarkup markup = (SpeechMarkup)(build.Invoke(null, arguments) ??
      throw new InvalidOperationException("Speech markup build returned null."));

    int sapiPause = markup.SapiXml.IndexOf(
      "<silence msec=\"250\"/>",
      StringComparison.Ordinal);
    int sapiText = markup.SapiXml.IndexOf("Destination.", StringComparison.Ordinal);
    int ssmlPause = markup.SsmlContent.IndexOf(
      "<break time=\"250ms\"/>",
      StringComparison.Ordinal);
    int ssmlText = markup.SsmlContent.IndexOf("Destination.", StringComparison.Ordinal);
    Require(sapiPause >= 0 && sapiPause < sapiText,
      "SAPI prefix pause is missing or after destination text.");
    Require(ssmlPause >= 0 && ssmlPause < ssmlText,
      "SSML prefix pause is missing or after destination text.");
  }

  private static MethodInfo? ToggleMethod()
  {
    return typeof(SpeechService).GetMethod(
      "SetShowRolledBackHistory",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
  }

  private static void SetShowHistory(SpeechService speech, bool show)
  {
    MethodInfo method = ToggleMethod() ?? throw new InvalidOperationException(
      "SpeechService has no in-place rolled-back-history policy entry point.");
    try
    {
      _ = method.Invoke(speech, new object[] { show });
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
      throw exception.InnerException;
    }
  }

  private static void RequireProperty<T>(string name)
  {
    PropertyInfo property = typeof(SpeechFragment).GetProperty(
      name,
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException(
        $"SpeechFragment does not expose {name}.");
    Require(property.PropertyType == typeof(T),
      $"SpeechFragment.{name} is {property.PropertyType}, expected {typeof(T)}.");
  }

  private static SpeechFragment Fragment(
    long nodeId,
    string text,
    bool historical,
    string status,
    int depth)
  {
    ConstructorInfo constructor = typeof(SpeechFragment).GetConstructors(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .OrderByDescending(item => item.GetParameters().Length)
      .First();
    object?[] arguments = constructor.GetParameters().Select(parameter =>
      parameter.Name switch
      {
        "NodeId" or "nodeId" => nodeId,
        "Category" or "category" => ContentCategory.Assistant,
        "Kind" or "kind" => SpeechFragmentKind.Prose,
        "Text" or "text" => text,
        "FenceType" or "fenceType" => string.Empty,
        "FenceBlockId" or "fenceBlockId" => -1,
        "FenceLineIndex" or "fenceLineIndex" => -1,
        "FenceLineCount" or "fenceLineCount" => 0,
        "PauseAfter" or "pauseAfter" => false,
        "NodeTimestampUtc" or "nodeTimestampUtc" => null,
        "StartsUserTurn" or "startsUserTurn" => false,
        "RevisionStatus" or "revisionStatus" => status,
        "RevisionDepth" or "revisionDepth" => depth,
        "ProjectionVisible" or "projectionVisible" => !historical,
        "RevisionHistoryControlled" or "revisionHistoryControlled" => true,
        "HistoricalRevision" or "historicalRevision" => historical,
        _ when parameter.HasDefaultValue => parameter.DefaultValue,
        _ => throw new InvalidOperationException(
          $"Unexpected SpeechFragment constructor parameter {parameter.Name}.")
      }).ToArray();
    return (SpeechFragment)constructor.Invoke(arguments);
  }

  private static SpeechService CreateSpeechService(int rate)
  {
    var speech = new SpeechService();
    InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault()
      ?? throw new InvalidOperationException("No installed voice is available.");
    var profile = new SpeechProfileSettings(voice.Name, rate, 0)
    {
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

  private static TranscriptPlaybackPosition LastPosition(
    List<TranscriptPlaybackPosition> positions)
  {
    lock (positions)
    {
      if (positions.Count == 0)
      {
        throw new InvalidOperationException(
          "SpeechService emitted no playback position.");
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
