from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

list_anchor = '''      ("virtual-window/shift-preserves-entire-physically-visible-range",\n        TestShiftPreservesEntirePhysicallyVisibleRange)\n'''
list_replacement = '''      ("virtual-window/shift-preserves-entire-physically-visible-range",\n        TestShiftPreservesEntirePhysicallyVisibleRange),\n      ("virtual-window/follow-off-initial-render-ignores-pending-playback",\n        TestFollowOffInitialRenderIgnoresPendingPlayback)\n'''
if text.count(list_anchor) != 1:
  raise SystemExit('follow-off test-list anchor mismatch')
text = text.replace(list_anchor, list_replacement, 1)

method_anchor = '''  /// <summary>\n  /// Loads a transcript large enough to exceed one virtual window through the\n'''
method = r'''  /// <summary>
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

'''
if text.count(method_anchor) != 1:
  raise SystemExit('follow-off method insertion anchor mismatch')
text = text.replace(method_anchor, method + method_anchor, 1)

fixture_anchor = '''  private static void WriteFixture(string path)\n'''
fixture = r'''  private static void WriteFollowOffFixture(string path)
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

'''
if text.count(fixture_anchor) != 1:
  raise SystemExit('follow-off fixture insertion anchor mismatch')
text = text.replace(fixture_anchor, fixture + fixture_anchor, 1)

path.write_text(text, encoding='utf-8')
