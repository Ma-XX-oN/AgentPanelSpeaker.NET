using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Independent acceptance tests for issue #37 transcript virtualization.
/// Production code creates the actual virtual records and browser DOM; expected
/// containment, convergence, and diagnostic bounds are authored here.
/// </summary>
internal static class Issue37VirtualWindowRegressionTestRunner
{
  private const int PairCount = 120;
  private const int SourceRecordCount = PairCount * 2;

  /// <summary>
  /// Runs the issue #37 virtual-window acceptance suite.
  /// </summary>
  /// <returns>Zero when every acceptance oracle passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("virtual-window/core-single-anchor-user-context-unit-is-preserved",
        TestCoreSingleAnchorUserContextUnitIsPreserved),
      ("virtual-window/browser-is-bounded-convergent-and-diagnostics-are-bounded",
        TestBrowserWindowBehaviour)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #37 virtual-window acceptance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #37 virtual-window tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #37 virtual-window tests failed.");
    return failures == 0 ? 0 : 1;
  }

/// <summary>
/// A complete Core-rendered User Context turn containing one source anchor
/// must remain one virtual unit.  AgentPanelSpeaker receives the legal cut
/// and source identities from Core; this test deliberately does not infer
/// atomicity from the unit's details markup.
/// </summary>
private static void TestCoreSingleAnchorUserContextUnitIsPreserved()
{
  const string html = """
<section class="transcript-turn" data-presentation-id="turn:user-context">
  <h2>User</h2>
  <blockquote class="transcript-turn-body">
    <blockquote class="user-context">
      <details class="user-context-details" data-presentation-id="context:1">
        <summary># Context from my IDE setup:</summary>
        <span class="record-anchor" data-jsonl-record="11" data-source-id="context"></span>
        <h2>Active file:</h2><p>sessions/example.jsonl</p>
        <h2>Active selection of the file:</h2><p>selected line</p>
        <h2>Open tabs:</h2><ul><li>example.jsonl: sessions/example.jsonl</li></ul>
      </details>
    </blockquote>
    <div class="presentation-content"><p>Actual prompt.</p></div>
  </blockquote>
</section>
""";

  CanonicalHtmlUnitProjection[] units =
  {
    new(
      "turn:user-context",
      "turn",
      true,
      new[]
      {
        new CanonicalHtmlSourceProjection(
          "event:user-context",
          "codex",
          "context",
          10,
          new[] { 0, 1 })
      },
      html)
  };

  MethodInfo? build = typeof(TranscriptVirtualDocument).GetMethod(
    "Build",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
    binder: null,
    types: new[] { typeof(IReadOnlyList<CanonicalHtmlUnitProjection>) },
    modifiers: null);
  Require(
    build is not null,
    "TranscriptVirtualDocument has no Core-unit Build overload; " +
    "the production path still discovers boundaries from completed HTML.");

  object? built = build!.Invoke(null, new object[] { units });
  TranscriptVirtualDocument document = built as TranscriptVirtualDocument ??
    throw new InvalidOperationException(
      "Core-unit Build overload did not return a TranscriptVirtualDocument.");
  Require(document.Count == 1,
    $"Expected one Core unit to remain one virtual record, got {document.Count}.");
  Require(
    document.TryGetIndex(11, "context", out int contextIndex),
    "Virtual document did not map Core source metadata to the one-based record identity.");
  TranscriptVirtualRecord contextRecord = document.Records[contextIndex];
  Require(
    string.Equals(contextRecord.Html, html, StringComparison.Ordinal),
    "Virtualization changed or split the already-rendered Core HTML unit.");
  Require(
    contextRecord.Html.Contains("Actual prompt.", StringComparison.Ordinal),
    "Core User Context turn lost its prompt while entering virtualization.");
}

  /// <summary>
  /// Loads a transcript large enough to exceed one virtual window through the
  /// real TranscriptView.  The first browser DOM must be bounded.  A scroll
  /// window request made while playback points outside that window must settle
  /// on the user-selected window instead of bouncing back to playback.  Normal
  /// mapping installation must emit one bounded aggregate rather than one large
  /// diagnostic event per speech node.
  /// </summary>
  private static void TestBrowserWindowBehaviour()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-vwindow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-vwindow.jsonl");
    WriteFixture(path);

    try
    {
      using var form = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      using var view = new TranscriptView
      {
        Dock = DockStyle.Fill
      };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization");

      int mappingNodeSummaryCount = 0;
      int mappingInstallSummaryCount = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          if (!message.RootElement.TryGetProperty(
                "type",
                out JsonElement typeElement) ||
              typeElement.ValueKind != JsonValueKind.String)
          {
            return;
          }
          string type = typeElement.GetString() ?? string.Empty;
          if (type == "mapping-node-summary")
          {
            ++mappingNodeSummaryCount;
          }
          else if (type == "mapping-install-summary")
          {
            ++mappingInstallSummaryCount;
          }
        }
        catch (JsonException)
        {
          // The production receiver owns malformed-message handling.  This
          // observer counts only the two independently specified diagnostics.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 virtual-window fixture");

      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual transcript window to finish rendering");

      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const anchors = [...document.querySelectorAll('.record-anchor')];
  const records = anchors
    .map(anchor => Number(anchor.dataset.jsonlRecord))
    .filter(Number.isFinite);
  return JSON.stringify({
    anchorCount: anchors.length,
    virtualRecordCount: document.querySelectorAll('.virtual-record').length,
    minRecord: records.length ? Math.min(...records) : -1,
    maxRecord: records.length ? Math.max(...records) : -1
  });
})()
""");

      int anchorCount = probe.GetProperty("anchorCount").GetInt32();
      int virtualRecordCount = probe.GetProperty("virtualRecordCount").GetInt32();
      int maxRecord = probe.GetProperty("maxRecord").GetInt32();
      Require(anchorCount > 0,
        "Initial transcript browser DOM contained no source record anchors.");
      Require(anchorCount < SourceRecordCount,
        $"Initial transcript browser DOM materialized all {anchorCount} source records; " +
        "virtualization did not bound first paint.");
      Require(virtualRecordCount > 0,
        "Initial transcript browser DOM contains no virtual-record containers.");
      Require(maxRecord == SourceRecordCount,
        $"Initial virtual window did not include the newest source record; " +
        $"expected {SourceRecordCount}, found {maxRecord}.");

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity playbackIdentity = identities
        .Last(identity =>
          identity.Segments.Count > 0 &&
          identity.RecordNumber >= maxRecord - 2);
      string playbackFragment = playbackIdentity.Segments[0];
      string playbackWord = SpeechTokenization.First(playbackFragment);
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        playbackFragment,
        0,
        playbackWord,
        playbackIdentity.NodeId,
        0,
        playbackWord.Length,
        Stopwatch.GetTimestamp()));
      PumpMessages(150);

      Task shiftTask = InvokeTask(
      view,
      "RenderWindowForIndexAsync",
      0,
      "scroll-up",
      null,
      string.Empty,
      null);
    PumpUntilCompleted(
      shiftTask,
      "manual scroll window to replace the initial playback window");
    Require(
      ReadField<int>(view, "_windowStartIndex") != initialStart ||
      ReadField<int>(view, "_windowEndIndex") != initialEnd,
      "Manual scroll render did not leave the initial playback window.");

      (int Start, int End) scrollWindow = (
        ReadField<int>(view, "_windowStartIndex"),
        ReadField<int>(view, "_windowEndIndex"));
      var observedWindows = new List<(int Start, int End)> { scrollWindow };
      DateTime settleDeadline = DateTime.UtcNow.AddSeconds(3);
      while (DateTime.UtcNow < settleDeadline)
      {
        Application.DoEvents();
        Thread.Sleep(25);
        var current = (
          ReadField<int>(view, "_windowStartIndex"),
          ReadField<int>(view, "_windowEndIndex"));
        if (observedWindows[^1] != current)
        {
          observedWindows.Add(current);
        }
      }

      Require(
        observedWindows.Count == 1,
        "Manual scroll window did not converge; observed virtual ranges: " +
        string.Join(", ", observedWindows.Select(
          range => $"[{range.Start}..{range.End}]")));
      Require(
        scrollWindow != (initialStart, initialEnd),
        "Manual scroll window immediately returned to the playback window.");

      Require(
        mappingNodeSummaryCount == 0,
        $"Normal mapping emitted {mappingNodeSummaryCount} per-node diagnostic " +
        "messages; expected none.");
      Require(
        mappingInstallSummaryCount >= 1,
        "Normal mapping emitted no bounded aggregate install summary.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>(SourceRecordCount);
    for (int index = 1; index <= PairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T01:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Issue 37 request {index:D3}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T01:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Issue 37 response {index:D3}."
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static Task InvokeTask(
  object target,
  string methodName,
  params object?[] arguments)
{
  MethodInfo method = target.GetType().GetMethod(
    methodName,
    BindingFlags.Instance | BindingFlags.NonPublic) ??
    throw new InvalidOperationException(
      $"Method '{methodName}' was not found on {target.GetType().Name}.");
  return method.Invoke(target, arguments) as Task ??
    throw new InvalidOperationException(
      $"Method '{methodName}' did not return a Task.");
}

  private static T ReadField<T>(object target, string fieldName)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    object? value = field.GetValue(target);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field '{fieldName}' had an unexpected value/type.");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser virtual-window probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException(
        "Browser virtual-window probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void ExecuteVoidScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser virtual-window action");
  }

  private static void PumpMessages(int milliseconds)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 60000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static void PumpUntilCompleted(
    Task task,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(task.IsCompleted, $"Timed out waiting for {description}.");
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
