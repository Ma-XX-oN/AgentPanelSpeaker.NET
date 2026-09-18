using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Production-path regressions for issue #139 Claude live-tail monitoring.
/// </summary>
internal static class Issue139ClaudeLiveTailRegressionTestRunner
{
  private const int TimeoutMilliseconds = 30000;
  private const string AppendedSpeech =
    "Appended Claude speech after retained session growth.";
  private const string FirstPreindexedAppend =
    "First Claude append after preindexed history.";
  private const string SecondPreindexedAppend =
    "Second Claude append after preindexed history.";

  /// <summary>
  /// Runs the issue #139 Claude live-tail regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("claude-live-tail/appended-record-reaches-speech-history",
        TestAppendedClaudeRecordReachesSpeechHistory),
      ("claude-live-tail/preindexed-history-repeated-appends-reach-speech-history",
        TestPreindexedHistoryRepeatedAppendsReachSpeechHistory)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #139 Claude live-tail suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #139 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #139 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Starts the production monitor on a Claude JSONL, appends one Assistant
  /// record, and requires that record to reach TextReady without killing the
  /// monitor.  This reproduces the real retained-session append fault.
  /// </summary>
  private static void TestAppendedClaudeRecordReachesSpeechHistory()
  {
    string root = CreateFixture(out string path);
    using var historyReady = new ManualResetEventSlim(false);
    using var appendedReady = new ManualResetEventSlim(false);
    using var faulted = new ManualResetEventSlim(false);
    using var monitor = new JsonlSessionMonitor();

    SpeechHistorySnapshot? initialHistory = null;
    SpeechFragment? appendedFragment = null;
    Exception? monitorFault = null;

    monitor.HistoryLoaded += history =>
    {
      initialHistory = history;
      historyReady.Set();
    };
    monitor.TextReady += fragment =>
    {
      if (fragment.Text == AppendedSpeech)
      {
        appendedFragment = fragment;
        appendedReady.Set();
      }
    };
    monitor.Faulted += exception =>
    {
      monitorFault = exception;
      faulted.Set();
    };

    try
    {
      monitor.Start(new MonitorSettings(
        AgentSource.Claude,
        path,
        FollowLatest: false,
        SpeakExistingLatestTurn: false,
        PollInterval: TimeSpan.FromMilliseconds(20)));

      WaitForSuccessOrFault(
        historyReady,
        faulted,
        () => monitorFault,
        "initial Claude history");
      Require(
        initialHistory is not null &&
        initialHistory.Fragments.Any(fragment =>
          fragment.Text.Contains("Initial Claude answer", StringComparison.Ordinal)),
        "Initial Claude history did not establish the test precondition.");

      AppendRecord(
        path,
        "issue139-assistant-2",
        "2026-09-18T00:00:02.000Z",
        AppendedSpeech);

      WaitForSuccessOrFault(
        appendedReady,
        faulted,
        () => monitorFault,
        "appended Claude speech fragment");

      Require(
        appendedFragment is not null &&
        appendedFragment.Text == AppendedSpeech,
        "The appended Claude Assistant record did not reach TextReady.");
      Require(
        monitorFault is null,
        $"Claude monitor faulted: {monitorFault}");
      Require(
        monitor.IsRunning,
        "Claude monitor stopped after the appended record.");
    }
    finally
    {
      StopAndDelete(monitor, root);
    }
  }

  /// <summary>
  /// Reproduces the MainForm path: index paused history first, reuse that exact
  /// snapshot when monitoring starts, then require multiple later file appends
  /// to enter live speech without republishing the indexed prefix.
  /// </summary>
  private static void TestPreindexedHistoryRepeatedAppendsReachSpeechHistory()
  {
    string root = CreateFixture(out string path);
    using var firstReady = new ManualResetEventSlim(false);
    using var secondReady = new ManualResetEventSlim(false);
    using var faulted = new ManualResetEventSlim(false);
    using var monitor = new JsonlSessionMonitor();

    Exception? monitorFault = null;
    SpeechFragment? firstFragment = null;
    SpeechFragment? secondFragment = null;
    int historyLoadedEvents = 0;
    int republishedInitialFragments = 0;

    try
    {
      LocatedSession session = SessionLocator.FromPath(
        path,
        AgentSource.Claude);
      SpeechHistorySnapshot snapshot = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: false,
        includeRolledBackTurns: true,
        includeUserContext: true);
      Require(
        snapshot.Fragments.Any(fragment =>
          fragment.Text.Contains("Initial Claude answer", StringComparison.Ordinal)),
        "Preindexed Claude history did not establish the production precondition.");

      object extractor = ReadRequiredField<object>(
        monitor,
        "_canonicalExtractor");
      object projectionBeforeStart = ReadField<object?>(
        extractor,
        "_projection") ??
        throw new InvalidOperationException(
          "Preindexed Claude history did not retain a canonical projection.");

      monitor.HistoryLoaded += _ => Interlocked.Increment(
        ref historyLoadedEvents);
      monitor.TextReady += fragment =>
      {
        if (fragment.Text.Contains(
              "Initial Claude",
              StringComparison.Ordinal))
        {
          Interlocked.Increment(ref republishedInitialFragments);
        }
        if (fragment.Text == FirstPreindexedAppend)
        {
          firstFragment = fragment;
          firstReady.Set();
        }
        if (fragment.Text == SecondPreindexedAppend)
        {
          secondFragment = fragment;
          secondReady.Set();
        }
      };
      monitor.Faulted += exception =>
      {
        monitorFault = exception;
        faulted.Set();
      };

      monitor.Start(new MonitorSettings(
        AgentSource.Claude,
        path,
        FollowLatest: false,
        SpeakExistingLatestTurn: false,
        PollInterval: TimeSpan.FromMilliseconds(20),
        PreindexedHistory: snapshot,
        IncludeRolledBackTurns: true,
        IncludeUserContext: true));

      WaitForProjectionReplacement(
        extractor,
        projectionBeforeStart,
        faulted,
        () => monitorFault);

      AppendRecord(
        path,
        "issue139-assistant-preindexed-1",
        "2026-09-18T00:00:03.000Z",
        FirstPreindexedAppend);
      WaitForSuccessOrFault(
        firstReady,
        faulted,
        () => monitorFault,
        "first preindexed-history Claude append");

      AppendRecord(
        path,
        "issue139-assistant-preindexed-2",
        "2026-09-18T00:00:04.000Z",
        SecondPreindexedAppend);
      WaitForSuccessOrFault(
        secondReady,
        faulted,
        () => monitorFault,
        "second preindexed-history Claude append");

      Require(
        firstFragment?.Text == FirstPreindexedAppend,
        "The first repeated Claude append did not reach TextReady.");
      Require(
        secondFragment?.Text == SecondPreindexedAppend,
        "The second repeated Claude append did not reach TextReady.");
      Require(
        Volatile.Read(ref historyLoadedEvents) == 0,
        "Reusing preindexed history unexpectedly republished HistoryLoaded.");
      Require(
        Volatile.Read(ref republishedInitialFragments) == 0,
        "Reusing preindexed history republished an already indexed speech fragment.");
      Require(
        monitorFault is null,
        $"Claude monitor faulted during repeated appends: {monitorFault}");
      Require(
        monitor.IsRunning,
        "Claude monitor stopped during repeated preindexed-history appends.");
    }
    finally
    {
      StopAndDelete(monitor, root);
    }
  }

  private static string CreateFixture(out string path)
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue139-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    path = Path.Combine(root, "claude-live-tail.jsonl");
    File.WriteAllText(
      path,
      BuildInitialJsonl(),
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return root;
  }

  private static string BuildInitialJsonl()
  {
    object[] records =
    {
      new
      {
        uuid = "issue139-user-1",
        type = "user",
        isSidechain = false,
        timestamp = "2026-09-18T00:00:00.000Z",
        message = new
        {
          role = "user",
          content = new[]
          {
            new { type = "text", text = "Initial Claude prompt." }
          }
        }
      },
      new
      {
        uuid = "issue139-assistant-1",
        type = "assistant",
        isSidechain = false,
        timestamp = "2026-09-18T00:00:01.000Z",
        message = new
        {
          model = "claude-test",
          role = "assistant",
          content = new[]
          {
            new { type = "text", text = "Initial Claude answer." }
          }
        }
      }
    };

    return string.Join(
      Environment.NewLine,
      records.Select(record => JsonSerializer.Serialize(record))) +
      Environment.NewLine;
  }

  private static void AppendRecord(
    string path,
    string uuid,
    string timestamp,
    string speech)
  {
    string record = JsonSerializer.Serialize(new
    {
      uuid,
      type = "assistant",
      isSidechain = false,
      timestamp,
      message = new
      {
        model = "claude-test",
        role = "assistant",
        content = new[]
        {
          new { type = "text", text = speech }
        }
      }
    });
    File.AppendAllText(
      path,
      record + Environment.NewLine,
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }

  private static void WaitForProjectionReplacement(
    object extractor,
    object projectionBeforeStart,
    ManualResetEventSlim fault,
    Func<Exception?> readFault)
  {
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.ElapsedMilliseconds < TimeoutMilliseconds)
    {
      if (fault.IsSet)
      {
        throw new InvalidOperationException(
          "Monitor faulted while priming preindexed Claude history: " +
          readFault());
      }

      object? current = ReadField<object?>(extractor, "_projection");
      if (current is not null &&
          !ReferenceEquals(current, projectionBeforeStart))
      {
        return;
      }

      Thread.Sleep(10);
    }

    throw new TimeoutException(
      "Timed out waiting for the preindexed Claude projection to be " +
      $"reprojected after {TimeoutMilliseconds} ms.");
  }

  private static T ReadRequiredField<T>(
    object instance,
    string fieldName)
    where T : class
  {
    T? value = ReadField<T?>(instance, fieldName);
    return value ?? throw new InvalidOperationException(
      $"Required field {fieldName} was null.");
  }

  private static T ReadField<T>(
    object instance,
    string fieldName)
  {
    FieldInfo field = instance.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field {fieldName} was not found on {instance.GetType().Name}.");
    return (T)field.GetValue(instance)!;
  }

  private static void WaitForSuccessOrFault(
    ManualResetEventSlim success,
    ManualResetEventSlim fault,
    Func<Exception?> readFault,
    string description)
  {
    int signaled = WaitHandle.WaitAny(
      new[] { success.WaitHandle, fault.WaitHandle },
      TimeoutMilliseconds);
    if (signaled == 0)
    {
      return;
    }

    Exception? exception = readFault();
    if (signaled == 1)
    {
      throw new InvalidOperationException(
        $"Monitor faulted while waiting for {description}: {exception}");
    }
    throw new TimeoutException(
      $"Timed out waiting for {description} after {TimeoutMilliseconds} ms.");
  }

  private static void StopAndDelete(
    JsonlSessionMonitor monitor,
    string root)
  {
    monitor.Stop("issue-139-regression");
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
