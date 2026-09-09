using Microsoft.Web.WebView2.WinForms;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Independent observable-output acceptance tests for issue #46. Production
/// code creates only the actual result; expected browser and playback facts are
/// fixed in these tests and are never derived by a production renderer.
/// </summary>
internal static class Issue46IndependentRegressionOracleTestRunner
{
  /// <summary>
  /// Runs the independent acceptance-oracle suite.
  /// </summary>
  /// <returns>Zero when every oracle and negative mutation passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("independent-oracle/test-runner-rejects-partial-completion",
        TestRunnerRejectsPartialCompletion),
      ("independent-oracle/issue24-provider-to-browser-playback",
        TestIssue24ProviderToBrowserPlayback),
      ("independent-oracle/issue35-real-toggle-browser-and-playback",
        TestIssue35RealToggleBrowserAndPlayback),
      ("independent-oracle/issue26-real-toggle-playback-policy",
        TestIssue26RealTogglePlaybackPolicy)
    };

    int failed = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #46 independent regression-oracle suite: {tests.Length} tests");
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failed;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failed == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #46 independent " +
        "regression-oracle tests passed."
      : $"FAIL: {failed}/{tests.Length} issue #46 independent " +
        "regression-oracle tests failed.");
    return failed == 0 ? 0 : 1;
  }

  /// <summary>
  /// Proves a zero exit code plus a suite header cannot be mistaken for a
  /// completed test run. The exact requested completion marker is mandatory.
  /// </summary>
  private static void TestRunnerRejectsPartialCompletion()
  {
    const string Partial =
      "Issue #44 independent-output-oracle suite: 3 tests\n" +
      "PASS  output-oracle/first-test\n";
    Require(
      !TestSuiteCompletionMarker.IsSuccessful(
        Partial,
        "output-oracle",
        exitCode: 0),
      "Header-only/partial output was accepted as a completed suite.");

    string complete = Partial +
      TestSuiteCompletionMarker.Format("output-oracle", 0) + "\n";
    Require(
      TestSuiteCompletionMarker.IsSuccessful(
        complete,
        "output-oracle",
        exitCode: 0),
      "A valid completion marker was not accepted.");
    Require(
      !TestSuiteCompletionMarker.IsSuccessful(
        complete,
        "output-oracle",
        exitCode: 1),
      "A non-zero process exit was accepted despite the marker.");
  }

  /// <summary>
  /// Runs a fixed Codex provider fixture through the real TranscriptView load,
  /// canonical rendering, browser installation, production playback message,
  /// and final word highlight. The test never calls assignRecordScopes(),
  /// assignNodeScopes(), or installs a stable word map itself.
  /// </summary>
  private static void TestIssue24ProviderToBrowserPlayback()
  {
    const string NestedFragment =
      "1. *Nested numbered item with a table inside it*";
    const string TableFragment = "| Column | Value | Style |";
    string root = CreateTempRoot("issue46-issue24");
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllText(path, BuildNestedListJsonl());

      using var host = CreateOffscreenHost(900, 700);
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);

      view.SelectSession(path, AgentSource.Codex, "Issue #46 issue #24 fixture");
      WaitForTranscriptRender(view);

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity identity = identities.FirstOrDefault(item =>
        item.Segments.Contains(NestedFragment, StringComparer.Ordinal) &&
        item.Segments.Contains(TableFragment, StringComparer.Ordinal)) ??
        throw new InvalidOperationException(
          "Production transcript identities omitted the fixed nested-list/table fragments.");

      var nestedPosition = new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        NestedFragment,
        3,
        "Nested",
        identity.NodeId,
        0,
        0,
        Stopwatch.GetTimestamp());
      view.ShowPlaybackPosition(nestedPosition);
      JsonElement nested = WaitForPlaybackProbe(
        view,
        NestedFragment,
        "Nested",
        "nested numbered-list fragment");
      RequirePlaybackProbe(
        nested,
        NestedFragment,
        "Nested",
        "nested numbered-list fragment");

      var tablePosition = new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        TableFragment,
        1,
        "Column",
        identity.NodeId,
        0,
        0,
        Stopwatch.GetTimestamp());
      view.ShowPlaybackPosition(tablePosition);
      JsonElement table = WaitForPlaybackProbe(
        view,
        TableFragment,
        "Column",
        "nested table fragment");
      RequirePlaybackProbe(
        table,
        TableFragment,
        "Column",
        "nested table fragment");

      // Mutation proof: retain the node ID but remove the production-created
      // segment ranges. The same final-output oracle must reject the result.
      ExecuteScript(
        ReadField<WebView2>(view, "_webView"),
        $"segmentRangesByNode.delete({JsonSerializer.Serialize(identity.NodeId.ToString())});" +
        $"knownNodeIds.add({JsonSerializer.Serialize(identity.NodeId.ToString())});");
      view.ShowPlaybackPosition(nestedPosition with
      {
        BoundaryTimestamp = Stopwatch.GetTimestamp()
      });
      Application.DoEvents();
      Thread.Sleep(25);
      Application.DoEvents();
      JsonElement mutated = ProbePlayback(view);
      ExpectOracleRejects(
        () => RequirePlaybackProbe(
          mutated,
          NestedFragment,
          "Nested",
          "mutated nested numbered-list fragment"),
        "The issue #24 oracle accepted a deliberately removed production segment map.");
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  /// <summary>
  /// Uses the real transcript-settings checkbox event wired by MainForm. OFF
  /// must hide historical browser output and suppress historical playback; ON
  /// must restore both without rebuilding indexed history or session identity.
  /// </summary>
  private static void TestIssue35RealToggleBrowserAndPlayback()
  {
    string root = CreateTempRoot("issue46-issue35");
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllText(path, BuildRevisionJsonl());

      using var form = CreateOffscreenMainForm();
      ConfigureMainFormSession(form, path);
      TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
      TranscriptSettingsPopup popup =
        ReadField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      SpeechService speech = ReadField<SpeechService>(form, "_speech");
      WaitForViewInitialization(view);

      TranscriptSettings baseline = popup.Settings with
      {
        ShowRolledBackHistory = true
      };
      popup.SetSettings(baseline, dark: false);
      view.ApplySettings(baseline, dark: false);
      speech.SetShowRolledBackHistory(true);

      using var previewMonitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = previewMonitor.LoadHistoryPreview(
        SessionLocator.FromPath(path, AgentSource.Codex),
        speakExistingLatestTurn: true,
        includeRolledBackTurns: true,
        includeUserContext: true);
      speech.LoadHistory(
        history.Fragments,
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.Beginning);

      SpeechFragment historical = history.Fragments.First(fragment =>
        fragment.HistoricalRevision);
      SpeechFragment edited = history.Fragments.First(fragment =>
        !fragment.HistoricalRevision &&
        fragment.Text.Contains("Edited", StringComparison.Ordinal));
      SpeechFragment[] retainedBefore = ReadSpeechHistory(speech);

      view.SelectSession(path, AgentSource.Codex, "Issue #46 issue #35 fixture");
      WaitForTranscriptRender(view);
      JsonElement visible = WaitForRevisionProbe(view, historicalVisible: true);
      RequireRevisionState(visible, historicalVisible: true);
      Require(
        speech.TrySeekToTranscriptWord(historical.NodeId, 0, out _),
        "Historical speech was not eligible while Show rolled-back history was ON.");

      int generation = ReadField<int>(form, "_historyPreviewGeneration");
      int monitorSession = ReadField<int>(form, "_monitorSession");
      CheckBox control = ReadField<CheckBox>(popup, "_showRolledBackCheckBox");
      Require(control.Checked,
        "Issue #35 baseline checkbox was not ON before the real toggle.");

      control.Checked = false;
      JsonElement hidden = WaitForRevisionProbe(view, historicalVisible: false);
      RequireRevisionState(hidden, historicalVisible: false);
      Require(
        !speech.TrySeekToTranscriptWord(historical.NodeId, 0, out _),
        "Real OFF control event left historical speech playback-eligible.");
      Require(
        speech.TrySeekToTranscriptWord(edited.NodeId, 0, out _),
        "Real OFF control event hid the active edited speech fragment.");
      RequireUnchangedSessionState(
        form,
        generation,
        monitorSession,
        retainedBefore,
        speech,
        "disabling Show rolled-back history");

      // Mutation proof: force the historical browser element visible after OFF.
      // The fixed visible-output oracle must reject this known-bad state.
      ExecuteScript(
        ReadField<WebView2>(view, "_webView"),
        "document.querySelectorAll('.revision-original,.revision-superseded')" +
        ".forEach(e=>{e.hidden=false;e.style.setProperty('display','block','important');" +
        "e.style.setProperty('visibility','visible','important');});");
      JsonElement mutated = ProbeRevisionState(view);
      ExpectOracleRejects(
        () => RequireRevisionState(mutated, historicalVisible: false),
        "The issue #35 browser oracle accepted deliberately visible rolled-back history while OFF.");

      control.Checked = true;
      JsonElement shownAgain = WaitForRevisionProbe(view, historicalVisible: true);
      RequireRevisionState(shownAgain, historicalVisible: true);
      Require(
        speech.TrySeekToTranscriptWord(historical.NodeId, 0, out _),
        "Real ON control event did not restore historical speech eligibility.");
      RequireUnchangedSessionState(
        form,
        generation,
        monitorSession,
        retainedBefore,
        speech,
        "enabling Show rolled-back history");
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  /// <summary>
  /// Uses the real Speak User/IDE context checkbox CheckedChanged event. User
  /// Context stays indexed once while only final playback eligibility changes.
  /// </summary>
  private static void TestIssue26RealTogglePlaybackPolicy()
  {
    string root = CreateTempRoot("issue46-issue26");
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllText(path, BuildUserContextJsonl());

      using var form = CreateOffscreenMainForm();
      ConfigureMainFormSession(form, path);
      TranscriptSettingsPopup popup =
        ReadField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      SpeechService speech = ReadField<SpeechService>(form, "_speech");

      TranscriptSettings baseline = popup.Settings with
      {
        SpeakUserContext = false
      };
      popup.SetSettings(baseline, dark: false);
      SetField(form, "_speakUserContext", false);

      using var previewMonitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = previewMonitor.LoadHistoryPreview(
        SessionLocator.FromPath(path, AgentSource.Codex),
        speakExistingLatestTurn: true,
        includeRolledBackTurns: false,
        includeUserContext: true);
      speech.LoadHistory(
        history.Fragments,
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.Beginning);

      SpeechFragment context = history.Fragments.First(fragment =>
        fragment.Category == ContentCategory.UserContext);
      SpeechFragment user = history.Fragments.First(fragment =>
        fragment.Category == ContentCategory.User);
      SpeechFragment[] retainedBefore = ReadSpeechHistory(speech);
      int generation = ReadField<int>(form, "_historyPreviewGeneration");
      int monitorSession = ReadField<int>(form, "_monitorSession");
      CheckBox control = ReadField<CheckBox>(popup, "_speakUserContextCheckBox");

      RequireUserContextState(
        speech,
        context,
        user,
        expectedContextEligible: false);
      Require(!control.Checked,
        "Issue #26 baseline checkbox was not OFF before the real toggle.");

      control.Checked = true;
      Application.DoEvents();
      RequireUserContextState(
        speech,
        context,
        user,
        expectedContextEligible: true);
      RequireUnchangedSessionState(
        form,
        generation,
        monitorSession,
        retainedBefore,
        speech,
        "enabling Speak User/IDE context");

      // Mutation proof: simulate broken event wiring by undoing the policy field
      // without changing the checked control. The same eligibility oracle must
      // reject the mismatch.
      SetField(form, "_speakUserContext", false);
      ExpectOracleRejects(
        () => RequireUserContextState(
          speech,
          context,
          user,
          expectedContextEligible: true),
        "The issue #26 oracle accepted an ON checkbox whose playback policy stayed OFF.");
      SetField(form, "_speakUserContext", true);

      control.Checked = false;
      Application.DoEvents();
      RequireUserContextState(
        speech,
        context,
        user,
        expectedContextEligible: false);
      RequireUnchangedSessionState(
        form,
        generation,
        monitorSession,
        retainedBefore,
        speech,
        "disabling Speak User/IDE context");
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  private static Form CreateOffscreenHost(int width, int height)
  {
    return new Form
    {
      Width = width,
      Height = height,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
  }

  private static MainForm CreateOffscreenMainForm()
  {
    var form = new MainForm
    {
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    form.Show();
    _ = form.Handle;
    return form;
  }

  private static void ConfigureMainFormSession(MainForm form, string path)
  {
    SetField(form, "_loadingSettings", true);
    try
    {
      ReadField<ComboBox>(form, "_sourceComboBox").SelectedItem = AgentSource.Codex;
      ReadField<TextBox>(form, "_sessionPathTextBox").Text = path;
      ReadField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
      SetField(form, "_pathIsManual", true);
      SetField(form, "_historyPreviewGeneration", 314);
      SetField(form, "_monitorSession", 271);
    }
    finally
    {
      SetField(form, "_loadingSettings", false);
    }
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

  private static JsonElement WaitForPlaybackProbe(
    TranscriptView view,
    string fragment,
    string word,
    string description)
  {
    JsonElement result = default;
    PumpUntil(
      () =>
      {
        result = ProbePlayback(view);
        return string.Equals(
            result.GetProperty("fragment").GetString(),
            fragment,
            StringComparison.Ordinal) &&
          string.Equals(
            result.GetProperty("activeWord").GetString(),
            word,
            StringComparison.Ordinal);
      },
      description);
    return result;
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
  listHighlights: transcript.querySelectorAll('.speech-list-item-active').length
}))()
""");
  }

  private static void RequirePlaybackProbe(
    JsonElement actual,
    string fragment,
    string word,
    string description)
  {
    Require(
      string.Equals(
        actual.GetProperty("fragment").GetString(),
        fragment,
        StringComparison.Ordinal),
      $"{description} resolved fragment '{actual.GetProperty("fragment").GetString()}', " +
      $"expected '{fragment}'.");
    Require(
      string.Equals(
        actual.GetProperty("activeWord").GetString(),
        word,
        StringComparison.Ordinal),
      $"{description} highlighted '{actual.GetProperty("activeWord").GetString()}', " +
      $"expected '{word}'.");
  }

  private static JsonElement WaitForRevisionProbe(
    TranscriptView view,
    bool historicalVisible)
  {
    JsonElement result = default;
    PumpUntil(
      () =>
      {
        result = ProbeRevisionState(view);
        bool actual = result.GetProperty("historicalVisible").GetBoolean();
        bool edited = result.GetProperty("editedVisible").GetBoolean();
        return actual == historicalVisible && edited;
      },
      historicalVisible
        ? "rolled-back browser history to become visible"
        : "rolled-back browser history to become hidden");
    return result;
  }

  private static JsonElement ProbeRevisionState(TranscriptView view)
  {
    return ExecuteJsonProbe(
      ReadField<WebView2>(view, "_webView"),
      """
(() => {
  const visible = element => {
    if (!element) return false;
    const style = getComputedStyle(element);
    return !element.hidden && style.display !== 'none' &&
      style.visibility !== 'hidden';
  };
  const historical = [...transcript.querySelectorAll(
    '.revision-original,.revision-superseded')]
    .find(element => element.textContent.includes('Original question'));
  const edited = [...transcript.querySelectorAll('.revision-edited')]
    .find(element => element.textContent.includes('Edited question')) ||
    [...transcript.querySelectorAll('*')]
      .find(element => element.textContent.trim() === 'Edited question');
  return JSON.stringify({
    historicalExists: !!historical,
    historicalVisible: visible(historical),
    editedExists: !!edited,
    editedVisible: visible(edited),
    visibleText: transcript.innerText
  });
})()
""");
  }

  private static void RequireRevisionState(
    JsonElement actual,
    bool historicalVisible)
  {
    Require(actual.GetProperty("historicalExists").GetBoolean(),
      "Historical original was discarded instead of retained in the browser DOM.");
    Require(actual.GetProperty("editedExists").GetBoolean(),
      "Active edited turn is missing from the browser DOM.");
    Require(
      actual.GetProperty("historicalVisible").GetBoolean() == historicalVisible,
      historicalVisible
        ? "Historical original is not visibly rendered while the setting is ON."
        : "Historical original remains visibly rendered while the setting is OFF.");
    Require(actual.GetProperty("editedVisible").GetBoolean(),
      "Active edited turn is not visibly rendered.");
    string visibleText = actual.GetProperty("visibleText").GetString() ?? string.Empty;
    Require(visibleText.Contains("Edited question", StringComparison.Ordinal),
      "Visible browser text lost the active edited question.");
    Require(
      visibleText.Contains("Original question", StringComparison.Ordinal) ==
        historicalVisible,
      historicalVisible
        ? "Visible browser text omitted the historical original while ON."
        : "Visible browser text still contains the historical original while OFF.");
  }

  private static void RequireUserContextState(
    SpeechService speech,
    SpeechFragment context,
    SpeechFragment user,
    bool expectedContextEligible)
  {
    bool contextEligible = speech.TrySeekToTranscriptWord(
      context.NodeId,
      0,
      out _);
    Require(
      contextEligible == expectedContextEligible,
      $"User Context eligibility is {contextEligible}, expected {expectedContextEligible}.");
    Require(
      speech.TrySeekToTranscriptWord(user.NodeId, 0, out _),
      "Actual User prompt became ineligible while toggling User Context.");
  }

  private static void RequireUnchangedSessionState(
    MainForm form,
    int generation,
    int monitorSession,
    IReadOnlyList<SpeechFragment> retainedBefore,
    SpeechService speech,
    string description)
  {
    Require(
      ReadField<int>(form, "_historyPreviewGeneration") == generation,
      $"{description} rebuilt canonical history.");
    Require(
      ReadField<int>(form, "_monitorSession") == monitorSession,
      $"{description} restarted/replaced the monitor session.");
    SpeechFragment[] retainedAfter = ReadSpeechHistory(speech);
    Require(
      retainedAfter.Select(fragment =>
          (fragment.NodeId, fragment.Category, fragment.Text, fragment.HistoricalRevision))
        .SequenceEqual(retainedBefore.Select(fragment =>
          (fragment.NodeId, fragment.Category, fragment.Text, fragment.HistoricalRevision))),
      $"{description} changed retained speech history instead of policy in place.");
  }

  private static SpeechFragment[] ReadSpeechHistory(SpeechService speech)
  {
    FieldInfo field = typeof(SpeechService).GetField(
      "_history",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService history field was not found.");
    if (field.GetValue(speech) is not IEnumerable history)
    {
      throw new InvalidOperationException("SpeechService history is not enumerable.");
    }
    return history.Cast<SpeechFragment>().ToArray();
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser output probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException("Browser output probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void ExecuteScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser mutation probe");
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
          $"Timed out waiting for {description} after {timeoutMilliseconds} ms.");
      }
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntilCompleted(Task task, string description)
  {
    PumpUntil(() => task.IsCompleted, description);
    task.GetAwaiter().GetResult();
  }

  private static void ExpectOracleRejects(Action oracle, string message)
  {
    bool rejected = false;
    try
    {
      oracle();
    }
    catch (InvalidOperationException)
    {
      rejected = true;
    }
    Require(rejected, message);
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

  private static void SetField<T>(object target, string name, T value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(target, value);
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

  private static string BuildNestedListJsonl()
  {
    const string Response = """
Like this:

1. Outer item
   1. *Nested numbered item with a table inside it*

      | Column | Value | Style |
      | --- | --- | --- |
      | Alpha | 1 | **bold** |
      | Beta | 2 | `code` |
      | Gamma | 3 | *italic* |

   2. Another nested numbered item
2. Second outer item
""";
    object[] records =
    {
      new
      {
        type = "turn_context",
        timestamp = "2026-09-09T00:00:00Z",
        payload = new { model = "gpt-5.5" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-09T00:00:01Z",
        payload = new { type = "user_message", message = "Show the nested example." }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-09T00:00:02Z",
        payload = new { type = "agent_message", phase = "final", message = Response }
      }
    };
    return JoinJsonl(records);
  }

  private static string BuildRevisionJsonl()
  {
    object[] records =
    {
      new
      {
        type = "turn_context",
        timestamp = "2026-09-08T00:00:00Z",
        payload = new { model = "gpt-5.5" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-08T00:00:01Z",
        payload = new { type = "user_message", message = "Original question" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-08T00:00:02Z",
        payload = new { type = "agent_message", phase = "final", message = "Original answer" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-08T00:00:03Z",
        payload = new { type = "thread_rolled_back", num_turns = 1 }
      },
      new
      {
        type = "turn_context",
        timestamp = "2026-09-08T00:00:04Z",
        payload = new { model = "gpt-5.5" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-08T00:00:05Z",
        payload = new { type = "user_message", message = "Edited question" }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-08T00:00:06Z",
        payload = new { type = "agent_message", phase = "final", message = "Edited answer" }
      }
    };
    return JoinJsonl(records);
  }

  private static string BuildUserContextJsonl()
  {
    const string Message =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n" +
      "What time is it in Paris?";
    object[] records =
    {
      new
      {
        type = "event_msg",
        timestamp = "2026-09-07T00:00:00Z",
        payload = new { type = "user_message", message = Message }
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

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
