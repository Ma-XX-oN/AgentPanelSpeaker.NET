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
    try
    {
      TestClaudeLeadingInjectedContext();
      Console.WriteLine("PASS  core/claude-leading-injected-context");
      Console.WriteLine("PASS: 1/1 core regression tests passed.");
      return 0;
    }
    catch (Exception exception)
    {
      Console.WriteLine("FAIL  core/claude-leading-injected-context");
      Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      Console.WriteLine("FAIL: 1/1 core regression tests failed.");
      return 1;
    }
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
  /// Counts non-overlapping ordinal occurrences of one string in another.
  /// </summary>
  /// <param name="text">Text to search.</param>
  /// <param name="value">Value to count.</param>
  /// <returns>The number of non-overlapping occurrences.</returns>
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
  /// <param name="condition">Condition that must be true.</param>
  /// <param name="message">Failure message.</param>
  private static void Require(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
  }
}
