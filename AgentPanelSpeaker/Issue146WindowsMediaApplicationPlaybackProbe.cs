using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace AgentPanelSpeaker;

/// <summary>
/// Exercises issue #146 through the retained speech service, MainForm playback
/// mailbox, and real WebView transcript projection.
/// </summary>
internal static class Issue146WindowsMediaApplicationPlaybackProbe
{
  private const string WindowsMediaVoiceName = "Zira";
  private const int TargetWordOffset = 84;
  private const string TargetWordText = "138";
  private const int TargetWordCharacterStart = 498;
  private const int TargetWordCharacterLength = 3;
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
  /// Runs the reproduced Windows.Media utterance through the production
  /// SpeechService and MainForm/WebView playback projection on an STA thread.
  /// </summary>
  internal static object GetApplicationPlaybackSnapshot()
  {
    return RunOnSta(RunApplicationPlaybackProbe);
  }

  /// <summary>
  /// Proves the same provider degrades to whole-fragment highlighting rather
  /// than inventing word ownership when explicit bookmarks are unavailable.
  /// </summary>
  internal static object GetFailClosedSnapshot()
  {
    return RunOnSta(RunFailClosedProbe);
  }

  private static object RunApplicationPlaybackProbe()
  {
    string root = CreateTempRoot("issue146-application-playback");
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllText(path, BuildCodexJsonl());

      using var lease = new MainFormTestLease();
      MainForm form = lease.Form;
      TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
      SpeechService speech = ReadField<SpeechService>(form, "_speech");
      WaitForViewInitialization(view);
      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue #146 Windows.Media application playback");
      WaitForTranscriptRender(view);

      using var monitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
        SessionLocator.FromPath(path, AgentSource.Codex),
        speakExistingLatestTurn: true,
        includeRolledBackTurns: false,
        includeUserContext: true);
      SpeechFragment fragment = history.Fragments.Single(candidate =>
        candidate.Kind == SpeechFragmentKind.FencedCodeLine &&
        string.Equals(candidate.Text, ReproducedText, StringComparison.Ordinal));
      IReadOnlyList<SpeechFragmentWord> words = fragment.TranscriptWords ??
        throw new InvalidOperationException(
          "Reproduced fenced fragment has no canonical Core word inventory.");
      Require(words.Count > TargetWordOffset,
        "Reproduced fenced fragment does not reach the fixed target owner.");
      SpeechFragmentWord targetWord = words[TargetWordOffset];
      Require(
        string.Equals(targetWord.Text, TargetWordText, StringComparison.Ordinal) &&
        targetWord.CharacterStart == TargetWordCharacterStart &&
        targetWord.CharacterLength == TargetWordCharacterLength,
        "Canonical target owner changed from the fixed source oracle: " +
        $"text={targetWord.Text}, start={targetWord.CharacterStart}, " +
        $"length={targetWord.CharacterLength}.");

      InstalledSpeechVoice voice = FindWindowsMediaVoice(speech);
      SpeechProfileSettings profile = CreateProfile(voice);
      ConfigureSpeechService(speech, profile, bookmarksEnabled: true);

      var observationGate = new object();
      var targetSeen = new ManualResetEventSlim();
      TranscriptPlaybackPosition? targetPosition = null;
      var observedWordIds = new HashSet<long>();
      int fragmentModeObserved = 0;
      int pauseRequested = 0;

      void PlaybackObserved(TranscriptPlaybackPosition position)
      {
        if (!string.Equals(
              position.FragmentText,
              ReproducedText,
              StringComparison.Ordinal))
        {
          return;
        }
        if (position.HighlightMode == TranscriptPlaybackHighlightMode.Fragment)
        {
          Interlocked.Exchange(ref fragmentModeObserved, 1);
        }
        if (position.WordIds is { Count: > 0 } wordIds)
        {
          lock (observationGate)
          {
            foreach (long wordId in wordIds)
            {
              observedWordIds.Add(wordId);
            }
          }
        }
        if (position.WordId != targetWord.Id ||
            position.HighlightMode != TranscriptPlaybackHighlightMode.Word)
        {
          return;
        }

        lock (observationGate)
        {
          targetPosition ??= position;
        }
        targetSeen.Set();
        if (Interlocked.CompareExchange(ref pauseRequested, 1, 0) == 0)
        {
          _ = speech.TogglePause();
        }
      }

      speech.PlaybackPositionChanged += PlaybackObserved;
      try
      {
        speech.LoadHistory(
          new[] { fragment },
          Array.Empty<TurnCompletion>(),
          Array.Empty<BackgroundWorkEvent>(),
          PlaybackStartMode.Beginning);
        Require(
          speech.TogglePause() == PauseToggleResult.Resumed,
          "Reproduced history did not resume into production speech playback.");

        PumpUntil(
          () => targetSeen.IsSet && speech.IsPaused,
          "recovered canonical target owner to reach paused application state",
          timeoutMilliseconds: 60000);

        TranscriptPlaybackPosition target;
        lock (observationGate)
        {
          target = targetPosition ?? throw new InvalidOperationException(
            "Target owner was signalled without a playback position.");
        }
        Require(target.WordId == targetWord.Id,
          "SpeechService projected the wrong canonical WordId at owner 84.");
        Require(target.WordIds is { Count: 1 } &&
          target.WordIds[0] == targetWord.Id,
          "Recovered owner 84 did not project as one exact canonical word.");
        Require(target.CharacterPosition == TargetWordCharacterStart &&
          target.CharacterCount == TargetWordCharacterLength,
          "SpeechService changed the fixed canonical target character range.");
        Require(Volatile.Read(ref fragmentModeObserved) == 0,
          "Reproduced Windows.Media playback degraded to fragment highlighting.");

        JsonElement browser = default;
        PumpUntil(
          () =>
          {
            browser = ProbePlayback(view);
            return string.Equals(
                browser.GetProperty("fragment").GetString(),
                ReproducedText,
                StringComparison.Ordinal) &&
              string.Equals(
                browser.GetProperty("activeWord").GetString(),
                TargetWordText,
                StringComparison.Ordinal);
          },
          "real WebView to render recovered owner 84",
          timeoutMilliseconds: 30000);

        long[] observed;
        lock (observationGate)
        {
          observed = observedWordIds.Order().ToArray();
        }
        Require(observed.Length > 1,
          "Application playback did not advance through multiple canonical words.");
        Require(observed.All(id => words.Any(word => word.Id == id)),
          "Application playback emitted a WordId outside the canonical fragment.");

        return new ApplicationPlaybackSnapshot(
          voice.ToString(),
          words.Count,
          targetWord.Id,
          target.WordId,
          target.WordIds?.ToArray() ?? Array.Empty<long>(),
          target.Word,
          target.CharacterPosition,
          target.CharacterCount,
          target.HighlightMode,
          observed.Length,
          Volatile.Read(ref fragmentModeObserved) != 0,
          browser.GetProperty("fragment").GetString() ?? string.Empty,
          browser.GetProperty("activeWord").GetString() ?? string.Empty,
          browser.GetProperty("activeWordCount").GetInt32());
      }
      finally
      {
        speech.PlaybackPositionChanged -= PlaybackObserved;
        speech.CancelAndMoveToLiveEnd();
        targetSeen.Dispose();
      }
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  private static object RunFailClosedProbe()
  {
    using var speech = new SpeechService();
    InstalledSpeechVoice voice = FindWindowsMediaVoice(speech);
    SpeechProfileSettings profile = CreateProfile(voice);
    ConfigureSpeechService(speech, profile, bookmarksEnabled: false);

    SpeechFragment fragment = BuildSyntheticFragment();
    var degraded = new ManualResetEventSlim();
    TranscriptPlaybackPosition? degradedPosition = null;
    string activity = string.Empty;

    void ActivityObserved(string message)
    {
      if (message.Contains(
            "windows_media_bookmarks_disabled",
            StringComparison.Ordinal))
      {
        activity = message;
      }
    }

    void PlaybackObserved(TranscriptPlaybackPosition position)
    {
      if (string.Equals(
            position.FragmentText,
            ReproducedText,
            StringComparison.Ordinal) &&
          position.HighlightMode == TranscriptPlaybackHighlightMode.Fragment)
      {
        degradedPosition = position;
        degraded.Set();
      }
    }

    speech.Activity += ActivityObserved;
    speech.PlaybackPositionChanged += PlaybackObserved;
    try
    {
      speech.LoadHistory(
        new[] { fragment },
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.Beginning);
      Require(
        speech.TogglePause() == PauseToggleResult.Resumed,
        "Fail-closed fixture did not resume into production playback.");
      PumpUntil(
        () => degraded.IsSet,
        "Windows.Media whole-fragment degradation",
        timeoutMilliseconds: 30000);

      TranscriptPlaybackPosition position = degradedPosition ??
        throw new InvalidOperationException(
          "Fragment degradation was signalled without a playback position.");
      Require(position.WordId is null,
        "Fragment degradation retained a fabricated canonical WordId.");
      Require(position.WordIds is null,
        "Fragment degradation retained fabricated canonical WordIds.");
      Require(activity.Contains(
          "windows_media_bookmarks_disabled",
          StringComparison.Ordinal),
        "Fragment degradation did not expose the exact provider reason.");

      return new FailClosedSnapshot(
        voice.ToString(),
        position.HighlightMode,
        position.WordId,
        position.WordIds?.Count ?? 0,
        activity);
    }
    finally
    {
      speech.Activity -= ActivityObserved;
      speech.PlaybackPositionChanged -= PlaybackObserved;
      speech.CancelAndMoveToLiveEnd();
      degraded.Dispose();
    }
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

  private static SpeechProfileSettings CreateProfile(InstalledSpeechVoice voice)
  {
    return new SpeechProfileSettings(voice.Name, 9, 0)
    {
      Volume = 70
    };
  }

  private static void ConfigureSpeechService(
    SpeechService speech,
    SpeechProfileSettings profile,
    bool bookmarksEnabled)
  {
    speech.SetPolicyProviders(
      _ => profile,
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.SetWordBoundaryPollMilliseconds(5);
    speech.SetWindowsMediaBookmarkMode(
      bookmarksEnabled
        ? WindowsMediaBookmarkMode.Fallback
        : WindowsMediaBookmarkMode.Off);
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

  private static string BuildCodexJsonl()
  {
    string response = "```text\n" + ReproducedText + "\n```";
    object[] records =
    {
      new
      {
        type = "turn_context",
        timestamp = "2026-09-18T20:00:00Z",
        payload = new { model = "gpt-5.6" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-18T20:00:01Z",
        payload = new
        {
          type = "user_message",
          message = "Show the reproduced commit history."
        }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-18T20:00:02Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = response
        }
      }
    };
    return string.Join(
      Environment.NewLine,
      records.Select(record => JsonSerializer.Serialize(record))) +
      Environment.NewLine;
  }

  private static JsonElement ProbePlayback(TranscriptView view)
  {
    return ExecuteJsonProbe(
      ReadField<WebView2>(view, "_webView"),
      """
(() => JSON.stringify({
  fragment: currentFragmentText,
  activeWord: [...transcript.querySelectorAll('.word.active')]
    .map(word => word.textContent).join(''),
  activeWordCount: transcript.querySelectorAll('.word.active').length
}))()
""");
  }

  private static void WaitForViewInitialization(TranscriptView view)
  {
    PumpUntil(
      () => ReadField<bool>(view, "_initialized"),
      "TranscriptView WebView initialization");
  }

  private static void WaitForTranscriptRender(TranscriptView view)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    PumpUntil(
      () =>
      {
        Label failure = ReadField<Label>(view, "_failureLabel");
        if (failure.Visible)
        {
          throw new InvalidOperationException(
            "TranscriptView reported render failure: " + failure.Text);
        }
        return !ReadField<bool>(view, "_refreshInProgress") &&
          webView.Visible &&
          !ReadField<Label>(view, "_loadingLabel").Visible;
      },
      "TranscriptView production render");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntil(() => task.IsCompleted, "browser output probe");
    string encoded = JsonSerializer.Deserialize<string>(
      task.GetAwaiter().GetResult()) ??
      throw new InvalidOperationException(
        "Browser output probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    var timer = Stopwatch.StartNew();
    while (!predicate())
    {
      if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
      {
        throw new TimeoutException(
          $"Timed out waiting for {description} after " +
          $"{timeoutMilliseconds} ms.");
      }
      Application.DoEvents();
      Thread.Sleep(5);
    }
  }

  private static T ReadField<T>(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    object? value = field.GetValue(target);
    if (value is not T typed)
    {
      throw new InvalidOperationException(
        $"Field {name} was not {typeof(T).Name}.");
    }
    return typed;
  }

  private static object RunOnSta(Func<object> action)
  {
    object? result = null;
    Exception? failure = null;
    using var completed = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
      try
      {
        ApplicationConfiguration.Initialize();
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
      Name = "Issue #146 Windows.Media application playback probe"
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!completed.Wait(TimeSpan.FromMinutes(2)))
    {
      throw new TimeoutException(
        "Issue #146 STA application playback probe exceeded two minutes.");
    }
    thread.Join();
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
    return result ?? throw new InvalidOperationException(
      "Issue #146 STA application playback probe returned no result.");
  }

  private static string CreateTempRoot(string prefix)
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-{prefix}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
  }

  private static void DeleteTempRoot(string root)
  {
    try
    {
      Directory.Delete(root, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private sealed record ApplicationPlaybackSnapshot(
    string Voice,
    int CanonicalWordCount,
    long ExpectedTargetWordId,
    long? ObservedTargetWordId,
    IReadOnlyList<long> ObservedTargetWordIds,
    string ObservedTargetText,
    int ObservedTargetCharacterStart,
    int ObservedTargetCharacterLength,
    TranscriptPlaybackHighlightMode HighlightMode,
    int ObservedCanonicalWordCount,
    bool FragmentModeObserved,
    string WebViewFragment,
    string WebViewActiveWord,
    int WebViewActiveWordCount);

  private sealed record FailClosedSnapshot(
    string Voice,
    TranscriptPlaybackHighlightMode HighlightMode,
    long? WordId,
    int WordIdCount,
    string Activity);
}
