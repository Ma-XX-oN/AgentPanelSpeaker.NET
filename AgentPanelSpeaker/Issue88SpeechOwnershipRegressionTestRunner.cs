using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issues #88-#90 exact speech ownership and
/// whole-fragment degradation.
/// </summary>
internal static class Issue88SpeechOwnershipRegressionTestRunner
{
  /// <summary>
  /// Runs the exact speech-ownership regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("speech-ownership/no-approximate-boundary-generator",
        TestNoApproximateBoundaryGenerator),
      ("speech-ownership/no-percentage-coverage-threshold",
        TestNoPercentageCoverageThreshold),
      ("speech-ownership/global-fragment-id-contract",
        TestGlobalFragmentIdContract),
      ("speech-ownership/multiword-boundary-contract",
        TestMultiwordBoundaryContract),
      ("speech-ownership/tracking-degradation-contract",
        TestTrackingDegradationContract),
      ("speech-ownership/spell-out-token-can-span-ssml-nodes",
        TestSpellOutTokenCanSpanSsmlNodes),
      ("speech-ownership/transformed-ssml-retains-canonical-bookmarks",
        TestTransformedSsmlRetainsCanonicalBookmarks),
      ("speech-ownership/browser-has-fragment-wrapper-path",
        TestBrowserHasFragmentWrapperPath),
      ("speech-ownership/fragment-ids-assigned-by-monitor",
        TestFragmentIdsAssignedByMonitor)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #88 speech-ownership regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #88 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #88 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestNoApproximateBoundaryGenerator()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains("CreateApproximateBoundaries", StringComparison.Ordinal),
      "Approximate/interpolated word-boundary generation still exists.");
    Require(!source.Contains("Exact: false", StringComparison.Ordinal),
      "A fabricated non-exact word boundary still exists.");
  }

  private static void TestNoPercentageCoverageThreshold()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains("coverage <", StringComparison.Ordinal),
      "Speech ownership still uses a percentage coverage threshold.");
    Require(!source.Contains("speakableTokenCount * 0.8", StringComparison.Ordinal),
      "Windows.Media still contains its second 80% reliability threshold.");
  }

  private static void TestGlobalFragmentIdContract()
  {
    PropertyInfo fragmentId = typeof(SpeechFragment).GetProperty("FragmentId") ??
      throw new InvalidOperationException("SpeechFragment has no FragmentId.");
    Require(fragmentId.PropertyType == typeof(long),
      "FragmentId is not a conversation-global integer identity.");

    PropertyInfo playbackFragmentId =
      typeof(TranscriptPlaybackPosition).GetProperty("FragmentId") ??
      throw new InvalidOperationException(
        "TranscriptPlaybackPosition has no FragmentId.");
    Require(playbackFragmentId.PropertyType == typeof(long?),
      "Playback FragmentId is not optional long identity.");
  }

  private static void TestMultiwordBoundaryContract()
  {
    PropertyInfo wordCount = typeof(SpeechWordBoundary).GetProperty("WordCount") ??
      throw new InvalidOperationException(
        "SpeechWordBoundary cannot own multiple textual words.");
    Require(wordCount.PropertyType == typeof(int),
      "SpeechWordBoundary.WordCount is not an integer range length.");

    PropertyInfo wordIds = typeof(TranscriptPlaybackPosition).GetProperty("WordIds") ??
      throw new InvalidOperationException(
        "TranscriptPlaybackPosition cannot carry multiple canonical WordIds.");
    Require(typeof(IEnumerable<long>).IsAssignableFrom(wordIds.PropertyType),
      "Playback WordIds is not a canonical word collection.");
  }

  private static void TestTrackingDegradationContract()
  {
    EventInfo trackingUnavailable = typeof(SapiSpeechEngine).GetEvent(
      "WordTrackingUnavailable",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "SapiSpeechEngine exposes no explicit word-tracking degradation event.");
    Require(trackingUnavailable.EventHandlerType is not null,
      "Word-tracking degradation event has no handler type.");

    string service = ReadSource("SpeechService.cs");
    Require(service.Contains("TranscriptPlaybackHighlightMode.Fragment", StringComparison.Ordinal),
      "SpeechService does not explicitly degrade to fragment highlighting.");
    int degradationStart = service.IndexOf(
      "private void EngineWordTrackingUnavailable(",
      StringComparison.Ordinal);
    int completedStart = service.IndexOf(
      "private void EngineCompleted()",
      degradationStart,
      StringComparison.Ordinal);
    Require(degradationStart >= 0 && completedStart > degradationStart,
      "SpeechService degradation handler could not be isolated.");
    string degradationHandler = service[degradationStart..completedStart];
    Require(degradationHandler.Contains("Activity?.Invoke(", StringComparison.Ordinal),
      "Fragment-level speech degradation is not visible in Activity.");
    Require(degradationHandler.Contains("degradation.Reason", StringComparison.Ordinal),
      "Activity degradation warning does not include the actual reason.");
  }

  private static void TestSpellOutTokenCanSpanSsmlNodes()
  {
    var markup = new SpeechMarkup(
      "AI-transcript",
      "AI-transcript",
      "<break time=\"100ms\"/><say-as interpret-as=\"spell-out\">AI</say-as>" +
        "<break time=\"100ms\"/>-transcript");
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "TryBuildBookmarkedSsml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Windows.Media bookmark builder is missing.");
    object?[] arguments = { markup, "en-US", null };
    bool built = Convert.ToBoolean(method.Invoke(null, arguments));
    Require(built,
      "Canonical token split across spell-out SSML nodes cannot retain a bookmark.");
    string ssml = arguments[2] as string ?? string.Empty;
    Require(ssml.Contains("aps_", StringComparison.Ordinal),
      "Spanning spell-out token produced no canonical ownership bookmark.");

    var document = System.Xml.Linq.XDocument.Parse(ssml);
    System.Xml.Linq.XElement mark = document
      .Descendants()
      .Single(element =>
        element.Name.LocalName == "mark" &&
        string.Equals(
          element.Attribute("name")?.Value,
          "aps_0",
          StringComparison.Ordinal));
    System.Xml.Linq.XElement sayAs = document
      .Descendants()
      .Single(element => element.Name.LocalName == "say-as");
    Require(mark.Parent == sayAs.Parent,
      "Spell-out ownership mark is not outside the say-as element.");
    System.Xml.Linq.XElement? nextElement = mark
      .NodesAfterSelf()
      .OfType<System.Xml.Linq.XElement>()
      .FirstOrDefault();
    Require(ReferenceEquals(nextElement, sayAs),
      "Spell-out ownership mark is not immediately before say-as.");
    Require(!sayAs.Descendants().Any(element => element.Name.LocalName == "mark"),
      "Spell-out say-as still contains a nested ownership mark.");
  }

  private static void TestTransformedSsmlRetainsCanonicalBookmarks()
  {
    const string text = "jsonl done.";
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      0,
      Array.Empty<string>(),
      PronunciationRuleSet.Parse("jsonl=jay son ell"));
    markup = markup with
    {
      Words = new[]
      {
        new SpeechMarkupWord(0, "jsonl", 0, 5),
        new SpeechMarkupWord(1, "done", 6, 4),
        new SpeechMarkupWord(2, ".", 10, 1)
      }
    };

    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "TryBuildBookmarkedSsml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Windows.Media bookmark builder is missing.");
    object?[] arguments = { markup, "en-US", null };
    bool built = Convert.ToBoolean(method.Invoke(null, arguments));
    Require(built,
      "A configured spoken-text transform caused canonical bookmark construction to fail.");
    string ssml = arguments[2] as string ?? string.Empty;
    foreach (int wordIndex in new[] { 0, 1, 2 })
    {
      Require(ssml.Contains($"aps_{wordIndex}", StringComparison.Ordinal),
        $"Canonical word {wordIndex} has no ownership bookmark after SSML transformation.");
    }

    var document = System.Xml.Linq.XDocument.Parse(ssml);
    System.Xml.Linq.XElement substitution = document
      .Descendants()
      .Single(element => element.Name.LocalName == "sub");
    System.Xml.Linq.XElement transformedMark = document
      .Descendants()
      .Single(element =>
        element.Name.LocalName == "mark" &&
        string.Equals(
          element.Attribute("name")?.Value,
          "aps_0",
          StringComparison.Ordinal));
    Require(transformedMark.Parent == substitution.Parent,
      "Transformed-word ownership mark is nested inside text-only SSML sub.");
    System.Xml.Linq.XElement? nextElement = transformedMark
      .NodesAfterSelf()
      .OfType<System.Xml.Linq.XElement>()
      .FirstOrDefault();
    Require(ReferenceEquals(nextElement, substitution),
      "Transformed-word ownership mark is not immediately before its SSML sub.");
    Require(!substitution.Descendants().Any(element => element.Name.LocalName == "mark"),
      "Text-only SSML sub still contains a nested ownership mark.");
  }

  private static void TestBrowserHasFragmentWrapperPath()
  {
    string source = ReadSource("TranscriptView.cs");
    Require(source.Contains("speech-fragments", StringComparison.Ordinal),
      "TranscriptView does not receive the canonical fragment inventory.");
    Require(source.Contains("frag-", StringComparison.Ordinal),
      "TranscriptView does not create stable frag-N DOM identities.");
    Require(source.Contains("createRange", StringComparison.Ordinal),
      "Fragment wrapping does not preserve whitespace through a DOM Range.");
  }

  private static void TestFragmentIdsAssignedByMonitor()
  {
    string source = ReadSource("JsonlSessionMonitor.cs");
    Require(source.Contains("nextFragmentId", StringComparison.Ordinal),
      "JsonlSessionMonitor has no conversation-global fragment counter.");
    Require(source.Contains("FragmentId = nextFragmentId++", StringComparison.Ordinal),
      "Speech fragments are not assigned monotonically increasing FragmentIds.");
  }

  private static string ReadSource(string fileName)
  {
    foreach (string start in new[]
    {
      Directory.GetCurrentDirectory(),
      AppContext.BaseDirectory
    })
    {
      DirectoryInfo? directory = new DirectoryInfo(start);
      for (int depth = 0; depth < 12 && directory is not null; ++depth)
      {
        string candidate = Path.Combine(
          directory.FullName,
          "AgentPanelSpeaker",
          fileName);
        if (File.Exists(candidate))
        {
          return File.ReadAllText(candidate);
        }
        directory = directory.Parent;
      }
    }
    throw new FileNotFoundException(
      $"Could not locate AgentPanelSpeaker/{fileName} from the test process.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
