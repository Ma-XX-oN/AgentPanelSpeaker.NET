using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
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
      ("real-session/live-end-window-retains-preceding-unit",
        TestLiveEndWindowRetainsPrecedingUnit),
      ("real-session/stale-find-materialization-is-cancelled-before-install",
        TestStaleFindMaterializationIsCancelledBeforeInstall),
      ("real-session/stale-find-invalidated-after-install-stays-visible",
        TestStaleFindInvalidatedAfterInstallStaysVisible),
      ("real-session/follow-enable-reattaches-retained-core-word",
        TestFollowEnableReattachesRetainedCoreWord),
      ("real-session/programmatic-scroll-does-not-disable-follow",
        TestProgrammaticScrollDoesNotDisableFollow),
      ("real-session/follow-opens-canonical-disclosure-only-when-enabled",
        TestFollowOpensCanonicalDisclosureOnlyWhenEnabled),
      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",
        TestInputDiagnosticsCaptureKeyMouseAndFollowState)
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

  /// <summary>
  /// Reproduces issue #71. An off-window Find materialization can already be
  /// queued behind the render gate when a newer query invalidates its browser
  /// navigation generation. The invalidated request must not install its stale
  /// virtual window or strand the viewport over a spacer.
  /// </summary>
  private static void TestStaleFindMaterializationIsCancelledBeforeInstall()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue71-stale-find-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue71-stale-find.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Issue 71 stale Find fixture");
      WaitForTranscriptRender(view);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      TranscriptWindow targetWindow = document.CreateWindow(
        0,
        TranscriptVirtualDocument.DefaultViewportHeight);
      int targetRecordNumber = targetWindow.Records[0].RecordNumber;
      Require(
        !document.TryGetIndex(targetRecordNumber, out int targetIndex) ||
        targetIndex < initialStart || targetIndex > initialEnd,
        "Issue #71 fixture did not choose an off-window Find target.");

      SemaphoreSlim gate = ReadField<SemaphoreSlim>(view, "_windowRenderGate");
      gate.Wait();
      try
      {
        ExecuteVoidScript(
          webView,
          $$"""
(() => {
  findNavigationGeneration = 105;
  chrome.webview.postMessage({
    type:'window-request',
    recordNumber:{{targetRecordNumber}},
    reason:'search',
    matchIndex:0,
    navigationGeneration:105
  });
})()
""");
        PumpUntil(
          () => ReadField<long>(
            view,
            "_latestFindWindowNavigationGeneration") == 105,
          "issue #71 stale Find request to reach C#");

        // Supersede the queued request exactly as query-as-you-type does.
        // Current production increments only the browser generation here;
        // issue #71 requires that invalidation to reach C# before installation.
        ExecuteVoidScript(
          webView,
          """
(() => {
  // The previous Find search has already completed. Only its off-window
  // materialization is still queued when the user edits the query.
  findSearchPending = false;
  cancelFindSearch(false);
})()
""");
        PumpMessages(250);
      }
      finally
      {
        gate.Release();
      }

      PumpMessages(1500);
      int finalStart = ReadField<int>(view, "_windowStartIndex");
      int finalEnd = ReadField<int>(view, "_windowEndIndex");
      int visibleRecords = ExecuteIntScript(
        webView,
        """
[...document.querySelectorAll('.virtual-record')].filter(record => {
  const rect = record.getBoundingClientRect();
  return rect.bottom > 0 && rect.top < window.innerHeight;
}).length
""");

      Require(
        finalStart == initialStart && finalEnd == initialEnd &&
        visibleRecords > 0,
        $"Superseded Find navigation changed window {initialStart}..{initialEnd} " +
        $"to {finalStart}..{finalEnd}; visible materialized records={visibleRecords}.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces the remaining issue #71 race observed on the real machine.
  /// The browser can invalidate Find after the replacement has started but
  /// before C# posts window-ready. A stale ready must never leave the viewport
  /// over an unloaded spacer with zero visible materialized records.
  /// </summary>
  private static void TestStaleFindInvalidatedAfterInstallStaysVisible()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue71-postinstall-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue71-postinstall.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Issue 71 post-install fixture");
      WaitForTranscriptRender(view);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      TranscriptWindow targetWindow = document.CreateWindow(
        0,
        TranscriptVirtualDocument.DefaultViewportHeight);
      int targetRecordNumber = targetWindow.Records[0].RecordNumber;
      Require(
        !document.TryGetIndex(targetRecordNumber, out int targetIndex) ||
        targetIndex < initialStart || targetIndex > initialEnd,
        "Issue #71 post-install fixture did not choose an off-window target.");

      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  findInput.value = 'Issue';
  findSearchPending = false;
  findMatches = [{
    fileOrdinal:1,
    recordNumber:{{targetRecordNumber}},
    startWordIndex:0,
    endWordIndex:0,
    nodeId:0,
    nodeWordIndex:-1
  }];
  currentFindMatch = 0;
  findNavigationGeneration = 205;
  window.__issue71PostInstallInvalidated = false;

  const observer = new MutationObserver(() => {
    observer.disconnect();
    findInput.value = 'Changed';
    cancelFindSearch(false);
    window.__issue71PostInstallInvalidated = true;
  });
  observer.observe(transcript, {childList:true});

  chrome.webview.postMessage({
    type:'window-request',
    recordNumber:{{targetRecordNumber}},
    reason:'search',
    matchIndex:0,
    navigationGeneration:205
  });
})()
""");

      PumpUntil(
        () => ReadField<long>(
          view,
          "_latestFindWindowNavigationGeneration") > 205,
        "issue #71 post-install invalidation to reach C#");
      PumpMessages(500);

      int invalidated = ExecuteIntScript(
        webView,
        "window.__issue71PostInstallInvalidated ? 1 : 0");
      int visibleRecords = ExecuteIntScript(
        webView,
        """
[...document.querySelectorAll('.virtual-record')].filter(record => {
  const rect = record.getBoundingClientRect();
  return rect.bottom > 0 && rect.top < window.innerHeight;
}).length
""");

      Require(
        invalidated == 1,
        "Issue #71 post-install invalidation hook did not execute.");
      Require(
        visibleRecords > 0,
        "Find invalidation after replacement began left zero visible " +
        "materialized records.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #57 on the current Core-word path. Follow OFF may retain
  /// a canonical paused cursor outside the materialized window. Turning Follow
  /// ON must replay that retained Core word, materialize its Core unit, and
  /// apply the marker without requiring another speech callback.
  /// </summary>
  private static void TestFollowEnableReattachesRetainedCoreWord()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue57-core-follow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue57-core-follow.jsonl");
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

      TranscriptSettings followOff =
        TranscriptSettings.Default with { FollowSpeech = false };
      TranscriptSettings followOn =
        TranscriptSettings.Default with { FollowSpeech = true };
      view.ApplySettings(followOff, dark: false);
      view.SelectSession(path, AgentSource.Codex, "Issue 57 Core follow fixture");
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      Require(initialStart > 0,
        "Issue #57 Core fixture did not leave earlier units unloaded.");

      int targetIndex = -1;
      long targetWordId = 0;
      for (int index = 0; index < initialStart; ++index)
      {
        long candidate = FirstCanonicalWordId(document.Records[index].Html);
        if (candidate <= 0)
        {
          continue;
        }
        targetIndex = index;
        targetWordId = candidate;
        break;
      }
      Require(targetIndex >= 0 && targetWordId > 0,
        "Issue #57 Core fixture exposed no off-window canonical word.");

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      int exactPlaybackApplied = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.GetString() == "playback-applied" &&
              rootElement.TryGetProperty("wordId", out JsonElement wordElement) &&
              wordElement.TryGetInt64(out long appliedWordId) &&
              appliedWordId == targetWordId)
          {
            ++exactPlaybackApplied;
          }
        }
        catch (JsonException)
        {
        }
      };

      var position = new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        "retained canonical cursor",
        0,
        "retained",
        0,
        0,
        8,
        Stopwatch.GetTimestamp(),
        targetWordId);
      view.ShowPlaybackPosition(position);
      PumpMessages(300);
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Follow OFF unexpectedly materialized the retained Core word.");

      // Remove only the browser-side retained projection. C# still owns the
      // authoritative current/last playback position. This makes the test prove
      // that the OFF->ON settings transition itself replays the cursor.
      ExecuteVoidScript(
        webView,
        "resetRetainedPlayback(); retireCanonicalPlayback(false); " +
        "requestedPlaybackWordId = 0;");
      int appliedBeforeEnable = exactPlaybackApplied;

      view.ApplySettings(followOn, dark: false);
      PumpUntil(
        () =>
          targetIndex >= ReadField<int>(view, "_windowStartIndex") &&
          targetIndex <= ReadField<int>(view, "_windowEndIndex") &&
          exactPlaybackApplied > appliedBeforeEnable,
        "Follow ON to reattach the retained canonical cursor",
        timeoutMilliseconds: 8000);

      // The OFF->ON transition can still be completing its asynchronous Core
      // materialization when the first applied marker arrives. Let that one
      // transition settle before taking the inverse-contract baseline. Any
      // later marker can then be attributed to the already-ON settings apply.
      PumpMessages(500);
      int appliedAfterEnable = exactPlaybackApplied;
      int startAfterEnable = ReadField<int>(view, "_windowStartIndex");
      int endAfterEnable = ReadField<int>(view, "_windowEndIndex");
      view.ApplySettings(followOn, dark: true);
      PumpMessages(500);
      Require(exactPlaybackApplied == appliedAfterEnable,
        "Reapplying settings while Follow was already ON replayed the cursor.");
      Require(
        ReadField<int>(view, "_windowStartIndex") == startAfterEnable &&
        ReadField<int>(view, "_windowEndIndex") == endAfterEnable,
        "Reapplying settings while Follow was already ON moved the window.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #78. A scroll event with no physical user-scroll intent
  /// is programmatic and may not silently turn Follow off. A real wheel gesture
  /// followed by scrolling must still turn Follow off.
  /// </summary>
  private static void TestProgrammaticScrollDoesNotDisableFollow()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue78-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue78-scroll.jsonl");
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
        TranscriptSettings.Default with { FollowSpeech = true },
        dark: false);
      view.SelectSession(path, AgentSource.Codex, "Issue 78 scroll fixture");
      WaitForTranscriptRender(view);
      // Let the host's debounced settings post settle before manipulating only
      // browser-side Follow state. Otherwise a pending C# settings message can
      // overwrite the state under test after the synthetic scroll gesture.
      PumpMessages(500);
      WebView2 webView = ReadField<WebView2>(view, "_webView");

      ExecuteVoidScript(
        webView,
        """
(() => {
  setFollowSpeech(true, false);
  userScrollIntentUntil = 0;
  userScrollIntentDirection = 0;
  programmaticScrollUntil = 0;
  // Drive the scroll handler with a deterministic nonzero delta without
  // creating any physical wheel/touch/key/scrollbar intent. The handler must
  // classify this as programmatic regardless of the runner's current layout.
  lastManualScrollY = window.scrollY - 1000;
  window.dispatchEvent(new Event('scroll'));
})()
""");
      PumpMessages(250);
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 1,
        "A programmatic scroll with no user input silently disabled Follow.");

      ExecuteVoidScript(
        webView,
        """
(() => {
  setFollowSpeech(true, false);
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  // The initial virtual window is normally at the end of the transcript, so a
  // real downward scroll can clamp at the document boundary on CI. Drive the
  // scroll handler with a deterministic positive delta after the physical wheel
  // event; this tests intent classification without depending on page geometry.
  lastManualScrollY = window.scrollY - 220;
  window.dispatchEvent(new Event('scroll'));
})()
""");
      PumpMessages(250);
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 0,
        "A physical wheel-scroll intent did not disable Follow.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Locks the documented Follow disclosure contract on the canonical WordId
  /// path: Follow OFF does not chase speech into a collapsed disclosure, while
  /// Follow ON opens the required disclosure when restoring retained playback.
  /// </summary>
  private static void TestFollowOpensCanonicalDisclosureOnlyWhenEnabled()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue78-details-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue78-details.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Issue 78 disclosure fixture");
      WaitForTranscriptRender(view);
      WebView2 webView = ReadField<WebView2>(view, "_webView");

      const long probeWordId = 900000001;
      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  const details = document.createElement('details');
  details.id = 'issue78-follow-details';
  details.innerHTML = '<summary>Thought probe</summary>' +
    '<p><span id="word-{{probeWordId}}">probe</span></p>';
  transcript.append(details);
  setFollowSpeech(false, false);
  setCanonicalPlayback('speaking', {{probeWordId}});
})()
""");
      Require(
        ExecuteIntScript(
          webView,
          "document.getElementById('issue78-follow-details').open ? 1 : 0") == 0,
        "Follow OFF opened a collapsed canonical disclosure.");

      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  retireCanonicalPlayback(false);
  const details = document.getElementById('issue78-follow-details');
  details.open = false;
  retainedPlayback = {
    state:'speaking',
    fragmentText:'probe',
    wordIndex:0,
    wordText:'probe',
    nodeId:0,
    wordId:{{probeWordId}}
  };
  setFollowSpeech(true, false);
  restoreRetainedPlaybackProjection();
})()
""");
      Require(
        ExecuteIntScript(
          webView,
          "document.getElementById('issue78-follow-details').open ? 1 : 0") == 1,
        "Follow ON did not open the disclosure containing retained playback.");
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 1,
        "Retained-playback restoration changed Follow from ON to OFF.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #77. The structured JSONL log must contain the physical
  /// key/mouse timeline plus the recognized Follow command and explicit state
  /// transition. Activity-text output and settings dirty-state are insufficient.
  /// </summary>
  private static void TestInputDiagnosticsCaptureKeyMouseAndFollowState()
  {
    DiagnosticLog.Initialize();
    string logPath = DiagnosticLog.FilePath;
    int before = File.Exists(logPath) ? File.ReadLines(logPath).Count() : 0;

    using var form = new MainForm();
    _ = form.Handle;

    Message keyDown = Message.Create(
      form.Handle,
      0x0100,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref keyDown);
    Message keyUp = Message.Create(
      form.Handle,
      0x0101,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref keyUp);
    Message mouseDown = Message.Create(
      form.Handle,
      0x0201,
      IntPtr.Zero,
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref mouseDown);
    Message mouseUp = Message.Create(
      form.Handle,
      0x0202,
      IntPtr.Zero,
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref mouseUp);
    Message wheel = Message.Create(
      form.Handle,
      0x020A,
      new IntPtr(120L << 16),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref wheel);
    // Restore the Follow setting so the test does not leave unsaved state, and
    // unregister this synthetic form from the application message filter before
    // disposal. The RED must come from missing diagnostics, not harness teardown.
    Message restoreKeyDown = Message.Create(
      form.Handle,
      0x0100,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref restoreKeyDown);
    Message restoreKeyUp = Message.Create(
      form.Handle,
      0x0101,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref restoreKeyUp);
    Application.RemoveMessageFilter(form);
    PumpMessages(150);

    JsonElement[] events = File.ReadLines(logPath)
      .Skip(before)
      .Select(line => JsonDocument.Parse(line).RootElement.Clone())
      .ToArray();

    bool HasInputPhase(string kind, string phase) => events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "input.physical" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("kind", out JsonElement kindElement) &&
      kindElement.GetString() == kind &&
      data.TryGetProperty("phase", out JsonElement phaseElement) &&
      phaseElement.GetString() == phase);

    Require(HasInputPhase("keyboard", "down"),
      "Structured diagnostics omitted physical key-down.");
    Require(HasInputPhase("keyboard", "up"),
      "Structured diagnostics omitted physical key-up.");
    Require(HasInputPhase("mouse", "down"),
      "Structured diagnostics omitted physical mouse-down.");
    Require(HasInputPhase("mouse", "up"),
      "Structured diagnostics omitted physical mouse-up.");
    Require(HasInputPhase("mouse", "wheel"),
      "Structured diagnostics omitted physical mouse wheel input.");

    Require(events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "input.command" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("command", out JsonElement commandElement) &&
      commandElement.GetString() == "ToggleFollow"),
      "Structured diagnostics omitted the recognized ToggleFollow command.");

    Require(events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "follow.changed" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("oldValue", out JsonElement oldElement) &&
      data.TryGetProperty("newValue", out JsonElement newElement) &&
      oldElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
      newElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
      oldElement.GetBoolean() != newElement.GetBoolean()),
      "Structured diagnostics omitted explicit old/new Follow state.");
  }

  private static long FirstCanonicalWordId(string html)
  {
    const string prefix = "id=\"word-";
    int start = html.IndexOf(prefix, StringComparison.Ordinal);
    if (start < 0)
    {
      return 0;
    }
    start += prefix.Length;
    int end = html.IndexOf('"', start);
    if (end <= start)
    {
      return 0;
    }
    return long.TryParse(
      html.AsSpan(start, end - start),
      System.Globalization.NumberStyles.None,
      System.Globalization.CultureInfo.InvariantCulture,
      out long wordId)
        ? wordId
        : 0;
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

  private static int ExecuteIntScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #71 probe");
    return JsonSerializer.Deserialize<int>(task.Result);
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
