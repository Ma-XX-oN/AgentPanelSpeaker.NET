using Markdig;
using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs issue #24 acceptance tests through the complete production mapping
/// chain. These tests must not inject browser node scopes or stable word maps
/// by hand because playback highlighting depends on those production outputs.
/// </summary>
internal static class Issue24ProductionPathRegressionTestRunner
{
  /// <summary>
  /// Runs the unsplit production-path acceptance tests.
  /// </summary>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("speech-ordinals/production-path-nested-list-table",
        TestNestedListAndTableThroughProductionPayload),
      ("speech-ordinals/production-path-secondary-structural-identity",
        TestSecondaryStructuralIdentityThroughProductionPayload),
      ("speech-ordinals/production-path-user-context-node-id-parity",
        TestUserContextNodeIdParity)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #24 production-path acceptance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #24 production-path " +
        "acceptance tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #24 production-path " +
        "acceptance tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Starts with a real Codex JSONL fixture, runs Core presentation, virtual
  /// document construction, search/stable-word mapping, the exact production
  /// virtual-window payload builder, the actual WebView2 DOM consumer,
  /// and finally the real playback JavaScript. No intermediate browser scope
  /// assignment is performed by the test.
  /// </summary>
  private static void TestNestedListAndTableThroughProductionPayload()
  {
    const string response = """
Like this:

1. Outer item
   1. *Nested numbered item with a table inside it*

      | Column | Value | Style |
      | --- | --- | --- |
      | Alpha | 1 | **bold** |
      | Beta | 2 | `code` |
      | Gamma | 3 | *italic* |

   2. Another nested numbered item
   3. One more for good measure
2. Second outer item
   1. Nested item after the table
   2. Nested item with ~~strikethrough~~
""";

    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue24-e2e-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = WriteProductionFixture(root, response);
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
      TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
        presentation.Units);
      TranscriptWindow window = document.CreateFullWindow();

      TranscriptNodeIdentity responseIdentity = identities.First(item =>
        item.Segments.Contains("1. Outer item", StringComparer.Ordinal));
      Require(responseIdentity.Segments.Contains(
          "1. *Nested numbered item with a table inside it*",
          StringComparer.Ordinal),
        "Production speech identity omitted the nested numbered-list fragment.");
      Require(responseIdentity.Segments.Contains(
          "| Column | Value | Style |",
          StringComparer.Ordinal),
        "Production speech identity omitted the nested table-header fragment.");

      string replaceScript = BuildProductionReplaceScript(
        window,
        identities,
        searchIndex);
      RunPlaybackProbe(
        replaceScript,
        responseIdentity.NodeId,
        "1. *Nested numbered item with a table inside it*",
        "Nested",
        3,
        "| Column | Value | Style |",
        "Column",
        1,
        "Codex JSONL production path");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Reproduces the installed-session node-ID namespace divergence.  The
  /// monitor always indexes Codex user context so speech navigation IDs remain
  /// stable even when that context is muted.  Transcript identity rebuilding
  /// must use the same projection or later rendered nodes receive smaller IDs.
  /// </summary>
  private static void TestUserContextNodeIdParity()
  {
    const string nestedFragment =
      "1. *Nested numbered item with a table inside it*";
    const string response = """
Like this:

1. Outer item
   1. *Nested numbered item with a table inside it*

      | Column | Value | Style |
      | --- | --- | --- |
      | Alpha | 1 | **bold** |
""";

    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue24-user-context-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = WriteUserContextProductionFixture(root, response);
      LocatedSession session = SessionLocator.FromPath(path, AgentSource.Codex);
      using var monitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: true,
        includeRolledBackTurns: false,
        includeUserContext: true);
      SpeechFragment speechFragment = history.Fragments.First(fragment =>
        string.Equals(fragment.Text, nestedFragment, StringComparison.Ordinal));

      IReadOnlyList<TranscriptNodeIdentity> identities =
        TranscriptNodeIdentityMap.Build(path, AgentSource.Codex);
      TranscriptNodeIdentity displayIdentity = identities.First(identity =>
        identity.Segments.Contains(nestedFragment, StringComparer.Ordinal));

      Require(displayIdentity.NodeId == speechFragment.NodeId,
        "User-context production path assigned different node IDs: " +
        $"speech={speechFragment.NodeId}, display={displayIdentity.NodeId}.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Exercises the dependent failure seam as one path: an atomic virtual unit
  /// owns two canonical record identities, the second identity is the nested
  /// list/table speech node, production payload construction must preserve that
  /// identity and its stable word map, and WebView playback must then resolve
  /// and highlight the nested content. The test never injects those scopes.
  /// </summary>
  private static void TestSecondaryStructuralIdentityThroughProductionPayload()
  {
    const string html = """
<details>
  <summary>Having 2 thoughts</summary>
  <span class="record-anchor" data-jsonl-record="1" data-source-id="one"></span>
  <p>Primary thought.</p>
  <span class="record-anchor" data-jsonl-record="2" data-source-id="two"></span>
  <ol>
    <li data-list-ordinal="1">
      <span class="speech-ordinal-map" aria-hidden="true" style="display:none">1. </span>
      <em>Nested numbered item with a table inside it</em>
      <table>
        <thead><tr><th>Column</th><th>Value</th><th>Style</th></tr></thead>
        <tbody><tr><td>Alpha</td><td>1</td><td>bold</td></tr></tbody>
      </table>
    </li>
  </ol>
</details>
""";

    TranscriptNodeIdentity[] identities =
    {
      new(201, 1, new[] { "Primary thought." }),
      new(202, 2, new[]
      {
        "1. *Nested numbered item with a table inside it*",
        "| Column | Value | Style |"
      })
    };
    TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
      html,
      identities,
      CancellationToken.None);
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
    Require(document.Records.Count == 1,
      "Acceptance fixture did not create one atomic structural unit.");
    Require(document.Records[0].Identities.Count == 2,
      "Acceptance fixture did not retain both canonical identities.");

    string replaceScript = BuildProductionReplaceScript(
      document.CreateFullWindow(),
      identities,
      searchIndex);

    RunPlaybackProbe(
      replaceScript,
      202,
      "1. *Nested numbered item with a table inside it*",
      "Nested",
      3,
      "| Column | Value | Style |",
      "Column",
      1,
      "secondary structural identity production path");
  }

  /// <summary>
  /// Uses the exact production payload builder. The view is allowed to finish
  /// WebView initialization before disposal so its asynchronous constructor
  /// work cannot leak into later regression tests.
  /// </summary>
  private static string BuildProductionReplaceScript(
    TranscriptWindow window,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    TranscriptSearchIndex searchIndex)
  {
    using var host = new Form
    {
      Width = 320,
      Height = 240,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    using var productionView = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(productionView);
    _ = host.Handle;
    _ = productionView.Handle;

    FieldInfo? webViewField = typeof(TranscriptView).GetField(
      "_webView",
      BindingFlags.NonPublic | BindingFlags.Instance);
    FieldInfo? identitiesField = typeof(TranscriptView).GetField(
      "_identities",
      BindingFlags.NonPublic | BindingFlags.Instance);
    FieldInfo? searchIndexField = typeof(TranscriptView).GetField(
      "_searchIndex",
      BindingFlags.NonPublic | BindingFlags.Instance);
    MethodInfo? buildReplaceWindow = typeof(TranscriptView).GetMethod(
      "BuildReplaceWindowScript",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Require(webViewField is not null &&
        identitiesField is not null &&
        searchIndexField is not null &&
        buildReplaceWindow is not null,
      "Production transcript payload members could not be located.");

    var internalWebView = (WebView2?)webViewField!.GetValue(productionView);
    Require(internalWebView is not null,
      "Production TranscriptView WebView could not be located.");
    Task ensure = internalWebView!.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, "production payload-builder WebView initialization");

    identitiesField!.SetValue(productionView, identities);
    searchIndexField!.SetValue(productionView, searchIndex);
    string script = buildReplaceWindow!.Invoke(productionView, new object?[]
    {
      window,
      false,
      null,
      null,
      null,
      null,
      null,
      null
    }) as string ?? string.Empty;
    Require(script.Length != 0,
      "Production BuildReplaceWindowScript returned no browser payload.");
    return script;
  }

  /// <summary>
  /// Executes the exact production replaceTranscriptWindow output in the actual
  /// transcript shell and then drives playback. Browser mapping is therefore
  /// entirely the output of the production payload; the test supplies none.
  /// </summary>
  private static void RunPlaybackProbe(
    string replaceScript,
    long nodeId,
    string nestedFragment,
    string nestedWord,
    int nestedWordIndex,
    string tableFragment,
    string tableWord,
    int tableWordIndex,
    string description)
  {
    using var host = new Form
    {
      Width = 800,
      Height = 600,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    using var webView = new WebView2 { Dock = DockStyle.Fill };
    host.Controls.Add(webView);
    _ = host.Handle;
    _ = webView.Handle;

    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, $"{description} WebView2 initialization");

    var fragmentRangeMisses = new List<JsonElement>();
    webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
    {
      try
      {
        using JsonDocument message = JsonDocument.Parse(
          eventArgs.WebMessageAsJson);
        JsonElement root = message.RootElement;
        if (root.TryGetProperty("type", out JsonElement type) &&
            string.Equals(
              type.GetString(),
              "fragment-range-miss",
              StringComparison.Ordinal))
        {
          fragmentRangeMisses.Add(root.Clone());
        }
      }
      catch (JsonException)
      {
      }
    };

    MethodInfo? shellMethod = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.NonPublic | BindingFlags.Static);
    Require(shellMethod is not null,
      "TranscriptView.BuildShellHtml() could not be located.");
    string shell = shellMethod!.Invoke(null, null) as string ?? string.Empty;
    Require(shell.Length != 0,
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
    webView.CoreWebView2.NavigateToString(shell);
    PumpUntilCompleted(navigated.Task, $"{description} transcript shell navigation");

    Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
    PumpUntilCompleted(replace, $"{description} replaceTranscriptDom payload");

    string nestedJson = JsonSerializer.Serialize(nestedFragment);
    string nestedWordJson = JsonSerializer.Serialize(nestedWord);
    string tableJson = JsonSerializer.Serialize(tableFragment);
    string tableWordJson = JsonSerializer.Serialize(tableWord);
    string probe = $$"""
(() => {
  setPlayback('speaking', {{nestedJson}}, {{nestedWordIndex}},
    {{nestedWordJson}}, {{nodeId}}, false);
  const nested = {
    fragment: currentFragmentText,
    activeWords: [...transcript.querySelectorAll('.word.active')]
      .map(word => word.textContent).join(''),
    listHighlights: transcript.querySelectorAll('.speech-list-item-active').length
  };

  setPlayback('speaking', {{tableJson}}, {{tableWordIndex}},
    {{tableWordJson}}, {{nodeId}}, false);
  const table = {
    fragment: currentFragmentText,
    activeWords: [...transcript.querySelectorAll('.word.active')]
      .map(word => word.textContent).join(''),
    listHighlights: transcript.querySelectorAll('.speech-list-item-active').length
  };

  return JSON.stringify({nested, table});
})()
""";
    Task<string> probeTask = webView.CoreWebView2.ExecuteScriptAsync(probe);
    PumpUntilCompleted(probeTask, $"{description} playback probe");
    string encoded = JsonSerializer.Deserialize<string>(probeTask.Result) ?? string.Empty;
    Require(encoded.Length != 0,
      $"{description} playback probe returned no result.");
    using JsonDocument result = JsonDocument.Parse(encoded);
    JsonElement nested = result.RootElement.GetProperty("nested");
    JsonElement table = result.RootElement.GetProperty("table");

    Require(string.Equals(
        nested.GetProperty("fragment").GetString(),
        nestedFragment,
        StringComparison.Ordinal),
      $"{description}: playback could not resolve the nested numbered-list fragment.");
    Require(string.Equals(
        nested.GetProperty("activeWords").GetString(),
        nestedWord,
        StringComparison.Ordinal),
      $"{description}: nested list body word was not highlighted.");
    Require(nested.GetProperty("listHighlights").GetInt32() == 0,
      $"{description}: ancestor list-item highlight remained during nested body speech.");

    Require(string.Equals(
        table.GetProperty("fragment").GetString(),
        tableFragment,
        StringComparison.Ordinal),
      $"{description}: playback could not resolve the nested table fragment.");
    Require(string.Equals(
        table.GetProperty("activeWords").GetString(),
        tableWord,
        StringComparison.Ordinal),
      $"{description}: nested table word was not highlighted.");
    Require(table.GetProperty("listHighlights").GetInt32() == 0,
      $"{description}: ancestor list-item highlight remained during nested table speech.");

    string missingFragment = "__issue24_diagnostic_missing_fragment__";
    string missingJson = JsonSerializer.Serialize(missingFragment);
    string missingWordJson = JsonSerializer.Serialize("missing");
    Task<string> diagnosticProbe = webView.CoreWebView2.ExecuteScriptAsync(
      $"setPlayback('speaking', {missingJson}, 0, {missingWordJson}, " +
      $"{nodeId}, false);");
    PumpUntilCompleted(
      diagnosticProbe,
      $"{description} unmatched-fragment diagnostic probe");
    DateTime diagnosticDeadline = DateTime.UtcNow.AddSeconds(5);
    while (fragmentRangeMisses.Count == 0 &&
           DateTime.UtcNow < diagnosticDeadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(fragmentRangeMisses.Count != 0,
      $"{description}: unmatched playback emitted no fragment-range diagnostic.");
    JsonElement diagnostic = fragmentRangeMisses[^1];
    Require(diagnostic.GetProperty("knownNode").GetBoolean(),
      $"{description}: diagnostic did not report the known node.");
    Require(diagnostic.GetProperty("mappingGeneration").GetInt32() > 0,
      $"{description}: diagnostic omitted mapping generation.");
    Require(diagnostic.GetProperty("storedRangeCount").GetInt32() > 0,
      $"{description}: diagnostic omitted the node's stored ranges.");
    Require(diagnostic.GetProperty("nodeWordCount").GetInt32() > 0,
      $"{description}: diagnostic omitted node-scoped words.");
    Require(!string.IsNullOrEmpty(
        diagnostic.GetProperty("displayKey").GetString()),
      $"{description}: diagnostic omitted requested display tokens.");
    Require(!string.IsNullOrEmpty(
        diagnostic.GetProperty("lexicalKey").GetString()),
      $"{description}: diagnostic omitted requested lexical tokens.");
  }

  private static string WriteUserContextProductionFixture(
    string root,
    string response)
  {
    string path = Path.Combine(root, "rollout-issue24-user-context.jsonl");
    const string contextPrefix =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/example.jsonl\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n";
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T16:59:58.000Z",
        payload = new
        {
          type = "user_message",
          message = contextPrefix + "Warm-up request"
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T16:59:59.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Warm-up response."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T17:00:00.000Z",
        payload = new
        {
          type = "user_message",
          message = contextPrefix +
            "Give me a nested numbered list with a table"
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

  private static string WriteProductionFixture(string root, string response)
  {
    string path = Path.Combine(root, "rollout-issue24-production-path.jsonl");
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T17:00:00.000Z",
        payload = new
        {
          type = "user_message",
          message = "Give me a nested numbered list with a table"
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

  private static void PumpUntilCompleted(Task task, string operation)
  {
    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(task.IsCompleted,
      $"Timed out waiting for {operation}.");
    task.GetAwaiter().GetResult();
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
