using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Production-path contract probe for issue #144 startup history ownership.
/// </summary>
internal static class Issue144HistoryPreviewReuseRegressionProbe
{
  private const int TurnCount = 400;
  private const int TimeoutMilliseconds = 60000;

  /// <summary>
  /// Starts paused-history preparation, proves that work is in flight after its
  /// retained Core session exists, then starts the real MainForm monitor path.
  /// The returned contract requires one complete history build to own both
  /// preview and monitor startup.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    ContractSnapshot? snapshot = null;
    Exception? failure = null;
    using var completed = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
      try
      {
        snapshot = RunContract();
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
      Name = "Issue #144 history preview reuse probe"
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!completed.Wait(TimeSpan.FromMilliseconds(TimeoutMilliseconds + 10000)))
    {
      throw new TimeoutException(
        "Issue #144 STA contract probe did not complete within its outer timeout.");
    }
    thread.Join();
    if (failure is not null)
    {
      throw new InvalidOperationException(
        "Issue #144 contract probe failed: " + failure);
    }
    return snapshot ?? throw new InvalidOperationException(
      "Issue #144 contract probe returned no snapshot.");
  }

  private static ContractSnapshot RunContract()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue144-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue144.jsonl");
    WriteFixture(path);

    try
    {
      DiagnosticLog.Initialize();
      using var lease = new MainFormTestLease();
      MainForm form = lease.Form;
      JsonlSessionMonitor monitor = ReadField<JsonlSessionMonitor>(form, "_monitor");
      SpeechHistorySnapshot? monitorSnapshot = null;
      using var monitorHistoryLoaded = new ManualResetEventSlim();
      monitor.HistoryLoaded += loaded =>
      {
        monitorSnapshot = loaded;
        monitorHistoryLoaded.Set();
      };

      ConfigureSession(form, path);
      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Codex);
      Task previewTask = InvokeTask(
        form,
        "LoadPausedHistoryPreviewAsync",
        session);

      object extractor = ReadField<object>(monitor, "_canonicalExtractor");
      PumpUntil(
        () => ReadNullableField(extractor, "_sessionId") is string,
        "paused preview to establish its retained Core session");
      bool previewWasInFlightAtPlay = !previewTask.IsCompleted;
      Require(
        previewWasInFlightAtPlay,
        "Paused history completed before the deterministic in-flight gate; " +
        "the fixture no longer exercises issue #144.");

      Task startTask = InvokeTask(form, "StartMonitoringAsync");
      PumpUntil(
        () => startTask.IsCompleted,
        "StartMonitoringAsync to return");
      startTask.GetAwaiter().GetResult();

      PumpUntil(
        () => monitorHistoryLoaded.IsSet,
        "monitor history to load after Play");
      PumpUntil(
        () => previewTask.IsCompleted,
        "original paused preview task to finish");
      previewTask.GetAwaiter().GetResult();
      Application.DoEvents();

      SpeechHistorySnapshot loadedSnapshot = monitorSnapshot ??
        throw new InvalidOperationException(
          "The monitor HistoryLoaded event supplied no snapshot.");
      string latestTurnUserText = ResolveLatestTurnUserText(loadedSnapshot);
      bool selectedHistoryPresent =
        ReadNullableField(form, "_selectedSessionHistory") is SpeechHistorySnapshot;

      monitor.Stop("issue-144-regression-probe");
      Application.DoEvents();

      int historyLoadCount = CountLogEvents(
        "monitor.history_loaded",
        path);
      int preindexedReuseCount = CountLogEvents(
        "monitor.preindexed_history_reused",
        path);

      return new ContractSnapshot(
        previewWasInFlightAtPlay,
        historyLoadCount,
        preindexedReuseCount,
        selectedHistoryPresent,
        latestTurnUserText);
    }
    finally
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
  }

  private static void ConfigureSession(MainForm form, string path)
  {
    SetField(form, "_loadingSettings", true);
    try
    {
      ReadField<ComboBox>(form, "_sourceComboBox").SelectedItem = AgentSource.Codex;
      ReadField<TextBox>(form, "_sessionPathTextBox").Text = path;
      ReadField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
      ReadField<CheckBox>(form, "_speakExistingCheckBox").Checked = false;
      SetField(form, "_pathIsManual", true);
    }
    finally
    {
      SetField(form, "_loadingSettings", false);
    }
  }

  private static string ResolveLatestTurnUserText(SpeechHistorySnapshot snapshot)
  {
    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Issue 144 Test Voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      snapshot.Fragments,
      snapshot.Completions,
      snapshot.BackgroundWorkEvents,
      PlaybackStartMode.LatestTurn);

    int nextHistoryIndex = ReadField<int>(speech, "_nextHistoryIndex");
    List<SpeechFragment> history = ReadField<List<SpeechFragment>>(speech, "_history");
    Require(
      nextHistoryIndex >= 0 && nextHistoryIndex < history.Count,
      $"LatestTurn selected invalid history index {nextHistoryIndex} of {history.Count}.");
    SpeechFragment selected = history[nextHistoryIndex];
    Require(
      selected.StartsUserTurn,
      "LatestTurn did not select the final genuine User prompt in the fixed fixture.");
    return selected.Text;
  }

  private static int CountLogEvents(string eventName, string sessionPath)
  {
    int count = 0;
    foreach (string rawLine in File.ReadLines(DiagnosticLog.FilePath))
    {
      string line = rawLine.TrimStart('\uFEFF');
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }
      using JsonDocument document = JsonDocument.Parse(line);
      JsonElement root = document.RootElement;
      if (!root.TryGetProperty("Event", out JsonElement eventElement) ||
          !string.Equals(
            eventElement.GetString(),
            eventName,
            StringComparison.Ordinal))
      {
        continue;
      }
      JsonElement data = root.GetProperty("Data");
      if (data.ValueKind != JsonValueKind.Object ||
          !data.TryGetProperty("Path", out JsonElement pathElement) ||
          !PathsReferToSameFile(pathElement.GetString(), sessionPath))
      {
        continue;
      }
      ++count;
    }
    return count;
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>(TurnCount * 2);
    DateTimeOffset start = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    string body = string.Join(
      " ",
      Enumerable.Repeat("issue144-history-payload", 24));
    for (int index = 0; index < TurnCount; ++index)
    {
      DateTimeOffset userTimestamp = start.AddSeconds(index * 2);
      DateTimeOffset assistantTimestamp = userTimestamp.AddSeconds(1);
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = userTimestamp.ToString("O"),
        payload = new
        {
          type = "user_message",
          message = $"Issue 144 user source {index:D4}. {body}"
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = assistantTimestamp.ToString("O"),
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Issue 144 assistant source {index:D4}. {body}"
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static Task InvokeTask(object target, string methodName, params object?[] arguments)
  {
    MethodInfo method = target.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Method '{methodName}' was not found on {target.GetType().Name}.");
    return method.Invoke(target, arguments) as Task ??
      throw new InvalidOperationException(
        $"Method '{methodName}' did not return Task.");
  }

  private static T ReadField<T>(object target, string fieldName)
  {
    object? value = ReadNullableField(target, fieldName);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field '{fieldName}' had an unexpected value/type.");
  }

  private static object? ReadNullableField(object target, string fieldName)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    return field.GetValue(target);
  }

  private static void SetField<T>(object target, string fieldName, T value)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    field.SetValue(target, value);
  }

  private static void PumpUntil(Func<bool> predicate, string description)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(5);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static bool PathsReferToSameFile(string? left, string right)
  {
    return !string.IsNullOrWhiteSpace(left) &&
      string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        StringComparison.OrdinalIgnoreCase);
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private sealed record ContractSnapshot(
    bool PreviewWasInFlightAtPlay,
    int HistoryLoadCount,
    int PreindexedReuseCount,
    bool SelectedHistoryPresent,
    string LatestTurnUserText);
}
