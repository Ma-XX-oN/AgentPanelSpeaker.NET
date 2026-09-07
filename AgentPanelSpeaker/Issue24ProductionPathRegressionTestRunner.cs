using Markdig;
using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs the issue #24 acceptance test through the complete production mapping
/// chain.  Unlike the focused component regressions, this test must not inject
/// browser node scopes or stable word mappings by hand.
/// </summary>
internal static class Issue24ProductionPathRegressionTestRunner
{
  /// <summary>
  /// Runs the unsplit JSONL-to-WebView playback/highlight acceptance test.
  /// </summary>
  public static int Run()
  {
    Console.WriteLine();
    Console.WriteLine("Issue #24 production-path acceptance suite: 1 test");
    try
    {
      TestNestedListAndTableThroughProductionPayload();
      Console.WriteLine("PASS  speech-ordinals/production-path-nested-list-table");
      Console.WriteLine();
      Console.WriteLine("PASS: 1/1 issue #24 production-path acceptance tests passed.");
      return 0;
    }
    catch (Exception exception)
    {
      Console.WriteLine("FAIL  speech-ordinals/production-path-nested-list-table");
      Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      Console.WriteLine();
      Console.WriteLine("FAIL: 1/1 issue #24 production-path acceptance tests failed.");
      return 1;
    }
  }

  /// <summary>
  /// Starts with a real Codex JSONL fixture, runs Core presentation, virtual
  /// document construction, search/stable-word mapping, the exact production
  /// replaceTranscriptDom payload builder, the actual WebView2 DOM consumer,
  /// and finally the real playback JavaScript.  No intermediate browser scope
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
        presentation.Html);
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

      string replaceScript;
      using (var productionView = new TranscriptView())
      {
        FieldInfo? identitiesField = typeof(TranscriptView).GetField(
          "_identities",
          BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo? searchIndexField = typeof(TranscriptView).GetField(
          "_searchIndex",
          BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo? buildReplaceDom = typeof(TranscriptView).GetMethod(
          "BuildReplaceDomScript",
          BindingFlags.NonPublic | BindingFlags.Instance);
        Require(identitiesField is not null &&
            searchIndexField is not null &&
            buildReplaceDom is not null,
          "Production transcript payload members could not be located.");

        identitiesField!.SetValue(productionView, identities);
        searchIndexField!.SetValue(productionView, searchIndex);
        replaceScript = buildReplaceDom!.Invoke(productionView, new object?[]
        {
          window,
          presentation.Nodes,
          false,
          null,
          null
        }) as string ?? string.Empty;
      }
      Require(replaceScript.Length != 0,
        "Production BuildReplaceDomScript returned no browser payload.");

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
      PumpUntilCompleted(ensure, "production-path WebView2 initialization");

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
      PumpUntilCompleted(navigated.Task, "production transcript shell navigation");

      Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
      PumpUntilCompleted(replace, "production replaceTranscriptDom payload");

      string nestedJson = JsonSerializer.Serialize(
        "1. *Nested numbered item with a table inside it*");
      string tableJson = JsonSerializer.Serialize("| Column | Value | Style |");
      string probe = $$"""
(() => {
  setPlayback('speaking', {{nestedJson}}, 3, 'Nested', {{responseIdentity.NodeId}}, false);
  const nested = {
    fragment: currentFragmentText,
    activeWords: [...transcript.querySelectorAll('.word.active')]
      .map(word => word.textContent).join(''),
    listHighlights: transcript.querySelectorAll('.speech-list-item-active').length
  };

  setPlayback('speaking', {{tableJson}}, 1, 'Column', {{responseIdentity.NodeId}}, false);
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
      PumpUntilCompleted(probeTask, "production nested-list playback probe");
      string encoded = JsonSerializer.Deserialize<string>(probeTask.Result) ?? string.Empty;
      Require(encoded.Length != 0,
        "Production nested-list playback probe returned no result.");
      using JsonDocument result = JsonDocument.Parse(encoded);
      JsonElement nested = result.RootElement.GetProperty("nested");
      JsonElement table = result.RootElement.GetProperty("table");

      Require(string.Equals(
          nested.GetProperty("fragment").GetString(),
          "1. *Nested numbered item with a table inside it*",
          StringComparison.Ordinal),
        "Production playback could not resolve the nested numbered-list fragment.");
      Require(string.Equals(
          nested.GetProperty("activeWords").GetString(),
          "Nested",
          StringComparison.Ordinal),
        "Production payload did not highlight the nested list body word.");
      Require(nested.GetProperty("listHighlights").GetInt32() == 0,
        "An ancestor whole-list-item ordinal highlight remained during nested body speech.");

      Require(string.Equals(
          table.GetProperty("fragment").GetString(),
          "| Column | Value | Style |",
          StringComparison.Ordinal),
        "Production playback could not resolve the nested table fragment.");
      Require(string.Equals(
          table.GetProperty("activeWords").GetString(),
          "Column",
          StringComparison.Ordinal),
        "Production payload did not highlight the nested table word.");
      Require(table.GetProperty("listHighlights").GetInt32() == 0,
        "An ancestor whole-list-item ordinal highlight remained during nested table speech.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
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
