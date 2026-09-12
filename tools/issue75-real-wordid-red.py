from pathlib import Path

path = Path('AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("core-word-id/playback-position-carries-canonical-word-id",\n        TestPlaybackPositionCarriesCanonicalWordId),\n      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n'''
new = '''      ("core-word-id/playback-position-carries-canonical-word-id",\n        TestPlaybackPositionCarriesCanonicalWordId),\n      ("core-word-id/production-history-carries-core-word-ids",\n        TestProductionHistoryCarriesCoreWordIds),\n      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n'''
if text.count(old) != 1:
  raise RuntimeError('test-list insertion point mismatch')
text = text.replace(old, new)

marker = '''  /// <summary>\n  /// Policy changes must be centralized. Static Core word elements may not be\n'''
method = r'''  /// <summary>
  /// The real monitor/history path must carry the same immutable Core word IDs
  /// exposed by the canonical HTML/speech projection for the source record.
  /// </summary>
  private static void TestProductionHistoryCarriesCoreWordIds()
  {
    string record = ClaudeRecord("user", "alpha 13.234 omega", 1, null);
    using var core = new AIConversationCoreClient();
    AIConversationProjection projection = core.Project(
      AgentSource.Claude,
      new[] { record });
    long[] expected = RequireOnlyHtmlUnit(projection)
      .SpeechWords?
      .Select(word => word.Id)
      .ToArray() ?? Array.Empty<long>();
    Require(expected.Length == 3,
      $"Expected three Core words in production fixture, got {expected.Length}.");

    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue75-real-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string sessionPath = Path.Combine(root, "session.jsonl");
      File.WriteAllText(sessionPath, record + Environment.NewLine);
      LocatedSession session = SessionLocator.FromPath(
        sessionPath,
        AgentSource.Claude);
      using var monitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: false);
      SpeechFragment fragment = history.Fragments.Single(item =>
        item.Category == ContentCategory.User &&
        string.Equals(item.Text, "alpha 13.234 omega", StringComparison.Ordinal));
      long[] actual = fragment.WordIds?.ToArray() ?? Array.Empty<long>();
      Require(actual.SequenceEqual(expected),
        "Production history did not carry the exact Core speech_words IDs.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if text.count(marker) != 1:
  raise RuntimeError('method insertion point mismatch')
text = text.replace(marker, method + marker)
path.write_text(text, encoding='utf-8', newline='\n')
