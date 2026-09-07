using Markdig;
using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs focused regressions for preserving ordered-list ordinals through the
/// production AgentPanelSpeaker speech and transcript-mapping pipelines.
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
      ("speech-ordinals/final-tts-markup", TestFinalTtsMarkup),
      ("speech-ordinals/rendered-marker-mapping", TestRenderedMarkerMapping),
      ("speech-ordinals/whole-list-item-highlight", TestWholeListItemHighlight)
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
  /// Verifies the production rendered-token mapping can associate each spoken
  /// numbered-list fragment with the corresponding HTML list item.  Browser
  /// ordered-list markers are implicit and therefore absent from text nodes;
  /// the mapping layer must still resolve the speech node rather than leaving
  /// the previous transcript marker active.
  /// </summary>
  private static void TestRenderedMarkerMapping()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue24-map-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = WriteProductionFixture(
        root,
        "1. First item\n2. Second item\n3. Third item");
      var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
      TranscriptPresentationDomResult presentation =
        TranscriptPresentationDomFormatter.Format(
          path,
          AgentSource.Codex,
          pipeline);
      IReadOnlyList<TranscriptNodeIdentity> identities =
        TranscriptNodeIdentityMap.Build(path, AgentSource.Codex);
      TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
        presentation.Html,
        identities,
        CancellationToken.None);

      string[] expectedSegments =
      {
        "1. First item",
        "2. Second item",
        "3. Third item"
      };
      foreach (string expectedSegment in expectedSegments)
      {
        TranscriptNodeIdentity? identity = identities.FirstOrDefault(item =>
          item.Segments.Contains(expectedSegment, StringComparer.Ordinal));
        Require(identity is not null,
          $"No transcript identity contains speech segment '{expectedSegment}'.");
        Require(searchIndex.TryResolveVoiceOrigin(
            identity!.NodeId,
            0,
            out int recordNumber,
            out string sourceId,
            out int recordWordIndex),
          $"Rendered transcript could not map speech segment '{expectedSegment}'.");
        Require(recordNumber == identity.RecordNumber,
          $"Speech segment '{expectedSegment}' mapped to record {recordNumber}, " +
          $"expected {identity.RecordNumber}.");
        Require(string.Equals(sourceId, identity.SourceId, StringComparison.Ordinal),
          $"Speech segment '{expectedSegment}' mapped to source '{sourceId}', " +
          $"expected '{identity.SourceId}'.");
        Require(recordWordIndex >= 0,
          $"Speech segment '{expectedSegment}' mapped to an invalid word index.");
      }
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Exercises the actual WebView playback JavaScript.  While the hidden
  /// mapping token for an ordered-list ordinal is the active speech boundary,
  /// the containing list item, including nested descendants, must carry the
  /// visible highlight.  Once speech advances to body text, normal word
  /// highlighting resumes and the list-item highlight is removed.
  /// </summary>
  private static void TestWholeListItemHighlight()
  {
    ApplicationConfiguration.Initialize();
    using var host = new Form
    {
      Width = 640,
      Height = 480,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    using var webView = new WebView2 { Dock = DockStyle.Fill };
    host.Controls.Add(webView);
    _ = host.Handle;
    _ = webView.Handle;

    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, "WebView2 initialization");

    MethodInfo? shellMethod = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.NonPublic | BindingFlags.Static);
    Require(shellMethod is not null,
      "TranscriptView.BuildShellHtml() could not be located.");
    string? shell = shellMethod!.Invoke(null, null) as string;
    Require(!string.IsNullOrWhiteSpace(shell),
      "TranscriptView.BuildShellHtml() returned no HTML.");

    var navigated = new TaskCompletionSource<bool>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    webView.NavigationCompleted += (_, eventArgs) =>
    {
      if (eventArgs.IsSuccess)
      {
        navigated.TrySetResult(true);
      }
      else
      {
        navigated.TrySetException(new InvalidOperationException(
          $"WebView navigation failed: {eventArgs.WebErrorStatus}."));
      }
    };
    webView.CoreWebView2.NavigateToString(shell!);
    PumpUntilCompleted(navigated.Task, "transcript shell navigation");

    const string probe = """
(() => {
  transcript.innerHTML =
    '<span class="record-anchor" data-jsonl-record="1" data-source-id="ordinal-test"></span>' +
    '<ol><li id="parent-item">' +
    '<span class="speech-ordinal-map" aria-hidden="true" style="display:none">1. </span>' +
    'Parent item<ul><li id="nested-item">Nested child</li></ul></li></ol>';
  wrapWords();
  assignRecordScopes();
  assignNodeScopes([{
    NodeId: 1,
    RecordNumber: 1,
    SourceId: 'ordinal-test',
    Segments: ['1. Parent item']
  }]);

  setPlayback('speaking', '1. Parent item', 0, '1', 1, false);
  const parent = document.getElementById('parent-item');
  const nested = document.getElementById('nested-item');
  const ordinalState = {
    active: parent.classList.contains('speech-list-item-active'),
    containsNested: parent.contains(nested),
    background: getComputedStyle(parent).backgroundColor
  };

  setPlayback('speaking', '1. Parent item', 3, 'Parent', 1, false);
  const bodyState = {
    listActive: parent.classList.contains('speech-list-item-active'),
    activeWord: [...parent.querySelectorAll('.word.active')]
      .map(word => word.textContent).join('')
  };

  return JSON.stringify({ordinalState, bodyState});
})()
""";

    Task<string> probeTask = webView.CoreWebView2.ExecuteScriptAsync(probe);
    PumpUntilCompleted(probeTask, "list-item highlight probe");
    string? encoded = JsonSerializer.Deserialize<string>(probeTask.Result);
    Require(!string.IsNullOrWhiteSpace(encoded),
      "WebView list-item highlight probe returned no result.");
    using JsonDocument result = JsonDocument.Parse(encoded!);
    JsonElement ordinalState = result.RootElement.GetProperty("ordinalState");
    JsonElement bodyState = result.RootElement.GetProperty("bodyState");

    Require(ordinalState.GetProperty("active").GetBoolean(),
      "Speaking the ordinal did not highlight the containing list item.");
    Require(ordinalState.GetProperty("containsNested").GetBoolean(),
      "The highlighted list item did not contain its nested list content.");
    string background = ordinalState.GetProperty("background").GetString() ?? string.Empty;
    Require(background is not "rgba(0, 0, 0, 0)" and not "transparent" and not "",
      $"The active list item has no visible highlight background: '{background}'.");
    Require(!bodyState.GetProperty("listActive").GetBoolean(),
      "The whole-list-item highlight remained after speech advanced to body text.");
    Require(string.Equals(
        bodyState.GetProperty("activeWord").GetString(),
        "Parent",
        StringComparison.Ordinal),
      "Normal word highlighting did not resume after the ordinal.");
  }

  /// <summary>
  /// Pumps the Windows message queue until one WebView task completes.
  /// </summary>
  private static void PumpUntilCompleted(Task task, string operation)
  {
    DateTime deadline = DateTime.UtcNow.AddSeconds(20);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(task.IsCompleted,
      $"Timed out waiting for {operation}.");
    task.GetAwaiter().GetResult();
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
      string path = WriteProductionFixture(root, response);
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
  /// Writes the two-record Codex fixture used by the production-path tests.
  /// </summary>
  private static string WriteProductionFixture(string root, string response)
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
    return path;
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
