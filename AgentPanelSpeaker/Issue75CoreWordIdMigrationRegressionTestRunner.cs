using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #75 canonical Core word-ID migration.
/// </summary>
internal static class Issue75CoreWordIdMigrationRegressionTestRunner
{
  private sealed record WordProbe(long Id, string Text, string SeparatorBefore);

  /// <summary>
  /// Runs the issue #75 canonical-word migration regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("core-word-id/ctrl-click-posts-exact-canonical-word-id",
        TestCtrlClickPostsExactCanonicalWordId),
      ("core-word-id/decimal-is-one-selectable-canonical-word",
        TestDecimalIsOneSelectableCanonicalWord),
      ("core-word-id/markup-split-keeps-one-canonical-word",
        TestMarkupSplitKeepsOneCanonicalWord),
      ("core-word-id/duplicate-text-lookup-is-id-exact",
        TestDuplicateTextLookupIsIdExact),
      ("core-word-id/off-window-lookup-materializes-core-unit",
        TestOffWindowLookupMaterializesCoreUnit),
      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",
        TestPolicyIsCentralizedWithoutPerWordEligibilityMutation)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #75 Core word-ID migration suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #75 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #75 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Ctrl+click must cross the browser/host boundary using only the numeric
  /// Core word handle represented by the static Core DOM ID `word-N`.
  /// </summary>
  private static void TestCtrlClickPostsExactCanonicalWordId()
  {
    Type argsType = typeof(FindSeekRequestedEventArgs);
    RequireProperty(argsType, "WordId");
    RequireNoProperty(argsType, "NodeId");
    RequireNoProperty(argsType, "NodeWordIndex");

    string shell = ShellHtml();
    string block = Around(shell, "source:'ctrl-click'", 1400);
    Require(block.Contains("wordId", StringComparison.Ordinal),
      "Ctrl+click does not post a canonical wordId.");
    Require(
      block.Contains("word.id", StringComparison.Ordinal) ||
      block.Contains("word-", StringComparison.Ordinal),
      "Ctrl+click does not derive its handle from the Core word-N DOM identity.");
    Require(!block.Contains("nodeWordIndex", StringComparison.Ordinal),
      "Ctrl+click still crosses the boundary with NodeWordIndex.");
    Require(!block.Contains("dataset.nodeId", StringComparison.Ordinal),
      "Ctrl+click still crosses the boundary with reconstructed NodeId identity.");
  }

  /// <summary>
  /// Core's decimal token must remain one word in the C# projection and the
  /// exact same numeric identity must be present once in the rendered DOM.
  /// </summary>
  private static void TestDecimalIsOneSelectableCanonicalWord()
  {
    AIConversationProjection projection = ProjectClaude("alpha 13.234 omega");
    CanonicalHtmlUnitProjection unit = RequireOnlyHtmlUnit(projection);
    WordProbe[] words = ReadSpeechWords(unit);
    WordProbe[] decimalWords = words
      .Where(word => string.Equals(word.Text, "13.234", StringComparison.Ordinal))
      .ToArray();

    Require(decimalWords.Length == 1,
      $"Expected one canonical decimal word, got {decimalWords.Length}.");
    long wordId = decimalWords[0].Id;
    Require(wordId > 0, "Canonical decimal word ID is not positive.");
    Require(CountOccurrences(unit.Html, $"id=\"word-{wordId}\"") == 1,
      "Canonical decimal word ID is not represented exactly once in Core HTML.");
  }

  /// <summary>
  /// Inline Markdown may change descendants inside a word but must not create
  /// another canonical word identity for the formatted suffix.
  /// </summary>
  private static void TestMarkupSplitKeepsOneCanonicalWord()
  {
    AIConversationProjection projection = ProjectClaude("turn_id**s**");
    CanonicalHtmlUnitProjection unit = RequireOnlyHtmlUnit(projection);
    WordProbe[] words = ReadSpeechWords(unit);

    Require(words.Length == 1,
      $"Expected one canonical word for markup-split token, got {words.Length}.");
    Require(string.Equals(words[0].Text, "turn_ids", StringComparison.Ordinal),
      $"Unexpected canonical markup-split word text: {words[0].Text}.");
    string id = $"id=\"word-{words[0].Id}\"";
    int idIndex = unit.Html.IndexOf(id, StringComparison.Ordinal);
    int strongIndex = unit.Html.IndexOf("<strong>s</strong>", StringComparison.Ordinal);
    int closeIndex = idIndex < 0
      ? -1
      : unit.Html.IndexOf("</span>", idIndex, StringComparison.Ordinal);
    Require(idIndex >= 0 && strongIndex > idIndex && closeIndex > strongIndex,
      "Formatted suffix is not nested inside the one canonical word-N element.");
    Require(CountOccurrences(unit.Html, id) == 1,
      "Markup-split word identity appears more than once in Core HTML.");
  }

  /// <summary>
  /// Duplicate visible text may never substitute for the requested Core word ID.
  /// The retained-session lookup must return the exact word and containing unit.
  /// </summary>
  private static void TestDuplicateTextLookupIsIdExact()
  {
    string[] records =
    {
      ClaudeRecord("user", "repeat first", 1, null),
      ClaudeRecord("assistant", "repeat second", 2, "word-id-1")
    };

    using var client = new AIConversationCoreClient();
    AIConversationCoreRetainedSession retained = client.CreateRetainedSession(
      AgentSource.Claude,
      records);
    try
    {
      CanonicalHtmlUnitProjection[] units = RequireHtmlUnits(retained.Projection);
      Require(units.Length >= 2,
        $"Duplicate-text fixture requires at least two Core units, got {units.Length}.");
      WordProbe first = ReadSpeechWords(units[0]).Single(word =>
        string.Equals(word.Text, "repeat", StringComparison.Ordinal));
      WordProbe second = ReadSpeechWords(units[1]).Single(word =>
        string.Equals(word.Text, "repeat", StringComparison.Ordinal));
      Require(first.Id != second.Id,
        "Duplicate visible text unexpectedly shares one canonical word ID.");

      object location = LocateRetainedWord(client, retained.Id, second.Id);
      object locatedWord = RequireObjectProperty(location, "Word");
      object locatedUnit = RequireObjectProperty(location, "Unit");
      long locatedId = Convert.ToInt64(
        RequireObjectProperty(locatedWord, "Id"),
        CultureInfo.InvariantCulture);
      string locatedUnitId = Convert.ToString(
        RequireObjectProperty(locatedUnit, "Id"),
        CultureInfo.InvariantCulture) ?? string.Empty;

      Require(locatedId == second.Id,
        $"Lookup returned word {locatedId} instead of requested {second.Id}.");
      Require(string.Equals(locatedUnitId, units[1].Id, StringComparison.Ordinal),
        "Duplicate text in another unit captured the canonical lookup.");
    }
    finally
    {
      client.CloseRetainedSession(retained.Id);
    }
  }

  /// <summary>
  /// An off-window word must resolve through Core to a unit ID, then the virtual
  /// document must materialize a window around that Core unit without a local
  /// word-to-unit identity map.
  /// </summary>
  private static void TestOffWindowLookupMaterializesCoreUnit()
  {
    string[] records = Enumerable.Range(0, 24)
      .Select(index => ClaudeRecord(
        index % 2 == 0 ? "user" : "assistant",
        $"target{index} " + new string('x', 1200),
        index + 1,
        index == 0 ? null : $"word-id-{index}"))
      .ToArray();

    using var client = new AIConversationCoreClient();
    AIConversationCoreRetainedSession retained = client.CreateRetainedSession(
      AgentSource.Claude,
      records);
    try
    {
      CanonicalHtmlUnitProjection[] units = RequireHtmlUnits(retained.Projection);
      Require(units.Length >= 12,
        $"Off-window fixture produced only {units.Length} Core units.");
      CanonicalHtmlUnitProjection targetUnit = units[^1];
      WordProbe targetWord = ReadSpeechWords(targetUnit)[0];

      var document = TranscriptVirtualDocument.Build(units);
      TranscriptWindow initial = document.CreateWindow(0, 100.0);
      object location = LocateRetainedWord(client, retained.Id, targetWord.Id);
      object locatedUnit = RequireObjectProperty(location, "Unit");
      string locatedUnitId = Convert.ToString(
        RequireObjectProperty(locatedUnit, "Id"),
        CultureInfo.InvariantCulture) ?? string.Empty;
      Require(string.Equals(locatedUnitId, targetUnit.Id, StringComparison.Ordinal),
        "Core lookup did not return the target off-window unit.");

      MethodInfo resolver = typeof(TranscriptVirtualDocument).GetMethod(
        "TryGetUnitIndex",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
        throw new InvalidOperationException(
          "TranscriptVirtualDocument.TryGetUnitIndex(string, out int) is missing.");
      object?[] arguments = { locatedUnitId, -1 };
      bool resolved = resolver.Invoke(document, arguments) is true;
      int targetIndex = arguments[1] is int value ? value : -1;
      Require(resolved && targetIndex >= 0,
        "Virtual document could not resolve the Core-returned unit ID.");
      Require(targetIndex < initial.StartIndex || targetIndex > initial.EndIndex,
        "Off-window fixture target was unexpectedly already materialized.");

      TranscriptWindow targetWindow = document.CreateWindow(targetIndex, 100.0);
      Require(
        targetIndex >= targetWindow.StartIndex && targetIndex <= targetWindow.EndIndex,
        "Core-returned unit index was not materialized in the target window.");
    }
    finally
    {
      client.CloseRetainedSession(retained.Id);
    }
  }

  /// <summary>
  /// Policy changes must be centralized. Static Core word elements may not be
  /// reclassified one by one when speech/category/fence eligibility changes.
  /// </summary>
  private static void TestPolicyIsCentralizedWithoutPerWordEligibilityMutation()
  {
    string shell = ShellHtml();
    Require(!shell.Contains("voice-selectable", StringComparison.Ordinal),
      "Browser still stamps per-word voice-selectable classes.");
    Require(!shell.Contains("voice-excluded", StringComparison.Ordinal),
      "Browser still stamps per-word voice-excluded classes.");
    Require(!shell.Contains("applyVoiceEligibilityClasses", StringComparison.Ordinal),
      "Browser still performs a transcript-wide per-word eligibility scan.");
    Require(!shell.Contains("dataset.nodeWordIndex", StringComparison.Ordinal),
      "Browser still reconstructs per-word speech ordinals in the DOM.");
    Require(shell.Contains("setVoicePolicy", StringComparison.Ordinal),
      "Browser has no centralized voice-policy state operation.");
    Require(shell.Contains("isVoiceWordEligible", StringComparison.Ordinal),
      "Ctrl+click does not consult centralized policy state.");
  }

  private static AIConversationProjection ProjectClaude(string text)
  {
    using var client = new AIConversationCoreClient();
    return client.Project(
      AgentSource.Claude,
      new[] { ClaudeRecord("user", text, 1, null) });
  }

  private static string ClaudeRecord(
    string role,
    string text,
    int ordinal,
    string? parentUuid)
  {
    return JsonSerializer.Serialize(new
    {
      parentUuid,
      isSidechain = false,
      uuid = $"word-id-{ordinal}",
      type = role,
      timestamp = $"2026-09-11T12:{ordinal % 60:00}:00.000Z",
      message = new
      {
        role,
        content = new[] { new { type = "text", text } }
      }
    });
  }

  private static CanonicalHtmlUnitProjection RequireOnlyHtmlUnit(
    AIConversationProjection projection)
  {
    CanonicalHtmlUnitProjection[] units = RequireHtmlUnits(projection);
    Require(units.Length == 1,
      $"Expected one Core HTML unit, got {units.Length}.");
    return units[0];
  }

  private static CanonicalHtmlUnitProjection[] RequireHtmlUnits(
    AIConversationProjection projection)
  {
    CanonicalHtmlUnitProjection[]? units = projection.HtmlUnits;
    if (units is null)
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted html_units.");
    }
    return units;
  }

  private static WordProbe[] ReadSpeechWords(CanonicalHtmlUnitProjection unit)
  {
    PropertyInfo property = typeof(CanonicalHtmlUnitProjection).GetProperty(
      "SpeechWords",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "CanonicalHtmlUnitProjection.SpeechWords is missing.");
    if (property.GetValue(unit) is not IEnumerable values)
    {
      throw new InvalidOperationException(
        "CanonicalHtmlUnitProjection.SpeechWords is not enumerable.");
    }

    var words = new List<WordProbe>();
    foreach (object? item in values)
    {
      if (item is null)
      {
        continue;
      }
      long id = Convert.ToInt64(
        RequireObjectProperty(item, "Id"),
        CultureInfo.InvariantCulture);
      string text = Convert.ToString(
        RequireObjectProperty(item, "Text"),
        CultureInfo.InvariantCulture) ?? string.Empty;
      string separator = Convert.ToString(
        RequireObjectProperty(item, "SeparatorBefore"),
        CultureInfo.InvariantCulture) ?? string.Empty;
      words.Add(new WordProbe(id, text, separator));
    }
    return words.ToArray();
  }

  private static object LocateRetainedWord(
    AIConversationCoreClient client,
    string sessionId,
    long wordId)
  {
    MethodInfo method = typeof(AIConversationCoreClient).GetMethod(
      "LocateRetainedWord",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "AIConversationCoreClient.LocateRetainedWord() is missing.");
    object? location = method.Invoke(
      client,
      new object?[] { sessionId, wordId, new AIConversationCoreProjectOptions() });
    return location ?? throw new InvalidOperationException(
      $"Core retained-session lookup returned null for word {wordId}.");
  }

  private static object RequireObjectProperty(object target, string propertyName)
  {
    PropertyInfo property = target.GetType().GetProperty(
      propertyName,
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"{target.GetType().Name}.{propertyName} is missing.");
    return property.GetValue(target) ?? throw new InvalidOperationException(
      $"{target.GetType().Name}.{propertyName} is null.");
  }

  private static string ShellHtml()
  {
    MethodInfo method = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TranscriptView.BuildShellHtml is missing.");
    return method.Invoke(null, null) as string ??
      throw new InvalidOperationException("BuildShellHtml did not return HTML.");
  }

  private static string Around(string text, string needle, int radius)
  {
    int index = text.IndexOf(needle, StringComparison.Ordinal);
    if (index < 0)
    {
      throw new InvalidOperationException(
        $"Expected shell marker was not found: {needle}");
    }
    int start = Math.Max(0, index - radius);
    int end = Math.Min(text.Length, index + needle.Length + radius);
    return text[start..end];
  }

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

  private static void RequireProperty(Type type, string propertyName)
  {
    Require(
      type.GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null,
      $"{type.Name}.{propertyName} is missing.");
  }

  private static void RequireNoProperty(Type type, string propertyName)
  {
    Require(
      type.GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null,
      $"{type.Name} still exposes legacy {propertyName} identity.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
