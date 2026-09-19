using System.Reflection;
using System.Runtime.ExceptionServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides the issue #146 application-level acceptance harness with the same
/// WinForms synchronization context used by production UI work, plus a direct
/// fail-closed provider/degradation proof that does not depend on WinMM clock
/// advancement.
/// </summary>
internal static class Issue146WindowsMediaApplicationPlaybackAcceptanceProbe
{
  private const string WindowsMediaVoiceName = "Zira";
  private const string ReproducedText =
    "chore: apply verified issue 138 production patch " +
    "chore: stage issue 138 patch helper " +
    "chore: run issue 138 patch helper " +
    "chore: remove issue 138 patch tooling " +
    "fix: preserve live-tail playback DOM identity first attempt, wrong target " +
    "chore: stage issue 138 reconciliation follow-up " +
    "chore: run issue 138 reconciliation follow-up " +
    "chore: remove issue 138 patch tooling " +
    "fix: reconcile all incoming transcript units second attempt " +
    "chore: stage issue 138 raw transcript compatibility patch " +
    "chore: run issue 138 raw transcript compatibility patch " +
    "fix: retain legacy transcript replacement path third attempt " +
    "chore: remove ... multiple " +
    "fix: preserve raw transcript replacement API fourth attempt " +
    "e8b666b test: reproduce issue 138 live-end refresh race " +
    "5d70c3d fix: retain spoken anchor at live end final fix.";

  /// <summary>
  /// Runs the existing real MainForm/SpeechService/WebView acceptance body on
  /// an STA that has an explicit WinForms synchronization context before any
  /// production control creates <see cref="Progress{T}"/> instances.
  /// </summary>
  internal static object GetApplicationPlaybackSnapshot()
  {
    return RunOnWinFormsSta(() =>
    {
      MethodInfo method =
        typeof(Issue146WindowsMediaApplicationPlaybackProbe).GetMethod(
          "RunApplicationPlaybackProbe",
          BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new InvalidOperationException(
          "Issue #146 application playback body was not found.");
      try
      {
        return method.Invoke(null, null) ??
          throw new InvalidOperationException(
            "Issue #146 application playback body returned no snapshot.");
      }
      catch (TargetInvocationException exception)
        when (exception.InnerException is not null)
      {
        ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        throw;
      }
    });
  }

  /// <summary>
  /// Proves both halves of the fail-closed contract without relying on the
  /// hosted WinMM playback clock: the real Windows.Media renderer emits no
  /// fabricated boundaries when bookmarks are disabled, then the real
  /// SpeechService degradation handler projects that provider result as one
  /// whole-fragment position with no canonical word identity.
  /// </summary>
  internal static object GetFailClosedSnapshot()
  {
    return RunOnWinFormsSta(RunFailClosedProbe);
  }

  private static object RunFailClosedProbe()
  {
    using var speech = new SpeechService();
    InstalledSpeechVoice voice = FindWindowsMediaVoice(speech);
    SpeechProfileSettings profile = new(voice.Name, 9, 0)
    {
      Volume = 70
    };
    SpeechFragment fragment = BuildSyntheticFragment();
    IReadOnlyList<SpeechFragmentWord> transcriptWords = fragment.TranscriptWords ??
      throw new InvalidOperationException(
        "Fail-closed fixture has no canonical word inventory.");

    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      ReproducedText,
      0,
      Array.Empty<string>(),
      PronunciationRuleSet.Parse(string.Empty));
    markup = markup with
    {
      Words = transcriptWords
        .Select((word, index) => new SpeechMarkupWord(
          index,
          word.Text,
          word.CharacterStart,
          word.CharacterLength))
        .ToArray()
    };

    MethodInfo renderer = typeof(SapiSpeechEngine).GetMethod(
      "RenderWindowsMediaSpeech",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Production Windows.Media renderer was not found.");
    using var synthesizer =
      new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
    object?[] arguments =
    {
      markup,
      profile,
      voice.ProviderVoiceId,
      synthesizer,
      WindowsMediaBookmarkMode.Off,
      null,
      null
    };
    try
    {
      _ = renderer.Invoke(null, arguments);
    }
    catch (TargetInvocationException exception)
      when (exception.InnerException is not null)
    {
      ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
      throw;
    }

    var boundaries = arguments[5] as IReadOnlyList<SpeechWordBoundary> ??
      throw new InvalidOperationException(
        "Windows.Media renderer returned no boundary collection.");
    var degradation = arguments[6] as SpeechTrackingDegradation ??
      throw new InvalidOperationException(
        "Windows.Media renderer returned no fail-closed degradation.");
    Require(boundaries.Count == 0,
      "Bookmarks-disabled Windows.Media rendering fabricated word boundaries.");
    Require(string.Equals(
        degradation.Reason,
        "windows_media_bookmarks_disabled",
        StringComparison.Ordinal),
      "Bookmarks-disabled renderer returned the wrong degradation reason: " +
      degradation.Reason);

    speech.SetPolicyProviders(
      _ => profile,
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[] { fragment },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    SetField(speech, "_activeHistoryIndex", 0);
    SetEnumField(speech, "_activeKind", "History");
    SetField(speech, "_activeTranscriptText", fragment.Text);
    SetField(speech, "_activeWordIndex", 0);
    SetField(speech, "_activeWordCount", 1);
    SetField(speech, "_activeCharacterPosition", transcriptWords[0].CharacterStart);
    SetField(speech, "_activeCharacterCount", transcriptWords[0].CharacterLength);
    SetField(speech, "_activeWord", transcriptWords[0].Text);

    TranscriptPlaybackPosition? projected = null;
    string activity = string.Empty;
    speech.PlaybackPositionChanged += position => projected = position;
    speech.Activity += message => activity = message;

    MethodInfo handler = typeof(SpeechService).GetMethod(
      "EngineWordTrackingUnavailable",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "SpeechService word-tracking degradation handler was not found.");
    try
    {
      handler.Invoke(speech, new object?[] { degradation });
    }
    catch (TargetInvocationException exception)
      when (exception.InnerException is not null)
    {
      ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
      throw;
    }

    TranscriptPlaybackPosition position = projected ??
      throw new InvalidOperationException(
        "SpeechService did not publish the fail-closed playback position.");
    Require(position.HighlightMode == TranscriptPlaybackHighlightMode.Fragment,
      "SpeechService did not project provider degradation as Fragment mode.");
    Require(position.WordId is null,
      "Fragment degradation retained a fabricated canonical WordId.");
    Require(position.WordIds is null,
      "Fragment degradation retained fabricated canonical WordIds.");
    Require(activity.Contains(
        degradation.Reason,
        StringComparison.Ordinal),
      "SpeechService activity omitted the provider degradation reason.");

    return new FailClosedSnapshot(
      voice.ToString(),
      boundaries.Count,
      degradation.Reason,
      position.HighlightMode,
      position.WordId,
      position.WordIds?.Count ?? 0,
      activity);
  }

  private static InstalledSpeechVoice FindWindowsMediaVoice(SpeechService speech)
  {
    return speech.GetInstalledVoices().FirstOrDefault(voice =>
      voice.Provider == SpeechVoiceProvider.WindowsMedia &&
      (voice.VoiceName.Contains(
          WindowsMediaVoiceName,
          StringComparison.OrdinalIgnoreCase) ||
       voice.ToString().Contains(
          WindowsMediaVoiceName,
          StringComparison.OrdinalIgnoreCase))) ??
      throw new InvalidOperationException(
        $"Required Windows.Media voice containing '{WindowsMediaVoiceName}' " +
        "is not installed.");
  }

  private static SpeechFragment BuildSyntheticFragment()
  {
    SpeechFragmentWord[] words = SpeechTokenization.Matches(ReproducedText)
      .Cast<System.Text.RegularExpressions.Match>()
      .Select((match, index) => new SpeechFragmentWord(
        146000L + index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
    return new SpeechFragment(
      146,
      ContentCategory.Assistant,
      SpeechFragmentKind.FencedCodeLine,
      ReproducedText,
      FenceType: "text",
      TranscriptWords: words,
      FragmentId: 146);
  }

  private static void SetField<T>(object target, string name, T value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(target, value);
  }

  private static void SetEnumField(object target, string name, string value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(target, Enum.Parse(field.FieldType, value));
  }

  private static object RunOnWinFormsSta(Func<object> action)
  {
    object? result = null;
    Exception? failure = null;
    using var completed = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
      try
      {
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(
          new WindowsFormsSynchronizationContext());
        result = action();
      }
      catch (Exception exception)
      {
        failure = exception;
      }
      finally
      {
        completed.Set();
      }
    })
    {
      IsBackground = true,
      Name = "Issue #146 Windows.Media acceptance STA"
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!completed.Wait(TimeSpan.FromMinutes(2)))
    {
      throw new TimeoutException(
        "Issue #146 Windows.Media acceptance STA exceeded two minutes.");
    }
    thread.Join();
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
    return result ?? throw new InvalidOperationException(
      "Issue #146 Windows.Media acceptance probe returned no result.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private sealed record FailClosedSnapshot(
    string Voice,
    int ProviderBoundaryCount,
    string ProviderDegradationReason,
    TranscriptPlaybackHighlightMode HighlightMode,
    long? WordId,
    int WordIdCount,
    string Activity);
}
