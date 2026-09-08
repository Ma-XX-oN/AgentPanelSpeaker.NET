using Markdig;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies issue #35: rolled-back Codex history is retained once and visibility
/// changes do not rebuild canonical identity, speech history, or provider state.
/// </summary>
internal static class Issue35RolledBackVisibilityRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #35 regression suite.
  /// </summary>
  /// <returns>Zero when all tests pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Test)[]
    {
      ("rolled-back-visibility/stopped-toggle-does-not-reindex",
        TestStoppedToggleDoesNotReindex),
      ("rolled-back-visibility/dom-retains-historical-turns",
        TestDomRetainsHistoricalTurns),
      ("rolled-back-visibility/hidden-records-have-zero-effective-spacer-height",
        TestHiddenRecordsHaveZeroEffectiveSpacerHeight),
      ("rolled-back-visibility/height-measurements-are-layout-generation-scoped",
        TestHeightMeasurementsAreLayoutGenerationScoped),
      ("rolled-back-visibility/speech-fragments-carry-revision-visibility",
        TestSpeechFragmentsCarryRevisionVisibility),
      ("rolled-back-visibility/speech-service-can-toggle-history-eligibility",
        TestSpeechServiceCanToggleHistoryEligibility)
    };

    int failed = 0;
    foreach ((string name, Action test) in tests)
    {
      try
      {
        test();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failed;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine(failed == 0
      ? $"PASS: {tests.Length}/{tests.Length} rolled-back visibility regressions passed."
      : $"FAIL: {failed}/{tests.Length} rolled-back visibility regressions failed.");
    return failed == 0 ? 0 : 1;
  }

  /// <summary>
  /// Exercises the actual stopped-session settings callback. Changing history
  /// visibility must not start a second history-preview generation or replace
  /// already indexed speech identity.
  /// </summary>
  private static void TestStoppedToggleDoesNotReindex()
  {
    string directory = Path.Combine(
      Path.GetTempPath(),
      "AgentPanelSpeaker-Issue35-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "rollout-issue35.jsonl");
    File.WriteAllText(path, BuildRevisionJsonl());

    MainForm? form = null;
    try
    {
      form = new MainForm();
      _ = form.Handle;
      SetField(form, "_loadingSettings", true);
      GetField<ComboBox>(form, "_sourceComboBox").SelectedItem = AgentSource.Codex;
      GetField<TextBox>(form, "_sessionPathTextBox").Text = path;
      GetField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
      SetField(form, "_pathIsManual", true);
      TranscriptSettingsPopup popup =
        GetField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      popup.SetSettings(
        popup.Settings with { ShowRolledBackHistory = false },
        dark: false);
      SetField(form, "_loadingSettings", false);

      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Codex);
      InvokeTask(form, "LoadPausedHistoryPreviewAsync", session)
        .GetAwaiter().GetResult();
      SpeechService speech = GetField<SpeechService>(form, "_speech");
      SpeechFragment[] before = ReadSpeechHistoryFragments(speech);
      Require(before.Length != 0, "The baseline paused history did not index any speech.");
      int previewGeneration = GetField<int>(form, "_historyPreviewGeneration");

      popup.SetSettings(
        popup.Settings with { ShowRolledBackHistory = true },
        dark: false);
      InvokeVoid(form, "TranscriptSettingsChanged");
      Application.DoEvents();

      Require(
        GetField<int>(form, "_historyPreviewGeneration") == previewGeneration,
        "Changing ShowRolledBackHistory started another history-preview/reparse generation.");
      SpeechFragment[] after = ReadSpeechHistoryFragments(speech);
      Require(
        after.Select(FragmentIdentity).SequenceEqual(before.Select(FragmentIdentity)),
        "Changing ShowRolledBackHistory destructively replaced indexed speech identity.");
    }
    finally
    {
      form?.Dispose();
      try
      {
        Directory.Delete(directory, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }

  /// <summary>
  /// Exercises the production DOM formatter. The serialized presentation must
  /// contain both the historical original and active edited replacement before
  /// the UI applies a visibility choice.
  /// </summary>
  private static void TestDomRetainsHistoricalTurns()
  {
    string directory = Path.Combine(
      Path.GetTempPath(),
      "AgentPanelSpeaker-Issue35-Dom-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "rollout-issue35-dom.jsonl");
    File.WriteAllText(path, BuildRevisionJsonl());
    try
    {
      MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
      TranscriptPresentationDomResult result =
        TranscriptPresentationDomFormatter.Format(
          path,
          AgentSource.Codex,
          pipeline);

      Require(
        result.Html.Contains("Original question", StringComparison.Ordinal),
        "Production DOM projection discarded the historical original turn.");
      Require(
        result.Html.Contains("Edited question", StringComparison.Ordinal),
        "Production DOM projection lost the active edited turn.");
      Require(
        result.Html.Contains("revision-original", StringComparison.Ordinal),
        "Historical DOM turn does not carry Core-owned revision-original semantics.");
      Require(
        result.Html.Contains("revision-edited", StringComparison.Ordinal),
        "Edited DOM turn does not carry Core-owned revision-edited semantics.");
    }
    finally
    {
      try
      {
        Directory.Delete(directory, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }

  /// <summary>
  /// Requires virtual spacer accounting to ignore hidden historical records.
  /// A long hidden prefix is intentionally outside the loaded window so the
  /// test observes production top-spacer calculation rather than DOM styling.
  /// </summary>
  private static void TestHiddenRecordsHaveZeroEffectiveSpacerHeight()
  {
    const int RecordCount = 140;
    const int HiddenCount = 40;
    var html = new StringBuilder();
    for (int index = 0; index < RecordCount; ++index)
    {
      int record = index + 1;
      html.Append("<span class=\"record-anchor\" data-jsonl-record=\"")
        .Append(record)
        .Append("\" data-source-id=\"r")
        .Append(record)
        .Append("\"></span>");
      if (index < HiddenCount)
      {
        html.Append("<div class=\"revision-original\" hidden><p>historical record ")
          .Append(record)
          .Append(" with enough text for a measurable block</p></div>");
      }
      else
      {
        html.Append("<div><p>visible record ")
          .Append(record)
          .Append(" with enough text for a measurable block</p></div>");
      }
    }

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
      html.ToString());
    TranscriptWindow window = document.CreateWindow(120);
    double expected = document.Records
      .Take(window.StartIndex)
      .Select((record, index) => index < HiddenCount ? 0.0 : record.EstimatedHeight)
      .Sum();

    Require(
      Math.Abs(window.TopSpacerHeight - expected) < 0.001,
      "Hidden historical records still contribute ordinary height to the virtual top spacer.");
  }

  /// <summary>
  /// Requires measured heights to be associated with a layout generation. A
  /// width/DPI/font/layout change must be able to invalidate measurements from
  /// the previous generation rather than treating them as timeless values.
  /// </summary>
  private static void TestHeightMeasurementsAreLayoutGenerationScoped()
  {
    MethodInfo[] methods = typeof(TranscriptVirtualDocument)
      .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .Where(method => method.Name == "UpdateMeasuredHeights")
      .ToArray();
    Require(
      methods.Any(method => method.GetParameters().Length >= 2),
      "TranscriptVirtualDocument height measurements have no layout-generation parameter.");
  }

  /// <summary>
  /// Requires every retained speech fragment to carry canonical historical
  /// visibility metadata so playback eligibility can change without rebuilding
  /// the history list or renumbering nodes.
  /// </summary>
  private static void TestSpeechFragmentsCarryRevisionVisibility()
  {
    PropertyInfo? property = typeof(SpeechFragment).GetProperty(
      "RevisionStatus",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Require(
      property is not null,
      "SpeechFragment does not carry canonical RevisionStatus metadata.");
  }

  /// <summary>
  /// Requires a production SpeechService policy entry point for changing
  /// historical eligibility in place. The behavior itself is exercised by the
  /// follow-up paused/active playback tests once the production seam exists.
  /// </summary>
  private static void TestSpeechServiceCanToggleHistoryEligibility()
  {
    MethodInfo? method = typeof(SpeechService).GetMethod(
      "SetShowRolledBackHistory",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Require(
      method is not null,
      "SpeechService has no in-place rolled-back history eligibility switch.");
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
    return string.Join(
      Environment.NewLine,
      records.Select(record => JsonSerializer.Serialize(record))) +
      Environment.NewLine;
  }

  private static (long NodeId, ContentCategory Category, string Text)
    FragmentIdentity(SpeechFragment fragment)
  {
    return (fragment.NodeId, fragment.Category, fragment.Text);
  }

  private static SpeechFragment[] ReadSpeechHistoryFragments(SpeechService speech)
  {
    FieldInfo historyField = typeof(SpeechService).GetField(
      "_history",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService history field was not found.");
    return historyField.GetValue(speech) is IEnumerable<SpeechFragment> history
      ? history.ToArray()
      : throw new InvalidOperationException("SpeechService history is not enumerable.");
  }

  private static T GetField<T>(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    return (T)(field.GetValue(target) ??
      throw new InvalidOperationException($"Field {name} was null."));
  }

  private static void SetField<T>(object target, string name, T value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    field.SetValue(target, value);
  }

  private static void InvokeVoid(object target, string name)
  {
    MethodInfo method = target.GetType().GetMethod(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Method {name} was not found.");
    _ = method.Invoke(target, null);
  }

  private static Task InvokeTask(object target, string name, params object[] arguments)
  {
    MethodInfo method = target.GetType().GetMethod(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Method {name} was not found.");
    return method.Invoke(target, arguments) as Task ??
      throw new InvalidOperationException($"Method {name} did not return Task.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
