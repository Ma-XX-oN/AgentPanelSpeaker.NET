using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Guards the expensive whole-session preparation boundary for issue #37.
/// </summary>
internal static class Issue37PreparationRegressionTestRunner
{
  /// <summary>
  /// Runs deterministic preparation-work regressions.
  /// </summary>
  /// <returns>Zero when every preparation regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("preparation/single-core-projection-per-display-generation",
        TestSingleCoreProjectionPerDisplayGeneration)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #37 preparation regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #37 preparation tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #37 preparation tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Loads a fixed Codex transcript through the real TranscriptView. One
  /// display generation must submit exactly one whole-session Core projection;
  /// identity reconstruction and presentation DOM construction must share it.
  /// The expected count is fixed by the acceptance requirement, not derived
  /// from either production formatter.
  /// </summary>
  private static void TestSingleCoreProjectionPerDisplayGeneration()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = Path.Combine(root, "rollout-issue37.jsonl");
      File.WriteAllLines(path, BuildFixture());

      using var host = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000)
      };
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      PumpUntil(
        () => ReadField<bool>(view, "_initialized"),
        "TranscriptView WebView2 initialization");

      long before = AIConversationCoreClient.ProjectRequestCount;
      view.SelectSession(path, AgentSource.Codex, "Issue #37 fixture");
      PumpUntil(
        () =>
        {
          if (ReadField<bool>(view, "_refreshInProgress"))
          {
            return false;
          }
          IReadOnlyList<TranscriptNodeIdentity> identities =
            ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
          return identities.Count != 0;
        },
        "issue #37 production transcript preparation");

      long projectionCount =
        AIConversationCoreClient.ProjectRequestCount - before;
      Require(
        projectionCount == 1,
        "One TranscriptView display generation submitted " +
        $"{projectionCount} whole-session Core projections; expected exactly 1.");
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

  private static string[] BuildFixture()
  {
    return
    [
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-09T01:00:00.000Z",
        payload = new
        {
          type = "user_message",
          message = "Explain the preparation path."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-09T01:00:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "The display should project this session only once."
        }
      })
    ];
  }

  private static T ReadField<T>(object instance, string name)
  {
    FieldInfo field = instance.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"{instance.GetType().Name}.{name} was not found.");
    return field.GetValue(instance) is T value
      ? value
      : throw new InvalidOperationException(
        $"{instance.GetType().Name}.{name} had an unexpected type.");
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
