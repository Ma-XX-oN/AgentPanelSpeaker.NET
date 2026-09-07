using Markdig;
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
      ("core/codex-user-context-markdown-html-parity", TestCodexUserContextMarkdownHtmlParity),
      ("core/codex-commentary-inside-reasoning-group",
        TestCodexCommentaryInsideReasoningGroup),
      ("core/codex-session-index-title", TestCodexSessionIndexTitle),
      ("core/codex-session-title-worker-reuse", TestCodexSessionTitleWorkerReuse)
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
  /// Verifies one Codex IDE-context record in canonical Markdown and direct HTML.
  /// </summary>
  private static void TestCodexUserContextMarkdownHtmlParity()
  {
    const string prompt = "I'm doing some testing. What time is it in Paris?";
    const string sourceMessage =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Active selection of the file:\n" +
      " the repo instructions and the current transcript script first\n" +
      "## Open tabs:\n" +
      "- example.jsonl: sessions/example.jsonl\n\n" +
      "## My request for Codex:\n" + prompt;
    string record = JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-06T15:17:11.000Z",
      payload = new { type = "user_message", message = sourceMessage }
    });

    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(AgentSource.Codex, new[] { record });
    string markdown = projection.Markdown;
    int mdStart = markdown.IndexOf("> <details><summary># Context from my IDE setup:</summary>", StringComparison.Ordinal);
    int mdEnd = markdown.IndexOf("> </details>", Math.Max(0, mdStart), StringComparison.Ordinal);
    int mdPrompt = markdown.IndexOf(prompt, StringComparison.Ordinal);
    Require(mdStart >= 0, "Markdown omitted blockquoted context details.");
    Require(mdEnd > mdStart && mdPrompt > mdEnd, "Markdown prompt is not after/outside context details.");
    Require(!markdown.Contains("## My request for Codex:", StringComparison.Ordinal), "Request marker leaked into Markdown.");
    int mdFile = markdown.IndexOf("## Active file:", StringComparison.Ordinal);
    int mdSelection = markdown.IndexOf("## Active selection of the file:", StringComparison.Ordinal);
    int mdTabs = markdown.IndexOf("## Open tabs:", StringComparison.Ordinal);
    Require(mdFile >= 0 && mdFile < mdSelection && mdSelection < mdTabs, "Markdown changed context order.");

    string root = Path.Combine(Path.GetTempPath(), $"AgentPanelSpeaker-user-context-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = Path.Combine(root, "context.jsonl");
      File.WriteAllText(path, record + Environment.NewLine);
      var pipeline = new MarkdownPipelineBuilder().Build();
      string html = TranscriptPresentationHtmlFormatter.Format(path, AgentSource.Codex, pipeline);
      int htmlStart = html.IndexOf("<details class=\"user-context-details\"", StringComparison.Ordinal);
      int htmlEnd = html.IndexOf("</details>", Math.Max(0, htmlStart), StringComparison.Ordinal);
      int nestedQuote = html.IndexOf("<blockquote class=\"user-context\">", StringComparison.Ordinal);
      int quoteEnd = html.IndexOf("</blockquote>", Math.Max(0, htmlEnd), StringComparison.Ordinal);
      int htmlPrompt = html.IndexOf(prompt, StringComparison.Ordinal);
      Require(nestedQuote >= 0 && htmlStart > nestedQuote, "HTML omitted context blockquote/details.");
      Require(html.Contains("<summary># Context from my IDE setup:</summary>", StringComparison.Ordinal), "HTML changed context summary.");
      Require(htmlEnd > htmlStart && quoteEnd > htmlEnd && htmlPrompt > quoteEnd, "HTML prompt is not after/outside context disclosure.");
      Require(!html.Contains("## My request for Codex:", StringComparison.Ordinal), "Request marker leaked into HTML.");
      int htmlFile = html.IndexOf("Active file:", StringComparison.Ordinal);
      int htmlSelection = html.IndexOf("Active selection of the file:", StringComparison.Ordinal);
      int htmlTabs = html.IndexOf("Open tabs:", StringComparison.Ordinal);
      Require(htmlFile >= 0 && htmlFile < htmlSelection && htmlSelection < htmlTabs, "HTML changed context order.");

      string plainRecord = JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T15:18:00.000Z",
        payload = new { type = "user_message", message = "Plain user prompt." }
      });
      string plainPath = Path.Combine(root, "plain.jsonl");
      File.WriteAllText(plainPath, plainRecord + Environment.NewLine);
      string plainHtml = TranscriptPresentationHtmlFormatter.Format(plainPath, AgentSource.Codex, pipeline);
      Require(!plainHtml.Contains("user-context", StringComparison.Ordinal), "No-context User emitted context HTML.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Verifies the exact consumer boundary for Codex thought commentary: visible
  /// commentary emitted while reasoning is active remains inside the same outer
  /// reasoning group as the surrounding thought/tool activity.
  /// </summary>
  private static void TestCodexCommentaryInsideReasoningGroup()
  {
    const string commentary =
      "I’ll load the local Codex memory for this turn, then answer plainly.";
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:08.000Z",
        payload = new
        {
          type = "agent_reasoning",
          text = "Need to inspect local state."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "response_item",
        timestamp = "2026-09-06T20:00:09.311Z",
        payload = new
        {
          type = "function_call",
          call_id = "call-1",
          name = "shell_command",
          arguments = "{\"command\":\"pwd\"}"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "response_item",
        timestamp = "2026-09-06T20:00:10.085Z",
        payload = new
        {
          type = "function_call_output",
          call_id = "call-1",
          output = "Exit code: 0"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:11.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "commentary",
          message = commentary
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:12.000Z",
        payload = new
        {
          type = "agent_reasoning",
          text = "Continue thinking."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T20:00:13.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Final answer."
        }
      })
    };

    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(AgentSource.Codex, records);
    AIConversationPresentation presentation = projection.Presentation ??
      throw new InvalidOperationException("Presentation tree is missing.");

    JsonElement turns = presentation.Tree.GetProperty("turns");
    Require(turns.GetArrayLength() == 1,
      $"Expected one Codex turn, got {turns.GetArrayLength()}.");
    JsonElement turn = turns[0];
    Require(turn.GetProperty("actor").GetProperty("role").GetString() == "assistant",
      "Expected the presentation turn to be an assistant turn.");

    JsonElement children = turn.GetProperty("children");
    Require(children.GetArrayLength() == 2,
      $"Expected reasoning group plus final response, got {children.GetArrayLength()} children.");
    JsonElement group = children[0];
    Require(group.GetProperty("kind").GetString() == "reasoning_group",
      "First assistant child is not the outer reasoning group.");

    JsonElement groupChildren = group.GetProperty("children");
    string[] kinds = groupChildren.EnumerateArray()
      .Select(child => child.GetProperty("kind").GetString() ?? string.Empty)
      .ToArray();
    Require(kinds.SequenceEqual(new[]
      {
        "reasoning",
        "tool",
        "commentary",
        "reasoning"
      }),
      "Commentary split the active thought/tool group instead of remaining inside it.");
    Require(groupChildren[2].GetProperty("blocks")[0]
        .GetProperty("text").GetString() == commentary,
      "Expected commentary text was not inside the active reasoning group.");

    JsonElement final = children[1];
    Require(final.GetProperty("kind").GetString() == "markdown",
      "Final assistant response did not remain outside the reasoning group.");
    Require(final.GetProperty("blocks")[0]
        .GetProperty("text").GetString() == "Final answer.",
      "Final assistant response text is incorrect.");
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
  /// Verifies that repeated latest-session discovery does not start a fresh core
  /// worker while neither the rollout nor its supplementary session index changed.
  /// A changed session index must invalidate the title cache exactly once.
  /// </summary>
  private static void TestCodexSessionTitleWorkerReuse()
  {
    const string sessionId = "01a07804-bcf3-7af3-8321-bdcf0c1ddc89";
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-codex-home-{Guid.NewGuid():N}");
    string sessionDirectory = Path.Combine(root, "sessions", "2026", "09", "06");
    Directory.CreateDirectory(sessionDirectory);
    string sessionPath = Path.Combine(
      sessionDirectory,
      "rollout-2026-09-06T15-17-01-" + sessionId + ".jsonl");
    string indexPath = Path.Combine(root, "session_index.jsonl");
    string? previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

    try
    {
      File.WriteAllLines(sessionPath, CodexRevisionRecords());
      File.WriteAllText(indexPath, JsonSerializer.Serialize(new
      {
        id = sessionId,
        thread_name = "Cached title"
      }) + Environment.NewLine);
      Environment.SetEnvironmentVariable("CODEX_HOME", root);

      int before = CountDiagnosticEvents("core.worker_started");
      LocatedSession first = SessionLocator.FindLatest(AgentSource.Codex);
      LocatedSession second = SessionLocator.FindLatest(AgentSource.Codex);
      int unchangedStarts = CountDiagnosticEvents("core.worker_started") - before;

      Require(first.Title == "Cached title" && second.Title == "Cached title",
        "Repeated Codex discovery did not resolve the expected title.");
      Require(unchangedStarts == 1,
        $"Expected one core worker start for unchanged repeated discovery, got {unchangedStarts}.");

      File.AppendAllText(indexPath, JsonSerializer.Serialize(new
      {
        id = sessionId,
        thread_name = "Renamed title"
      }) + Environment.NewLine);
      LocatedSession renamed = SessionLocator.FindLatest(AgentSource.Codex);
      int totalStarts = CountDiagnosticEvents("core.worker_started") - before;

      Require(renamed.Title == "Renamed title",
        $"Changed Codex session index did not refresh title: {renamed.Title}.");
      Require(totalStarts == 2,
        $"Expected one additional core worker start after index change, got {totalStarts} total.");
    }
    finally
    {
      Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Counts one diagnostic event in the current process log.
  /// </summary>
  private static int CountDiagnosticEvents(string eventName)
  {
    if (!File.Exists(DiagnosticLog.FilePath))
    {
      return 0;
    }
    string needle = $"\"Event\":\"{eventName}\"";
    return File.ReadLines(DiagnosticLog.FilePath)
      .Count(line => line.Contains(needle, StringComparison.Ordinal));
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