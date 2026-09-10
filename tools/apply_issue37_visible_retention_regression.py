from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

list_anchor = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn)\n'''
list_replacement = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn),\n      ("virtual-window/visible-turn-survives-shift-until-outside-viewport",\n        TestVisibleTurnSurvivesShiftUntilOutsideViewport)\n'''
if list_anchor not in text:
  raise SystemExit('test-list anchor not found')
text = text.replace(list_anchor, list_replacement, 1)

method_anchor = '''  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n'''
method = r'''  /// <summary>
  /// A virtual-window shift must not evict any Core atomic unit that still
  /// intersects the physical browser viewport.  This fixture mixes one taller
  /// turn with compact neighbours so the five-viewport height floor alone is
  /// insufficient to protect the still-visible leading turn during a downward
  /// shift.
  /// </summary>
  private static void TestVisibleTurnSurvivesShiftUntilOutsideViewport()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-visible-retention-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-visible-retention.jsonl");
    WriteVisibleRetentionFixture(path);

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
        "WebView2 core initialization for visible-turn retention regression");

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 visible-turn retention fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for visible-turn retention regression");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int tallIndex = document.Records
        .Select((record, index) => new { record, index })
        .Where(item => item.record.Html.Contains(
          "Visible-retention tall paragraph 001",
          StringComparison.Ordinal))
        .Select(item => item.index)
        .DefaultIfEmpty(-1)
        .First();
      Require(tallIndex >= 0 && tallIndex + 1 < document.Count,
        "Visible-retention fixture did not expose the taller atomic turn.");

      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        tallIndex + 1,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        middleWindow,
        "mixed-height virtual-window precondition for visible-turn retention");
      PumpMessages(300);
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Visible-retention fixture did not leave unloaded content on both sides.");
      Require(
        ReadField<int>(view, "_windowStartIndex") == tallIndex,
        "Visible-retention precondition did not place the taller turn at the " +
        "leading materialized edge.");

      JsonElement positioned = ExecuteJsonProbe(
        webView,
        $$"""
(() => {
  const tall = document.querySelector(
    '.virtual-record[data-virtual-index="{{tallIndex}}"]');
  if (!tall) return JSON.stringify({ok:false});
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 0.40;
  window.scrollBy(0, tall.getBoundingClientRect().bottom - desiredBottom);
  return JSON.stringify({
    ok:true,
    tallHeight:tall.getBoundingClientRect().height,
    innerHeight:window.innerHeight
  });
})()
""");
      Require(positioned.GetProperty("ok").GetBoolean(),
        "Could not position the taller visible turn.");
      double tallHeight = positioned.GetProperty("tallHeight").GetDouble();
      double viewportHeight = positioned.GetProperty("innerHeight").GetDouble();
      Require(tallHeight > viewportHeight * 0.75 &&
              tallHeight < viewportHeight * 2.0,
        "Visible-retention fixture taller turn did not have the intended " +
        $"physical size: height={tallHeight:F1}, viewport={viewportHeight:F1}.");
      PumpMessages(650);

      JsonElement before = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const visible = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  });
  return JSON.stringify({
    visible:visible.map(record => Number(record.dataset.virtualIndex)),
    firstVisibleTop:visible.length ? visible[0].getBoundingClientRect().top : null,
    firstVisibleBottom:visible.length ? visible[0].getBoundingClientRect().bottom : null,
    innerHeight:window.innerHeight
  });
})()
""");
      int[] visibleBefore = before.GetProperty("visible")
        .EnumerateArray()
        .Select(item => item.GetInt32())
        .ToArray();
      Require(visibleBefore.Length >= 2 && visibleBefore[0] == tallIndex,
        "Visible-retention precondition did not leave the taller leading turn " +
        "physically visible together with following content.");
      Require(before.GetProperty("firstVisibleBottom").GetDouble() > 0,
        "Taller leading turn had already left the physical viewport.");

      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      ExecuteVoidScript(
        webView,
        "requestVirtualShift(1, 'scroll-down');");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != beforeStart ||
          ReadField<int>(view, "_windowEndIndex") != beforeEnd,
        "downward shift to replace the visible-retention window",
        timeoutMilliseconds: 5000);
      PumpMessages(250);

      JsonElement after = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const materialized = records.map(record => Number(record.dataset.virtualIndex));
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  }).map(record => Number(record.dataset.virtualIndex));
  return JSON.stringify({materialized, intersecting});
})()
""");
      var materializedAfter = after.GetProperty("materialized")
        .EnumerateArray()
        .Select(item => item.GetInt32())
        .ToHashSet();
      int[] evictedVisible = visibleBefore
        .Where(index => !materializedAfter.Contains(index))
        .ToArray();
      Require(
        evictedVisible.Length == 0,
        "Virtual-window replacement evicted turn(s) that still intersected " +
        "the physical viewport before the shift: " +
        string.Join(", ", evictedVisible) + ".");
      Require(
        after.GetProperty("intersecting").GetArrayLength() > 0,
        "Virtual-window replacement left no materialized turn intersecting " +
        "the physical viewport.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if method_anchor not in text:
  raise SystemExit('method insertion anchor not found')
text = text.replace(method_anchor, method + method_anchor, 1)

fixture_anchor = '''  private static void WriteTallTurnFixture(string path)\n'''
fixture = r'''  private static void WriteVisibleRetentionFixture(string path)
  {
    const int tallPair = PairCount / 2;
    const int tallParagraphCount = 34;
    string tallResponse = string.Join(
      "\n\n",
      Enumerable.Range(1, tallParagraphCount).Select(
        index => $"Visible-retention tall paragraph {index:D3}. " +
          "This line gives the turn enough physical height to remain partially " +
          "visible when the neighbouring window is shifted."));
    var records = new List<string>(SourceRecordCount);
    for (int index = 1; index <= PairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T04:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Visible-retention issue 37 request {index:D3}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T04:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = index == tallPair
            ? tallResponse
            : $"Visible-retention issue 37 response {index:D3}."
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

'''
if fixture_anchor not in text:
  raise SystemExit('fixture insertion anchor not found')
text = text.replace(fixture_anchor, fixture + fixture_anchor, 1)

path.write_text(text, encoding='utf-8')
