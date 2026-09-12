using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #76 removal of obsolete browser speech
/// identity reconstruction after the Core word-ID migration.
/// </summary>
internal static class Issue76LegacyWordMappingDebtRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #76 legacy word-mapping debt regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("legacy-word-mapping/lexical-speech-alignment-removed",
        TestLexicalSpeechAlignmentRemoved),
      ("legacy-word-mapping/node-text-playback-fallback-removed",
        TestNodeTextPlaybackFallbackRemoved),
      ("legacy-word-mapping/per-word-eligibility-mutation-absent",
        TestPerWordEligibilityMutationAbsent),
      ("legacy-word-mapping/find-search-index-retained",
        TestFindSearchIndexRetained),
      ("legacy-word-mapping/no-local-word-to-unit-map",
        TestNoLocalWordToUnitMap)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #76 legacy word-mapping debt suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #76 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #76 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Browser speech identity must not retokenize rendered text or maintain a
  /// lexical shadow word stream after Core word IDs become authoritative.
  /// </summary>
  private static void TestLexicalSpeechAlignmentRemoved()
  {
    string shell = ShellHtml();
    RequireAbsent(shell, "let lexicalWords", "lexicalWords shadow stream");
    RequireAbsent(shell, "lexicalWordsByRecord", "record-local lexical map");
    RequireAbsent(
      shell,
      "findSpeechLexicalAlignment",
      "lexical speech alignment fallback");
    RequireAbsent(shell, "function isLexical", "browser lexical classifier");
  }

  /// <summary>
  /// Playback must never recover transcript identity from node IDs, fragment
  /// text, or reconstructed segment ranges once canonical word IDs are used.
  /// </summary>
  private static void TestNodeTextPlaybackFallbackRemoved()
  {
    string shell = ShellHtml();
    RequireAbsent(shell, "segmentRangesByNode", "node segment-range identity map");
    RequireAbsent(shell, "knownNodeIds", "browser node-identity cache");
    RequireAbsent(shell, "findFragmentRange", "fragment text identity lookup");
    RequireAbsent(shell, "assignNodeScopes", "browser node-scope reconstruction");
  }

  /// <summary>
  /// Speech policy must remain centralized and must not stamp eligibility state
  /// onto each word node.
  /// </summary>
  private static void TestPerWordEligibilityMutationAbsent()
  {
    string shell = ShellHtml();
    RequireAbsent(shell, "voice-selectable", "voice-selectable word class");
    RequireAbsent(shell, "voice-excluded", "voice-excluded word class");
    RequireAbsent(
      shell,
      "applyVoiceEligibilityClasses",
      "per-word eligibility scan");
    RequireAbsent(
      shell,
      "markVoiceSelectableWords",
      "obsolete selectable-word compatibility hook");
  }

  /// <summary>
  /// Removing speech identity reconstruction must not delete the independent
  /// C# full-session Find index.
  /// </summary>
  private static void TestFindSearchIndexRetained()
  {
    MethodInfo? build = typeof(TranscriptSearchIndex).GetMethod(
      "Build",
      BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    Require(build is not null,
      "TranscriptSearchIndex.Build was removed with speech mapping debt.");
  }

  /// <summary>
  /// Viewport indexing may map Core unit IDs to virtual indexes, but it must not
  /// reintroduce a consumer word-ID-to-unit identity map.
  /// </summary>
  private static void TestNoLocalWordToUnitMap()
  {
    FieldInfo[] fields = typeof(TranscriptVirtualDocument).GetFields(
      BindingFlags.Instance | BindingFlags.Static |
      BindingFlags.Public | BindingFlags.NonPublic);
    string[] forbidden =
    {
      "wordindex",
      "wordindices",
      "wordmap",
      "wordsbyid",
      "wordstounit",
      "wordtounit"
    };
    foreach (FieldInfo field in fields)
    {
      string compact = new string(field.Name
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
      Require(!forbidden.Any(compact.Contains),
        $"TranscriptVirtualDocument still owns word-to-unit state: {field.Name}.");
    }
  }

  private static string ShellHtml()
  {
    MethodInfo method = typeof(TranscriptView).GetMethod(
      "ShellHtml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TranscriptView.ShellHtml is missing.");
    return method.Invoke(null, null) as string ??
      throw new InvalidOperationException("TranscriptView.ShellHtml returned null.");
  }

  private static void RequireAbsent(
    string text,
    string needle,
    string description)
  {
    Require(!text.Contains(needle, StringComparison.Ordinal),
      $"Obsolete {description} remains in the browser shell: {needle}.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
