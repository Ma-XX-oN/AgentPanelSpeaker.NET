using Markdig;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs deterministic subsystem regressions that complement the core transcript
/// and presentation checks in <see cref="RegressionTestRunner"/>.
/// </summary>
internal static class ExtendedRegressionTestRunner
{
  private static readonly Encoding Utf8NoBom =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("input/large-unicode-record", TestLargeUnicodeRecord),
      ("bridge/missing-worker", TestMissingWorker),
      ("bridge/malformed-input", TestMalformedBridgeInput),
      ("bridge/disposed-client", TestDisposedBridge),
      ("markdown/empty-file", TestEmptyMarkdownFile),
      ("presentation/empty-file", TestEmptyPresentationFile),
      ("virtualization/no-anchor-fallback", TestVirtualizationWithoutAnchors),
      ("virtualization/window-boundaries", TestVirtualizationWindowBoundaries),
      ("search/unicode", TestUnicodeSearch),
      ("speech/markdown-cleanup", TestSpeechMarkdownCleanup),
      ("speech/fenced-code-lines", TestSpeechFencedCode),
      ("speech/context-blockquote", TestSpeechContext),
      ("speech/canonical-eligibility", TestCanonicalSpeechEligibility),
      ("playback/mailbox-overflow", TestPlaybackMailboxOverflow),
      ("playback/mailbox-wake-state", TestPlaybackMailboxWakeState),
      ("playback/mailbox-resize", TestPlaybackMailboxResize),
      ("settings/speech-profile-normalization", TestSpeechProfileNormalization),
      ("settings/audio-wake-normalization", TestAudioWakeNormalization),
      ("settings/transcript-normalization", TestTranscriptSettingsNormalization),
      ("settings/default-voice-selection", TestDefaultVoiceSelection)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Extended regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} extended regression tests passed."
      : $"FAIL: {failures}/{tests.Length} extended regression tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestLargeUnicodeRecord()
  {
    string path = TemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      var reader = new JsonlTailReader(path);
      string payload = string.Concat(Enumerable.Repeat("—café東京😀", 20000));
      string record = JsonSerializer.Serialize(new { text = payload });
      File.AppendAllText(path, record + "\n", Utf8NoBom);
      IReadOnlyList<string> lines = reader.ReadAvailableLines();
      Require(lines.Count == 1 && lines[0] == record,
        "Large UTF-8 JSONL record did not round-trip exactly.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestMissingWorker()
  {
    string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mjs");
    using var client = new AIConversationCoreClient(missing);
    RequireThrows<FileNotFoundException>(() => client.Project(
      AgentSource.Claude,
      new[] { ClaudeUserRecord("missing", "test") }));
  }

  private static void TestMalformedBridgeInput()
  {
    using var client = new AIConversationCoreClient();
    RequireThrows<JsonException>(() => client.Project(
      AgentSource.Claude,
      new[] { "{ definitely not JSON" }));
  }

  private static void TestDisposedBridge()
  {
    var client = new AIConversationCoreClient();
    client.Dispose();
    RequireThrows<ObjectDisposedException>(() => client.Project(
      AgentSource.Claude,
      new[] { ClaudeUserRecord("disposed", "test") }));
  }

  private static void TestEmptyMarkdownFile()
  {
    string path = TemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      string markdown = TranscriptMarkdownFormatter.Format(path, AgentSource.Claude);
      Require(markdown.Contains("records: 0", StringComparison.Ordinal),
        "Empty transcript did not render a zero-record session header.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestEmptyPresentationFile()
  {
    string path = TemporaryPath();
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      TranscriptPresentationDomResult result = TranscriptPresentationDomFormatter.Format(
        path,
        AgentSource.Claude,
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
      Require(result.Nodes.Count == 0 && result.Html.Length == 0,
        "Empty transcript produced presentation nodes or HTML.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestVirtualizationWithoutAnchors()
  {
    const string html = "<p>plain transcript</p>";
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
    Require(document.Count == 1, "Unanchored HTML should form one virtual record.");
    Require(document.CreateFullWindow().Html == html,
      "Unanchored HTML changed in the full virtual window.");
  }

  private static void TestVirtualizationWindowBoundaries()
  {
    var html = new StringBuilder();
    for (int index = 1; index <= 100; ++index)
    {
      html.Append("<span class=\"record-anchor\" data-jsonl-record=\"")
        .Append(index)
        .Append("\" data-source-id=\"id-")
        .Append(index)
        .Append("\"></span><p>record ")
        .Append(index)
        .Append("</p>");
    }
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html.ToString());
    Require(document.Count == 100, $"Expected 100 virtual records, got {document.Count}.");
    Require(document.TryGetIndex(1, "id-1", out int first) && first == 0,
      "First virtual identity resolved incorrectly.");
    Require(document.TryGetIndex(100, "id-100", out int last) && last == 99,
      "Last virtual identity resolved incorrectly.");
    TranscriptWindow firstWindow = document.CreateWindow(first);
    TranscriptWindow lastWindow = document.CreateWindow(last);
    Require(firstWindow.StartIndex == 0, "First virtual window does not begin at zero.");
    Require(lastWindow.EndIndex == 99, "Last virtual window does not end at final record.");
  }

  private static void TestUnicodeSearch()
  {
    const string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"u\"></span>" +
      "<p>café 東京 Ελληνικά 😀 — résumé</p>";
    TranscriptSearchIndex index = TranscriptSearchIndex.Build(
      html,
      Array.Empty<TranscriptNodeIdentity>(),
      CancellationToken.None);
    IReadOnlyList<TranscriptSearchMatch> matches = index.SearchAsync(
      new TranscriptSearchRequest(1, "東京", true, false, false, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(matches.Count == 1, "Unicode literal search did not find the expected match.");
  }

  private static void TestSpeechMarkdownCleanup()
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
      "# Heading\n\nA **bold** [link](https://example.com) and `a < b`.\n\n---\n\nFinal.");
    string[] text = parts.Select(part => part.Text).ToArray();
    Require(text.Contains("Heading"), "Heading was lost during speech cleanup.");
    Require(text.Contains("A bold link and a < b."),
      "Markdown prose cleanup changed spoken text unexpectedly.");
    Require(text.Contains("Final."), "Post-rule prose was lost during speech cleanup.");
    Require(!text.Any(value => value.Contains("example.com", StringComparison.Ordinal)),
      "Markdown link URL leaked into spoken text.");
  }

  private static void TestSpeechFencedCode()
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
      "```python\nprint('one')\n\nprint('two')\n```\n");
    SpeechTextPart[] code = parts
      .Where(part => part.Kind == SpeechFragmentKind.FencedCodeLine)
      .ToArray();
    Require(code.Length == 2, "Fenced code did not produce two non-empty spoken lines.");
    Require(code.All(part => part.FenceType == "python"),
      "Fenced-code language was not preserved.");
    Require(code[0].FenceLineIndex == 0 && code[1].FenceLineIndex == 1,
      "Fenced-code line indexes are incorrect.");
    Require(code.All(part => part.FenceLineCount == 2),
      "Fenced-code line count is incorrect.");
  }

  private static void TestSpeechContext()
  {
    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
      "> quoted context\n> second line\n\nmain line");
    Require(parts.Any(part =>
        part.Style == SpeechTextStyle.Context &&
        part.Text.Contains("quoted context", StringComparison.Ordinal)),
      "Blockquote was not classified as context speech.");
    Require(parts.Any(part =>
        part.Style == SpeechTextStyle.Main &&
        part.Text.Contains("main line", StringComparison.Ordinal)),
      "Main prose was not classified as main speech.");
  }

  private static void TestCanonicalSpeechEligibility()
  {
    JsonElement eligible = Json("""
      {"id":"eligible","speech":{"eligible":true},"relationships":{}}
      """);
    JsonElement hidden = Json("""
      {"id":"hidden","speech":{"eligible":false},"relationships":{}}
      """);
    JsonElement background = Json("""
      {"id":"background","speech":{"eligible":true,"background_work_identity":{"kind":"task_timestamp"}},"relationships":{"tool_call_id":"call-1"}}
      """);
    var projection = new AIConversationProjection(
      2,
      new[] { eligible, hidden, background },
      Array.Empty<CanonicalTurnProjection>(),
      Array.Empty<CanonicalUnitProjection>(),
      null,
      string.Empty);
    AIConversationProjection prepared = CanonicalSpeechProjection.Prepare(projection);
    Require(prepared.Events.Length == 2,
      "Non-speakable canonical event was not removed.");
    JsonElement preparedBackground = prepared.Events.Single(item =>
      item.GetProperty("id").GetString() == "background");
    JsonElement relationships = preparedBackground.GetProperty("relationships");
    Require(relationships.GetProperty("tool_call_id").ValueKind == JsonValueKind.Null,
      "Task-timestamp background identity retained tool-call correlation.");
  }

  private static void TestPlaybackMailboxOverflow()
  {
    var mailbox = new TranscriptPlaybackMailbox(2);
    mailbox.Publish(Position(1));
    mailbox.Publish(Position(2));
    mailbox.Publish(Position(3));
    Require(mailbox.GetWakeBatchCount() == 2,
      "Mailbox did not remain bounded at capacity.");
    Require(mailbox.TryTake(out TranscriptPlaybackPosition first) && first.NodeId == 2,
      "Mailbox did not discard the oldest item on overflow.");
    Require(mailbox.TryTake(out TranscriptPlaybackPosition second) && second.NodeId == 3,
      "Mailbox did not retain newest item order.");
  }

  private static void TestPlaybackMailboxWakeState()
  {
    var mailbox = new TranscriptPlaybackMailbox(4);
    Require(mailbox.Publish(Position(1)), "First publish did not request a wake-up.");
    Require(!mailbox.Publish(Position(2)), "Second publish incorrectly requested another wake-up.");
    Require(mailbox.TryTake(out _), "First mailbox item was unavailable.");
    Require(mailbox.CompleteWake(),
      "Completing a wake with retained work did not request another wake-up.");
    Require(mailbox.TryTake(out _), "Second mailbox item was unavailable.");
    Require(!mailbox.CompleteWake(),
      "Empty mailbox incorrectly requested another wake-up.");
    Require(mailbox.Publish(Position(3)),
      "Publish after completed wake did not request a fresh wake-up.");
  }

  private static void TestPlaybackMailboxResize()
  {
    var mailbox = new TranscriptPlaybackMailbox(4);
    for (int index = 1; index <= 4; ++index)
    {
      mailbox.Publish(Position(index));
    }
    mailbox.SetCapacity(2);
    Require(mailbox.GetWakeBatchCount() == 2,
      "Mailbox resize did not retain the requested capacity.");
    Require(mailbox.TryTake(out TranscriptPlaybackPosition first) && first.NodeId == 3,
      "Mailbox resize did not preserve newest retained values.");
    Require(mailbox.TryTake(out TranscriptPlaybackPosition second) && second.NodeId == 4,
      "Mailbox resize changed retained order.");
    mailbox.Clear();
    Require(mailbox.GetWakeBatchCount() == 0 && !mailbox.TryTake(out _),
      "Mailbox clear did not remove retained state.");
  }

  private static void TestSpeechProfileNormalization()
  {
    SpeechProfileSettings profile = new SpeechProfileSettings(
      "  Hazel  ", 99, -99) { Volume = 150 }.Normalize();
    Require(profile.VoiceName == "Hazel", "Speech voice name was not trimmed.");
    Require(profile.Rate == 10 && profile.Pitch == -10 && profile.Volume == 100,
      "Speech profile bounds are incorrect.");
    Require(new SpeechProfileSettings(" ", 0, 0).Normalize().VoiceName ==
      SpeechProfileSettings.NotSpoken,
      "Blank voice did not normalize to Not Spoken.");
  }

  private static void TestAudioWakeNormalization()
  {
    AudioWakeSettings settings = new AudioWakeSettings(
      true, -1, 99999, 101, 0, 99999, -1).Normalize();
    Require(settings.QuietDurationMilliseconds == 0,
      "Audio-wake quiet duration lower bound failed.");
    Require(settings.FrequencyHertz == 22000 && settings.ToneVolume == 100,
      "Audio-wake frequency/volume upper bounds failed.");
    Require(settings.PlayDurationMilliseconds == 10 &&
            settings.SettleDurationMilliseconds == 5000 &&
            settings.IpaExampleDelayMilliseconds == 0,
      "Audio-wake duration bounds failed.");
  }

  private static void TestTranscriptSettingsNormalization()
  {
    TranscriptSettings settings = TranscriptSettings.Default with
    {
      FadeMilliseconds = 99999,
      HighlightUpdateMilliseconds = 43,
      HighlightQueueCapacity = 99,
      LightHighlightArgb = 0,
      DarkHighlightArgb = 0
    };
    TranscriptSettings normalized = settings.Normalize();
    Require(normalized.FadeMilliseconds <= 500,
      "Transcript fade was not bounded to the supported 32-step range.");
    Require(normalized.HighlightUpdateMilliseconds == 40,
      "Highlight update interval upper bound failed.");
    Require(normalized.HighlightQueueCapacity == 16,
      "Highlight queue capacity upper bound failed.");
    Require(normalized.LightHighlightArgb == TranscriptSettings.Default.LightHighlightArgb &&
            normalized.DarkHighlightArgb == TranscriptSettings.Default.DarkHighlightArgb,
      "Transparent highlight colours did not fall back to defaults.");
  }

  private static void TestDefaultVoiceSelection()
  {
    UserSettings settings = UserSettings.CreateDefault(new[] { "Voice A", "Voice B", "Voice C" });
    Require(settings.Assistant.VoiceName == "Voice A",
      "Default assistant voice selection changed.");
    Require(settings.SubagentAssistant.VoiceName == "Voice B",
      "Default subagent voice selection changed.");
    Require(settings.User.VoiceName == "Voice C",
      "Default user voice selection changed.");
    UserSettings noVoices = UserSettings.CreateDefault(Array.Empty<string>());
    Require(noVoices.Assistant.VoiceName == SpeechProfileSettings.NotSpoken,
      "No-voice default did not select Not Spoken.");
  }

  private static TranscriptPlaybackPosition Position(long nodeId)
  {
    return new TranscriptPlaybackPosition(
      TranscriptPlaybackState.Speaking,
      $"node {nodeId}",
      0,
      "node",
      nodeId,
      0,
      4,
      nodeId);
  }

  private static JsonElement Json(string json)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
  }

  private static string ClaudeUserRecord(string uuid, string text)
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

  private static string TemporaryPath()
  {
    return Path.Combine(Path.GetTempPath(), $"AgentPanelSpeaker-extended-{Guid.NewGuid():N}.jsonl");
  }

  private static void Delete(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
    }
  }

  private static void RequireThrows<TException>(Action action)
    where TException : Exception
  {
    try
    {
      action();
    }
    catch (TException)
    {
      return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
