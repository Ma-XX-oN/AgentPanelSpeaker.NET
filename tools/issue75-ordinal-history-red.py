from pathlib import Path

path = Path('AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("core-word-id/production-history-carries-core-word-ids",\n        TestProductionHistoryCarriesCoreWordIds),\n      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n'''
new = '''      ("core-word-id/production-history-carries-core-word-ids",\n        TestProductionHistoryCarriesCoreWordIds),\n      ("core-word-id/ordered-list-history-preserves-core-word-boundaries",\n        TestOrderedListHistoryPreservesCoreWordBoundaries),\n      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n'''
if text.count(old) != 1:
  raise RuntimeError('test-list insertion point mismatch')
text = text.replace(old, new)

marker = '''  /// <summary>\n  /// Policy changes must be centralized. Static Core word elements may not be\n'''
method = r'''  /// <summary>
  /// Ordered-list ordinals are ordinary Core words. Production history must
  /// carry that exact identity without decomposing `1.` into consumer tokens,
  /// and seeking the following body word must land on that body word.
  /// </summary>
  private static void TestOrderedListHistoryPreservesCoreWordBoundaries()
  {
    string record = ClaudeRecord("user", "1. Item", 1, null);
    using var core = new AIConversationCoreClient();
    AIConversationProjection projection = core.Project(
      AgentSource.Claude,
      new[] { record });
    CanonicalSpeechWordProjection[] expectedWords =
      RequireOnlyHtmlUnit(projection).SpeechWords ??
      Array.Empty<CanonicalSpeechWordProjection>();
    Require(expectedWords.Length == 2,
      $"Expected ordinal and body Core words, got {expectedWords.Length}.");
    Require(string.Equals(expectedWords[0].Text, "1.", StringComparison.Ordinal),
      $"Core ordinal is not one canonical word: {expectedWords[0].Text}.");
    Require(string.Equals(expectedWords[1].Text, "Item", StringComparison.Ordinal),
      $"Unexpected ordered-list body word: {expectedWords[1].Text}.");

    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue75-ordinal-{Guid.NewGuid():N}");
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
        string.Equals(item.Text, "1. Item", StringComparison.Ordinal));
      long[] expectedIds = expectedWords.Select(word => word.Id).ToArray();
      long[] actualIds = fragment.WordIds?.ToArray() ?? Array.Empty<long>();
      Require(actualIds.SequenceEqual(expectedIds),
        "Production ordered-list history did not carry Core ordinal/body IDs.");

      using var speech = new SpeechService();
      speech.SetPolicyProviders(
        _ => new SpeechProfileSettings("Test voice", 0, 0),
        _ => true,
        () => Array.Empty<string>(),
        () => PronunciationRuleSet.Parse(string.Empty),
        () => AudioWakeSettings.Default);
      speech.LoadHistory(
        history.Fragments,
        history.Completions,
        history.BackgroundWorkEvents,
        PlaybackStartMode.Beginning);
      TranscriptPlaybackPosition? position = null;
      speech.PlaybackPositionChanged += value => position = value;

      bool sought = speech.TrySeekToTranscriptWord(expectedWords[1].Id, out _);
      Require(sought, "Ordered-list body Core word ID was not seekable.");
      Require(position is not null &&
          string.Equals(position.Word, "Item", StringComparison.Ordinal),
        "Seeking the ordered-list body word did not land on Item.");
      Require(position!.WordId == expectedWords[1].Id,
        "Ordered-list body seek changed the authoritative Core word ID.");
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
