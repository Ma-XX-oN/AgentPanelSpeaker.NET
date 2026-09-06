using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs regressions that exercise the AgentPanelSpeaker-to-AIConversationCore
/// boundary directly.
/// </summary>
internal static class CoreRegressionTestRunner
{
  /// <summary>
  /// Runs the core-boundary regression suite.
  /// </summary>
  /// <returns>Zero when all tests pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Test)[]
    {
      ("core/claude-leading-injected-context", TestClaudeLeadingInjectedContext),
      ("core/codex-revision-default-hidden", TestCodexRevisionDefaultHidden),
      ("core/codex-revision-opt-in", TestCodexRevisionOptIn),
      ("core/codex-session-index-title", TestCodexSessionIndexTitle)
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

    if (failed == 0)
    {
      Console.WriteLine($"PASS: {tests.Length}/{tests.Length} core regression tests passed.");
      return 0;
    }

    Console.WriteLine($"FAIL: {failed}/{tests.Length} core regression tests failed.");
    return 1;
  }

  /// <summary>
  /// Verifies that Claude Code's injected leading IDE-selection block cannot
  /// become a second visible User event at the start of the transcript.
  /// </summary>
  private static void TestClaudeLeadingInjectedContext()
  {
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "queue-operation",
        operation = "enqueue",
        timestamp = "2026-08-27T02:46:01.346Z",
        sessionId = "307a0d60-0528-43e9-84e6-790185c0e391"
      }),
      JsonSerializer.Serialize(new
      {
        type = "queue-operation",
        operation = "dequeue",
        timestamp = "2026-08-27T02:46:01.369Z",
        sessionId = "307a0d60-0528-43e9-84e6-790185c0e391"
      }),
      JsonSerializer.Serialize(new
      {
        parentUuid = (string?)null,
        isSidechain = false,
        uuid = "42c35592-4d34-474c-95bc-c0451d6c2312",
        type = "user",
        timestamp = "2026-08-27T02:46:01.400Z",
        message = new
        {
          role = "user",
          content = new object[]
          {
            new
            {
              type = "text",
              text = "<ide_selection>The user selected the lines 3513 to 3513 from " +
                     "c:\\Users\\adria\\Downloads\\codex-transcript.md:\n" +
                     "repo is clean after the push. The post\n\n" +
                     "This may or may not be related to the current task.</ide_selection>"
            },
            new
            {
              type = "text",
              text = "Can you provide me with simulations of these:"
            }
          }
        }
      })
    };

    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(AgentSource.Claude, records);

    Require(projection.Events.Length == 1,
      $"Expected one visible canonical event, got {projection.Events.Length}.");
    Require(projection.Units.Length == 1,
      $"Expected one visible canonical unit, got {projection.Units.Length}.");
    Require(projection.Turns.Length == 1,
      $"Expected one visible turn, got {projection.Turns.Length}.");
    Require(projection.Turns[0].Role == "user",
      $"Expected the only visible turn to be User, got {projection.Turns[0].Role}.");

    CanonicalUnitProjection unit = projection.Units[0];
    Require(unit.SourceIndex == 2,
      $"Expected visible content to come from record index 2, got {unit.SourceIndex}.");
    Require(unit.Block.ValueKind == JsonValueKind.Object &&
            unit.Block.TryGetProperty("text", out JsonElement text) &&
            text.GetString() == "Can you provide me with simulations of these:",
      "Visible User content did not match the real prompt.");
    Require(!projection.Markdown.Contains("ide_selection", StringComparison.Ordinal),
      "Injected ide_selection markup leaked into canonical Markdown.");
    Require(!projection.Markdown.Contains("repo is clean after the push", StringComparison.Ordinal),
      "Injected IDE-selection text leaked into canonical Markdown.");
    Require(CountOccurrences(projection.Markdown, "## User") == 1,
      "Canonical Markdown contains an extra User heading.");
  }

  /// <summary>
  /// Verifies that rolled-back Codex revisions remain hidden by default while
  /// the active replacement and recorded IDE context stay visible.
  /// </summary>
  private static void TestCodexRevisionDefaultHidden()
  {
    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(
      AgentSource.Codex,
      CodexRevisionRecords(),
      new AIConversationCoreProjectOptions());

    Require(!projection.Markdown.Contains("Original prompt", StringComparison.Ordinal),
      "Rolled-back original leaked into default Codex projection.");
    Require(projection.Markdown.Contains("## User (edited)", StringComparison.Ordinal),
      "Current Codex replacement is not labelled edited.");
    Require(projection.Markdown.Contains("# Context from my IDE setup:", StringComparison.Ordinal),
      "Recorded Codex IDE context was stripped.");
    Require(projection.Markdown.Contains("Edited prompt", StringComparison.Ordinal),
      "Current Codex replacement is missing.");
  }

  /// <summary>
  /// Verifies that the explicit history option exposes original/aborted and
  /// current edited Codex revisions from the same canonical projection.
  /// </summary>
  private static void TestCodexRevisionOptIn()
  {
    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(
      AgentSource.Codex,
      CodexRevisionRecords(),
      new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));

    Require(projection.Markdown.Contains(
        "## User (original, aborted)",
        StringComparison.Ordinal),
      "Opt-in Codex history omitted the original/aborted label.");
    Require(projection.Markdown.Contains("Original prompt", StringComparison.Ordinal),
      "Opt-in Codex history omitted the original prompt.");
    Require(projection.Markdown.Contains("## User (edited)", StringComparison.Ordinal),
      "Opt-in Codex history omitted the edited label.");
  }

  /// <summary>
  /// Verifies that AgentPanelSpeaker discovers a supplementary Codex index path
  /// while AIConversationCore reads/parses it and resolves the latest title.
  /// </summary>
  private static void TestCodexSessionIndexTitle()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-core-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string indexPath = Path.Combine(root, "session_index.jsonl");
    try
    {
      File.WriteAllLines(indexPath, new[]
      {
        JsonSerializer.Serialize(new
        {
          id = "01a07804-bcf3-7af3-8321-bdcf0c1ddc89",
          thread_name = "Find London time"
        }),
        JsonSerializer.Serialize(new
        {
          id = "01a07804-bcf3-7af3-8321-bdcf0c1ddc89",
          thread_name = "Find London time (MODIFIED)"
        })
      });

      using var client = new AIConversationCoreClient();
      AIConversationProjection projection = client.Project(
        AgentSource.Codex,
        CodexRevisionRecords(),
        new AIConversationCoreProjectOptions(CodexSessionIndexPath: indexPath));

      Require(projection.SessionMetadata?.Title == "Find London time (MODIFIED)",
        $"Expected latest indexed title, got {projection.SessionMetadata?.Title ?? "<null>"}.");
      Require(projection.SessionMetadata?.TitleSource == "codex-session-index",
        $"Unexpected title source {projection.SessionMetadata?.TitleSource ?? "<null>"}.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Returns a minimal rollback/edit sequence containing recorded IDE context.
  /// </summary>
  private static string[] CodexRevisionRecords()
  {
    const string sessionId = "01a07804-bcf3-7af3-8321-bdcf0c1ddc89";
    return new[]
    {
      JsonSerializer.Serialize(new
      {
        type = "session_meta",
        timestamp = "2026-09-06T20:00:00.000Z",
        payload = new { id = sessionId }
      }),
      JsonSerializer.Serialize(new
      {
        type = "turn_context",
        timestamp = "2026-09-06T20:00:01.000Z",
        payload = new { model = "gpt-5.4" }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:02.000Z",
        payload = new
        {
          type = "user_message",
          message = "# Context from my IDE setup:\n\n## My request for Codex:\nOriginal prompt"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:03.000Z",
        payload = new { type = "turn_aborted" }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:04.000Z",
        payload = new { type = "thread_rolled_back", num_turns = 1 }
      }),
      JsonSerializer.Serialize(new
      {
        type = "turn_context",
        timestamp = "2026-09-06T20:00:05.000Z",
        payload = new { model = "gpt-5.5" }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:06.000Z",
        payload = new
        {
          type = "user_message",
          message = "# Context from my IDE setup:\n\n## My request for Codex:\nEdited prompt"
        }
      })
    };
  }

  /// <summary>
  /// Counts non-overlapping ordinal occurrences of one string in another.
  /// </summary>
  private static int CountOccurrences(string text, string value)
  {
    int count = 0;
    int index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
      ++count;
      index += value.Length;
    }
    return count;
  }

  /// <summary>
  /// Throws when a regression assertion is false.
  /// </summary>
  private static void Require(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
  }
}
