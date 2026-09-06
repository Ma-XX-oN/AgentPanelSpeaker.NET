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
  private const string UnicodeProbe =
    "UTF-8 probe — café 東京 😀";

  /// <summary>
  /// Runs all built-in regression tests and returns a process exit code.
  /// </summary>
  /// <returns>Zero when every test passes; otherwise one.</returns>
  public static int Run()
  {
    var output = new List<string>();
    var tests = new (string Name, Action Body)[]
    {
      ("JSONL tail reader preserves UTF-8", TestJsonlTailReaderUtf8),
      ("AIConversationCore bridge round-trips Unicode", TestCoreBridgeUnicode),
      ("Canonical Markdown preserves Unicode", TestMarkdownUnicode),
      ("Canonical presentation DOM preserves structure", TestPresentationDom),
      ("Virtualization keeps grouped thoughts atomic", TestVirtualizationAtomicity)
    };

    int failures = 0;
    Write(output, $"AgentPanelSpeaker regression suite: {tests.Length} tests");
    Write(output, $"Runtime: {Environment.Version}");
    Write(output, string.Empty);

    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Write(output, $"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Write(output, $"FAIL  {name}");
        Write(output, $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Write(output, string.Empty);
    Write(
      output,
      failures == 0
        ? $"PASS: {tests.Length}/{tests.Length} regression tests passed."
        : $"FAIL: {failures}/{tests.Length} regression tests failed.");

    string reportPath = Path.Combine(
      Environment.CurrentDirectory,
      "AgentPanelSpeaker-test-results.txt");
    File.WriteAllLines(reportPath, output, Utf8NoBom);
    return failures == 0 ? 0 : 1;
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
    finally
    {
      DeleteTemporaryFile(path);
    }
  }

  private static void TestCoreBridgeUnicode()
  {
    string first = CreateClaudeUserRecord("core-unicode-1", UnicodeProbe);
    string secondText = "Second request — déjà vu Ελληνικά 🧪";
    string second = CreateClaudeUserRecord("core-unicode-2", secondText);

    using var client = new AIConversationCoreClient();
    AIConversationProjection firstProjection = client.Project(
      AgentSource.Claude,
      new[] { first });
    RequireProjectionText(firstProjection, UnicodeProbe);
    RequireCoreContract(firstProjection);

    // Use the same worker a second time.  This catches both first-write BOM
    // problems and persistent-stream encoding corruption.
    AIConversationProjection secondProjection = client.Project(
      AgentSource.Claude,
      new[] { second });
    RequireProjectionText(secondProjection, secondText);
    RequireCoreContract(secondProjection);
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
    finally
    {
      DeleteTemporaryFile(path);
    }
  }

  private static void TestPresentationDom()
  {
    string path = CreateClaudeFixture();
    try
    {
      var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
      TranscriptPresentationDomResult result = TranscriptPresentationDomFormatter.Format(
        path,
        AgentSource.Claude,
        pipeline);
      string html = result.Html;
      Require(html.Contains(UnicodeProbe, StringComparison.Ordinal),
        "Presentation HTML lost the Unicode probe.");
      Require(!html.Contains("â€”", StringComparison.Ordinal),
        "Presentation HTML contains em-dash mojibake.");
      Require(html.Contains("<summary>Having 2 thoughts</summary>", StringComparison.Ordinal),
        "Grouped reasoning disclosure is missing or has the wrong count.");
      Require(html.Contains("<hr", StringComparison.OrdinalIgnoreCase),
        "Markdown horizontal rule did not render as HTML <hr>.");
      Require(html.Contains("data-source-id=\"thought-one\"", StringComparison.Ordinal),
        "First thought source identity is missing from presentation HTML.");
      Require(html.Contains("data-source-id=\"thought-two\"", StringComparison.Ordinal),
        "Second thought source identity is missing from presentation HTML.");
    }
    finally
    {
      DeleteTemporaryFile(path);
    }
  }

  private static void TestVirtualizationAtomicity()
  {
    string path = CreateClaudeFixture();
    try
    {
      var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
      TranscriptPresentationDomResult result = TranscriptPresentationDomFormatter.Format(
        path,
        AgentSource.Claude,
        pipeline);
      TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(result.Html);
      Require(document.TryGetIndex(2, "thought-one", out int firstIndex),
        "First thought has no virtual-document identity.");
      Require(document.TryGetIndex(3, "thought-two", out int secondIndex),
        "Second thought has no virtual-document identity.");
      Require(firstIndex == secondIndex,
        $"Grouped thoughts were split across virtual units {firstIndex} and {secondIndex}.");
      TranscriptWindow window = document.CreateWindow(firstIndex);
      int detailsStart = window.Html.IndexOf("<details", StringComparison.OrdinalIgnoreCase);
      int detailsEnd = window.Html.IndexOf("</details>", StringComparison.OrdinalIgnoreCase);
      Require(detailsStart >= 0 && detailsEnd > detailsStart,
        "Virtualized window does not contain one complete reasoning disclosure.");
      int firstThought = window.Html.IndexOf(UnicodeProbe, StringComparison.Ordinal);
      int secondThought = window.Html.IndexOf("Second thought — naïve façade", StringComparison.Ordinal);
      Require(firstThought > detailsStart && firstThought < detailsEnd,
        "First thought escaped the reasoning disclosure.");
      Require(secondThought > detailsStart && secondThought < detailsEnd,
        "Second thought escaped the reasoning disclosure.");
    }
    finally
    {
      DeleteTemporaryFile(path);
    }
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
    Require(
      projection.Presentation.SplitPolicy == "presentation-tree",
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
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
      // The test result has already been determined.  Cleanup is best effort.
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private static void Write(ICollection<string> output, string line)
  {
    output.Add(line);
    Console.WriteLine(line);
  }
}
