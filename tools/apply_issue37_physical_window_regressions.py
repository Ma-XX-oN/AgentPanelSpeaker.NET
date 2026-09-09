from pathlib import Path
import re

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

constant_anchor = "  private const int SourceRecordCount = PairCount * 2;\n"
constants = constant_anchor + (
  "  private const double MinimumWindowViewportHeights = 5.0;\n"
  "  private const double EdgeTriggerViewportHeights = 2.0;\n"
)
if "MinimumWindowViewportHeights" not in text:
  if text.count(constant_anchor) != 1:
    raise SystemExit("unexpected test constant anchor")
  text = text.replace(constant_anchor, constants, 1)

list_old = '''      ("virtual-window/directional-prefetch-follows-scroll-direction",
        TestDirectionalPrefetchFollowsScrollDirection)
'''
list_new = '''      ("virtual-window/directional-prefetch-follows-scroll-direction",
        TestDirectionalPrefetchFollowsScrollDirection),
      ("virtual-window/physical-window-keeps-five-viewports-materialized",
        TestPhysicalWindowKeepsFiveViewportsMaterialized),
      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",
        TestPlaybackVoiceCursorPrefetchesInsideTallTurn)
'''
if "physical-window-keeps-five-viewports-materialized" not in text:
  if text.count(list_old) != 1:
    raise SystemExit("unexpected virtual-window test list anchor")
  text = text.replace(list_old, list_new, 1)

# The old directional fixture deliberately inflated every turn to 32 KB to
# force record-count trigger bands to overlap.  That is the behaviour under
# correction: the oracle must use physical browser geometry, not oversized
# turns.  Keep the normal compact fixture instead.
fixture_pattern = re.compile(
  r'''  private static void WriteDirectionalFixture\(string path\)\n  \{.*?\n  \}\n\n  private static void WriteFixture''',
  re.S)
fixture_replacement = '''  private static void WriteDirectionalFixture(string path)
  {
    WriteFixture(path);
  }

  private static void WriteTallTurnFixture(string path)
  {
    const int tallPair = 60;
    const int tallParagraphCount = 240;
    string tallResponse = string.Join(
      "\\n\\n",
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

  private static void WriteFixture'''
if "WriteTallTurnFixture" not in text:
  text, count = fixture_pattern.subn(fixture_replacement, text, count=1)
  if count != 1:
    raise SystemExit("unexpected directional fixture method")

insert_anchor = "  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n"
if "TestPhysicalWindowKeepsFiveViewportsMaterialized" not in text:
  raise SystemExit("test list changed without methods insertion guard")

if "private static void TestPhysicalWindowKeepsFiveViewportsMaterialized()" not in text:
  methods = r'''  /// <summary>
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
        string.Empty,
        null);
      PumpUntilCompleted(middleWindow, "middle physical virtual window");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Physical-window fixture did not retain unloaded content on both sides.");

      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0]?.getBoundingClientRect();
  const last = records[records.length - 1]?.getBoundingClientRect();
  return JSON.stringify({
    recordCount: records.length,
    innerHeight: window.innerHeight,
    materializedHeight: first && last ? last.bottom - first.top : 0
  });
})()
""");
      double innerHeight = probe.GetProperty("innerHeight").GetDouble();
      double materializedHeight =
        probe.GetProperty("materializedHeight").GetDouble();
      Require(
        materializedHeight >= innerHeight * MinimumWindowViewportHeights,
        "Materialized vwindow is shorter than the required physical minimum: " +
        $"height={materializedHeight:F1}, viewport={innerHeight:F1}, " +
        $"minimumViewports={MinimumWindowViewportHeights:F1}.");
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
          tallIdentity.SourceId,
          out int tallIndex),
        "Tall playback turn is absent from the virtual document.");

      int focalIndex = -1;
      for (int candidate = 0; candidate < document.Count; ++candidate)
      {
        TranscriptWindow window = document.CreateWindow(candidate);
        if (window.StartIndex > 0 &&
            window.EndIndex < document.Count - 1 &&
            tallIndex >= window.StartIndex &&
            tallIndex <= window.EndIndex &&
            window.EndIndex - tallIndex <= 2)
        {
          focalIndex = candidate;
          break;
        }
      }
      Require(
        focalIndex >= 0,
        "Could not position the compact tall-turn fixture near a physical " +
        "materialized-window edge without oversized synthetic turns.");

      Task positionedWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        focalIndex,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(positionedWindow, "tall-turn edge-positioned window");

      string fragment = tallIdentity.Segments.Last(segment =>
        segment.Contains("Tall marker paragraph", StringComparison.Ordinal));
      var matches = SpeechTokenization.Matches(fragment);
      Require(matches.Count > 0, "Tall playback fragment contained no speech words.");
      int wordIndex = matches.Count - 1;
      string word = matches[wordIndex].Value;
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
        () => ReadBrowserBoolean(
          webView,
          "document.querySelector('.word.active,.word.paused') !== null"),
        "voice cursor inside the tall materialized turn");
      PumpMessages(250);

      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const marker = document.querySelector('.word.active,.word.paused');
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0]?.getBoundingClientRect();
  const last = records[records.length - 1]?.getBoundingClientRect();
  const markerRect = marker?.getBoundingClientRect();
  const bottomSpacer = document.querySelector(
    '.virtual-spacer[data-virtual-spacer="bottom"]');
  return JSON.stringify({
    innerHeight: window.innerHeight,
    materializedHeight: first && last ? last.bottom - first.top : 0,
    cursorDistanceToBottom: markerRect && last
      ? last.bottom - markerRect.bottom
      : Number.MAX_SAFE_INTEGER,
    bottomSpacerHeight: bottomSpacer?.getBoundingClientRect().height ?? 0
  });
})()
""");
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
        "Tall-turn voice cursor did not enter the lower physical trigger zone: " +
        $"distance={cursorDistanceToBottom:F1}, viewport={innerHeight:F1}.");

      PumpMessages(600);
      string[] playbackReasons = windowShiftReasons
        .Skip(shiftsBeforePlayback)
        .ToArray();
      Require(
        playbackReasons.Any(reason => reason == "playback-down"),
        "Voice cursor entered the lower physical vwindow trigger zone while " +
        "remaining inside one tall turn, but no playback-down shift was requested.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
  if text.count(insert_anchor) != 1:
    raise SystemExit("unexpected helper insertion anchor")
  text = text.replace(insert_anchor, methods + insert_anchor, 1)

path.write_text(text, encoding="utf-8")
