using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Production-path contract probe for a complete Claude turn appended after
/// paused history has been transferred to live monitoring.
/// </summary>
internal static class Issue139CompleteTurnRegressionProbe
{
  private const int TimeoutMilliseconds = 30000;
  private const string InitialUserPrompt = "Initial Claude prompt.";
  private const string InitialAssistantResponse = "Initial Claude answer.";
  private const string NextUserPrompt =
    "Next Claude user prompt after monitoring started.";
  private const string NextAssistantResponse =
    "Next Claude assistant response after monitoring started.";

  /// <summary>
  /// Runs the complete-turn live-tail contract and returns observable results.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue139-turn-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "claude-complete-turn.jsonl");
    File.WriteAllText(
      path,
      BuildInitialJsonl(),
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    using var userReady = new ManualResetEventSlim(false);
    using var assistantReady = new ManualResetEventSlim(false);
    using var sessionReady = new ManualResetEventSlim(false);
    using var faulted = new ManualResetEventSlim(false);
    using var monitor = new JsonlSessionMonitor();
    var emitted = new ConcurrentQueue<SpeechFragment>();
    Exception? monitorFault = null;

    try
    {
      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Claude);
      SpeechHistorySnapshot snapshot = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: false,
        includeRolledBackTurns: true,
        includeUserContext: true);
      Require(
        snapshot.Fragments.Any(fragment => fragment.Text == InitialUserPrompt) &&
        snapshot.Fragments.Any(fragment =>
          fragment.Text == InitialAssistantResponse),
        "Paused-history preview did not establish the complete initial turn.");

      monitor.SessionChanged += selected =>
      {
        if (PathsReferToSameFile(selected.Path, path))
        {
          sessionReady.Set();
        }
      };
      monitor.TextReady += fragment =>
      {
        emitted.Enqueue(fragment);
        if (fragment.Text == NextUserPrompt)
        {
          userReady.Set();
        }
        if (fragment.Text == NextAssistantResponse)
        {
          assistantReady.Set();
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

      WaitForSuccessOrFault(
        sessionReady,
        faulted,
        () => monitorFault,
        "production monitor to select the prepared Claude session");

      AppendUserRecord(path);
      WaitForSuccessOrFault(
        userReady,
        faulted,
        () => monitorFault,
        "new Claude User prompt to reach live speech history");

      AppendAssistantRecord(path);
      WaitForSuccessOrFault(
        assistantReady,
        faulted,
        () => monitorFault,
        "new Claude Assistant response to reach live speech history");

      SpeechFragment[] observed = emitted.ToArray();
      SpeechFragment[] user = observed
        .Where(fragment => fragment.Text == NextUserPrompt)
        .ToArray();
      SpeechFragment[] assistant = observed
        .Where(fragment => fragment.Text == NextAssistantResponse)
        .ToArray();
      int userIndex = Array.FindIndex(
        observed,
        fragment => fragment.Text == NextUserPrompt);
      int assistantIndex = Array.FindIndex(
        observed,
        fragment => fragment.Text == NextAssistantResponse);
      bool prefixRepublished = observed.Any(fragment =>
        fragment.Text == InitialUserPrompt ||
        fragment.Text == InitialAssistantResponse);

      Require(user.Length == 1,
        $"Expected one live User prompt, observed {user.Length}.");
      Require(assistant.Length == 1,
        $"Expected one live Assistant response, observed {assistant.Length}.");
      Require(user[0].Category == ContentCategory.User &&
              user[0].StartsUserTurn,
        "The appended User record did not retain User-turn semantics.");
      Require(assistant[0].Category == ContentCategory.Assistant,
        "The appended Assistant record did not retain Assistant semantics.");
      Require(userIndex >= 0 && assistantIndex > userIndex,
        "The appended User and Assistant records were not emitted in source order.");
      Require(!prefixRepublished,
        "Starting live monitoring republished the already indexed prefix.");
      Require(monitorFault is null,
        $"Claude monitor faulted during the complete appended turn: {monitorFault}");
      Require(monitor.IsRunning,
        "Claude monitor stopped during the complete appended turn.");

      return new ContractSnapshot(
        UserCount: user.Length,
        AssistantCount: assistant.Length,
        UserBeforeAssistant: assistantIndex > userIndex,
        UserStartsTurn: user[0].StartsUserTurn,
        PrefixRepublished: prefixRepublished,
        MonitorRunning: monitor.IsRunning,
        Faulted: monitorFault is not null);
    }
    finally
    {
      monitor.Stop("issue-139-complete-turn-regression");
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
    return string.Join(
      Environment.NewLine,
      BuildUserRecord(
        "issue139-complete-user-1",
        "2026-09-18T00:00:00.000Z",
        InitialUserPrompt),
      BuildAssistantRecord(
        "issue139-complete-assistant-1",
        "2026-09-18T00:00:01.000Z",
        InitialAssistantResponse)) + Environment.NewLine;
  }

  private static void AppendUserRecord(string path)
  {
    AppendLine(
      path,
      BuildUserRecord(
        "issue139-complete-user-2",
        "2026-09-18T00:00:02.000Z",
        NextUserPrompt));
  }

  private static void AppendAssistantRecord(string path)
  {
    AppendLine(
      path,
      BuildAssistantRecord(
        "issue139-complete-assistant-2",
        "2026-09-18T00:00:03.000Z",
        NextAssistantResponse));
  }

  private static string BuildUserRecord(
    string uuid,
    string timestamp,
    string text)
  {
    return JsonSerializer.Serialize(new
    {
      uuid,
      type = "user",
      isSidechain = false,
      timestamp,
      message = new
      {
        role = "user",
        content = new[]
        {
          new { type = "text", text }
        }
      }
    });
  }

  private static string BuildAssistantRecord(
    string uuid,
    string timestamp,
    string text)
  {
    return JsonSerializer.Serialize(new
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
          new { type = "text", text }
        }
      }
    });
  }

  private static void AppendLine(string path, string line)
  {
    File.AppendAllText(
      path,
      line + Environment.NewLine,
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
    if (signaled == 1)
    {
      throw new InvalidOperationException(
        $"Monitor faulted while waiting for {description}: {readFault()}");
    }
    throw new TimeoutException(
      $"Timed out waiting for {description} after {TimeoutMilliseconds} ms.");
  }

  private static bool PathsReferToSameFile(string left, string right)
  {
    return string.Equals(
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
    int UserCount,
    int AssistantCount,
    bool UserBeforeAssistant,
    bool UserStartsTurn,
    bool PrefixRepublished,
    bool MonitorRunning,
    bool Faulted);
}
