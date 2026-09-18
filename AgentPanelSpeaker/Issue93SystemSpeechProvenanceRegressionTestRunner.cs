using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #93 System.Speech SSML provenance.
/// </summary>
internal static class Issue93SystemSpeechProvenanceRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("system-speech-provenance/spelling-scope-stops-before-hyphenated-tail",
        TestSpellingScopeStopsBeforeHyphenatedTail),
      ("system-speech-provenance/hyphenated-sub-alias-maps-source",
        TestHyphenatedSubAliasMapsSource),
      ("system-speech-provenance/provider-boundary-payload-logged",
        TestProviderBoundaryPayloadLogged),
      ("system-speech-provenance/spell-out-many-events-one-word",
        TestSpellOutManyEventsOneWord),
      ("system-speech-provenance/provider-range-multiple-words",
        TestProviderRangeCanOwnMultipleWords),
      ("system-speech-provenance/ordinary-event-remains-exact",
        TestOrdinaryEventRemainsExact),
      ("system-speech-provenance/no-fixed-offset-or-string-sequence",
        TestNoFixedOffsetOrStringSequenceMapper),
      ("system-speech-provenance/no-text-completeness-rejection",
        TestNoTextCompletenessRejection)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #93 System.Speech provenance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #93 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #93 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestSpellingScopeStopsBeforeHyphenatedTail()
  {
    SpeechMarkup markup = BuildTrackedMarkup(
      "scripts/AI-transcript.py",
      new[] { "AI" });
    string ssml = BuildSsmlDocument(markup);
    const string spelling =
      "<say-as interpret-as=\"characters\">AI</say-as>";
    int spellingStart = ssml.IndexOf(spelling, StringComparison.Ordinal);
    Require(spellingStart >= 0,
      "Configured AI spelling does not use isolated characters semantics.");
    Require(!ssml.Contains(
        "interpret-as=\"spell-out\"",
        StringComparison.Ordinal),
      "System.Speech still uses spell-out semantics that can spill into the hyphenated tail.");
    int close = ssml.IndexOf(
      "</say-as>",
      spellingStart,
      StringComparison.Ordinal);
    int tail = ssml.IndexOf(
      "<sub alias=\"transcript\">-transcript</sub>.py",
      spellingStart,
      StringComparison.Ordinal);
    Require(close >= 0 && tail > close,
      "The hyphenated transcript tail is not outside the explicit AI spelling element.");
  }

  private static void TestHyphenatedSubAliasMapsSource()
  {
    const string text = "scripts/AI-transcript.py";
    SpeechMarkup markup = BuildTrackedMarkup(text, new[] { "AI" });
    string ssml = BuildSsmlDocument(markup);
    const string expected =
      "<say-as interpret-as=\"characters\">AI</say-as>" +
      "<sub alias=\"transcript\">-transcript</sub>.py";
    int sequenceStart = ssml.IndexOf(expected, StringComparison.Ordinal);
    Require(sequenceStart >= 0,
      "System.Speech does not use the proven case-5 sub-alias form for AI-transcript.py.");

    int contentAliasStart = markup.SsmlContent.IndexOf(
      "alias=\"transcript\"",
      StringComparison.Ordinal);
    Require(contentAliasStart >= 0,
      "System.Speech SSML content has no transcript alias.");
    contentAliasStart += "alias=\"".Length;
    int sourceStart = text.IndexOf("-transcript", StringComparison.Ordinal);
    Require(sourceStart >= 0, "Test source has no -transcript span.");
    SpeechMarkupProvenanceSpan? aliasProvenance = markup.SsmlProvenance?
      .SingleOrDefault(span =>
        span.SsmlCharacterStart == contentAliasStart &&
        span.SsmlCharacterLength == "transcript".Length);
    SpeechMarkupProvenanceSpan provenAlias = aliasProvenance ??
      throw new InvalidOperationException(
        "The sub alias value has no explicit provenance.");
    Require(
      provenAlias.SourceCharacterStart == sourceStart &&
      provenAlias.SourceCharacterLength == "-transcript".Length,
      "The sub alias provenance does not own the complete original -transcript span.");

    int subStart = ssml.IndexOf("<sub alias=", StringComparison.Ordinal);
    int aliasStart = subStart < 0
      ? -1
      : ssml.IndexOf("transcript", subStart, StringComparison.Ordinal);
    Require(aliasStart >= 0, "Wrapped SSML has no transcript alias value.");
    SpeechWordBoundary transcript = Map(
      markup,
      ssml,
      aliasStart,
      "transcript".Length,
      "transcript",
      TimeSpan.FromMilliseconds(200));
    int expectedWord = markup.Words!
      .Single(word => word.Text == "AI-transcript")
      .WordIndex;
    Require(
      transcript.WordIndex == expectedWord && transcript.WordCount == 1,
      "Provider progress over the sub alias did not map exactly to AI-transcript.");
    Require(transcript.Exact,
      "Provider-native sub-alias timing is not marked exact.");

    int extensionStart = ssml.IndexOf(
      ".py",
      sequenceStart,
      StringComparison.Ordinal);
    Require(extensionStart >= 0, "Wrapped SSML has no .py extension.");
    SpeechWordBoundary dot = Map(
      markup,
      ssml,
      extensionStart,
      1,
      ".",
      TimeSpan.FromMilliseconds(300));
    SpeechWordBoundary py = Map(
      markup,
      ssml,
      extensionStart + 1,
      2,
      "py",
      TimeSpan.FromMilliseconds(350));
    Require(
      dot.WordIndex == markup.Words!.Single(word => word.Text == ".").WordIndex &&
      py.WordIndex == markup.Words!.Single(word => word.Text == "py").WordIndex,
      "The .py extension lost its separate canonical ownership.");

    SpeechMarkup explicitTail = BuildTrackedMarkup(
      "AI-IDE",
      new[] { "AI", "IDE" });
    Require(!explicitTail.SsmlContent.Contains(
        "<sub alias=\"IDE\">",
        StringComparison.Ordinal),
      "The automatic hyphen-tail alias swallowed an explicitly configured spelling transform.");
    int firstSayAs = explicitTail.SsmlContent.IndexOf(
      "<say-as interpret-as=\"characters\">AI</say-as>",
      StringComparison.Ordinal);
    int secondSayAs = explicitTail.SsmlContent.IndexOf(
      "<say-as interpret-as=\"characters\">IDE</say-as>",
      StringComparison.Ordinal);
    Require(firstSayAs >= 0 && secondSayAs > firstSayAs,
      "Explicit spelling on the hyphenated tail did not retain precedence.");
  }

  private static void TestProviderBoundaryPayloadLogged()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    int start = source.IndexOf(
      "  private static PcmWaveData RenderSystemSpeech(",
      StringComparison.Ordinal);
    Require(start >= 0, "RenderSystemSpeech is missing.");
    int end = source.IndexOf(
      "  private static bool TryMapSystemSpeechProgress(",
      start,
      StringComparison.Ordinal);
    Require(end > start, "Could not isolate RenderSystemSpeech diagnostics.");
    string method = source[start..end];

    Require(method.Contains(
        "DiagnosticLog.Write(\"speech.system_speech_markup_ssml_content\"",
        StringComparison.Ordinal),
      "System.Speech does not log the exact markup SSML content.");
    Require(method.Contains(
        "DiagnosticLog.Write(\"speech.system_speech_ssml_document\"",
        StringComparison.Ordinal),
      "System.Speech does not log the fully wrapped SSML document.");
    int submitted = method.IndexOf(
      "DiagnosticLog.Write(\"speech.system_speech_ssml_submitted\"",
      StringComparison.Ordinal);
    int speak = method.IndexOf(
      "synthesizer.SpeakSsml(ssml);",
      StringComparison.Ordinal);
    Require(submitted >= 0 && speak > submitted,
      "The final System.Speech payload is not logged immediately before SpeakSsml.");
    string boundary = method[submitted..speak];
    Require(boundary.Contains("ssml", StringComparison.Ordinal) &&
            boundary.Contains("ComputeUtf8Sha256(ssml)", StringComparison.Ordinal),
      "The provider-boundary diagnostic does not include the exact SSML and its SHA-256.");
    Require(source.Contains(
        "SHA256.HashData(Encoding.UTF8.GetBytes(value))",
        StringComparison.Ordinal),
      "System.Speech payload fingerprint is not SHA-256 over UTF-8 bytes.");
  }

  private static void TestSpellOutManyEventsOneWord()
  {
    SpeechMarkup markup = BuildTrackedMarkup(
      "For local code/files, I can cite exact file locations like " +
        "scripts/AI-transcript.py.",
      new[] { "AI" });
    string ssml = BuildSsmlDocument(markup);
    int aiStart = ssml.IndexOf("AI</say-as>", StringComparison.Ordinal);
    Require(aiStart >= 0, "Generated SSML has no spell-out AI text.");

    SpeechWordBoundary a = Map(
      markup,
      ssml,
      aiStart,
      1,
      "A",
      TimeSpan.FromMilliseconds(100));
    SpeechWordBoundary i = Map(
      markup,
      ssml,
      aiStart + 1,
      1,
      "I",
      TimeSpan.FromMilliseconds(150));

    int expected = markup.Words!
      .Single(word => word.Text == "AI-transcript")
      .WordIndex;
    Require(a.WordIndex == expected && i.WordIndex == expected,
      "Separate A/I progress events did not retain AI-transcript ownership.");
    Require(a.WordCount == 1 && i.WordCount == 1,
      "Spell-out events unexpectedly widened canonical ownership.");
    Require(a.Exact && i.Exact,
      "Provider-native spell-out timing is not marked exact.");
  }

  private static void TestProviderRangeCanOwnMultipleWords()
  {
    SpeechMarkup markup = BuildTrackedMarkup(
      "alpha beta",
      Array.Empty<string>());
    string ssml = BuildSsmlDocument(markup);
    int start = ssml.IndexOf("alpha beta", StringComparison.Ordinal);
    Require(start >= 0, "Generated SSML has no alpha beta text.");

    SpeechWordBoundary boundary = Map(
      markup,
      ssml,
      start,
      "alpha beta".Length,
      "alpha beta",
      TimeSpan.FromMilliseconds(200));

    Require(boundary.WordIndex == 0,
      "Provider range across two words did not start at alpha.");
    Require(boundary.WordCount == 2,
      "One provider range did not retain both canonical word owners.");
    Require(boundary.Exact,
      "Provider-native multi-word timing is not marked exact.");
  }

  private static void TestOrdinaryEventRemainsExact()
  {
    SpeechMarkup markup = BuildTrackedMarkup("alpha beta", Array.Empty<string>());
    string ssml = BuildSsmlDocument(markup);
    int start = ssml.IndexOf("beta", StringComparison.Ordinal);
    Require(start >= 0, "Generated SSML has no beta text.");
    SpeechWordBoundary boundary = Map(
      markup,
      ssml,
      start,
      4,
      "beta",
      TimeSpan.FromMilliseconds(250));
    Require(boundary.WordIndex == 1 && boundary.WordCount == 1,
      "Ordinary provider progress did not map to the exact second word.");
    Require(boundary.Exact,
      "Ordinary provider-native timing is not exact.");
  }

  private static void TestNoFixedOffsetOrStringSequenceMapper()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains("synthesisCharacterOffset", StringComparison.Ordinal),
      "System.Speech still assumes one fixed synthesis/plain-text offset.");
    Require(!source.Contains("FindSequentialTokenIndex", StringComparison.Ordinal),
      "System.Speech still reverse-matches provider text sequentially.");
    Require(source.Contains("SsmlProvenance", StringComparison.Ordinal),
      "System.Speech does not consume explicit SSML provenance.");
  }

  private static void TestNoTextCompletenessRejection()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains(
        "system_speech_incomplete_word_mapping",
        StringComparison.Ordinal),
      "System.Speech still rejects exact provider timing merely because " +
        "not every textual token produced a progress event.");
  }

  private static SpeechMarkup BuildTrackedMarkup(
    string text,
    IReadOnlyList<string> spelledWords)
  {
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      0,
      spelledWords,
      PronunciationRuleSet.Parse(string.Empty));
    MatchCollection matches = SpeechTokenization.Matches(text);
    SpeechMarkupWord[] words = matches
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
    return markup with { Words = words };
  }

  private static string BuildSsmlDocument(SpeechMarkup markup)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "BuildSsmlDocument",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("BuildSsmlDocument is missing.");
    return method.Invoke(null, new object[] { markup.SsmlContent, "en-US" })
      as string ?? throw new InvalidOperationException("SSML build failed.");
  }

  private static SpeechWordBoundary Map(
    SpeechMarkup markup,
    string ssml,
    int characterPosition,
    int characterCount,
    string spokenText,
    TimeSpan audioPosition)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "TryMapSystemSpeechProgress",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "System.Speech has no provenance-based progress mapper.");
    object?[] arguments =
    {
      markup,
      ssml,
      characterPosition,
      characterCount,
      spokenText,
      audioPosition,
      null
    };
    bool mapped = Convert.ToBoolean(method.Invoke(null, arguments));
    Require(mapped, $"Provider range {characterPosition}:{characterCount} did not map.");
    return arguments[6] as SpeechWordBoundary ??
      throw new InvalidOperationException("Mapped boundary was not returned.");
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
