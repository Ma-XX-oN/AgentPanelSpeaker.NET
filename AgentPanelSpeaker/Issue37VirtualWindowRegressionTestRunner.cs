using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Independent browser-output regression tests for issue #37 transcript
/// virtualization.  Production code creates the actual initial transcript DOM;
/// expected bounds are fixed here and are not derived from the window builder.
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
      ("virtual-window/initial-browser-dom-is-bounded",
        TestInitialBrowserDomIsBounded)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #37 virtual-window acceptance suite: {tests.Length} test");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #37 virtual-window test passed."
      : $"FAIL: {failures}/{tests.Length} issue #37 virtual-window test failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Loads a transcript large enough to exceed one virtual window through the
  /// real TranscriptView and inspects only the final browser DOM.  A correct
  /// first paint contains the newest source record but not the complete source
  /// inventory.  The old CreateFullWindow path fails this oracle because all
  /// source records are materialized before first display.
  /// </summary>
  private static void TestInitialBrowserDomIsBounded()
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

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 virtual-window fixture");

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.CoreWebView2 is not null &&
          webView.Visible,
        "initial virtual transcript window to finish rendering");

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
