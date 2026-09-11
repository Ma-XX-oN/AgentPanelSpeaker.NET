using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent production-path regressions for the real large-session defects
/// tracked by issue #54 and its child issues.
/// </summary>
internal static class Issue54RealSessionRegressionTestRunner
{
  private const int PairCount = 120;

  /// <summary>
  /// Runs the real-session regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("real-session/scroll-replacement-does-not-reverse-user-intent",
        TestReplacementScrollDoesNotReverseUserIntent),
      ("real-session/stale-window-shift-is-coalesced",
        TestStaleWindowShiftIsCoalesced),
      ("real-session/window-replacement-emits-transaction-diagnostics",
        TestWindowReplacementInstrumentationContract),
      ("real-session/directional-shift-keeps-prefetch-headroom",
        TestDirectionalShiftKeepsPrefetchHeadroom),
      ("real-session/search-window-retains-preceding-unit",
        TestSearchWindowRetainsPrecedingUnit),
      ("real-session/live-end-window-retains-preceding-unit",
        TestLiveEndWindowRetainsPrecedingUnit)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #54 real-session regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #54 real-session tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #54 real-session tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Reproduces issue #56. A downward physical wheel gesture remains the active
  /// user intent while a virtual-window replacement restores its anchor with an
  /// upward programmatic scroll. The restoration must not be reclassified as a
  /// new upward user gesture and request a competing scroll-up window shift.
  /// </summary>
  private static void TestReplacementScrollDoesNotReverseUserIntent()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-scroll.jsonl");
    WriteFixture(path);

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      var shiftReasons = new List<string>();
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (!rootElement.TryGetProperty("type", out JsonElement typeElement) ||
              typeElement.ValueKind != JsonValueKind.String ||
              typeElement.GetString() != "window-shift")
          {
            return;
          }
          if (rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
              reasonElement.ValueKind == JsonValueKind.String)
          {
            shiftReasons.Add(reasonElement.GetString() ?? string.Empty);
          }
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling. This observer records
          // only well-formed virtual-window requests.
        }
      };

      view.SelectSession(path, AgentSource.Codex, "Issue 56 scroll fixture");
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(middleWindow, "middle issue #56 virtual window");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0,
        "Issue #56 fixture has no unloaded content above the middle window.");

      // Put the first materialized unit near the upper prefetch boundary using
      // an explicitly guarded programmatic scroll. Let that guard expire so the
      // test starts from a stable browser position.
      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0];
  programmaticScrollUntil = performance.now() + 500;
  const desiredTop = -window.innerHeight * 0.25;
  const delta = first.getBoundingClientRect().top - desiredTop;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);

      int shiftsBefore = shiftReasons.Count;
      ExecuteVoidScript(
        webView,
        """
(() => {
  virtualShiftPending = false;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));

  // This is the opposite-sign scroll produced by replacement/anchor
  // restoration while the physical downward-wheel intent is still alive.
  programmaticScrollUntil = performance.now() + 1000;
  window.scrollBy(0, -160);
})()
""");
      PumpMessages(500);

      string[] competing = shiftReasons
        .Skip(shiftsBefore)
        .Where(reason => reason == "scroll-up")
        .ToArray();
      Require(
        competing.Length == 0,
        "A replacement-induced upward scroll reversed the active downward " +
        "physical user intent and requested scroll-up.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces the remaining issue #56 duplicate-window work. Two shift
  /// requests produced from the same browser window are the same navigation
  /// transaction. Once the first replacement advances the window, the stale
  /// second request must not rebuild the same materialized range again.
  /// </summary>
  private static void TestStaleWindowShiftIsCoalesced()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-stale-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-stale.jsonl");
    WriteFixture(path);

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      int mappingInstallSummaries = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.ValueKind == JsonValueKind.String &&
              typeElement.GetString() == "mapping-install-summary")
          {
            ++mappingInstallSummaries;
          }
        }
        catch (JsonException)
        {
        }
      };

      view.SelectSession(path, AgentSource.Codex, "Issue 56 stale shift fixture");
      WaitForTranscriptRender(view);
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(middleWindow, "middle stale-shift virtual window");
      PumpMessages(100);

      int start = ReadField<int>(view, "_windowStartIndex");
      int end = ReadField<int>(view, "_windowEndIndex");
      Require(start > 0 && end > start,
        "Issue #56 stale-shift fixture did not create a movable middle window.");
      int summariesBefore = mappingInstallSummaries;

      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const reference = records[0];
  const anchor = reference?.querySelector('.record-anchor');
  if (!reference || !anchor) throw new Error('No virtual shift reference record.');
  const message = {
    type:'window-shift',
    reason:'scroll-up',
    focalIndex:Number(reference.dataset.virtualIndex || -1),
    sourceStartIndex:windowStartIndex,
    sourceEndIndex:windowEndIndex,
    visibleStartIndex:Number(reference.dataset.virtualIndex || -1),
    visibleEndIndex:Number(reference.dataset.virtualIndex || -1),
    viewportHeight:window.innerHeight,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorOffset:anchor.getBoundingClientRect().top
  };
  chrome.webview.postMessage(message);
  chrome.webview.postMessage(message);
})()
""");
      PumpMessages(2500);

      int replacements = mappingInstallSummaries - summariesBefore;
      Require(
        replacements == 1,
        $"Two stale requests from one source window caused {replacements} " +
        "full browser mapping installs instead of one coalesced replacement.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Locks the issue #56 diagnostic contract used to distinguish C# window
  /// construction, browser mapping, DOM/layout, and anchor-restoration cost.
  /// Instrumentation must report one correlated transaction without changing
  /// the virtual-window navigation result.
  /// </summary>
  private static void TestWindowReplacementInstrumentationContract()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-instrumentation-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-instrumentation.jsonl");
    WriteFixture(path);

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      string? diagnosticJson = null;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.ValueKind == JsonValueKind.String &&
              typeElement.GetString() == "window-transaction-diagnostic")
          {
            diagnosticJson = eventArgs.WebMessageAsJson;
          }
        }
        catch (JsonException)
        {
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 56 instrumentation fixture");
      WaitForTranscriptRender(view);
      PumpUntil(
        () => ReadNullableField<TranscriptSearchIndex>(view, "_searchIndex") is not null,
        "search index for issue #56 instrumentation fixture");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      Require(
        middleIndex < beforeStart || middleIndex > beforeEnd,
        "Issue #56 instrumentation fixture did not leave its middle unit unloaded.");

      Task replacement = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "instrumentation-contract",
        null,
        null);
      PumpUntilCompleted(replacement, "instrumented issue #56 replacement");
      PumpUntil(
        () => diagnosticJson is not null,
        "window replacement transaction diagnostic",
        timeoutMilliseconds: 5000);

      using JsonDocument diagnostic = JsonDocument.Parse(diagnosticJson!);
      JsonElement rootElement = diagnostic.RootElement;
      string[] requiredNumericFields =
      {
        "transactionId",
        "requestSequence",
        "startIndex",
        "endIndex",
        "recordCount",
        "nodeCount",
        "wordCount",
        "beforeScrollY",
        "beforeViewportHeight",
        "beforeDocumentHeight",
        "beforeTopSpacerHeight",
        "beforeBottomSpacerHeight",
        "beforeVisibleStartIndex",
        "beforeVisibleEndIndex",
        "afterScrollY",
        "afterViewportHeight",
        "afterDocumentHeight",
        "afterTopSpacerHeight",
        "afterBottomSpacerHeight",
        "afterVisibleStartIndex",
        "afterVisibleEndIndex",
        "innerHtmlMilliseconds",
        "wrapWordsMilliseconds",
        "recordScopesMilliseconds",
        "nodeScopesMilliseconds",
        "mappingSummaryMilliseconds",
        "measurementMilliseconds",
        "anchorRestoreMilliseconds",
        "totalMilliseconds"
      };
      foreach (string field in requiredNumericFields)
      {
        Require(
          rootElement.TryGetProperty(field, out JsonElement value) &&
          value.ValueKind == JsonValueKind.Number,
          $"Instrumentation diagnostic omitted numeric field '{field}'.");
      }
      Require(
        rootElement.GetProperty("transactionId").GetInt64() > 0,
        "Instrumentation diagnostic did not carry a positive transaction ID.");
      Require(
        rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
        reasonElement.GetString() == "instrumentation-contract",
        "Instrumentation diagnostic lost the replacement reason.");
      Require(
        rootElement.TryGetProperty("searchIndexAvailable", out JsonElement searchElement) &&
        searchElement.ValueKind == JsonValueKind.True,
        "Instrumentation diagnostic did not report the completed search index.");
      Require(
        rootElement.GetProperty("startIndex").GetInt32() <= middleIndex &&
        rootElement.GetProperty("endIndex").GetInt32() >= middleIndex,
        "Instrumentation changed the requested virtual-window navigation result.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces the remaining issue #56 scroll-ahead deficit. Once a downward
  /// shift is requested, the resulting window must carry enough materialized
  /// content beyond the currently visible range to absorb continued physical
  /// scrolling while the next asynchronous replacement is prepared.
  /// </summary>
  private static void TestDirectionalShiftKeepsPrefetchHeadroom()
  {
    const int recordCount = 30;
    const double viewportHeight = 700.0;
    CanonicalHtmlUnitProjection[] units = Enumerable.Range(0, recordCount)
      .Select(index => CreateUnit(
        index,
        $"prefetch-{index}",
        $"<p>Issue 56 prefetch record {index}.</p>"))
      .ToArray();
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);
    const int layoutGeneration = 17;
    document.UpdateMeasuredHeights(
      Enumerable.Range(0, recordCount)
        .ToDictionary(index => index, _ => viewportHeight),
      layoutGeneration);

    TranscriptWindow shifted = document.CreateShiftedWindow(
      focalIndex: 12,
      currentStartIndex: 8,
      currentEndIndex: 12,
      direction: 1,
      viewportHeight: viewportHeight,
      protectedStartIndex: 11,
      protectedEndIndex: 12);

    Require(
      shifted.StartIndex <= 11,
      "Directional shift discarded a protected physically visible Core unit.");
    Require(
      shifted.EndIndex - 12 >= 4,
      "Directional shift left fewer than four viewport-heights of measured " +
      "materialized headroom beyond the visible range.");
  }

  /// <summary>
  /// Reproduces issue #69 through the production Find materialization path. A
  /// tall searched assistant turn must not materialize alone when a visible
  /// predecessor Core turn exists.
  /// </summary>
  private static void TestSearchWindowRetainsPrecedingUnit()
  {
    var units = new[]
    {
      CreateUnit(0, "search-preceding",
        "<p>Issue 69 preceding user turn.</p>"),
      CreateUnit(1, "search-target",
        "<p>Issue 69 searched assistant turn.</p>"),
      CreateUnit(2, "search-following",
        "<p>Issue 69 following turn.</p>")
    };
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);
    document.SetLayoutGeneration(1);
    document.UpdateMeasuredHeights(
      new Dictionary<int, double>
      {
        [0] = 120.0,
        [1] = TranscriptVirtualDocument.DefaultViewportHeight * 6.0,
        [2] = 120.0
      },
      1);
    Require(document.TryGetIndex(2, out int focalIndex),
      "Issue #69 fixture target record did not resolve.");

    using var host = CreateOffscreenHost();
    using var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    WaitForViewInitialization(view);

    FieldInfo documentField = typeof(TranscriptView).GetField(
      "_virtualDocument",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "TranscriptView._virtualDocument field was not found.");
    documentField.SetValue(view, document);

    MethodInfo render = typeof(TranscriptView).GetMethod(
      "RenderWindowForRecordAsync",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "TranscriptView.RenderWindowForRecordAsync() was not found.");
    object? invoked = render.Invoke(view, new object?[]
    {
      2,
      "search",
      0,
      null
    });
    Task task = invoked as Task ??
      throw new InvalidOperationException(
        "Search-window materialization did not return a Task.");
    PumpUntilCompleted(task, "issue #69 search-window materialization");

    int start = ReadField<int>(view, "_windowStartIndex");
    int end = ReadField<int>(view, "_windowEndIndex");
    Require(start < focalIndex,
      "Find materialized the tall searched Core unit without its immediately " +
      "preceding visible turn.");
    Require(end >= focalIndex,
      "Find materialization lost the searched Core unit while retaining context.");
  }

  /// <summary>
  /// Reproduces issue #58 without splitting Core atomic units. A final turn can
  /// itself exceed the five-viewport materialization target; live-end startup
  /// must still retain at least one earlier visible unit so the user can see
  /// preceding context without first scrolling into an unloaded spacer.
  /// </summary>
  private static void TestLiveEndWindowRetainsPrecedingUnit()
  {
    string tallText = string.Join(
      " ",
      Enumerable.Repeat("issue58-tall-final-turn", 18000));
    var units = new[]
    {
      CreateUnit(0, "preceding", "<p>Issue 58 preceding turn.</p>"),
      CreateUnit(1, "final", $"<p>{tallText}</p>")
    };
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);
    TranscriptWindow window = document.CreateWindow(
      focalIndex: document.Count - 1,
      viewportHeight: TranscriptVirtualDocument.DefaultViewportHeight);

    Require(
      window.EndIndex == document.Count - 1,
      "Live-end window did not retain the final Core unit.");
    Require(
      window.StartIndex < window.EndIndex,
      "A tall final Core unit satisfied the height floor by itself and caused " +
      "the immediately preceding visible unit to remain unloaded.");
    Require(
      window.Records.Any(record =>
        record.Html.Contains("Issue 58 preceding turn.", StringComparison.Ordinal)),
      "Live-end window omitted the preceding visible Core unit.");
  }

  private static CanonicalHtmlUnitProjection CreateUnit(
    int recordIndex,
    string id,
    string html)
  {
    return new CanonicalHtmlUnitProjection(
      Id: $"issue58-{id}",
      Kind: "turn",
      Atomic: true,
      Source: new[]
      {
        new CanonicalHtmlSourceProjection(
          EventId: $"issue58-event-{recordIndex}",
          Provider: "codex",
          RecordId: $"issue58-record-{recordIndex}",
          RecordIndex: recordIndex,
          BlockIndexes: new[] { 0 })
      },
      Html: html);
  }

  private static Form CreateOffscreenHost()
  {
    return new Form
    {
      Width = 900,
      Height = 700,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-30000, -30000)
    };
  }

  private static void WaitForViewInitialization(TranscriptView view)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    PumpUntil(
      () => webView.CoreWebView2 is not null,
      "WebView2 core initialization");
  }

  private static void WaitForTranscriptRender(TranscriptView view)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    PumpUntil(
      () =>
        ReadField<int>(view, "_windowStartIndex") >= 0 &&
        ReadField<int>(view, "_windowEndIndex") >=
          ReadField<int>(view, "_windowStartIndex") &&
        webView.Visible,
      "production transcript window to finish rendering");
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>(PairCount * 2);
    for (int index = 1; index <= PairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T01:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Issue 56 request {index:D3}."
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
          message = $"Issue 56 response {index:D3}."
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

  private static T? ReadNullableField<T>(object target, string fieldName)
    where T : class
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    return field.GetValue(target) as T;
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

  private static void ExecuteVoidScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #54 action");
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
