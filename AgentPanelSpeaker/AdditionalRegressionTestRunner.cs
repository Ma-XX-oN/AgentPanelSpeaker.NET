using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs additional regression checks for session detection, worker protocol
/// failures, regex edge cases, and speech-to-rendered-word identity mapping.
/// </summary>
internal static class AdditionalRegressionTestRunner
{
  private static readonly Encoding Utf8NoBom =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("input/many-records", TestManyRecords),
      ("session/detect-claude", TestDetectClaude),
      ("session/detect-codex", TestDetectCodex),
      ("session/requested-provider-mismatch", TestProviderMismatch),
      ("bridge/wrong-core-commit", TestWrongCoreCommit),
      ("bridge/malformed-worker-response", TestMalformedWorkerResponse),
      ("bridge/missing-presentation-contract", TestMissingPresentationContract),
      ("search/invalid-regex", TestInvalidRegex),
      ("search/zero-length-block-anchors", TestZeroLengthRegexAnchors),
      ("mapping/repeated-text-stays-record-scoped", TestRepeatedTextMapping),
      ("mapping/word-id-round-trip", TestWordIdRoundTrip),
      ("mapping/unknown-word-id-rejected", TestUnknownWordId)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Additional regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} additional regression tests passed."
      : $"FAIL: {failures}/{tests.Length} additional regression tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestManyRecords()
  {
    string path = TemporaryPath("jsonl");
    try
    {
      File.WriteAllText(path, string.Empty, Utf8NoBom);
      var reader = new JsonlTailReader(path);
      string[] expected = Enumerable.Range(1, 1000)
        .Select(index => JsonSerializer.Serialize(new
        {
          index,
          text = $"record {index} — café 東京 😀"
        }))
        .ToArray();
      File.AppendAllText(path, string.Join("\n", expected) + "\n", Utf8NoBom);
      IReadOnlyList<string> actual = reader.ReadAvailableLines();
      Require(actual.SequenceEqual(expected),
        "Tail reader changed ordering or content in a 1000-record append.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestDetectClaude()
  {
    string path = TemporaryPath("jsonl");
    try
    {
      File.WriteAllText(path, ClaudeUserRecord("claude-detect", "hello") + "\n", Utf8NoBom);
      Require(SessionLocator.DetectSource(path) == AgentSource.Claude,
        "Claude session was not detected from record structure.");
      LocatedSession located = SessionLocator.FromPath(path, AgentSource.Auto);
      Require(located.Source == AgentSource.Claude,
        "Auto-selected Claude session resolved to the wrong provider.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestDetectCodex()
  {
    string path = TemporaryPath("jsonl");
    try
    {
      File.WriteAllText(path, CodexUserRecord("hello from Codex") + "\n", Utf8NoBom);
      Require(SessionLocator.DetectSource(path) == AgentSource.Codex,
        "Codex session was not detected from record structure.");
      LocatedSession located = SessionLocator.FromPath(path, AgentSource.Auto);
      Require(located.Source == AgentSource.Codex,
        "Auto-selected Codex session resolved to the wrong provider.");
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestProviderMismatch()
  {
    string path = TemporaryPath("jsonl");
    try
    {
      File.WriteAllText(path, ClaudeUserRecord("mismatch", "hello") + "\n", Utf8NoBom);
      RequireThrows<InvalidDataException>(() =>
        SessionLocator.FromPath(path, AgentSource.Codex));
    }
    finally
    {
      Delete(path);
    }
  }

  private static void TestWrongCoreCommit()
  {
    string worker = TemporaryWorker("""
      import readline from 'node:readline';
      const rl = readline.createInterface({ input: process.stdin });
      for await (const line of rl) {
        JSON.parse(line);
        console.log(JSON.stringify({ ok: true, core_commit: 'wrong-commit' }));
      }
      """);
    try
    {
      using var client = new AIConversationCoreClient(worker);
      RequireThrows<InvalidOperationException>(() => client.Project(
        AgentSource.Claude,
        new[] { ClaudeUserRecord("wrong-commit", "hello") }));
    }
    finally
    {
      Delete(worker);
    }
  }

  private static void TestMalformedWorkerResponse()
  {
    string worker = TemporaryWorker($$"""
      import readline from 'node:readline';
      const rl = readline.createInterface({ input: process.stdin });
      for await (const line of rl) {
        const request = JSON.parse(line);
        if (request.operation === 'ping') {
          console.log(JSON.stringify({ ok: true, core_commit: '{{AIConversationCoreClient.ExpectedCoreCommit}}' }));
        } else {
          console.log('not-json');
        }
      }
      """);
    try
    {
      using var client = new AIConversationCoreClient(worker);
      RequireThrows<JsonException>(() => client.Project(
        AgentSource.Claude,
        new[] { ClaudeUserRecord("malformed-response", "hello") }));
    }
    finally
    {
      Delete(worker);
    }
  }

  private static void TestMissingPresentationContract()
  {
    string worker = TemporaryWorker($$"""
      import readline from 'node:readline';
      const rl = readline.createInterface({ input: process.stdin });
      for await (const line of rl) {
        const request = JSON.parse(line);
        if (request.operation === 'ping') {
          console.log(JSON.stringify({ ok: true, core_commit: '{{AIConversationCoreClient.ExpectedCoreCommit}}' }));
        } else {
          console.log(JSON.stringify({
            ok: true,
            core_commit: '{{AIConversationCoreClient.ExpectedCoreCommit}}',
            projection: {
              schema_version: 2,
              events: [],
              turns: [],
              units: [],
              presentation: null,
              markdown: ''
            }
          }));
        }
      }
      """);
    try
    {
      using var client = new AIConversationCoreClient(worker);
      RequireThrows<InvalidOperationException>(() => client.Project(
        AgentSource.Claude,
        new[] { ClaudeUserRecord("missing-presentation", "hello") }));
    }
    finally
    {
      Delete(worker);
    }
  }

  private static void TestInvalidRegex()
  {
    TranscriptSearchIndex index = BuildSearchIndex(
      "<p>alpha beta</p>",
      Array.Empty<TranscriptNodeIdentity>());
    RequireThrows<ArgumentException>(() => index.SearchAsync(
      new TranscriptSearchRequest(1, "(", false, false, true, false),
      CancellationToken.None).GetAwaiter().GetResult());
  }

  private static void TestZeroLengthRegexAnchors()
  {
    TranscriptSearchIndex index = BuildSearchIndex(
      "<p>first block</p><p>second block</p>",
      Array.Empty<TranscriptNodeIdentity>());
    IReadOnlyList<TranscriptSearchMatch> starts = index.SearchAsync(
      new TranscriptSearchRequest(1, "^", true, false, true, false),
      CancellationToken.None).GetAwaiter().GetResult();
    IReadOnlyList<TranscriptSearchMatch> ends = index.SearchAsync(
      new TranscriptSearchRequest(2, "$", true, false, true, false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(starts.Count == 2, $"Expected two block-start matches, got {starts.Count}.");
    Require(ends.Count == 2, $"Expected two block-end matches, got {ends.Count}.");
  }

  private static void TestRepeatedTextMapping()
  {
    const string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"a\"></span><p>same text</p>" +
      "<span class=\"record-anchor\" data-jsonl-record=\"2\" data-source-id=\"b\"></span><p>same text</p>";
    TranscriptNodeIdentity[] identities =
    {
      new(101, 1, "a", new[] { "same text" }),
      new(202, 2, "b", new[] { "same text" })
    };
    TranscriptSearchIndex index = BuildSearchIndex(html, identities);
    Require(index.TryResolveVoiceOrigin(101, 0, out int firstRecord, out string firstSource, out _),
      "First repeated speech text did not resolve.");
    Require(index.TryResolveVoiceOrigin(202, 0, out int secondRecord, out string secondSource, out _),
      "Second repeated speech text did not resolve.");
    Require(firstRecord == 1 && firstSource == "a",
      "First repeated text mapped to the wrong record.");
    Require(secondRecord == 2 && secondSource == "b",
      "Second repeated text mapped to the wrong record.");
  }

  private static void TestWordIdRoundTrip()
  {
    const string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"a\"></span><p>alpha beta</p>";
    TranscriptNodeIdentity[] identities =
    {
      new(77, 1, "a", new[] { "alpha beta" })
    };
    TranscriptSearchIndex index = BuildSearchIndex(html, identities);
    TranscriptVirtualDocument virtualDocument = TranscriptVirtualDocument.Build(html);
    TranscriptRecordWordMap map = index.GetWordMaps(virtualDocument.Records).Single();
    Require(map.Words.Count == 2, "Expected two rendered word identities.");
    for (int wordIndex = 0; wordIndex < map.Words.Count; ++wordIndex)
    {
      TranscriptWordMap word = map.Words[wordIndex];
      Require(index.TryResolveSpeechWord(word.WordId, out long nodeId, out int nodeWordIndex),
        $"Word id {word.WordId} did not resolve to speech coordinates.");
      Require(nodeId == 77 && nodeWordIndex == wordIndex,
        $"Word id {word.WordId} resolved to the wrong speech coordinate.");
    }
  }

  private static void TestUnknownWordId()
  {
    TranscriptSearchIndex index = BuildSearchIndex(
      "<p>alpha</p>",
      Array.Empty<TranscriptNodeIdentity>());
    Require(!index.TryResolveSpeechWord(long.MaxValue, out long nodeId, out int nodeWordIndex),
      "Unknown word id unexpectedly resolved.");
    Require(nodeId == 0 && nodeWordIndex == -1,
      "Unknown word id returned non-sentinel speech coordinates.");
  }

  private static TranscriptSearchIndex BuildSearchIndex(
    string bodyHtml,
    IReadOnlyList<TranscriptNodeIdentity> identities)
  {
    string html = bodyHtml.Contains("record-anchor", StringComparison.Ordinal)
      ? bodyHtml
      : "<span class=\"record-anchor\" data-jsonl-record=\"1\" data-source-id=\"test\"></span>" + bodyHtml;
    return TranscriptSearchIndex.Build(html, identities, CancellationToken.None);
  }

  private static string TemporaryWorker(string source)
  {
    string path = TemporaryPath("mjs");
    File.WriteAllText(path, source, Utf8NoBom);
    return path;
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

  private static string CodexUserRecord(string text)
  {
    return JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-06T00:00:00Z",
      payload = new
      {
        type = "user_message",
        message = text
      }
    });
  }

  private static string TemporaryPath(string extension)
  {
    return Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-additional-{Guid.NewGuid():N}.{extension}");
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
