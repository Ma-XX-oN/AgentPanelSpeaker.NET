using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies the AgentPanelSpeaker integration contract for Core-owned
/// User-context speech selection.
/// </summary>
internal static class Issue26UserContextSpeechRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #26 regression suite.
  /// </summary>
  /// <returns>Zero when all tests pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Test)[]
    {
      ("user-context-speech/settings-default-and-persistence",
        TestSettingsDefaultAndPersistence),
      ("user-context-speech/core-option-and-extraction",
        TestCoreOptionAndExtraction)
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

    Console.WriteLine(failed == 0
      ? $"PASS: {tests.Length}/{tests.Length} user-context speech regressions passed."
      : $"FAIL: {failed}/{tests.Length} user-context speech regressions failed.");
    return failed == 0 ? 0 : 1;
  }

  /// <summary>
  /// Requires a persisted Transcript Settings switch whose default preserves
  /// current behaviour by leaving IDE/User context speech disabled.
  /// </summary>
  private static void TestSettingsDefaultAndPersistence()
  {
    PropertyInfo property = typeof(TranscriptSettings).GetProperty(
      "SpeakUserContext") ?? throw new InvalidOperationException(
        "TranscriptSettings does not expose SpeakUserContext.");
    Require(property.PropertyType == typeof(bool),
      "TranscriptSettings.SpeakUserContext is not Boolean.");
    Require(property.GetValue(TranscriptSettings.Default) is false,
      "SpeakUserContext must default to false.");

    string json = JsonSerializer.Serialize(TranscriptSettings.Default);
    TranscriptSettings? roundTrip = JsonSerializer.Deserialize<TranscriptSettings>(json);
    Require(roundTrip is not null, "TranscriptSettings JSON round-trip failed.");
    Require(property.GetValue(roundTrip) is false,
      "SpeakUserContext did not survive the settings JSON round-trip.");
  }

  /// <summary>
  /// Exercises the real Core client, speech-selection preparation, and
  /// canonical extraction path with the option disabled and enabled.
  /// </summary>
  private static void TestCoreOptionAndExtraction()
  {
    const string prompt = "What time is it in Paris?";
    const string sourceMessage =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n" + prompt;
    string record = JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-07T00:00:00Z",
      payload = new { type = "user_message", message = sourceMessage }
    });

    PropertyInfo optionProperty = typeof(AIConversationCoreProjectOptions)
      .GetProperty("IncludeUserContext") ?? throw new InvalidOperationException(
        "AIConversationCoreProjectOptions does not expose IncludeUserContext.");
    Require(optionProperty.PropertyType == typeof(bool),
      "AIConversationCoreProjectOptions.IncludeUserContext is not Boolean.");

    using var client = new AIConversationCoreClient();
    AIConversationProjection disabledRaw = client.Project(
      AgentSource.Codex,
      new[] { record },
      CreateProjectOptions(includeUserContext: false));
    AIConversationProjection enabledRaw = client.Project(
      AgentSource.Codex,
      new[] { record },
      CreateProjectOptions(includeUserContext: true));

    Require(disabledRaw.Markdown == enabledRaw.Markdown,
      "Speech selection changed the canonical rendered transcript.");

    ExtractionResult disabled = CanonicalProjectionExtractor.ExtractRecord(
      CanonicalSpeechProjection.Prepare(disabledRaw),
      AgentSource.Codex,
      0);
    ExtractionResult enabled = CanonicalProjectionExtractor.ExtractRecord(
      CanonicalSpeechProjection.Prepare(enabledRaw),
      AgentSource.Codex,
      0);

    IReadOnlyList<(string Category, string Text)> disabledNodes = ReadNodes(disabled);
    IReadOnlyList<(string Category, string Text)> enabledNodes = ReadNodes(enabled);

    Require(disabledNodes.Count == 1,
      $"Disabled context speech emitted {disabledNodes.Count} nodes instead of one prompt.");
    Require(disabledNodes[0].Category == nameof(ContentCategory.User),
      "Disabled context speech changed the prompt voice category.");
    Require(disabledNodes[0].Text == prompt,
      "Disabled context speech did not leave the actual User prompt intact.");

    Require(enabledNodes.Count == 2,
      $"Enabled context speech emitted {enabledNodes.Count} nodes instead of context + prompt.");
    Require(enabledNodes[0].Category == nameof(ContentCategory.UserContext),
      "Included IDE context did not use the alternate User-context voice category.");
    Require(enabledNodes[0].Text.Contains("## Active file:", StringComparison.Ordinal),
      "Included IDE context lost canonical context content.");
    Require(enabledNodes[1].Category == nameof(ContentCategory.User),
      "Actual prompt did not retain the normal User voice category.");
    Require(enabledNodes[1].Text == prompt,
      "Actual prompt changed when IDE context speech was enabled.");
  }

  /// <summary>
  /// Creates project options by the production record constructor while keeping
  /// this RED test compilable before IncludeUserContext exists.
  /// </summary>
  private static AIConversationCoreProjectOptions CreateProjectOptions(
    bool includeUserContext)
  {
    ConstructorInfo constructor = typeof(AIConversationCoreProjectOptions)
      .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .OrderByDescending(item => item.GetParameters().Length)
      .First();
    ParameterInfo[] parameters = constructor.GetParameters();
    var arguments = new object?[parameters.Length];
    for (int index = 0; index < parameters.Length; ++index)
    {
      ParameterInfo parameter = parameters[index];
      arguments[index] = parameter.Name switch
      {
        "IncludeRolledBackTurns" or "includeRolledBackTurns" => false,
        "CodexSessionIndexPath" or "codexSessionIndexPath" => null,
        "IncludeUserContext" or "includeUserContext" => includeUserContext,
        _ when parameter.HasDefaultValue => parameter.DefaultValue,
        _ => throw new InvalidOperationException(
          $"Unexpected project-option constructor parameter: {parameter.Name}")
      };
    }

    return (AIConversationCoreProjectOptions)constructor.Invoke(arguments);
  }

  /// <summary>
  /// Reads app extraction nodes without coupling the test to their record
  /// declaration location.
  /// </summary>
  private static IReadOnlyList<(string Category, string Text)> ReadNodes(
    ExtractionResult extraction)
  {
    PropertyInfo nodesProperty = extraction.GetType().GetProperty("Nodes") ??
      throw new InvalidOperationException("ExtractionResult does not expose Nodes.");
    if (nodesProperty.GetValue(extraction) is not IEnumerable nodes)
    {
      throw new InvalidOperationException("ExtractionResult.Nodes is not enumerable.");
    }

    var result = new List<(string Category, string Text)>();
    foreach (object node in nodes)
    {
      Type type = node.GetType();
      string category = type.GetProperty("Category")?.GetValue(node)?.ToString() ?? string.Empty;
      string text = type.GetProperty("Text")?.GetValue(node)?.ToString() ?? string.Empty;
      result.Add((category, text));
    }
    return result;
  }

  /// <summary>
  /// Throws when an acceptance condition is false.
  /// </summary>
  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
