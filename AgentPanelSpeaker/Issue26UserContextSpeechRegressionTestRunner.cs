using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies the AgentPanelSpeaker integration contract for Core-owned
/// User-context speech selection.
/// </summary>
internal static class Issue26UserContextSpeechRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #26 regression suite.
  /// </summary>
  /// <returns>Zero when all tests pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Test)[]
    {
      ("user-context-speech/settings-default-and-persistence",
        TestSettingsDefaultAndPersistence),
      ("user-context-speech/settings-change-tracking",
        TestSettingsChangeTracking),
      ("user-context-speech/core-option-and-extraction",
        TestCoreOptionAndExtraction),
      ("user-context-speech/live-monitor-toggle",
        TestLiveMonitorToggle)
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
      ? $"PASS: {tests.Length}/{tests.Length} user-context speech regressions passed."
      : $"FAIL: {failed}/{tests.Length} user-context speech regressions failed.");
    return failed == 0 ? 0 : 1;
  }

  /// <summary>
  /// Requires a persisted Transcript Settings switch whose default preserves
  /// current behaviour by leaving IDE/User context speech disabled.
  /// </summary>
  private static void TestSettingsDefaultAndPersistence()
  {
    PropertyInfo property = typeof(TranscriptSettings).GetProperty(
      "SpeakUserContext") ?? throw new InvalidOperationException(
        "TranscriptSettings does not expose SpeakUserContext.");
    Require(property.PropertyType == typeof(bool),
      "TranscriptSettings.SpeakUserContext is not Boolean.");
    Require(property.GetValue(TranscriptSettings.Default) is false,
      "SpeakUserContext must default to false.");

    string json = JsonSerializer.Serialize(TranscriptSettings.Default);
    TranscriptSettings? roundTrip = JsonSerializer.Deserialize<TranscriptSettings>(json);
    Require(roundTrip is not null, "TranscriptSettings JSON round-trip failed.");
    Require(property.GetValue(roundTrip) is false,
      "SpeakUserContext did not survive the settings JSON round-trip.");
  }

  /// <summary>
  /// Requires the switch to participate in the application's ordinary dirty
  /// tracking and selective-save merge path.
  /// </summary>
  private static void TestSettingsChangeTracking()
  {
    UserSettings saved = UserSettings.CreateDefault(Array.Empty<string>());
    UserSettings working = saved with
    {
      Transcript = saved.Transcript with { SpeakUserContext = true }
    };

    IReadOnlyList<SettingsChangeSet.Change> changes =
      SettingsChangeSet.GetChanges(saved, working);
    Require(
      changes.Any(change =>
        string.Equals(
          change.Key,
          "Transcript/SpeakUserContext",
          StringComparison.Ordinal)),
      "SpeakUserContext is not reported by SettingsChangeSet.GetChanges.");

    UserSettings merged = SettingsChangeSet.MergeSelected(
      saved,
      working,
      new HashSet<string>(StringComparer.Ordinal)
      {
        "Transcript/SpeakUserContext"
      });
    Require(
      merged.Transcript.SpeakUserContext,
      "Selective settings merge did not retain SpeakUserContext.");
  }

  /// <summary>
  /// Exercises the real Core client, speech-selection preparation, and
  /// canonical extraction path with the option disabled and enabled.
  /// </summary>
  private static void TestCoreOptionAndExtraction()
  {
    const string prompt = "What time is it in Paris?";
    const string sourceMessage =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n" + prompt;
    string record = JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-07T00:00:00Z",
      payload = new { type = "user_message", message = sourceMessage }
    });

    PropertyInfo optionProperty = typeof(AIConversationCoreProjectOptions)
      .GetProperty("IncludeUserContext") ?? throw new InvalidOperationException(
        "AIConversationCoreProjectOptions does not expose IncludeUserContext.");
    Require(optionProperty.PropertyType == typeof(bool),
      "AIConversationCoreProjectOptions.IncludeUserContext is not Boolean.");

    using var client = new AIConversationCoreClient();
    AIConversationProjection disabledRaw = client.Project(
      AgentSource.Codex,
      new[] { record },
      CreateProjectOptions(includeUserContext: false));
    AIConversationProjection enabledRaw = client.Project(
      AgentSource.Codex,
      new[] { record },
      CreateProjectOptions(includeUserContext: true));

    Require(disabledRaw.Markdown == enabledRaw.Markdown,
      "Speech selection changed the canonical rendered transcript.");

    ExtractionResult disabled = CanonicalProjectionExtractor.ExtractRecord(
      CanonicalSpeechProjection.Prepare(disabledRaw),
      AgentSource.Codex,
      0);
    ExtractionResult enabled = CanonicalProjectionExtractor.ExtractRecord(
      CanonicalSpeechProjection.Prepare(enabledRaw),
      AgentSource.Codex,
      0);

    IReadOnlyList<(string Category, string Text)> disabledNodes = ReadNodes(disabled);
    IReadOnlyList<(string Category, string Text)> enabledNodes = ReadNodes(enabled);

    Require(disabledNodes.Count == 1,
      $"Disabled context speech emitted {disabledNodes.Count} nodes instead of one prompt.");
    Require(disabledNodes[0].Category == nameof(ContentCategory.User),
      "Disabled context speech changed the prompt voice category.");
    Require(disabledNodes[0].Text == prompt,
      "Disabled context speech did not leave the actual User prompt intact.");

    Require(enabledNodes.Count == 2,
      $"Enabled context speech emitted {enabledNodes.Count} nodes instead of context + prompt.");
    Require(enabledNodes[0].Category == nameof(ContentCategory.UserContext),
      "Included IDE context did not use the alternate User-context voice category.");
    Require(enabledNodes[0].Text.Contains("## Active file:", StringComparison.Ordinal),
      "Included IDE context lost canonical context content.");
    Require(enabledNodes[1].Category == nameof(ContentCategory.User),
      "Actual prompt did not retain the normal User voice category.");
    Require(enabledNodes[1].Text == prompt,
      "Actual prompt changed when IDE context speech was enabled.");
  }

  /// <summary>
/// Verifies User Context is always indexed once Core has classified it,
/// while the UI switch changes only final playback eligibility. Toggling
/// must not rebuild canonical history or restart the live monitor.
/// </summary>
private static void TestLiveMonitorToggle()
{
  const string prompt = "What time is it in Paris?";
  const string sourceMessage =
    "# Context from my IDE setup:\n\n" +
    "## Active file: sessions/example.jsonl\n\n" +
    "## Open tabs:\n" +
    "- sessions/example.jsonl\n\n" +
    "## My request for Codex:\n" + prompt;
  string record = JsonSerializer.Serialize(new
  {
    type = "event_msg",
    timestamp = "2026-09-07T00:00:00Z",
    payload = new { type = "user_message", message = sourceMessage }
  });

  string directory = Path.Combine(
    Path.GetTempPath(),
    "AgentPanelSpeaker-Issue26-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  string path = Path.Combine(directory, "rollout-issue26.jsonl");
  File.WriteAllText(path, record + Environment.NewLine);

  MainForm? form = null;
  try
  {
    form = new MainForm();
    _ = form.Handle;

    SetField(form, "_loadingSettings", true);
    GetField<ComboBox>(form, "_sourceComboBox").SelectedItem =
      AgentSource.Codex;
    GetField<TextBox>(form, "_sessionPathTextBox").Text = path;
    GetField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
    GetField<CheckBox>(form, "_speakExistingCheckBox").Checked = false;
    SetField(form, "_pathIsManual", true);
    TranscriptSettingsPopup popup =
      GetField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
    popup.SetSettings(
      popup.Settings with { SpeakUserContext = false },
      dark: false);
    SetField(form, "_loadingSettings", false);

    InvokeTask(form, "StartMonitoringAsync").GetAwaiter().GetResult();
    JsonlSessionMonitor monitor =
      GetField<JsonlSessionMonitor>(form, "_monitor");
    SpeechService speech = GetField<SpeechService>(form, "_speech");
    WaitUntil(
      () =>
      {
        IReadOnlyList<ContentCategory> categories =
          ReadSpeechHistoryCategories(speech);
        return monitor.IsRunning &&
          categories.Contains(ContentCategory.UserContext) &&
          categories.Contains(ContentCategory.User);
      },
      "SpeakUserContext OFF must retain User Context in indexed speech history.");

    SpeechFragment[] initialHistory = ReadSpeechHistoryFragments(speech);
    SpeechFragment context = initialHistory.First(fragment =>
      fragment.Category == ContentCategory.UserContext);
    SpeechFragment user = initialHistory.First(fragment =>
      fragment.Category == ContentCategory.User);
    int monitorSession = GetField<int>(form, "_monitorSession");
    int previewGeneration = GetField<int>(form, "_historyPreviewGeneration");

    Require(
      !speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),
      "SpeakUserContext OFF left indexed User Context eligible for playback.");
    Require(
      speech.TrySeekToTranscriptWord(user.NodeId, 0, out _),
      "SpeakUserContext OFF made the actual User prompt ineligible.");
    speech.MoveToPausedLiveEnd();

    popup.SetSettings(
      popup.Settings with { SpeakUserContext = true },
      dark: false);
    InvokeVoid(form, "TranscriptSettingsChanged");
    Application.DoEvents();

    Require(monitor.IsRunning,
      "Enabling SpeakUserContext stopped the live monitor.");
    Require(GetField<int>(form, "_monitorSession") == monitorSession,
      "Enabling SpeakUserContext restarted the live monitor.");
    Require(
      GetField<int>(form, "_historyPreviewGeneration") == previewGeneration,
      "Enabling SpeakUserContext rebuilt canonical history.");
    Require(
      ReadSpeechHistoryFragments(speech)
        .Select(fragment => (fragment.NodeId, fragment.Category, fragment.Text))
        .SequenceEqual(initialHistory.Select(fragment =>
          (fragment.NodeId, fragment.Category, fragment.Text))),
      "Enabling SpeakUserContext changed already-indexed speech history.");
    Require(
      speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),
      "SpeakUserContext ON did not make retained User Context playback-eligible.");
    speech.MoveToPausedLiveEnd();

    popup.SetSettings(
      popup.Settings with { SpeakUserContext = false },
      dark: false);
    InvokeVoid(form, "TranscriptSettingsChanged");
    Application.DoEvents();

    Require(monitor.IsRunning,
      "Disabling SpeakUserContext stopped the live monitor.");
    Require(GetField<int>(form, "_monitorSession") == monitorSession,
      "Disabling SpeakUserContext restarted the live monitor.");
    Require(
      GetField<int>(form, "_historyPreviewGeneration") == previewGeneration,
      "Disabling SpeakUserContext rebuilt canonical history.");
    Require(
      !speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),
      "SpeakUserContext OFF did not immediately suppress retained User Context.");
  }
  finally
  {
    if (form is not null)
    {
      GetField<JsonlSessionMonitor>(form, "_monitor").Stop(
        "issue26-regression-cleanup");
      form.Dispose();
    }
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
  /// Creates project options by the production record constructor while keeping
  /// this RED test compilable before IncludeUserContext exists.
  /// </summary>
  private static AIConversationCoreProjectOptions CreateProjectOptions(
    bool includeUserContext)
  {
    ConstructorInfo constructor = typeof(AIConversationCoreProjectOptions)
      .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .OrderByDescending(item => item.GetParameters().Length)
      .First();
    ParameterInfo[] parameters = constructor.GetParameters();
    var arguments = new object?[parameters.Length];
    for (int index = 0; index < parameters.Length; ++index)
    {
      ParameterInfo parameter = parameters[index];
      arguments[index] = parameter.Name switch
      {
        "IncludeRolledBackTurns" or "includeRolledBackTurns" => false,
        "CodexSessionIndexPath" or "codexSessionIndexPath" => null,
        "IncludeUserContext" or "includeUserContext" => includeUserContext,
        _ when parameter.HasDefaultValue => parameter.DefaultValue,
        _ => throw new InvalidOperationException(
          $"Unexpected project-option constructor parameter: {parameter.Name}")
      };
    }

    return (AIConversationCoreProjectOptions)constructor.Invoke(arguments);
  }

  /// <summary>
  /// Reads app extraction nodes without coupling the test to their record
  /// declaration location.
  /// </summary>
  private static IReadOnlyList<(string Category, string Text)> ReadNodes(
    ExtractionResult extraction)
  {
    PropertyInfo nodesProperty = extraction.GetType().GetProperty("Nodes") ??
      throw new InvalidOperationException("ExtractionResult does not expose Nodes.");
    if (nodesProperty.GetValue(extraction) is not IEnumerable nodes)
    {
      throw new InvalidOperationException("ExtractionResult.Nodes is not enumerable.");
    }

    var result = new List<(string Category, string Text)>();
    foreach (object node in nodes)
    {
      Type type = node.GetType();
      string category = type.GetProperty("Category")?.GetValue(node)?.ToString() ?? string.Empty;
      string text = type.GetProperty("Text")?.GetValue(node)?.ToString() ?? string.Empty;
      result.Add((category, text));
    }
    return result;
  }

  /// <summary>
  /// Reads the actual SpeechService history used by playback.
  /// </summary>
  /// <summary>
/// Reads the actual retained SpeechService fragments used by playback.
/// </summary>
private static SpeechFragment[] ReadSpeechHistoryFragments(
  SpeechService speech)
{
  FieldInfo historyField = typeof(SpeechService).GetField(
    "_history",
    BindingFlags.Instance | BindingFlags.NonPublic) ??
    throw new InvalidOperationException(
      "SpeechService history field was not found.");
  if (historyField.GetValue(speech) is not IEnumerable history)
  {
    throw new InvalidOperationException(
      "SpeechService history is not enumerable.");
  }
  return history.Cast<SpeechFragment>().ToArray();
}

  private static IReadOnlyList<ContentCategory> ReadSpeechHistoryCategories(
    SpeechService speech)
  {
    FieldInfo historyField = typeof(SpeechService).GetField(
      "_history",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService history field was not found.");
    if (historyField.GetValue(speech) is not IEnumerable history)
    {
      throw new InvalidOperationException("SpeechService history is not enumerable.");
    }

    var result = new List<ContentCategory>();
    foreach (object item in history)
    {
      PropertyInfo categoryProperty = item.GetType().GetProperty("Category") ??
        throw new InvalidOperationException("Speech history item has no Category.");
      result.Add((ContentCategory)(categoryProperty.GetValue(item) ??
        throw new InvalidOperationException("Speech history category is null.")));
    }
    return result;
  }

  /// <summary>
  /// Waits for asynchronous monitor/UI work while pumping the STA message loop.
  /// </summary>
  private static void WaitUntil(Func<bool> condition, string failure)
  {
    DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      if (condition())
      {
        return;
      }
      Thread.Sleep(20);
    }
    throw new InvalidOperationException(failure);
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

  private static Task InvokeTask(object target, string name)
  {
    MethodInfo method = target.GetType().GetMethod(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Method {name} was not found.");
    return method.Invoke(target, null) as Task ??
      throw new InvalidOperationException($"Method {name} did not return Task.");
  }

  /// <summary>
  /// Throws when an acceptance condition is false.
  /// </summary>
  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
