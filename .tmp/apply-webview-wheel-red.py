from pathlib import Path


path = Path("AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",
        TestInputDiagnosticsCaptureKeyMouseAndFollowState)
'''
new = '''      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",
        TestInputDiagnosticsCaptureKeyMouseAndFollowState),
      ("real-session/webview-wheel-input-is-logged-and-correlated",
        TestWebViewWheelInputIsLoggedAndCorrelated)
'''
if text.count(old) != 1:
  raise RuntimeError("Expected one #77 test-list insertion point.")
text = text.replace(old, new, 1)

method = r'''
  /// <summary>
  /// Reproduces the real-machine #77 gap where a wheel consumed by WebView2
  /// changes Follow through manual scrolling but never appears as the physical
  /// input correlated to that state transition.
  /// </summary>
  private static void TestWebViewWheelInputIsLoggedAndCorrelated()
  {
    DiagnosticLog.Initialize();
    string logPath = DiagnosticLog.FilePath;
    int before = File.Exists(logPath) ? File.ReadLines(logPath).Count() : 0;
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-webview-wheel-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string sessionPath = Path.Combine(root, "webview-wheel.jsonl");
    WriteFixture(sessionPath);

    MainForm? form = null;
    try
    {
      form = new MainForm
      {
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      form.Show();
      _ = form.Handle;
      TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = true },
        dark: false);
      view.SelectSession(
        sessionPath,
        AgentSource.Codex,
        "WebView wheel correlation fixture");
      WaitForTranscriptRender(view);
      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpMessages(1700);

      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = 0;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY:120,
    bubbles:true,
    cancelable:true
  }));
  window.scrollBy(0, Math.max(80, window.innerHeight * 0.2));
})()
""");
      PumpMessages(500);

      JsonElement[] events = File.ReadLines(logPath)
        .Skip(before)
        .Select(line => JsonDocument.Parse(line).RootElement.Clone())
        .ToArray();
      JsonElement? wheel = events.FirstOrDefault(record =>
        record.TryGetProperty("Event", out JsonElement eventElement) &&
        eventElement.GetString() == "input.physical" &&
        record.TryGetProperty("Data", out JsonElement data) &&
        data.TryGetProperty("kind", out JsonElement kindElement) &&
        kindElement.GetString() == "mouse" &&
        data.TryGetProperty("phase", out JsonElement phaseElement) &&
        phaseElement.GetString() == "wheel" &&
        data.TryGetProperty("route", out JsonElement routeElement) &&
        routeElement.GetString() == "webview");
      Require(wheel is JsonElement,
        "A physical wheel handled inside WebView2 was omitted from input.physical diagnostics.");

      JsonElement wheelData = wheel.Value.GetProperty("Data");
      long wheelInputId = wheelData.GetProperty("inputId").GetInt64();
      JsonElement? followChange = events.FirstOrDefault(record =>
        record.TryGetProperty("Event", out JsonElement eventElement) &&
        eventElement.GetString() == "follow.changed" &&
        record.TryGetProperty("Data", out JsonElement data) &&
        data.TryGetProperty("reason", out JsonElement reasonElement) &&
        reasonElement.GetString() == "manual-scroll");
      Require(followChange is JsonElement,
        "The WebView wheel fixture did not produce its manual-scroll Follow transition.");
      JsonElement followData = followChange.Value.GetProperty("Data");
      Require(
        followData.TryGetProperty("physicalInputId", out JsonElement inputElement) &&
        inputElement.ValueKind == JsonValueKind.Number &&
        inputElement.GetInt64() == wheelInputId,
        "The manual-scroll Follow transition was not correlated to the physical WebView wheel.");
    }
    finally
    {
      if (form is not null)
      {
        Application.RemoveMessageFilter(form);
        form.Dispose();
      }
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
anchor = '''  private static long FirstCanonicalWordId(string html)
'''
if text.count(anchor) != 1:
  raise RuntimeError("Expected one #77 method insertion point.")
text = text.replace(anchor, method + anchor, 1)
path.write_text(text, encoding="utf-8")
