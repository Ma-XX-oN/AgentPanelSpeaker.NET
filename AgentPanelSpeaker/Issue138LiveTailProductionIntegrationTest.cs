using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Black-box production-path verification for issue #138.  The test drives the
/// real MainForm, WebView2, monitor, SpeechService, source-file tail, and
/// diagnostic log rather than calling the DOM reconciliation function directly.
/// </summary>
internal static class Issue138LiveTailProductionIntegrationTest
{
  private const int GeneralTimeoutMilliseconds = 30000;
  private const int PlaybackAdvanceTimeoutMilliseconds = 12000;
  private const int TestThreadTimeoutMilliseconds = 180000;
  private const int LiveAppendCount = 3;
  private const string InitialSpeechMarker =
    "stable playback continues while the live transcript grows";

  private sealed record DiagnosticEntry(string Event, JsonElement? Data);

  /// <summary>
  /// Runs the UI-bearing acceptance test on an STA thread when invoked from the
  /// generic Robot test probe.
  /// </summary>
  public static void Run()
  {
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
    {
      RunOnStaThread();
      return;
    }

    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        ApplicationConfiguration.Initialize();
        RunOnStaThread();
      }
      catch (Exception exception)
      {
        failure = exception;
      }
    })
    {
      IsBackground = true,
      Name = "Issue #138 live-tail production integration"
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TestThreadTimeoutMilliseconds))
    {
      throw new TimeoutException(
        "Issue #138 production integration did not finish within " +
        $"{TestThreadTimeoutMilliseconds} ms.");
    }
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }

  /// <summary>
  /// Starts real monitored playback, appends three live turns, and proves each
  /// transcript refresh preserves stable DOM object identity and playback-word
  /// resolution.  A deliberate removed-word mutation then proves the log oracle
  /// rejects the protected failure class.
  /// </summary>
  private static void RunOnStaThread()
  {
    DiagnosticLog.Initialize();
    string root = CreateTempRoot();
    string path = Path.Combine(root, "issue-138-live-tail.jsonl");
    File.WriteAllText(
      path,
      BuildInitialJsonl(),
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    try
    {
      long startupLogOffset = GetDiagnosticLogLength();
      using var formLease = new MainFormTestLease();
      MainForm form = formLease.Form;
      ConfigureSession(form, path);

      TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
      SpeechService speech = ReadField<SpeechService>(form, "_speech");
      JsonlSessionMonitor monitor =
        ReadField<JsonlSessionMonitor>(form, "_monitor");
      GlyphButton playPause = ReadField<GlyphButton>(form, "_playPauseButton");

      PumpUntil(
        () => ReadField<bool>(view, "_initialized"),
        "production TranscriptView WebView2 initialization");
      InvokeVoid(form, "UpdateControlState");
      Require(playPause.Enabled,
        "Production Play/Pause control was disabled for the fixed test session.");

      playPause.PerformClick();
      PumpUntil(
        () => monitor.IsRunning,
        "production JSONL monitor to start");
      PumpUntil(
        () => speech.HasHistory,
        "production speech history to load");
      PumpUntil(
        () => speech.IsSpeaking,
        "production SpeechService to enter active playback");
      PumpUntil(
        () => HasInitialSpeechBoundary(startupLogOffset),
        "initial long Assistant fragment to produce a word boundary",
        PlaybackAdvanceTimeoutMilliseconds);
      PumpUntil(
        () => HasActivePlaybackWord(view),
        "initial playback word to resolve in the real WebView2 DOM",
        PlaybackAdvanceTimeoutMilliseconds);

      // Ignore setup-time rendering while the initial transcript and playback
      // marker are first materialized.  The behavioural oracle begins only
      // once real playback is active and the current word is already resolved.
      long scenarioLogOffset = GetDiagnosticLogLength();

      for (int appendIndex = 1;
           appendIndex <= LiveAppendCount;
           ++appendIndex)
      {
        JsonElement before = CaptureDomIdentityProbe(view);
        Require(before.GetProperty("captured").GetBoolean(),
          $"Live append {appendIndex} had no active playback word to retain.");

        int renderCountBefore = CountEventSince(
          scenarioLogOffset,
          "transcript.render_completed");
        int recordCountBefore = CountEventSince(
          scenarioLogOffset,
          "jsonl.record");

        AppendLiveTurn(path, appendIndex);

        PumpUntil(
          () => CountEventSince(scenarioLogOffset, "jsonl.record") >=
            recordCountBefore + 3,
          $"live append {appendIndex} to be consumed by the production monitor");
        PumpUntil(
          () => CountEventSince(
            scenarioLogOffset,
            "transcript.render_completed") > renderCountBefore,
          $"live append {appendIndex} to complete a production transcript render");

        JsonElement after = ReadDomIdentityProbe(view, appendIndex);
        Require(after.GetProperty("sameRecord").GetBoolean(),
          $"Live append {appendIndex} replaced the unchanged virtual record DOM object.");
        Require(after.GetProperty("sameUnit").GetBoolean(),
          $"Live append {appendIndex} replaced the unchanged Core unit DOM object.");
        Require(after.GetProperty("sameWord").GetBoolean(),
          $"Live append {appendIndex} replaced the retained playback word DOM object.");
        Require(after.GetProperty("recordConnected").GetBoolean(),
          $"Live append {appendIndex} disconnected the retained virtual record.");
        Require(after.GetProperty("unitConnected").GetBoolean(),
          $"Live append {appendIndex} disconnected the retained Core unit.");
        Require(after.GetProperty("wordConnected").GetBoolean(),
          $"Live append {appendIndex} disconnected the retained playback word.");
        Require(after.GetProperty("tailCount").GetInt32() == 1,
          $"Live append {appendIndex} did not render its new response exactly once.");

        Require(speech.IsSpeaking,
          $"Playback stopped during live append {appendIndex}.");
        int boundaryCountAfterRender = CountEventSince(
          scenarioLogOffset,
          "speech.word_boundary");
        PumpUntil(
          () => CountEventSince(
            scenarioLogOffset,
            "speech.word_boundary") > boundaryCountAfterRender,
          $"playback to advance after live append {appendIndex}",
          PlaybackAdvanceTimeoutMilliseconds);
        PumpUntil(
          () => HasActivePlaybackWord(view),
          $"playback word to remain resolvable after live append {appendIndex}",
          PlaybackAdvanceTimeoutMilliseconds);

        Require(
          CountEventSince(
            scenarioLogOffset,
            "transcript.playback_word_dom_missing") == 0,
          $"Live append {appendIndex} caused the production transcript to " +
          "lose a playback word from the materialized DOM.");
      }

      IReadOnlyList<DiagnosticEntry> scenario =
        ReadDiagnosticEntriesSince(scenarioLogOffset);
      Require(
        scenario.Count(entry => entry.Event == "transcript.render_completed") >=
          LiveAppendCount,
        "The scenario did not exercise one completed transcript refresh per live append.");
      Require(
        scenario.Count(entry => entry.Event == "speech.word_boundary") >=
          LiveAppendCount,
        "The scenario did not observe continuing production word-boundary progress.");
      Require(
        scenario.Count(entry => entry.Event == "monitor.emit") >=
          LiveAppendCount,
        "The scenario did not exercise production live-fragment emission.");
      Require(
        scenario.All(entry =>
          entry.Event != "transcript.playback_word_dom_missing"),
        "Production diagnostics reported playback-word DOM loss during live growth.");

      ProveMissingWordOracleRejects(view, speech, playPause);
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  private static void ConfigureSession(MainForm form, string path)
  {
    SetField(form, "_loadingSettings", true);
    try
    {
      ReadField<ComboBox>(form, "_sourceComboBox").SelectedItem =
        AgentSource.Codex;
      ReadField<TextBox>(form, "_sessionPathTextBox").Text = path;
      ReadField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
      ReadField<CheckBox>(form, "_speakExistingCheckBox").Checked = true;
      SetField(form, "_pathIsManual", true);
    }
    finally
    {
      SetField(form, "_loadingSettings", false);
    }

    TranscriptSettingsPopup popup =
      ReadField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
    TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
    TranscriptSettings settings = popup.Settings with
    {
      FollowSpeech = true,
      HighlightUpdateMilliseconds = 10
    };
    popup.SetSettings(settings, dark: false);
    view.ApplySettings(settings, dark: false);
  }

  private static JsonElement CaptureDomIdentityProbe(TranscriptView view)
  {
    return ExecuteJsonProbe(view, """
(() => {
  const word = document.querySelector('.word.active');
  const record = word?.closest('.virtual-record') || null;
  const unit = record?.querySelector(
    '.aicore-structural-unit[data-aicore-unit-id]') || null;
  window.issue138ProductionProbe = {
    word,
    record,
    unit,
    wordId: word?.id || '',
    unitId: unit?.dataset?.aicoreUnitId || ''
  };
  return JSON.stringify({captured: !!word && !!record && !!unit});
})()
""");
  }

  private static JsonElement ReadDomIdentityProbe(
    TranscriptView view,
    int appendIndex)
  {
    string marker = JsonSerializer.Serialize(
      $"Live append response {appendIndex}.");
    return ExecuteJsonProbe(view, $$"""
(() => {
  const probe = window.issue138ProductionProbe;
  if (!probe || !probe.word || !probe.record || !probe.unit) {
    return JSON.stringify({
      sameRecord:false,
      sameUnit:false,
      sameWord:false,
      recordConnected:false,
      unitConnected:false,
      wordConnected:false,
      tailCount:0
    });
  }
  const wordNow = document.getElementById(probe.wordId);
  const unitNow = [...document.querySelectorAll(
    '.aicore-structural-unit[data-aicore-unit-id]')]
    .find(element => element.dataset.aicoreUnitId === probe.unitId) || null;
  const recordNow = wordNow?.closest('.virtual-record') || null;
  const marker = {{marker}};
  const visibleText = transcript.innerText;
  return JSON.stringify({
    sameRecord: probe.record === recordNow,
    sameUnit: probe.unit === unitNow,
    sameWord: probe.word === wordNow,
    recordConnected: probe.record.isConnected,
    unitConnected: probe.unit.isConnected,
    wordConnected: probe.word.isConnected,
    tailCount: visibleText.split(marker).length - 1
  });
})()
""");
  }

  private static void ProveMissingWordOracleRejects(
    TranscriptView view,
    SpeechService speech,
    GlyphButton playPause)
  {
    if (!speech.IsPaused)
    {
      playPause.PerformClick();
      PumpUntil(() => speech.IsPaused, "production playback to pause");
    }
    PumpUntil(
      () => HasActiveOrPausedPlaybackWord(view),
      "paused playback word before negative mutation");

    TranscriptPlaybackPosition position =
      ReadNullableField<TranscriptPlaybackPosition>(view, "_pendingPosition") ??
      throw new InvalidOperationException(
        "No production playback position was available for the negative oracle.");
    Require(position.WordId is > 0,
      "Negative oracle playback position had no canonical Core word ID.");

    long negativeOffset = GetDiagnosticLogLength();
    ExecuteVoidScript(view, """
(() => {
  const word = document.querySelector('.word.active,.word.paused');
  if (!word) throw new Error('No playback word was available to remove.');
  word.remove();
})()
""");
    view.ShowPlaybackPosition(position);

    PumpUntil(
      () => CountEventSince(
        negativeOffset,
        "transcript.playback_word_dom_missing") > 0,
      "the production missing-word oracle to reject a removed playback word");
  }

  private static bool HasInitialSpeechBoundary(long offset)
  {
    foreach (DiagnosticEntry entry in ReadDiagnosticEntriesSince(offset))
    {
      if (entry.Event != "speech.word_boundary" || entry.Data is not JsonElement data)
      {
        continue;
      }
      if (data.TryGetProperty("activeFragmentText", out JsonElement text) &&
          (text.GetString() ?? string.Empty).Contains(
            InitialSpeechMarker,
            StringComparison.Ordinal))
      {
        return true;
      }
    }
    return false;
  }

  private static bool HasActivePlaybackWord(TranscriptView view)
  {
    return ExecuteBooleanScript(
      view,
      "document.querySelector('.word.active') !== null");
  }

  private static bool HasActiveOrPausedPlaybackWord(TranscriptView view)
  {
    return ExecuteBooleanScript(
      view,
      "document.querySelector('.word.active,.word.paused') !== null");
  }

  private static JsonElement ExecuteJsonProbe(
    TranscriptView view,
    string script)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #138 production browser probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException(
        "Issue #138 production browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static bool ExecuteBooleanScript(
    TranscriptView view,
    string script)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #138 production boolean browser probe");
    return JsonSerializer.Deserialize<bool>(task.Result);
  }

  private static void ExecuteVoidScript(
    TranscriptView view,
    string script)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #138 production browser mutation");
  }

  private static void AppendLiveTurn(string path, int index)
  {
    string timestampPrefix = $"2026-09-17T12:00:{index * 3:00}";
    object[] records =
    {
      new
      {
        type = "turn_context",
        timestamp = timestampPrefix + "Z",
        payload = new { model = "gpt-5.5" }
      },
      new
      {
        type = "event_msg",
        timestamp = timestampPrefix + ".100Z",
        payload = new
        {
          type = "user_message",
          message = $"Live append request {index}."
        }
      },
      new
      {
        type = "event_msg",
        timestamp = timestampPrefix + ".200Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Live append response {index}."
        }
      }
    };
    File.AppendAllText(
      path,
      JoinJsonl(records),
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }

  private static string BuildInitialJsonl()
  {
    string response = string.Join(
      ' ',
      Enumerable.Repeat(InitialSpeechMarker, 120)) + ".";
    object[] records =
    {
      new
      {
        type = "turn_context",
        timestamp = "2026-09-17T11:59:50Z",
        payload = new { model = "gpt-5.5" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-17T11:59:51Z",
        payload = new { type = "user_message", message = "Begin the long response." }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-17T11:59:52Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = response
        }
      }
    };
    return JoinJsonl(records);
  }

  private static string JoinJsonl(IEnumerable<object> records)
  {
    return string.Join(
      Environment.NewLine,
      records.Select(record => JsonSerializer.Serialize(record))) +
      Environment.NewLine;
  }

  private static long GetDiagnosticLogLength()
  {
    return new FileInfo(DiagnosticLog.FilePath).Length;
  }

  private static int CountEventSince(long offset, string eventName)
  {
    return ReadDiagnosticEntriesSince(offset).Count(entry =>
      string.Equals(entry.Event, eventName, StringComparison.Ordinal));
  }

  private static IReadOnlyList<DiagnosticEntry> ReadDiagnosticEntriesSince(
    long offset)
  {
    using var stream = new FileStream(
      DiagnosticLog.FilePath,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    if (offset < 0 || offset > stream.Length)
    {
      throw new InvalidOperationException(
        $"Diagnostic-log offset {offset} is outside file length {stream.Length}.");
    }
    stream.Position = offset;
    using var reader = new StreamReader(
      stream,
      Encoding.UTF8,
      detectEncodingFromByteOrderMarks: true,
      bufferSize: 4096,
      leaveOpen: false);
    string tail = reader.ReadToEnd();
    int lastCompleteLine = tail.LastIndexOf('\n');
    if (lastCompleteLine < 0)
    {
      return Array.Empty<DiagnosticEntry>();
    }

    var entries = new List<DiagnosticEntry>();
    foreach (string rawLine in tail[..lastCompleteLine].Split('\n'))
    {
      string line = rawLine.TrimEnd('\r');
      if (line.Length == 0)
      {
        continue;
      }
      using JsonDocument document = JsonDocument.Parse(line);
      JsonElement root = document.RootElement;
      string eventName = root.GetProperty("Event").GetString() ??
        throw new InvalidOperationException(
          "Diagnostic record contained no event name.");
      JsonElement? data = root.TryGetProperty("Data", out JsonElement dataElement) &&
        dataElement.ValueKind != JsonValueKind.Null
          ? dataElement.Clone()
          : null;
      entries.Add(new DiagnosticEntry(eventName, data));
    }
    return entries;
  }

  private static T ReadField<T>(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    object? value = field.GetValue(target);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field {name} was not {typeof(T).Name}.");
  }

  private static T? ReadNullableField<T>(object target, string name)
    where T : class
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    return field.GetValue(target) as T;
  }

  private static void SetField<T>(object target, string name, T value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(target, value);
  }

  private static void InvokeVoid(object target, string methodName)
  {
    MethodInfo method = target.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Method {methodName} was not found.");
    try
    {
      _ = method.Invoke(target, null);
    }
    catch (TargetInvocationException exception) when (
      exception.InnerException is not null)
    {
      ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
    }
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = GeneralTimeoutMilliseconds)
  {
    var timer = Stopwatch.StartNew();
    while (!predicate())
    {
      if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
      {
        throw new TimeoutException(
          $"Timed out waiting for {description} after {timeoutMilliseconds} ms.");
      }
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntilCompleted(
    Task task,
    string description,
    int timeoutMilliseconds = GeneralTimeoutMilliseconds)
  {
    PumpUntil(() => task.IsCompleted, description, timeoutMilliseconds);
    task.GetAwaiter().GetResult();
  }

  private static string CreateTempRoot()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue138-live-{Guid.NewGuid():N}");
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
}
