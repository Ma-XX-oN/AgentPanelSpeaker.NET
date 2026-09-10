from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

list_anchor = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn)\n'''
list_replacement = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn),\n      ("virtual-window/follow-off-initial-render-ignores-pending-playback",\n        TestFollowOffInitialRenderIgnoresPendingPlayback),\n      ("virtual-window/user-context-stable-word-map-is-exact",\n        TestUserContextStableWordMapIsExact)\n'''
if list_anchor not in text:
  raise SystemExit('test-list anchor not found')
text = text.replace(list_anchor, list_replacement, 1)

method_anchor = '''  private static void TestBrowserWindowBehaviour()\n'''
methods = r'''  /// <summary>
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
  /// Runs a production User Context shape containing headings, Windows paths,
  /// and a long Open tabs list through the real Core->virtual-window->browser
  /// path. Stable search-word maps and browser-rendered words must have exactly
  /// the same cardinality for every materialized source record.
  /// </summary>
  private static void TestUserContextStableWordMapIsExact()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-stable-map-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteUserContextStableMapFixture(path);

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
        TranscriptSettings.Default with
        {
          FollowSpeech = false,
          SpeakUserContext = true
        },
        dark: false);

      var failures = new List<string>();
      WebView2 webView = ReadField<WebView2>(view, "_webView");
      webView.CoreWebView2.WebMessageReceived += (_, args) =>
      {
        using JsonDocument message = JsonDocument.Parse(args.WebMessageAsJson);
        JsonElement rootElement = message.RootElement;
        if (!rootElement.TryGetProperty("type", out JsonElement type) ||
            type.GetString() != "stable-word-map-failure")
        {
          return;
        }
        failures.Add(rootElement.GetRawText());
      };

      view.SelectSession(path, AgentSource.Codex, "stable-map production");
      WaitForTranscriptRender(view);
      Application.DoEvents();

      Require(failures.Count == 0,
        "Production User Context emitted stable-word-map cardinality failure(s): " +
        string.Join(" | ", failures));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
if method_anchor not in text:
  raise SystemExit('method insertion anchor not found')
text = text.replace(method_anchor, methods + method_anchor, 1)

fixture_anchor = '''  private static void WriteFixture(string path)\n'''
fixtures = r'''  private static void WriteFollowOffFixture(string path)
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

  private static void WriteUserContextStableMapFixture(string path)
  {
    const string Context = """
# Context from my IDE setup:

## Open tabs:
- codex-transcript.md: C:\Users\adria\Downloads\codex-transcript.md
- Download Conversation - 3. Phase 2 Classification Update (6).md: C:\Users\adria\Downloads\Download Conversation - 3. Phase 2 Classification Update (6).md
- test.md: test.md
- Download Conversation - 3. Phase 2 Classification Update (5).md: C:\Users\adria\Downloads\Download Conversation - 3. Phase 2 Classification Update (5).md
- Download Conversation - 3. Phase 2 Classification Update (4).md: C:\Users\adria\Downloads\Download Conversation - 3. Phase 2 Classification Update (4).md
- DESIGN.md: C:\Users\adria\projects\AgentPanelSpeaker.NET\DESIGN.md
- TranscriptView.cs: C:\Users\adria\projects\AgentPanelSpeaker.NET\AgentPanelSpeaker\TranscriptView.cs
- TranscriptVirtualDocument.cs: C:\Users\adria\projects\AgentPanelSpeaker.NET\AgentPanelSpeaker\TranscriptVirtualDocument.cs

## Current work:
1. Verify the virtual transcript window.
2. Keep the current speech cursor stable.
3. Preserve the User Context disclosure.

## My request for Codex:
Continue the current verification without rebuilding stable identities.
""";
    object[] records =
    {
      new
      {
        type = "event_msg",
        timestamp = "2026-09-09T00:00:00Z",
        payload = new { type = "user_message", message = Context }
      },
      new
      {
        type = "event_msg",
        timestamp = "2026-09-09T00:00:01Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Acknowledged. The production window is ready for verification."
        }
      }
    };
    File.WriteAllLines(path, records.Select(JsonSerializer.Serialize));
  }

'''
if fixture_anchor not in text:
  raise SystemExit('fixture insertion anchor not found')
text = text.replace(fixture_anchor, fixtures + fixture_anchor, 1)

path.write_text(text, encoding='utf-8')
