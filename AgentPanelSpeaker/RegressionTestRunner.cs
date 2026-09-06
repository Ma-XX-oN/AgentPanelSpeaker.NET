using Markdig;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs the built-in regression suite used by the <c>--test</c> command-line
/// flag and by CI.  These tests exercise production code paths rather than
/// parallel test-only reimplementations.
/// </summary>
internal static class RegressionTestRunner
{
  private static readonly Encoding Utf8NoBom =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
  private const string UnicodeProbe = "UTF-8 probe — café 東京 😀";

  private sealed record TestCase(string Suite, string Name, Action Body);

  public static int Run(string? requestedSuite = null)
  {
    TestCase[] allTests = BuildTests();
    TestCase[] tests = string.IsNullOrWhiteSpace(requestedSuite)
      ? allTests
      : allTests.Where(test => string.Equals(
          test.Suite,
          requestedSuite,
          StringComparison.OrdinalIgnoreCase)).ToArray();

    var output = new List<string>();
    if (tests.Length == 0)
    {
      Write(output, $"Unknown regression suite: {requestedSuite}");
      Write(output, "Available suites: " + string.Join(
        ", ",
        allTests.Select(test => test.Suite).Distinct(StringComparer.OrdinalIgnoreCase)));
      WriteReport(output);
      return 2;
    }

    int failures = 0;
    Write(output, $"AgentPanelSpeaker regression suite: {tests.Length} tests");
    Write(output, $"Runtime: {Environment.Version}");
    if (!string.IsNullOrWhiteSpace(requestedSuite))
    {
      Write(output, $"Suite: {requestedSuite}");
    }
    Write(output, string.Empty);

    foreach (TestCase test in tests)
    {
      try
      {
        test.Body();
        Write(output, $"PASS  {test.Suite}/{test.Name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Write(output, $"FAIL  {test.Suite}/{test.Name}");
        Write(output, $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Write(output, string.Empty);
    Write(output, failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} regression tests passed."
      : $"FAIL: {failures}/{tests.Length} regression tests failed.");
    WriteReport(output);
    return failures == 0 ? 0 : 1;
  }

  private static TestCase[] BuildTests()
  {
    return new[]
    {
      new TestCase("input", "utf8", TestJsonlTailReaderUtf8),
      new TestCase("input", "partial-line", TestJsonlTailReaderPartialLine),
      new TestCase("input", "crlf", TestJsonlTailReaderCrLf),
      new TestCase("input", "truncate", TestJsonlTailReaderTruncate),
      new TestCase("bridge", "unicode-first-and-reused-worker", TestCoreBridgeUnicode),
      new TestCase("bridge", "repeated-requests", TestCoreBridgeRepeatedRequests),
      new TestCase("bridge", "claude-contract", TestCoreBridgeClaudeContract),
      new TestCase("bridge", "codex-contract", TestCoreBridgeCodexContract),
      new TestCase("markdown", "unicode-and-horizontal-rule", TestMarkdownUnicode),
      new TestCase("markdown", "malformed-json-rejected", TestMarkdownMalformedJson),
      new TestCase("presentation", "dom-structure", TestPresentationDom),
      new TestCase("presentation", "source-identities-unique", TestPresentationIdentityUniqueness),
      new TestCase("virtualization", "grouped-thoughts-atomic", TestVirtualizationAtomicity),
      new TestCase("virtualization", "identity-lookup", TestVirtualizationIdentityLookup),
      new TestCase("virtualization", "full-window-preserves-html", TestVirtualizationFullWindow),
      new TestCase("search", "literal-case-and-whole-word", TestSearchLiteral),
      new TestCase("search", "record-boundary", TestSearchRecordBoundary),
      new TestCase("search", "regex-block-boundaries", TestSearchRegexBoundaries),
      new TestCase("speech", "sentence-splitting", TestSentenceSplitting),
      new TestCase("speech", "sentence-structural-pause", TestSentencePause),
      new TestCase("pronunciation", "parse-and-normalize", TestPronunciationParsing),
      new TestCase("pronunciation", "matching-precedence", TestPronunciationMatching),
      new TestCase("settings", "fenced-code-normalization", TestFencedCodeTypes),
      new TestCase("settings", "spelled-word-normalization", TestSpelledWords),
      new TestCase("settings", "hotkey-round-trip", TestHotkeys),
      new TestCase("settings", "hotkey-collision-normalization", TestHotkeyCollisions),
      new TestCase("packaging", "bundled-core-runtime-present", TestBundledRuntime),
      new TestCase("regression", "no-em-dash-mojibake", TestNoMojibake),
      new TestCase("regression", "no-stdin-bom", TestNoBridgeBom),
      new TestCase("regression", "reasoning-disclosure-ownership", TestReasoningDisclosureOwnership)
    };
  }

  private static void TestJsonlTailReaderUtf8()
  {
    string path = CreateTemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      var reader = new JsonlTailReader(path);
      string record = CreateClaudeUserRecord("tail-unicode", UnicodeProbe);
      File.AppendAllText(path, record + Environment.NewLine, Utf8NoBom);
      IReadOnlyList<string> lines = reader.ReadAvailableLines();
      Require(lines.Count == 1, $"Expected one appended JSONL record, got {lines.Count}.");
      Require(lines[0] == record, "JSONL tail reader changed UTF-8 record text.");
      Require(!lines[0].Contains("â€”", StringComparison.Ordinal),
        "JSONL tail reader produced em-dash mojibake.");
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestJsonlTailReaderPartialLine()
  {
    string path = CreateTemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      var reader = new JsonlTailReader(path);
      string record = CreateClaudeUserRecord("partial", UnicodeProbe);
      int split = record.Length / 2;
      File.AppendAllText(path, record[..split], Utf8NoBom);
      Require(reader.ReadAvailableLines().Count == 0,
        "Partial JSONL record was emitted before newline completion.");
      File.AppendAllText(path, record[split..] + "\n", Utf8NoBom);
      IReadOnlyList<string> lines = reader.ReadAvailableLines();
      Require(lines.Count == 1 && lines[0] == record,
        "Completed partial JSONL record was not reconstructed exactly.");
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestJsonlTailReaderCrLf()
  {
    string path = CreateTemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      var reader = new JsonlTailReader(path);
      string record = CreateClaudeUserRecord("crlf", "CRLF — café");
      File.AppendAllText(path, record + "\r\n", Utf8NoBom);
      IReadOnlyList<string> lines = reader.ReadAvailableLines();
      Require(lines.Count == 1 && lines[0] == record,
        "CRLF terminator was not removed correctly.");
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestJsonlTailReaderTruncate()
  {
    string path = CreateTemporaryPath();
    try
    {
      File.WriteAllText(path, "seed\n", Utf8NoBom);
      var reader = new JsonlTailReader(path);
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      Require(reader.ReadAvailableLines().Count == 0,
        "Truncation should reset the tail reader without emitting content.");
      string record = CreateClaudeUserRecord("after-truncate", "after truncate");
      File.AppendAllText(path, record + "\n", Utf8NoBom);
      IReadOnlyList<string> lines = reader.ReadAvailableLines();
      Require(lines.Count == 1 && lines[0] == record,
        "Tail reader failed to resume after truncation.");
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestCoreBridgeUnicode()
  {
    string first = CreateClaudeUserRecord("core-unicode-1", UnicodeProbe);
    string secondText = "Second request — déjà vu Ελληνικά 🧪";
    string second = CreateClaudeUserRecord("core-unicode-2", secondText);
    using var client = new AIConversationCoreClient();
    AIConversationProjection firstProjection = client.Project(AgentSource.Claude, new[] { first });
    RequireProjectionText(firstProjection, UnicodeProbe);
    RequireCoreContract(firstProjection);
    AIConversationProjection secondProjection = client.Project(AgentSource.Claude, new[] { second });
    RequireProjectionText(secondProjection, secondText);
    RequireCoreContract(secondProjection);
  }

  private static void TestCoreBridgeRepeatedRequests()
  {
    using var client = new AIConversationCoreClient();
    for (int index = 0; index < 25; ++index)
    {
      string text = $"request {index:D2} — café 東京 🧪";
      AIConversationProjection projection = client.Project(
        AgentSource.Claude,
        new[] { CreateClaudeUserRecord($"repeat-{index}", text) });
      RequireProjectionText(projection, text);
      RequireCoreContract(projection);
    }
  }

  private static void TestCoreBridgeClaudeContract()
  {
    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(
      AgentSource.Claude,
      new[] { CreateClaudeUserRecord("contract", "contract check") });
    RequireCoreContract(projection);
    Require(projection.Events.Length != 0, "Claude projection returned no canonical events.");
  }

  private static void TestCoreBridgeCodexContract()
  {
    const string userText = "arbitrary context\nsome heading\nCodex request — café 東京";
    string record = JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-06T00:00:00Z",
      payload = new
      {
        type = "user_message",
        message = userText
      }
    });
    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(AgentSource.Codex, new[] { record });
    RequireCoreContract(projection);
    RequireProjectionText(projection, userText);
  }

  private static void TestMarkdownUnicode()
  {
    string path = CreateClaudeFixture();
    try
    {
      string markdown = TranscriptMarkdownFormatter.Format(path, AgentSource.Claude);
      Require(markdown.Contains(UnicodeProbe, StringComparison.Ordinal),
        "Canonical Markdown lost the Unicode probe.");
      Require(!markdown.Contains("â€”", StringComparison.Ordinal),
        "Canonical Markdown contains em-dash mojibake.");
      Require(markdown.Contains("---", StringComparison.Ordinal),
        "Markdown horizontal-rule source text was lost.");
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestMarkdownMalformedJson()
  {
    string path = CreateTemporaryPath();
    try
    {
      File.WriteAllText(path, "{ not json }\n", Utf8NoBom);
      RequireThrows<JsonException>(() =>
        TranscriptMarkdownFormatter.Format(path, AgentSource.Claude));
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static void TestPresentationDom()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    string html = result.Html;
    Require(html.Contains(UnicodeProbe, StringComparison.Ordinal),
      "Presentation HTML lost the Unicode probe.");
    Require(!html.Contains("â€”", StringComparison.Ordinal),
      "Presentation HTML contains em-dash mojibake.");
    Require(html.Contains("<summary>Having 2 thoughts</summary>", StringComparison.Ordinal),
      "Grouped reasoning disclosure is missing or has the wrong count.");
    Require(html.Contains("<hr", StringComparison.OrdinalIgnoreCase),
      "Markdown horizontal rule did not render as HTML <hr>.");
  }

  private static void TestPresentationIdentityUniqueness()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    Require(CountOccurrences(result.Html, "data-source-id=\"thought-one\"") == 1,
      "First thought source identity was missing or duplicated.");
    Require(CountOccurrences(result.Html, "data-source-id=\"thought-two\"") == 1,
      "Second thought source identity was missing or duplicated.");
    Require(CountOccurrences(result.Html, "data-source-id=\"regression-user\"") == 1,
      "User source identity was missing or duplicated.");
    Require(CountOccurrences(result.Html, "data-source-id=\"regression-final\"") == 1,
      "Final response source identity was missing or duplicated.");
  }

  private static void TestVirtualizationAtomicity()
  {
    TranscriptVirtualDocument document = BuildVirtualFixture();
    Require(document.TryGetIndex(2, "thought-one", out int firstIndex),
      "First thought has no virtual-document identity.");
    Require(document.TryGetIndex(3, "thought-two", out int secondIndex),
      "Second thought has no virtual-document identity.");
    Require(firstIndex == secondIndex,
      $"Grouped thoughts were split across virtual units {firstIndex} and {secondIndex}.");
  }

  private static void TestVirtualizationIdentityLookup()
  {
    TranscriptVirtualDocument document = BuildVirtualFixture();
    Require(document.TryGetIndex(1, "regression-user", out _), "User identity was not indexed.");
    Require(document.TryGetIndex(2, "thought-one", out _), "First thought identity was not indexed.");
    Require(document.TryGetIndex(3, "thought-two", out _), "Second thought identity was not indexed.");
    Require(document.TryGetIndex(4, "regression-final", out _), "Final response identity was not indexed.");
    Require(!document.TryGetIndex(999, "missing", out _), "Unknown identity incorrectly resolved.");
  }

  private static void TestVirtualizationFullWindow()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(result.Html);
    TranscriptWindow window = document.CreateFullWindow();
    Require(window.Html == result.Html,
      "Full virtual window changed serialized presentation HTML.");
  }

  private static void TestSearchLiteral()
  {
    string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"a\"></span>" +
      "<p>Alpha alphabet ALPHA café</p>";
    TranscriptSearchIndex index = TranscriptSearchIndex.Build(
      html,
      Array.Empty<TranscriptNodeIdentity>(),
      CancellationToken.None);
    IReadOnlyList<TranscriptSearchMatch> insensitive = index.SearchAsync(
      new TranscriptSearchRequest(1, "alpha", false, true, false, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(insensitive.Count == 2,
      $"Expected two whole-word case-insensitive matches, got {insensitive.Count}.");
    IReadOnlyList<TranscriptSearchMatch> sensitive = index.SearchAsync(
      new TranscriptSearchRequest(2, "Alpha", true, true, false, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(sensitive.Count == 1, "Case-sensitive search returned the wrong count.");
  }

  private static void TestSearchRecordBoundary()
  {
    string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"a\"></span><p>cross</p>" +
      "<span class=\"record-anchor\" data-jsonl-record=\"2\" data-source-id=\"b\"></span><p>boundary</p>";
    TranscriptSearchIndex index = TranscriptSearchIndex.Build(
      html,
      Array.Empty<TranscriptNodeIdentity>(),
      CancellationToken.None);
    IReadOnlyList<TranscriptSearchMatch> matches = index.SearchAsync(
      new TranscriptSearchRequest(1, "cross boundary", false, false, false, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(matches.Count == 0, "Search crossed a canonical record boundary.");
  }

  private static void TestSearchRegexBoundaries()
  {
    string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"a\"></span>" +
      "<p>first block</p><p>second block</p>";
    TranscriptSearchIndex index = TranscriptSearchIndex.Build(
      html,
      Array.Empty<TranscriptNodeIdentity>(),
      CancellationToken.None);
    IReadOnlyList<TranscriptSearchMatch> starts = index.SearchAsync(
      new TranscriptSearchRequest(1, "^second", false, false, true, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(starts.Count == 1, "Regex ^ did not match the start of the second block.");
    IReadOnlyList<TranscriptSearchMatch> noCross = index.SearchAsync(
      new TranscriptSearchRequest(2, "first.*second", false, false, true, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(noCross.Count == 0, "Regex dot unexpectedly crossed a block newline.");
  }

  private static void TestSentenceSplitting()
  {
    IReadOnlyList<SentenceSegment> parts = SentenceSegmenter.Split(
      "One. Two? Three! \"Four.\" Five",
      pauseAfterLast: false);
    Require(parts.Select(part => part.Text).SequenceEqual(new[]
    {
      "One.", "Two?", "Three!", "\"Four.\"", "Five"
    }), "Sentence segmentation changed expected boundaries.");
  }

  private static void TestSentencePause()
  {
    Require(SentenceSegmenter.Split("   ", true).Count == 0,
      "Whitespace-only speech created a sentence.");
    IReadOnlyList<SentenceSegment> parts = SentenceSegmenter.Split("One. Two.", true);
    Require(parts.Count == 2, "Expected two sentences.");
    Require(!parts[0].PauseAfter && parts[1].PauseAfter,
      "Structural pause was not assigned only to the final sentence.");
  }

  private static void TestPronunciationParsing()
  {
    PronunciationRuleSet set = PronunciationRuleSet.Parse(
      "git=hard g\nClaude/i=ipa:klɔːd\ngit=soft g\nbad-line\nempty=");
    Require(set.Rules.Count == 2, "Duplicate pronunciation rule was not replaced.");
    Require(set.Errors.Count == 2, "Invalid pronunciation lines were not reported.");
    Require(set.NormalizedText.Contains("git=soft g", StringComparison.Ordinal),
      "Latest duplicate pronunciation did not win.");
    Require(set.NormalizedText.Contains("Claude/i=ipa:klɔːd", StringComparison.Ordinal),
      "IPA pronunciation was not normalized correctly.");
  }

  private static void TestPronunciationMatching()
  {
    PronunciationRuleSet set = PronunciationRuleSet.Parse(
      "cat/i=case-insensitive\ncat=exact\ncatalog=long");
    PronunciationMatch? exact = set.FindNext("A cat and catalog.", 0);
    Require(exact is not null && exact.Rule.Value == "exact",
      "Exact-case pronunciation did not beat equal-length /i match.");
    PronunciationMatch? second = set.FindNext(
      "A cat and catalog.",
      exact!.Match.Index + exact.Match.Length);
    Require(second is not null && second.Rule.Token == "catalog",
      "Whole-token pronunciation matching failed for later token.");
  }

  private static void TestFencedCodeTypes()
  {
    FencedCodeTypeSet set = FencedCodeTypeSet.Parse(" C++, python, PYTHON, text , ");
    Require(set.NormalizedCsv == "c++, python, text",
      "Fenced-code CSV normalization changed.");
    Require(set.Contains("PYTHON") && !set.Contains("rust"),
      "Fenced-code membership is incorrect.");
    Require(FencedCodeTypeSet.Parse("*").Contains("anything"),
      "Wildcard fenced-code setting is not honored.");
  }

  private static void TestSpelledWords()
  {
    SpelledWordSet set = SpelledWordSet.Parse(" IDE\r\napi\nAPI\n GPU ");
    Require(set.OrderedWords.SequenceEqual(new[] { "IDE", "api", "GPU" }),
      "Spelled-word normalization changed.");
    Require(set.Contains("Api"), "Spelled-word lookup should be case-insensitive.");
  }

  private static void TestHotkeys()
  {
    foreach ((HotkeyAction action, string value) in HotkeySettings.Default.Entries())
    {
      Keys parsed = HotkeySettings.ParseKey(value);
      Require(parsed != Keys.None, $"Default hotkey {action} could not be parsed.");
      Require(HotkeySettings.FormatKey(parsed) == value,
        $"Default hotkey {action} failed parse/format round-trip.");
      Require(HotkeySettings.Default.GetAction(parsed) == action,
        $"Default hotkey {action} resolved to the wrong action.");
    }
  }

  private static void TestHotkeyCollisions()
  {
    HotkeySettings settings = HotkeySettings.Default with
    {
      PreviousSpeaker = "K",
      PreviousNode = "K",
      PreviousSentence = "?"
    };
    HotkeySettings normalized = settings.Normalize();
    Keys[] keys = normalized.Entries()
      .Select(entry => HotkeySettings.ParseKey(entry.Value))
      .ToArray();
    Require(keys.All(key => key != Keys.None),
      "Hotkey normalization left an invalid key.");
    Require(keys.Distinct().Count() == keys.Length,
      "Hotkey normalization left duplicate keys.");
  }

  private static void TestBundledRuntime()
  {
    string tools = Path.Combine(AppContext.BaseDirectory, "tools");
    string worker = Path.Combine(tools, "AIConversationCore-worker.mjs");
    string marker = Path.Combine(tools, "AIConversationCore-runtime", "CORE_COMMIT");
    string package = Path.Combine(tools, "AIConversationCore-runtime", "package.json");
    Require(File.Exists(worker), $"Bundled worker missing: {worker}");
    Require(File.Exists(marker), $"Bundled CORE_COMMIT missing: {marker}");
    Require(File.Exists(package), $"Bundled package metadata missing: {package}");
    Require(File.ReadAllText(marker).Trim() == AIConversationCoreClient.ExpectedCoreCommit,
      "Bundled AIConversationCore commit marker does not match the client pin.");
  }

  private static void TestNoMojibake()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    Require(!result.Html.Contains("â€”", StringComparison.Ordinal),
      "Historical em-dash mojibake regression returned.");
    Require(result.Html.Contains("—", StringComparison.Ordinal),
      "Expected em dash is missing from presentation output.");
  }

  private static void TestNoBridgeBom()
  {
    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = client.Project(
      AgentSource.Claude,
      new[] { CreateClaudeUserRecord("bom", "first request") });
    RequireProjectionText(projection, "first request");
  }

  private static void TestReasoningDisclosureOwnership()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    int detailsStart = result.Html.IndexOf("<details", StringComparison.OrdinalIgnoreCase);
    int detailsEnd = result.Html.IndexOf("</details>", StringComparison.OrdinalIgnoreCase);
    int firstThought = result.Html.IndexOf(UnicodeProbe, StringComparison.Ordinal);
    int secondThought = result.Html.IndexOf("Second thought — naïve façade", StringComparison.Ordinal);
    int finalResponse = result.Html.IndexOf("Before horizontal rule", StringComparison.Ordinal);
    Require(detailsStart >= 0 && detailsEnd > detailsStart,
      "Reasoning disclosure is incomplete.");
    Require(firstThought > detailsStart && firstThought < detailsEnd,
      "First thought escaped its reasoning disclosure.");
    Require(secondThought > detailsStart && secondThought < detailsEnd,
      "Second thought escaped its reasoning disclosure.");
    Require(finalResponse > detailsEnd,
      "Final response was incorrectly captured inside reasoning disclosure.");
  }

  private static TranscriptPresentationDomResult BuildPresentationFixture()
  {
    string path = CreateClaudeFixture();
    try
    {
      var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
      return TranscriptPresentationDomFormatter.Format(path, AgentSource.Claude, pipeline);
    }
    finally { DeleteTemporaryFile(path); }
  }

  private static TranscriptVirtualDocument BuildVirtualFixture()
  {
    TranscriptPresentationDomResult result = BuildPresentationFixture();
    return TranscriptVirtualDocument.Build(result.Html);
  }

  private static string CreateClaudeFixture()
  {
    string path = CreateTemporaryPath();
    string[] records =
    {
      CreateClaudeUserRecord("regression-user", "Run the regression fixture."),
      CreateClaudeThinkingRecord("thought-one", UnicodeProbe),
      CreateClaudeThinkingRecord("thought-two", "Second thought — naïve façade"),
      CreateClaudeAssistantRecord(
        "regression-final",
        "Before horizontal rule\n\n---\n\nAfter horizontal rule — résumé")
    };
    File.WriteAllLines(path, records, Utf8NoBom);
    return path;
  }

  private static string CreateClaudeUserRecord(string uuid, string text)
  {
    return JsonSerializer.Serialize(new
    {
      type = "user",
      isSidechain = false,
      timestamp = "2026-09-06T00:00:00.000Z",
      uuid,
      message = new
      {
        role = "user",
        content = new[] { new { type = "text", text } }
      }
    });
  }

  private static string CreateClaudeAssistantRecord(string uuid, string text)
  {
    return JsonSerializer.Serialize(new
    {
      type = "assistant",
      isSidechain = false,
      timestamp = "2026-09-06T00:00:03.000Z",
      uuid,
      message = new
      {
        model = "claude-regression",
        role = "assistant",
        content = new[] { new { type = "text", text } }
      }
    });
  }

  private static string CreateClaudeThinkingRecord(string uuid, string thinking)
  {
    return JsonSerializer.Serialize(new
    {
      type = "assistant",
      isSidechain = false,
      timestamp = "2026-09-06T00:00:01.000Z",
      uuid,
      message = new
      {
        model = "claude-regression",
        role = "assistant",
        content = new[] { new { type = "thinking", thinking } }
      }
    });
  }

  private static void RequireProjectionText(
    AIConversationProjection projection,
    string expected)
  {
    bool found = projection.Units.Any(unit =>
      unit.Block.ValueKind == JsonValueKind.Object &&
      unit.Block.TryGetProperty("text", out JsonElement textElement) &&
      textElement.ValueKind == JsonValueKind.String &&
      textElement.GetString() == expected);
    Require(found, $"Core projection did not round-trip expected text: {expected}");
  }

  private static void RequireCoreContract(AIConversationProjection projection)
  {
    Require(projection.SchemaVersion == 2,
      $"Unexpected projection schema {projection.SchemaVersion}.");
    Require(projection.Presentation is not null,
      "Core projection omitted the presentation contract.");
    Require(projection.Presentation!.SchemaVersion == 2,
      $"Unexpected presentation schema {projection.Presentation.SchemaVersion}.");
    Require(projection.Presentation.SplitPolicy == "presentation-tree",
      $"Unexpected presentation split policy {projection.Presentation.SplitPolicy}.");
  }

  private static string CreateTemporaryPath()
  {
    return Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-regression-{Guid.NewGuid():N}.jsonl");
  }

  private static void DeleteTemporaryFile(string path)
  {
    try { File.Delete(path); }
    catch (IOException) { }
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

  private static void RequireThrows<TException>(Action action)
    where TException : Exception
  {
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
  }

  private static void WriteReport(IReadOnlyCollection<string> output)
  {
    string reportPath = Path.Combine(
      Environment.CurrentDirectory,
      "AgentPanelSpeaker-test-results.txt");
    File.WriteAllLines(reportPath, output, Utf8NoBom);
  }

  private static void Write(ICollection<string> output, string line)
  {
    output.Add(line);
    Console.WriteLine(line);
  }
}
