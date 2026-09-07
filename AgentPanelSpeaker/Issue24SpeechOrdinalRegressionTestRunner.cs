using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs focused regressions for preserving ordered-list ordinals through the
/// production AgentPanelSpeaker speech pipeline.
/// </summary>
internal static class Issue24SpeechOrdinalRegressionTestRunner
{
  /// <summary>
  /// Runs all ordered-list speech regressions.
  /// </summary>
  /// <returns>Zero when all focused regressions pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("speech-ordinals/production-history", TestProductionHistory),
      ("speech-ordinals/non-one-and-nested", TestNonOneAndNested),
      ("speech-ordinals/markdown-prefix-regressions", TestMarkdownPrefixRegressions),
      ("speech-ordinals/final-tts-markup", TestFinalTtsMarkup)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #24 speech-ordinal regression suite: {tests.Length} tests");
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
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} speech-ordinal regressions passed."
      : $"FAIL: {failures}/{tests.Length} speech-ordinal regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Exercises the same history construction path used by the production
  /// monitor for the exact three-item failure shape reported by the user.
  /// </summary>
  private static void TestProductionHistory()
  {
    SpeechHistorySnapshot history = BuildProductionHistory(
      "1. First item\n2. Second item\n3. Third item");
    string[] assistant = history.Fragments
      .Where(fragment => fragment.Category == ContentCategory.Assistant)
      .Select(fragment => fragment.Text)
      .ToArray();

    RequireSequence(
      assistant,
      "1. First item",
      "2. Second item",
      "3. Third item");
  }

  /// <summary>
  /// Verifies non-one starts and nested ordered lists retain their ordinals at
  /// the speech-cleanup seam.
  /// </summary>
  private static void TestNonOneAndNested()
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
      "3. Third\n4. Fourth\n   7. Nested seven\n   8. Nested eight\n5. Fifth");
    string[] text = parts
      .Where(part => part.Kind == SpeechFragmentKind.Prose)
      .Select(part => part.Text)
      .ToArray();

    RequireSequence(
      text,
      "3. Third",
      "4. Fourth",
      "7. Nested seven",
      "8. Nested eight",
      "5. Fifth");
  }

  /// <summary>
  /// Verifies unrelated Markdown prefixes remain non-spoken while ordered-list
  /// ordinals remain part of spoken prose.
  /// </summary>
  private static void TestMarkdownPrefixRegressions()
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
      "# Heading\n\n> 3. Quoted ordered item\n\n- Bullet\n* Star bullet\n+ Plus bullet");
    string[] text = parts.Select(part => part.Text).ToArray();

    Require(text.Contains("Heading", StringComparer.Ordinal),
      "Heading marker was not stripped.");
    Require(text.Contains("3. Quoted ordered item", StringComparer.Ordinal),
      "Quoted ordered-list ordinal was stripped.");
    Require(text.Contains("Bullet", StringComparer.Ordinal),
      "Dash bullet text was lost.");
    Require(text.Contains("Star bullet", StringComparer.Ordinal),
      "Star bullet text was lost.");
    Require(text.Contains("Plus bullet", StringComparer.Ordinal),
      "Plus bullet text was lost.");
    Require(!text.Any(value => value.StartsWith("- ", StringComparison.Ordinal) ||
                               value.StartsWith("* ", StringComparison.Ordinal) ||
                               value.StartsWith("+ ", StringComparison.Ordinal) ||
                               value.StartsWith(">", StringComparison.Ordinal) ||
                               value.StartsWith("#", StringComparison.Ordinal)),
      "A non-spoken Markdown prefix leaked into speech text.");
  }

  /// <summary>
  /// Verifies the final markup handed to the speech engine still contains each
  /// ordinal after production history construction and sentence segmentation.
  /// </summary>
  private static void TestFinalTtsMarkup()
  {
    SpeechHistorySnapshot history = BuildProductionHistory(
      "1. First item\n2. Second item\n3. Third item");
    SpeechFragment[] assistant = history.Fragments
      .Where(fragment => fragment.Category == ContentCategory.Assistant)
      .ToArray();
    var pronunciations = PronunciationRuleSet.Parse(string.Empty);

    Require(assistant.Length == 3,
      $"Expected three Assistant speech fragments, got {assistant.Length}.");
    for (int index = 0; index < assistant.Length; ++index)
    {
      int ordinal = index + 1;
      SpeechFragment fragment = assistant[index];
      SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
        fragment.Text,
        pitchSetting: 0,
        Array.Empty<string>(),
        pronunciations,
        fragment.PauseAfter);
      string expected = $"{ordinal}. ";
      Require(markup.PlainText.StartsWith(expected, StringComparison.Ordinal),
        $"Plain TTS text omitted ordinal {ordinal}: {markup.PlainText}");
      Require(markup.SapiXml.Contains(expected, StringComparison.Ordinal),
        $"SAPI XML omitted ordinal {ordinal}: {markup.SapiXml}");
      Require(markup.SsmlContent.Contains(expected, StringComparison.Ordinal),
        $"SSML omitted ordinal {ordinal}: {markup.SsmlContent}");
    }
  }

  /// <summary>
  /// Builds speech history through the same Core projection, monitor cleanup,
  /// sentence segmentation, and fragment construction used for existing
  /// production transcript history.
  /// </summary>
  private static SpeechHistorySnapshot BuildProductionHistory(string response)
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue24-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = Path.Combine(root, "rollout-issue24.jsonl");
      string[] records =
      {
        JsonSerializer.Serialize(new
        {
          type = "event_msg",
          timestamp = "2026-09-07T17:00:00.000Z",
          payload = new
          {
            type = "user_message",
            message = "Give me a numbered list"
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

      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Codex);
      using var monitor = new JsonlSessionMonitor();
      return monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: true);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Requires an exact speech-text sequence.
  /// </summary>
  private static void RequireSequence(string[] actual, params string[] expected)
  {
    Require(actual.Length == expected.Length,
      $"Expected {expected.Length} speech fragments, got {actual.Length}: " +
      string.Join(" | ", actual));
    for (int index = 0; index < expected.Length; ++index)
    {
      Require(string.Equals(actual[index], expected[index], StringComparison.Ordinal),
        $"Speech fragment {index} mismatch. Expected '{expected[index]}', got '{actual[index]}'.");
    }
  }

  /// <summary>
  /// Throws when one regression condition is false.
  /// </summary>
  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
