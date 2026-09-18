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

  /// <summary>
  /// Runs the issue #139 Claude live-tail regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("claude-live-tail/appended-record-reaches-speech-history",
        TestAppendedClaudeRecordReachesSpeechHistory)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #139 Claude live-tail suite: {tests.Length} test");
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
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue139-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "claude-live-tail.jsonl");
    File.WriteAllText(
      path,
      BuildInitialJsonl(),
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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
      if (fragment.Text.Contains(
            "Appended Claude speech",
            StringComparison.Ordinal))
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

      File.AppendAllText(
        path,
        BuildAppendedRecord() + Environment.NewLine,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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

  private static string BuildAppendedRecord()
  {
    return JsonSerializer.Serialize(new
    {
      uuid = "issue139-assistant-2",
      type = "assistant",
      isSidechain = false,
      timestamp = "2026-09-18T00:00:02.000Z",
      message = new
      {
        model = "claude-test",
        role = "assistant",
        content = new[]
        {
          new { type = "text", text = AppendedSpeech }
        }
      }
    });
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

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
