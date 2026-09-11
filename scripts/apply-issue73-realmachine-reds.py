from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

issue73 = ROOT / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
text = issue73.read_text(encoding="utf-8")
old = '''      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",\n        TestBrowserAffordanceAndClickContract),\n      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",\n        TestHostCtrlRoutingDoesNotRequireWebViewFocus)'''
new = '''      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",\n        TestBrowserAffordanceAndClickContract),\n      ("ctrl-click-voice-pointer/punctuation-rich-browser-coordinate-matches-speech-history",\n        TestPunctuationRichBrowserCoordinateMatchesSpeechHistory),\n      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",\n        TestHostCtrlRoutingDoesNotRequireWebViewFocus)'''
if old not in text:
  raise SystemExit("issue73 test-list insertion point not found")
text = text.replace(old, new, 1)
marker = '''  /// <summary>\n  /// Ctrl is a momentary discoverability affordance: only currently eligible\n'''
method = r'''  /// <summary>
  /// Browser node-global coordinates must use the same speech-token ordinal
  /// space as SpeechService.  Punctuation-rich path/file-name fragments from
  /// the real node-308 failure must not make a click in the later (5).md entry
  /// seek backward into the preceding (6).md fragment.
  /// </summary>
  private static void TestPunctuationRichBrowserCoordinateMatchesSpeechHistory()
  {
    const long NodeId = 308;
    const string first =
      "Phase 2 Classification Update (6).md: c:\\Users\\adria\\Downloads\\" +
      "Download Conversation - 3.";
    const string second =
      "Phase 2 Classification Update (5).md: c:\\Users\\adria\\Downloads\\" +
      "Download Conversation - 3.";

    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          NodeId,
          ContentCategory.UserContext,
          SpeechFragmentKind.Prose,
          first),
        new SpeechFragment(
          NodeId,
          ContentCategory.UserContext,
          SpeechFragmentKind.Prose,
          second)
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    var secondTokens = SpeechTokenization.Matches(second);
    int secondFive = Enumerable.Range(0, secondTokens.Count).First(index =>
      string.Equals(secondTokens[index].Value, "5", StringComparison.Ordinal));
    int expectedNodeWordIndex =
      SpeechTokenization.Matches(first).Count + secondFive;

    using BrowserFixture fixture = BrowserFixture.Create();
    var seekMessage = new TaskCompletionSource<JsonElement>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    void MessageReceived(
      object? sender,
      CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
      using JsonDocument document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (root.TryGetProperty("type", out JsonElement typeElement) &&
          string.Equals(typeElement.GetString(), "find-seek", StringComparison.Ordinal) &&
          root.TryGetProperty("source", out JsonElement sourceElement) &&
          string.Equals(sourceElement.GetString(), "ctrl-click", StringComparison.Ordinal))
      {
        seekMessage.TrySetResult(root.Clone());
      }
    }

    fixture.WebView.CoreWebView2.WebMessageReceived += MessageReceived;
    try
    {
      ExecuteScript(
        fixture.WebView,
        """
(() => {
  const first = 'Phase 2 Classification Update (6).md: c:\\Users\\adria\\Downloads\\Download Conversation - 3.';
  const second = 'Phase 2 Classification Update (5).md: c:\\Users\\adria\\Downloads\\Download Conversation - 3.';
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span><p>' +
      first + ' ' + second + '</p>',
    false,
    [{NodeId:308, RecordNumber:1, Segments:[first, second]}]);
  setSeekableVoiceRanges([
    {NodeId:308, StartNodeWordIndex:0, WordCount:200}
  ]);
  setVoicePointerSelectMode(true);
  const fives = [...document.querySelectorAll('.word.voice-selectable')]
    .filter(word => word.textContent === '5');
  if (fives.length !== 1) {
    throw new Error('Expected exactly one selectable 5 token in the later entry.');
  }
  fives[0].dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
      PumpUntilCompleted(
        seekMessage.Task,
        "issue #73 punctuation-rich Ctrl+click seek message");
      JsonElement seek = seekMessage.Task.Result;
      int postedIndex = seek.GetProperty("nodeWordIndex").GetInt32();
      Require(seek.GetProperty("nodeId").GetInt64() == NodeId,
        "Punctuation-rich Ctrl+click changed the node identity.");
      Require(postedIndex == expectedNodeWordIndex,
        $"Browser posted node word {postedIndex}; speech history requires " +
        $"{expectedNodeWordIndex} for the clicked 5 in the later (5).md entry.");

      TranscriptPlaybackPosition? position = null;
      speech.PlaybackPositionChanged += value => position = value;
      Require(
        speech.TrySeekToTranscriptWord(NodeId, postedIndex, out string sought) &&
          string.Equals(sought, second, StringComparison.Ordinal),
        "The browser coordinate did not resolve to the later (5).md speech fragment.");
      Require(position is not null &&
          position.State == TranscriptPlaybackState.Paused &&
          position.NodeId == NodeId &&
          string.Equals(position.Word, "5", StringComparison.Ordinal),
        "The browser coordinate did not place the paused voice cursor on the clicked 5 token.");
    }
    finally
    {
      fixture.WebView.CoreWebView2.WebMessageReceived -= MessageReceived;
    }
  }

'''
if marker not in text:
  raise SystemExit("issue73 method insertion point not found")
text = text.replace(marker, method + marker, 1)
issue73.write_text(text, encoding="utf-8")

issue26 = ROOT / "AgentPanelSpeaker" / "Issue26UserContextSpeechRegressionTestRunner.cs"
text = issue26.read_text(encoding="utf-8")
old = '''    Require(\n      speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),\n      "SpeakUserContext ON did not make retained User Context playback-eligible.");\n    speech.MoveToPausedLiveEnd();\n\n    popup.SetSettings(\n      popup.Settings with { SpeakUserContext = false },\n      dark: false);\n    InvokeVoid(form, "TranscriptSettingsChanged");\n    Application.DoEvents();\n\n    Require(monitor.IsRunning,\n'''
new = '''    Require(\n      speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),\n      "SpeakUserContext ON did not make retained User Context playback-eligible.");\n    TranscriptPlaybackPosition? relocation = null;\n    speech.PlaybackPositionChanged += position => relocation = position;\n\n    popup.SetSettings(\n      popup.Settings with { SpeakUserContext = false },\n      dark: false);\n    relocation = null;\n    InvokeVoid(form, "TranscriptSettingsChanged");\n    Application.DoEvents();\n\n    Require(monitor.IsRunning,\n'''
if old not in text:
  raise SystemExit("issue26 toggle insertion point not found")
text = text.replace(old, new, 1)
old = '''    Require(\n      !speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),\n      "SpeakUserContext OFF did not immediately suppress retained User Context.");\n'''
new = '''    Require(\n      !speech.TrySeekToTranscriptWord(context.NodeId, 0, out _),\n      "SpeakUserContext OFF did not immediately suppress retained User Context.");\n    Require(relocation is not null,\n      "Disabling SpeakUserContext left the paused cursor on newly ineligible User Context.");\n    Require(relocation.State == TranscriptPlaybackState.Paused &&\n        relocation.NodeId == user.NodeId &&\n        string.Equals(relocation.FragmentText, user.Text, StringComparison.Ordinal) &&\n        string.Equals(\n          relocation.Word,\n          SpeechTokenization.First(user.Text),\n          StringComparison.Ordinal),\n      "Disabling SpeakUserContext did not move the paused cursor forward to the next eligible User fragment.");\n'''
if old not in text:
  raise SystemExit("issue26 relocation assertion point not found")
text = text.replace(old, new, 1)
issue26.write_text(text, encoding="utf-8")
