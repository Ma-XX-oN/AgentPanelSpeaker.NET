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
  private const double MinimumWindowViewportHeights = 5.0;
  private const double EdgeTriggerViewportHeights = 2.0;

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
        TestBrowserWindowBehaviour),
      ("virtual-window/programmatic-playback-scroll-does-not-request-shift",
        TestProgrammaticPlaybackScrollDoesNotRequestShift),
      ("virtual-window/spacer-shift-restores-materialized-content",
        TestSpacerShiftRestoresMaterializedContent),
      ("virtual-window/directional-prefetch-follows-scroll-direction",
        TestDirectionalPrefetchFollowsScrollDirection),
      ("virtual-window/manual-scroll-disables-follow-before-vwindow-shift",
        TestManualScrollDisablesFollowBeforeVirtualShift),
      ("virtual-window/physical-window-keeps-five-viewports-materialized",
        TestPhysicalWindowKeepsFiveViewportsMaterialized),
      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",
        TestPlaybackVoiceCursorPrefetchesInsideTallTurn),
      ("virtual-window/shift-preserves-entire-physically-visible-range",
        TestShiftPreservesEntirePhysicallyVisibleRange),
      ("virtual-window/follow-off-initial-render-ignores-pending-playback",
        TestFollowOffInitialRenderIgnoresPendingPlayback),
      ("virtual-window/manual-scroll-preserves-disclosure-state",
        TestManualScrollPreservesDisclosureState),
      ("virtual-window/disclosure-open-state-survives-eviction-and-close-clears-override",
        TestDisclosureOpenStateSurvivesEvictionAndCloseClearsOverride),
      ("virtual-window/manual-scroll-rematerializes-paused-marker-and-required-disclosure",
        TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure),
      ("virtual-window/programmatic-disclosure-state-survives-eviction",
        TestProgrammaticDisclosureStateSurvivesEviction),
      ("virtual-window/retained-playback-never-rebinds-to-duplicate-text-in-another-node",
        TestRetainedPlaybackNeverRebindsToDuplicateTextInAnotherNode)
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
    document.TryGetIndex(11, out int contextIndex),
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
  /// Reproduces the real startup state where speech has a paused position while
  /// Follow Speech is OFF. The pending playback position may still be drawn if
  /// it happens to be materialized, but it must not choose the initial vwindow,
  /// scroll to itself, or open a disclosure that was outside the user-controlled
  /// startup window.
  /// </summary>
  private static void TestFollowOffInitialRenderIgnoresPendingPlayback()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-follow-off-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteFollowOffFixture(path);

    try
    {
      TranscriptNodeIdentity targetIdentity;
      using (var discoveryHost = CreateOffscreenHost())
      using (var discoveryView = new TranscriptView { Dock = DockStyle.Fill })
      {
        discoveryHost.Controls.Add(discoveryView);
        discoveryHost.Show();
        _ = discoveryHost.Handle;
        _ = discoveryView.Handle;
        WaitForViewInitialization(discoveryView);
        discoveryView.ApplySettings(
          TranscriptSettings.Default with { FollowSpeech = false },
          dark: false);
        discoveryView.SelectSession(path, AgentSource.Codex, "follow-off discovery");
        WaitForTranscriptRender(discoveryView);
        IReadOnlyList<TranscriptNodeIdentity> identities =
          ReadField<IReadOnlyList<TranscriptNodeIdentity>>(
            discoveryView,
            "_identities");
        targetIdentity = identities.FirstOrDefault(identity =>
          identity.Segments.Any(segment =>
            segment.Contains("Open tabs:", StringComparison.Ordinal))) ??
          throw new InvalidOperationException(
            "Follow-OFF fixture did not expose the expected User Context identity.");
      }

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

      view.SelectSession(path, AgentSource.Codex, "follow-off production");
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        "Open tabs:",
        0,
        "Open",
        targetIdentity.NodeId,
        0,
        0,
        Stopwatch.GetTimestamp()));
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int start = ReadField<int>(view, "_windowStartIndex");
      int end = ReadField<int>(view, "_windowEndIndex");
      Require(document.Count > 10,
        "Follow-OFF fixture did not create a meaningful virtual transcript.");
      Require(end == document.Count - 1,
        $"Follow OFF let pending playback choose startup vwindow [{start}..{end}] " +
        $"instead of the user-controlled live-end window ending at {document.Count - 1}.");

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      JsonElement browser = ExecuteJsonProbe(
        webView,
        "JSON.stringify({follow:followSpeech,openDetails:" +
        "document.querySelectorAll('details[open]').length," +
        "windowStart:windowStartIndex,windowEnd:windowEndIndex})");
      Require(!browser.GetProperty("follow").GetBoolean(),
        "Browser Follow Speech state was not OFF in the production fixture.");
      Require(browser.GetProperty("windowEnd").GetInt32() == document.Count - 1,
        "Browser window did not remain at the live-end startup range with Follow OFF.");
      Require(browser.GetProperty("openDetails").GetInt32() == 0,
        "Follow-OFF pending playback opened a transcript disclosure during startup.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Real-machine regression: a paused marker inside a programmatically opened
  /// disclosure may be evicted by manual virtual scrolling.  Returning to that
  /// virtual record must redraw the retained voice marker and reopen only the
  /// disclosure required to expose it.  Reapplying marker state must not turn
  /// manual scrolling back into playback-controlled window navigation.
  /// </summary>
  private static void TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-marker-return-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteFollowOffFixture(path);

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
      view.SelectSession(path, AgentSource.Codex, "marker rematerialization");
      WaitForTranscriptRender(view);

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity target = identities.FirstOrDefault(identity =>
        identity.Segments.Any(segment =>
          segment.Contains("Open tabs:", StringComparison.Ordinal))) ??
        throw new InvalidOperationException(
          "Marker-rematerialization fixture has no User Context Open tabs identity.");
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(document.TryGetIndex(target.RecordNumber, out int targetIndex),
        "User Context marker target is absent from the virtual document.");

      Task materializeTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(materializeTarget, "User Context target materialization");

      string fragment = target.Segments.First(segment =>
        segment.Contains("Open tabs:", StringComparison.Ordinal));
      string word = SpeechTokenization.First(fragment);
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        fragment,
        0,
        word,
        target.NodeId,
        0,
        word.Length,
        Stopwatch.GetTimestamp()));

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
          "open:!!document.querySelector('.word.paused')?.closest('details')?.open})")
          .GetProperty("paused").GetBoolean(),
        "paused User Context marker to appear");
      JsonElement initial = ExecuteJsonProbe(
        webView,
        "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
        "open:!!document.querySelector('.word.paused')?.closest('details')?.open})");
      Require(initial.GetProperty("open").GetBoolean(),
        "Paused User Context marker did not programmatically open its disclosure.");

      int awayIndex = targetIndex == 0 ? document.Count - 1 : 0;
      Task moveAway = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        awayIndex,
        targetIndex == 0 ? "scroll-down" : "scroll-up",
        null,
        null);
      PumpUntilCompleted(moveAway, "manual scroll to evict paused marker");
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not evict the paused marker record.");

      Task returnToTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        targetIndex < awayIndex ? "scroll-up" : "scroll-down",
        null,
        null);
      PumpUntilCompleted(returnToTarget, "manual scroll back to paused marker record");
      PumpMessages(200);

      Require(
        targetIndex >= ReadField<int>(view, "_windowStartIndex") &&
        targetIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Manual scroll did not return to the paused marker record.");
      JsonElement restored = ExecuteJsonProbe(
        webView,
        "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
        "open:!!document.querySelector('.word.paused')?.closest('details')?.open})");
      Require(restored.GetProperty("paused").GetBoolean(),
        "Paused voice cursor disappeared after manual-scroll eviction and rematerialization.");
      Require(restored.GetProperty("open").GetBoolean(),
        "Programmatically required disclosure stayed closed after the paused marker rematerialized.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Disclosure state belongs to the logical disclosure, not to the cause of
  /// its last transition.  A programmatic open must survive full eviction and
  /// rematerialization; a later programmatic close returns it to the default
  /// closed state and removes the remembered-open state.
  /// </summary>
  private static void TestProgrammaticDisclosureStateSurvivesEviction()
  {
    using var host = CreateOffscreenHost();
    using var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    WaitForViewInitialization(view);

    WebView2 webView = ReadField<WebView2>(view, "_webView");
    JsonElement result = ExecuteJsonProbe(
      webView,
      """
(() => {
  const withDetails =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<details data-presentation-id="context:programmatic"><summary>Context</summary>' +
    '<p>payload</p></details></section>';
  const away =
    '<section class="virtual-record" data-virtual-index="30"><p>far away</p></section>';

  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  let details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  setDisclosureOpenProgrammatically(details, true);
  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  const openAfterProgrammaticReturn = Boolean(details?.open);

  setDisclosureOpenProgrammatically(details, false);
  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  return JSON.stringify({
    openAfterProgrammaticReturn,
    closedAfterProgrammaticCloseAndReturn: details ? !details.open : false
  });
})()
""");

    Require(result.GetProperty("openAfterProgrammaticReturn").GetBoolean(),
      "Programmatically opened disclosure lost state after eviction/rematerialization.");
    Require(result.GetProperty("closedAfterProgrammaticCloseAndReturn").GetBoolean(),
      "Programmatic close did not clear the remembered open disclosure state.");
  }

  /// <summary>
  /// Real-machine regression: when the retained playback node is evicted, a
  /// duplicate fragment in another materialized node must never inherit the
  /// voice marker. Stable NodeId identity is authoritative across virtual DOM
  /// replacement; the marker may be absent while its node is absent, then must
  /// return only when that exact node rematerializes.
  /// </summary>
  private static void TestRetainedPlaybackNeverRebindsToDuplicateTextInAnotherNode()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-node-identity-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteFollowOffFixture(path);
    File.AppendAllLines(path, new[]
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T01:59:59Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Open tabs:"
        }
      })
    });

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
      view.SelectSession(path, AgentSource.Codex, "stable-node playback identity");
      WaitForTranscriptRender(view);

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity[] matches = identities
        .Where(identity => identity.Segments.Any(segment =>
          string.Equals(segment.Trim(), "Open tabs:", StringComparison.Ordinal)))
        .ToArray();
      Require(matches.Length >= 2,
        $"Duplicate-text fixture exposed only {matches.Length} Open tabs node(s).");
      TranscriptNodeIdentity target = matches[0];
      TranscriptNodeIdentity decoy = matches[^1];
      Require(target.NodeId != decoy.NodeId,
        "Duplicate-text fixture did not create distinct stable node identities.");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(document.TryGetIndex(target.RecordNumber, out int targetIndex),
        "Target playback node is absent from virtual document.");
      Require(document.TryGetIndex(decoy.RecordNumber, out int decoyIndex),
        "Duplicate-text decoy node is absent from virtual document.");
      Require(Math.Abs(decoyIndex - targetIndex) > 5,
        "Duplicate-text fixture did not separate target and decoy enough for eviction.");

      Task materializeTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(materializeTarget, "target playback node materialization");

      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        "Open tabs:",
        0,
        "Open",
        target.NodeId,
        0,
        4,
        Stopwatch.GetTimestamp()));

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})")
          .GetProperty("node").GetInt64() == target.NodeId,
        "paused marker to bind to its target stable node");

      Task materializeDecoy = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        decoyIndex,
        decoyIndex > targetIndex ? "scroll-down" : "scroll-up",
        null,
        null);
      PumpUntilCompleted(materializeDecoy, "manual scroll to duplicate-text decoy");
      PumpMessages(200);
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not evict the authoritative playback node.");
      Require(
        decoyIndex >= ReadField<int>(view, "_windowStartIndex") &&
        decoyIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not materialize the duplicate-text decoy.");

      JsonElement absentTarget = ExecuteJsonProbe(
        webView,
        "JSON.stringify({" +
        "paused:!!document.querySelector('.word.paused')," +
        "node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})");
      Require(!absentTarget.GetProperty("paused").GetBoolean(),
        "Retained voice cursor rebound to duplicate text in another node while " +
        $"authoritative node {target.NodeId} was evicted; rendered node was " +
        $"{absentTarget.GetProperty("node").GetInt64()}.");

      Task returnToTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        targetIndex < decoyIndex ? "scroll-up" : "scroll-down",
        null,
        null);
      PumpUntilCompleted(returnToTarget, "manual scroll back to authoritative playback node");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})")
          .GetProperty("node").GetInt64() == target.NodeId,
        "retained marker to return only to its authoritative stable node");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
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

      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);
      PumpMessages(500);

      Task shiftTask = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        0,
        "scroll-up",
        null,
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

  /// <summary>
  /// Reproduces the real-machine #37 playback/window ping-pong.  Moving playback
  /// from an old virtual window to a distant node replaces the browser window
  /// and performs a programmatic focus scroll.  That scroll must not be
  /// reinterpreted as user navigation and emit a competing virtual-window shift.
  /// Once the programmatic-scroll guard expires, an edge scroll must still be
  /// able to request normal virtualization movement.
  /// </summary>
  private static void TestProgrammaticPlaybackScrollDoesNotRequestShift()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-programmatic-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-programmatic-scroll.jsonl");
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
        "WebView2 core initialization for programmatic-scroll regression");

      int windowShiftCount = 0;
      var windowShiftReasons = new List<string>();
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
          ++windowShiftCount;
          windowShiftReasons.Add(
            rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String
              ? reasonElement.GetString() ?? string.Empty
              : string.Empty);
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling.  This independent
          // observer records only well-formed virtual-window shift requests.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 programmatic-scroll fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for programmatic-scroll regression");

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity playbackIdentity = identities
        .Last(identity => identity.Segments.Count > 0);
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(
        document.TryGetIndex(
          playbackIdentity.RecordNumber,
          out int playbackVirtualIndex),
        "Playback identity was not present in the virtual document.");

      Task moveAway = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        0,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(
        moveAway,
        "precondition window to move away from playback target");
      Require(
        playbackVirtualIndex < ReadField<int>(view, "_windowStartIndex") ||
        playbackVirtualIndex > ReadField<int>(view, "_windowEndIndex"),
        "Precondition window still contains the playback target.");
      PumpMessages(250);

      int shiftsBeforePlayback = windowShiftCount;
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

      PumpUntil(
        () =>
          playbackVirtualIndex >= ReadField<int>(view, "_windowStartIndex") &&
          playbackVirtualIndex <= ReadField<int>(view, "_windowEndIndex"),
        "playback target window to materialize");
      PumpMessages(1200);

      Require(
        windowShiftCount == shiftsBeforePlayback,
        "Playback-driven programmatic scroll emitted competing virtual-window " +
        $"shift request(s): {string.Join(", ", windowShiftReasons.Skip(shiftsBeforePlayback))}.");
      Require(
        playbackVirtualIndex >= ReadField<int>(view, "_windowStartIndex") &&
        playbackVirtualIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Playback window was displaced after its programmatic focus scroll.");

      // The production focus-scroll guard currently lasts at most two seconds.
      // After it expires, a normal edge scroll must still drive virtualization.
      PumpMessages(2100);
      int shiftsBeforeEdgeScroll = windowShiftCount;
      ExecuteVoidScript(
        webView,
        """
(() => {
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: -160,
    bubbles: true,
    cancelable: true
  }));
  window.scrollTo(0, 0);
})()
""");
      PumpUntil(
        () => windowShiftCount > shiftsBeforeEdgeScroll,
        "unguarded edge scroll to request a virtual-window shift",
        timeoutMilliseconds: 5000);
      Require(
        windowShiftReasons.Skip(shiftsBeforeEdgeScroll).Any(
          reason => reason == "scroll-up"),
        "Unguarded edge scroll did not preserve normal upward virtualization.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Reproduces the real-machine blank-viewport failure.  A fast manual scroll
  /// can enter the synthetic spacer before the next virtual window is ready.
  /// Once that shift completes, the browser viewport must intersect actual
  /// materialized transcript records rather than remain wholly in the spacer.
  /// </summary>
  private static void TestSpacerShiftRestoresMaterializedContent()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-spacer-recovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-spacer-recovery.jsonl");
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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for spacer recovery regression");

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 spacer recovery fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") > 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial trailing virtual window for spacer recovery regression");

      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      PumpMessages(600);

      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = 0;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: -160,
    bubbles: true,
    cancelable: true
  }));
  window.scrollTo(0, 0);
})()
""");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != initialStart ||
          ReadField<int>(view, "_windowEndIndex") != initialEnd,
        "spacer scroll to install a predecessor virtual window",
        timeoutMilliseconds: 5000);
      PumpMessages(250);

      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  });
  const topSpacer = document.querySelector(
    '.virtual-spacer[data-virtual-spacer="top"]');
  return JSON.stringify({
    intersectingRecordCount: intersecting.length,
    scrollY: window.scrollY,
    innerHeight: window.innerHeight,
    topSpacerHeight: topSpacer?.getBoundingClientRect().height ?? 0,
    firstRecordTop: records.length
      ? records[0].getBoundingClientRect().top
      : null,
    lastRecordBottom: records.length
      ? records[records.length - 1].getBoundingClientRect().bottom
      : null
  });
})()
""");

      int intersectingRecordCount =
        probe.GetProperty("intersectingRecordCount").GetInt32();
      Require(
        intersectingRecordCount > 0,
        "Completed virtual-window shift left the viewport wholly in synthetic " +
        "spacer instead of restoring materialized transcript content. " +
        $"scrollY={probe.GetProperty("scrollY").GetDouble():F1}, " +
        $"topSpacerHeight={probe.GetProperty("topSpacerHeight").GetDouble():F1}.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Manual virtualization must follow the user's actual scroll direction and
  /// prefetch before the viewport crosses into synthetic spacer.  The current
  /// window can be smaller than the historical 20-record threshold, so record
  /// index bands are not a valid direction detector.
  /// </summary>
  private static void TestDirectionalPrefetchFollowsScrollDirection()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-directional-prefetch-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-directional-prefetch.jsonl");
    WriteDirectionalFixture(path);

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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for directional prefetch regression");

      var windowShiftReasons = new List<string>();
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
          windowShiftReasons.Add(
            rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String
              ? reasonElement.GetString() ?? string.Empty
              : string.Empty);
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling.  This observer records
          // only well-formed window-shift direction requests.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 directional prefetch fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for directional prefetch regression");

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
      PumpUntilCompleted(
        middleWindow,
        "middle virtual-window precondition for directional prefetch");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Directional prefetch precondition did not leave unloaded content " +
        "on both sides of the materialized window.");

      JsonElement positioned = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  const bottomSpacer = document.querySelector(
    '.virtual-spacer[data-virtual-spacer="bottom"]');
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 1.25;
  const delta = last.getBoundingClientRect().bottom - desiredBottom;
  window.scrollBy(0, delta);
  return JSON.stringify({
    recordCount: records.length,
    bottomSpacerHeight: bottomSpacer?.getBoundingClientRect().height ?? 0,
    innerHeight: window.innerHeight
  });
})()
""");
      Require(
        positioned.GetProperty("recordCount").GetInt32() > 0,
        "Directional prefetch fixture has no materialized records.");
      Require(
        positioned.GetProperty("bottomSpacerHeight").GetDouble() > 0,
        "Directional prefetch precondition has no unloaded content below.");
      PumpMessages(650);

      JsonElement beforeDown = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  }).length;
  return JSON.stringify({
    intersecting,
    lastBottom: last.getBoundingClientRect().bottom,
    innerHeight: window.innerHeight
  });
})()
""");
      Require(
        beforeDown.GetProperty("intersecting").GetInt32() > 0,
        "Downward prefetch test started after materialized content was already lost.");
      Require(
        beforeDown.GetProperty("lastBottom").GetDouble() >
          beforeDown.GetProperty("innerHeight").GetDouble() + 80,
        "Downward prefetch test did not retain visible materialized margin.");

      int shiftsBeforeReverse = windowShiftReasons.Count;
      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = 0;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: -80,
    bubbles: true,
    cancelable: true
  }));
  window.scrollBy(0, -80);
})()
""");
      PumpMessages(300);
      string[] reverseReasons = windowShiftReasons
        .Skip(shiftsBeforeReverse)
        .ToArray();
      Require(
        !reverseReasons.Any(reason => reason == "scroll-down"),
        "Upward scrolling near the lower prefetch boundary requested a " +
        "competing scroll-down virtual-window shift.");

      int shiftsBeforeDown = windowShiftReasons.Count;
      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = 0;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  window.scrollBy(0, 160);
})()
""");
      PumpUntil(
        () => windowShiftReasons.Count > shiftsBeforeDown,
        "downward scroll to request predictive virtual-window movement",
        timeoutMilliseconds: 5000);
      string downwardReason = windowShiftReasons[shiftsBeforeDown];
      Require(
        string.Equals(downwardReason, "scroll-down", StringComparison.Ordinal),
        "Downward scrolling did not request a downward prefetch: " +
        downwardReason + ".");

      PumpUntil(
        () => !ReadBrowserBoolean(webView, "virtualShiftPending"),
        "downward virtual-window request to settle",
        timeoutMilliseconds: 5000);

      Task resetMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(
        resetMiddleWindow,
        "middle virtual-window reset before upward prefetch");

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

      int shiftsBeforeUp = windowShiftReasons.Count;
      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = 0;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: -80,
    bubbles: true,
    cancelable: true
  }));
  window.scrollBy(0, -80);
})()
""");
      PumpUntil(
        () => windowShiftReasons.Count > shiftsBeforeUp,
        "upward scroll to request predictive virtual-window movement",
        timeoutMilliseconds: 5000);
      string upwardReason = windowShiftReasons[shiftsBeforeUp];
      Require(
        string.Equals(upwardReason, "scroll-up", StringComparison.Ordinal),
        "Upward scrolling requested the wrong virtual-window direction: " +
        upwardReason + ".");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// A genuine user scroll has priority over speech following.  Follow mode
  /// must be disabled before a vwindow replacement can mark its own anchor
  /// restoration as programmatic; otherwise the delayed follow-off timer can
  /// be suppressed and leave the UI claiming that speech is still followed.
  /// </summary>
  private static void TestManualScrollDisablesFollowBeforeVirtualShift()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-manual-follow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-manual-follow.jsonl");
    WriteDirectionalFixture(path);

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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for manual-follow regression");

      bool? notifiedFollowState = null;
      view.FollowSpeechChanged += (enabled, _) => notifiedFollowState = enabled;
      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 manual-follow fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial manual-follow virtual window");

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
      PumpUntilCompleted(
        middleWindow,
        "middle manual-follow virtual-window precondition");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Manual-follow fixture did not retain unloaded content on both sides.");

      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 1.25;
  const delta = last.getBoundingClientRect().bottom - desiredBottom;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);
      Require(
        ReadBrowserBoolean(webView, "followSpeech"),
        "Manual-follow precondition unexpectedly disabled Follow mode.");

      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = performance.now() + 2000;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  window.scrollBy(0, 160);
})()
""");

      PumpUntil(
        () => notifiedFollowState == false,
        "manual wheel input to override the active programmatic-scroll guard",
        timeoutMilliseconds: 1000);
      Require(
        !ReadBrowserBoolean(webView, "followSpeech"),
        "Manual user scrolling during an active programmatic-scroll guard " +
        "did not disable Follow mode.");

      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != beforeStart ||
          ReadField<int>(view, "_windowEndIndex") != beforeEnd,
        "manual scroll to move the virtual window after disabling Follow",
        timeoutMilliseconds: 5000);
      PumpMessages(300);

      Require(
        notifiedFollowState == false,
        "Manual user scrolling did not emit FollowSpeechChanged(false) before " +
        "the vwindow replacement completed.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// The materialized browser window is sized in physical viewport heights,
  /// not record/turn count.  A normal compact transcript must retain at least
  /// the configured five viewport heights when unloaded content exists on both
  /// sides, without manufacturing giant turns to influence the result.
  /// </summary>
  private static void TestPhysicalWindowKeepsFiveViewportsMaterialized()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-physical-window-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-physical-window.jsonl");
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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for physical-window regression");
      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 physical-window fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          webView.Visible,
        "initial physical virtual window");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        document.Count / 2,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(middleWindow, "middle physical virtual window");
      // Let the browser's natural-height measurements reach the virtual
      // document, then ask for the same focal window again.  A physical-window
      // implementation must use those measurements to avoid retaining an
      // unnecessarily large record-count window.
      PumpMessages(150);
      Task measuredMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        document.Count / 2,
        "test-measured-refinement",
        null,
        null);
      PumpUntilCompleted(
        measuredMiddleWindow,
        "measured middle physical virtual window");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Physical-window fixture did not retain unloaded content on both sides.");

      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const firstRecord = records[0];
  const lastRecord = records[records.length - 1];
  const first = firstRecord?.getBoundingClientRect();
  const last = lastRecord?.getBoundingClientRect();
  return JSON.stringify({
    recordCount: records.length,
    innerHeight: window.innerHeight,
    materializedHeight: first && last ? last.bottom - first.top : 0,
    firstHeight: first?.height ?? 0,
    lastHeight: last?.height ?? 0,
    firstIndex: Number(firstRecord?.dataset.virtualIndex ?? -1),
    lastIndex: Number(lastRecord?.dataset.virtualIndex ?? -1)
  });
})()
""");
      double innerHeight = probe.GetProperty("innerHeight").GetDouble();
      double materializedHeight =
        probe.GetProperty("materializedHeight").GetDouble();
      double targetHeight = innerHeight * MinimumWindowViewportHeights;
      Require(
        materializedHeight >= targetHeight,
        "Materialized vwindow is shorter than the required physical minimum: " +
        $"height={materializedHeight:F1}, viewport={innerHeight:F1}, " +
        $"minimumViewports={MinimumWindowViewportHeights:F1}.");

      int focalIndex = document.Count / 2;
      int firstIndex = probe.GetProperty("firstIndex").GetInt32();
      int lastIndex = probe.GetProperty("lastIndex").GetInt32();
      double firstHeight = probe.GetProperty("firstHeight").GetDouble();
      double lastHeight = probe.GetProperty("lastHeight").GetDouble();
      if (firstIndex < focalIndex)
      {
        Require(
          materializedHeight - firstHeight < targetHeight,
          "Physical vwindow retains a removable leading unit after measured " +
          "heights are known; this unnecessarily increases render work. " +
          $"height={materializedHeight:F1}, firstHeight={firstHeight:F1}, " +
          $"target={targetHeight:F1}.");
      }
      if (lastIndex > focalIndex)
      {
        Require(
          materializedHeight - lastHeight < targetHeight,
          "Physical vwindow retains a removable trailing unit after measured " +
          "heights are known; this unnecessarily increases render work. " +
          $"height={materializedHeight:F1}, lastHeight={lastHeight:F1}, " +
          $"target={targetHeight:F1}.");
      }
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Playback inside one physically tall turn must prefetch from the voice
  /// cursor's vertical position.  Remaining inside the same virtual record is
  /// not a reason to wait until the cursor reaches synthetic spacer.
  /// </summary>
  private static void TestPlaybackVoiceCursorPrefetchesInsideTallTurn()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-tall-turn-cursor-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-tall-turn-cursor.jsonl");
    WriteTallTurnFixture(path);

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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for tall-turn cursor regression");

      var windowShiftReasons = new List<string>();
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
            windowShiftReasons.Add(reasonElement.GetString() ?? string.Empty);
          }
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 tall-turn cursor fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          webView.Visible,
        "initial tall-turn virtual window");

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity tallIdentity = identities.First(identity =>
        identity.Segments.Any(segment => segment.Contains(
          "Tall marker paragraph 001.",
          StringComparison.Ordinal)));
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(
        document.TryGetIndex(
          tallIdentity.RecordNumber,
          out int tallIndex),
        "Tall playback turn is absent from the virtual document.");

      int physicalStart = Math.Max(0, tallIndex - 1);
      int physicalEnd = Math.Min(document.Count - 2, tallIndex + 1);
      Require(
        physicalStart < tallIndex &&
        physicalEnd > tallIndex &&
        physicalEnd < document.Count - 1,
        "Tall-turn physical fixture could not retain materialized neighbours " +
        "and unloaded content below the tall Core unit.");

      TranscriptVirtualRecord[] physicalRecords = document.Records
        .Skip(physicalStart)
        .Take(physicalEnd - physicalStart + 1)
        .ToArray();
      string physicalHtml = string.Concat(
        physicalRecords.Select((record, offset) =>
          "<section class=\"virtual-record\" data-virtual-index=\"" +
          (physicalStart + offset) + "\">" + record.Html + "</section>"));
      double topSpacerHeight = document.Records
        .Take(physicalStart)
        .Sum(record => record.EstimatedHeight);
      double bottomSpacerHeight = document.Records
        .Skip(physicalEnd + 1)
        .Sum(record => record.EstimatedHeight);
      var physicalWindow = new TranscriptWindow(
        physicalHtml,
        physicalStart,
        physicalEnd,
        topSpacerHeight,
        bottomSpacerHeight,
        physicalRecords);

      MethodInfo? buildWindowScript = typeof(TranscriptView).GetMethod(
        "BuildReplaceWindowScript",
        BindingFlags.Instance | BindingFlags.NonPublic);
      Require(
        buildWindowScript is not null,
        "Could not access the production virtual-window replacement builder.");
      string script = buildWindowScript!.Invoke(
        view,
        new object?[]
        {
          physicalWindow,
          false,
          null,
          null,
          null,
          null,
          null,
          null
        }) as string ?? string.Empty;
      Require(
        !string.IsNullOrWhiteSpace(script),
        "Production virtual-window replacement builder returned no script.");
      ExecuteVoidScript(webView, script);
      SetField(view, "_windowStartIndex", physicalStart);
      SetField(view, "_windowEndIndex", physicalEnd);
      PumpMessages(250);

      string fragment = tallIdentity.Segments.Last(segment =>
        segment.Contains("Tall marker paragraph", StringComparison.Ordinal));
      var matches = SpeechTokenization.Matches(fragment);
      Require(matches.Count > 0, "Tall playback fragment contained no speech words.");
      int wordIndex = matches.Count - 1;
      string word = matches[wordIndex].Value;
      string fragmentJson = JsonSerializer.Serialize(fragment);
      string wordJson = JsonSerializer.Serialize(word);
      string targetProbeScript =
        "(() => {" +
        "const fragment=" + fragmentJson + ";" +
        "const wordText=" + wordJson + ";" +
        $"const wordIndex={wordIndex};" +
        $"const nodeId={tallIdentity.NodeId};" +
        "const fragmentRange=findFragmentRange(fragment,nodeId);" +
        "const boundaryRange=fragmentRange?" +
          "findBoundaryRange(fragmentRange,wordText,wordIndex,true):null;" +
        "const target=boundaryRange?words[boundaryRange.start]:null;" +
        "const records=[...document.querySelectorAll('.virtual-record')];" +
        "const first=records[0]?.getBoundingClientRect();" +
        "const last=records[records.length-1]?.getBoundingClientRect();" +
        "const targetRect=target?.getBoundingClientRect();" +
        "const bottomSpacer=document.querySelector(" +
          "'.virtual-spacer[data-virtual-spacer=\"bottom\"]');" +
        "return JSON.stringify({" +
          "targetFound:Boolean(target)," +
          "innerHeight:window.innerHeight," +
          "materializedHeight:first&&last?last.bottom-first.top:0," +
          "cursorDistanceToBottom:targetRect&&last?" +
            "last.bottom-targetRect.bottom:Number.MAX_SAFE_INTEGER," +
          "bottomSpacerHeight:bottomSpacer?.getBoundingClientRect().height??0" +
        "});" +
        "})()";
      JsonElement probe = ExecuteJsonProbe(webView, targetProbeScript);
      Require(
        probe.GetProperty("targetFound").GetBoolean(),
        "Tall-turn speech map could not resolve the exact target word before playback.");
      double innerHeight = probe.GetProperty("innerHeight").GetDouble();
      double cursorDistanceToBottom =
        probe.GetProperty("cursorDistanceToBottom").GetDouble();
      Require(
        probe.GetProperty("materializedHeight").GetDouble() >=
          innerHeight * MinimumWindowViewportHeights,
        "Tall-turn test window did not meet the physical minimum height.");
      Require(
        probe.GetProperty("bottomSpacerHeight").GetDouble() > 0,
        "Tall-turn test has no unloaded content below the materialized window.");
      Require(
        cursorDistanceToBottom <= innerHeight * EdgeTriggerViewportHeights,
        "Tall-turn voice cursor target did not enter the lower physical trigger " +
        $"zone: distance={cursorDistanceToBottom:F1}, viewport={innerHeight:F1}.");

      int shiftsBeforePlayback = windowShiftReasons.Count;
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        fragment,
        wordIndex,
        word,
        tallIdentity.NodeId,
        matches[wordIndex].Index,
        matches[wordIndex].Length,
        Stopwatch.GetTimestamp()));
      PumpUntil(
        () => windowShiftReasons
          .Skip(shiftsBeforePlayback)
          .Any(reason => reason == "playback-down"),
        "voice cursor to request playback-down from the lower physical trigger zone",
        timeoutMilliseconds: 5000);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Directional window trimming must preserve every Core atomic unit that is
  /// still physically visible, not only the directional focal unit.  The
  /// browser can report several intersecting units while the shifted window has
  /// enough off-screen height to satisfy the five-viewport floor after dropping
  /// one of them.
  /// </summary>
  private static void TestShiftPreservesEntirePhysicallyVisibleRange()
  {
    const double viewportHeight = 100.0;
    const int layoutGeneration = 37;
    const int currentStartIndex = 0;
    const int currentEndIndex = 6;
    const int visibleStartIndex = 2;
    const int visibleEndIndex = 4;
    const int focalIndex = visibleEndIndex;

    CanonicalHtmlUnitProjection[] units = Enumerable.Range(0, 12)
      .Select(index => new CanonicalHtmlUnitProjection(
        $"turn:visible-range:{index}",
        "turn",
        true,
        new[]
        {
          new CanonicalHtmlSourceProjection(
            $"event:visible-range:{index}",
            "codex",
            $"visible-range-{index}",
            index,
            new[] { 0 })
        },
        $"<section class=\"transcript-turn\" data-presentation-id=\"turn:visible-range:{index}\">" +
        $"<span class=\"record-anchor\" data-jsonl-record=\"{index + 1}\" " +
        $"data-source-id=\"visible-range-{index}\"></span>" +
        $"<p>Visible range unit {index}.</p></section>"))
      .ToArray();

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);
    document.SetLayoutGeneration(layoutGeneration);
    double[] heights =
    {
      120.0, 120.0, 40.0, 40.0, 40.0, 100.0,
      100.0, 120.0, 120.0, 100.0, 100.0, 100.0
    };
    document.UpdateMeasuredHeights(
      heights.Select((height, index) => (index, height))
        .ToDictionary(item => item.index, item => item.height),
      layoutGeneration);

    TranscriptWindow shifted = document.CreateShiftedWindow(
      focalIndex,
      currentStartIndex,
      currentEndIndex,
      direction: 1,
      viewportHeight);

    Require(
      shifted.StartIndex <= visibleStartIndex &&
      shifted.EndIndex >= visibleEndIndex,
      "Directional shift evicted part of the physically visible Core-unit " +
      $"range [{visibleStartIndex}..{visibleEndIndex}]; shifted window is " +
      $"[{shifted.StartIndex}..{shifted.EndIndex}].");
  }

  /// <summary>
  /// A disclosure opened by the user must stay open when virtualization
  /// replaces an overlapping materialized window during ordinary scrolling.
  /// The replacement itself may use preserve=false for scroll-position policy;
  /// disclosure state is a separate UI invariant and must survive that swap.
  /// </summary>
  private static void TestManualScrollPreservesDisclosureState()
  {
    using var host = CreateOffscreenHost();
    using var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    WaitForViewInitialization(view);

    WebView2 webView = ReadField<WebView2>(view, "_webView");
    JsonElement result = ExecuteJsonProbe(
      webView,
      """
(() => {
  const first =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<details data-presentation-id="context:stable"><summary>Context</summary>' +
    '<p>first window</p></details></section>';
  const second =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<details data-presentation-id="context:stable"><summary>Context</summary>' +
    '<p>shifted window</p></details></section>' +
    '<section class="virtual-record" data-virtual-index="11"><p>next</p></section>';
  replaceTranscriptWindow(first, false, [], 10, 10, 100, 100);
  const before = document.querySelector('details[data-presentation-id="context:stable"]');
  if (!before) throw new Error('Initial disclosure was not materialized.');
  before.open = true;
  replaceTranscriptWindow(second, false, [], 10, 11, 100, 50);
  const after = document.querySelector('details[data-presentation-id="context:stable"]');
  return JSON.stringify({exists:Boolean(after), open:Boolean(after?.open)});
})()
""");

    Require(result.GetProperty("exists").GetBoolean(),
      "Overlapping disclosure disappeared during virtual-window replacement.");
    Require(result.GetProperty("open").GetBoolean(),
      "Virtual-window replacement collapsed a disclosure that the user opened.");
  }

  /// <summary>
  /// SV3: disclosure state must outlive the DOM node itself. Opening a default-
  /// closed details element creates an open override that survives eviction and
  /// rematerialization. Closing it again returns to default and removes that
  /// override, so a later rematerialization is closed.
  /// </summary>
  private static void TestDisclosureOpenStateSurvivesEvictionAndCloseClearsOverride()
  {
    using var host = CreateOffscreenHost();
    using var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    WaitForViewInitialization(view);

    WebView2 webView = ReadField<WebView2>(view, "_webView");
    JsonElement result = ExecuteJsonProbe(
      webView,
      """
(() => {
  const withDetails =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<details data-presentation-id="context:persist"><summary>Context</summary>' +
    '<p>payload</p></details></section>';
  const away =
    '<section class="virtual-record" data-virtual-index="30"><p>far away</p></section>';
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  let details = document.querySelector('details[data-presentation-id="context:persist"]');
  details.open = true;
  details.dispatchEvent(new Event('toggle'));

  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:persist"]');
  const openAfterReturn = Boolean(details?.open);

  details.open = false;
  details.dispatchEvent(new Event('toggle'));
  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:persist"]');
  return JSON.stringify({
    openAfterReturn,
    closedAfterUserCloseAndReturn: details ? !details.open : false
  });
})()
""");

    Require(result.GetProperty("openAfterReturn").GetBoolean(),
      "An opened disclosure lost state after eviction and rematerialization.");
    Require(result.GetProperty("closedAfterUserCloseAndReturn").GetBoolean(),
      "Closing a disclosure did not clear its remembered open override.");
  }

  private static bool ReadBrowserBoolean(WebView2 webView, string expression)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(
      "Boolean(" + expression + ")");
    PumpUntilCompleted(task, "browser boolean probe");
    return string.Equals(task.Result, "true", StringComparison.OrdinalIgnoreCase);
  }

  private static void WriteDirectionalFixture(string path)
  {
    WriteFixture(path);
  }

  private static void WriteTallTurnFixture(string path)
  {
    const int tallPair = 60;
    const int tallParagraphCount = 240;
    string tallResponse = string.Join(
      "\n\n",
      Enumerable.Range(1, tallParagraphCount).Select(
        index => $"Tall marker paragraph {index:D3}."));
    var records = new List<string>(SourceRecordCount);
    for (int index = 1; index <= PairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T03:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Tall-turn issue 37 request {index:D3}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T03:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = index == tallPair
            ? tallResponse
            : $"Tall-turn issue 37 response {index:D3}."
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static void WriteFollowOffFixture(string path)
  {
    var records = new List<string>();
    const string Context = """
# Context from my IDE setup:

## Active file: sessions/example.jsonl

## Open tabs:
- codex-transcript.md: C:\Users\adria\Downloads\codex-transcript.md
- Download Conversation - 3. Phase 2 Classification Update (6).md: C:\Users\adria\Downloads\Download Conversation - 3. Phase 2 Classification Update (6).md
- test.md: test.md

## My request for Codex:
What time is it in Paris?
""";
    records.Add(JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-07T00:00:00Z",
      payload = new { type = "user_message", message = Context }
    }));
    for (int index = 1; index <= 40; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-07T00:{index % 60:00}:01Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Later assistant turn {index}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-07T00:{index % 60:00}:02Z",
        payload = new
        {
          type = "user_message",
          message = $"Later user turn {index}."
        }
      }));
    }
    File.WriteAllLines(path, records);
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

  private static void SetField<T>(
    object target,
    string fieldName,
    T value)
  {
    FieldInfo? field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
      throw new InvalidOperationException(
        $"Field {fieldName} was not found on {target.GetType().Name}.");
    }
    field.SetValue(target, value);
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
