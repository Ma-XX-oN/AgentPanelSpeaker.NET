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
  private const string DuplicateUuid =
    "367fa423-f933-46a3-8061-d1a9c94eac15";
  private const string DuplicateReasoning =
    "Repeated reasoning text for source occurrence identity.";

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
        TestPreindexedHistoryRepeatedAppendsReachSpeechHistory),
      ("claude-live-tail/duplicate-uuid-keeps-source-word-occurrences-separate",
        TestDuplicateUuidKeepsSourceWordOccurrencesSeparate)
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
  /// Reproduces the MainForm path: index paused history first, append one record
  /// after that exact prepared extent, then reuse the snapshot when monitoring
  /// starts. Both the between-preview-and-Start append and a later live append
  /// must enter speech without republishing the indexed prefix.
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
      long maximumInitialNodeId = snapshot.Fragments.Max(fragment => fragment.NodeId);
      long maximumInitialFragmentId = snapshot.Fragments.Max(fragment => fragment.FragmentId);

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

      AppendRecord(
        path,
        "issue139-assistant-preindexed-1",
        "2026-09-18T00:00:03.000Z",
        FirstPreindexedAppend);

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
        "The append after the prepared history extent did not reach TextReady.");
      Require(
        firstFragment.NodeId > maximumInitialNodeId &&
        firstFragment.FragmentId > maximumInitialFragmentId,
        "The first append did not continue prepared node/fragment identity.");
      Require(
        secondFragment?.Text == SecondPreindexedAppend,
        "The second repeated Claude append did not reach TextReady.");
      Require(
        secondFragment.NodeId > firstFragment.NodeId &&
        secondFragment.FragmentId > firstFragment.FragmentId,
        "The second append did not continue live node/fragment identity.");
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

  /// <summary>
  /// Reproduces Claude branch replay where two exact source occurrences reuse
  /// one provider UUID and therefore one canonical block ID. The app must not
  /// combine the two Core provenance sequences when extracting either record.
  /// </summary>
  private static void TestDuplicateUuidKeepsSourceWordOccurrencesSeparate()
  {
    string[] records =
    {
      BuildDuplicateReasoningRecord(
        parentUuid: "branch-parent-old",
        branch: "issue-old"),
      BuildDuplicateReasoningRecord(
        parentUuid: "branch-parent-new",
        branch: "issue-new")
    };

    using var extractor = new CanonicalSessionExtractor();
    IReadOnlyList<ExtractionResult> results = extractor.Load(
      AgentSource.Claude,
      records);

    Require(results.Count == 2,
      $"Expected two source slices, received {results.Count}.");

    ExtractedNode first = RequireReasoningNode(results[0], 0);
    ExtractedNode second = RequireReasoningNode(results[1], 1);
    Require(
      string.Equals(
        first.CanonicalBlockId,
        second.CanonicalBlockId,
        StringComparison.Ordinal),
      "Fixture did not reproduce the shared canonical block identity.");

    AssertExactSourceOccurrence(first, sourceIndex: 0);
    AssertExactSourceOccurrence(second, sourceIndex: 1);

    long[] firstIds = first.CanonicalWords!.Select(word => word.Id).ToArray();
    long[] secondIds = second.CanonicalWords!.Select(word => word.Id).ToArray();
    Require(
      !firstIds.Intersect(secondIds).Any(),
      "Distinct source occurrences unexpectedly shared canonical word IDs.");
  }

  private static ExtractedNode RequireReasoningNode(
    ExtractionResult result,
    int sourceIndex)
  {
    ExtractedNode[] nodes = result.Nodes
      .Where(node => node.Category == ContentCategory.Reasoning)
      .ToArray();
    Require(
      nodes.Length == 1,
      $"Source {sourceIndex} produced {nodes.Length} reasoning nodes instead of one.");
    Require(
      nodes[0].CanonicalWords is { Count: > 0 },
      $"Source {sourceIndex} reasoning node has no canonical words.");
    return nodes[0];
  }

  private static void AssertExactSourceOccurrence(
    ExtractedNode node,
    int sourceIndex)
  {
    IReadOnlyList<CanonicalSpeechWordProjection> words = node.CanonicalWords!;
    for (int index = 0; index < words.Count; ++index)
    {
      CanonicalSpeechWordProjection word = words[index];
      Require(
        word.Provenance?.BlockWordIndex == index,
        $"Source {sourceIndex} block word {index} was not contiguous.");
      Require(
        ReadSourceRecordIndex(word) == sourceIndex,
        $"Source {sourceIndex} attached a word owned by another source occurrence.");
    }
  }

  private static int ReadSourceRecordIndex(
    CanonicalSpeechWordProjection word)
  {
    JsonElement? provenanceSource = word.Provenance?.Source;
    if (provenanceSource is not JsonElement source ||
        source.ValueKind != JsonValueKind.Object ||
        !source.TryGetProperty("record_index", out JsonElement recordIndex) ||
        recordIndex.ValueKind != JsonValueKind.Number ||
        !recordIndex.TryGetInt32(out int value))
    {
      throw new InvalidOperationException(
        $"Core word {word.Id} omitted provenance source record_index.");
    }
    return value;
  }

  private static string BuildDuplicateReasoningRecord(
    string parentUuid,
    string branch)
  {
    return JsonSerializer.Serialize(new
    {
      uuid = DuplicateUuid,
      parentUuid,
      type = "assistant",
      isSidechain = false,
      timestamp = "2026-09-17T02:55:27.339Z",
      gitBranch = branch,
      message = new
      {
        id = "msg_011Cf8FqfuhDSW4b9C9n8q5s",
        model = "claude-sonnet-4-6",
        role = "assistant",
        content = new object[]
        {
          new
          {
            type = "thinking",
            thinking = DuplicateReasoning
          }
        }
      }
    });
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
