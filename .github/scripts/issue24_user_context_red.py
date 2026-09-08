from pathlib import Path

path = Path('AgentPanelSpeaker/Issue24ProductionPathRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("speech-ordinals/production-path-secondary-structural-identity",
        TestSecondaryStructuralIdentityThroughProductionPayload)
    };'''
new = '''      ("speech-ordinals/production-path-secondary-structural-identity",
        TestSecondaryStructuralIdentityThroughProductionPayload),
      ("speech-ordinals/production-path-user-context-node-id-parity",
        TestUserContextNodeIdParity)
    };'''
if old not in text:
  raise SystemExit('test list anchor not found')
text = text.replace(old, new, 1)

anchor = '''  /// <summary>
  /// Exercises the dependent failure seam as one path: an atomic virtual unit'''
method = r'''  /// <summary>
  /// Reproduces the installed-session node-ID namespace divergence.  The
  /// monitor always indexes Codex user context so speech navigation IDs remain
  /// stable even when that context is muted.  Transcript identity rebuilding
  /// must use the same projection or later rendered nodes receive smaller IDs.
  /// </summary>
  private static void TestUserContextNodeIdParity()
  {
    const string nestedFragment =
      "1. *Nested numbered item with a table inside it*";
    const string response = """
Like this:

1. Outer item
   1. *Nested numbered item with a table inside it*

      | Column | Value | Style |
      | --- | --- | --- |
      | Alpha | 1 | **bold** |
""";

    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue24-user-context-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = WriteUserContextProductionFixture(root, response);
      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Codex);
      using var monitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: true,
        includeRolledBackTurns: false,
        includeUserContext: true);
      SpeechFragment speechFragment = history.Fragments.First(fragment =>
        string.Equals(fragment.Text, nestedFragment, StringComparison.Ordinal));

      IReadOnlyList<TranscriptNodeIdentity> identities =
        TranscriptNodeIdentityMap.Build(path, AgentSource.Codex);
      TranscriptNodeIdentity displayIdentity = identities.First(identity =>
        identity.Segments.Contains(nestedFragment, StringComparer.Ordinal));

      Require(displayIdentity.NodeId == speechFragment.NodeId,
        "User-context production path assigned different node IDs: " +
        $"speech={speechFragment.NodeId}, display={displayIdentity.NodeId}.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if anchor not in text:
  raise SystemExit('method anchor not found')
text = text.replace(anchor, method + anchor, 1)

anchor = '''  private static string WriteProductionFixture(string root, string response)
  {'''
helper = r'''  private static string WriteUserContextProductionFixture(
    string root,
    string response)
  {
    string path = Path.Combine(root, "rollout-issue24-user-context.jsonl");
    const string contextPrefix =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n";
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T16:59:58.000Z",
        payload = new
        {
          type = "user_message",
          message = contextPrefix + "Warm-up request"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T16:59:59.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Warm-up response."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T17:00:00.000Z",
        payload = new
        {
          type = "user_message",
          message = contextPrefix +
            "Give me a nested numbered list with a table"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T17:00:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = response
        }
      })
    };
    File.WriteAllLines(path, records);
    return path;
  }

'''
if anchor not in text:
  raise SystemExit('fixture anchor not found')
text = text.replace(anchor, helper + anchor, 1)
path.write_text(text, encoding='utf-8')
