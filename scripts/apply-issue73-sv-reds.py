from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one match, found {count}")
  path.write_text(text.replace(old, new), encoding="utf-8")


issue73 = ROOT / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
replace_once(
  issue73,
  '''      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",\n        TestOwnedPopupFocusPreservesHeldCtrl)\n''',
  '''      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",\n        TestOwnedPopupFocusPreservesHeldCtrl),\n      ("ctrl-click-voice-pointer/spoken-md-markup-split-token-is-selectable",\n        TestSpokenMarkdownMarkupSplitTokenIsSelectable),\n      ("ctrl-click-voice-pointer/spoken-quoted-markup-split-token-is-selectable",\n        TestSpokenQuotedMarkupSplitTokenIsSelectable)\n''')
replace_once(
  issue73,
  '''  private static TranscriptRangeProbe[] ReadRanges(object? value)\n''',
  r'''  /// <summary>
  /// The real SV1 failure: an enabled md fence visibly renders Markdown source
  /// where inline backticks split one spoken token (turn_ids) into DOM pieces.
  /// Every visible lexical piece belonging to that spoken token must advertise
  /// the same authoritative speech coordinate.
  /// </summary>
  private static void TestSpokenMarkdownMarkupSplitTokenIsSelectable()
  {
    TestMarkupSplitTokenMapping(
      recordNumber: 1,
      nodeId: 501,
      html: "<pre><code class=\"language-md\">&gt; Are `turn_id`s the id guids with author.role user?</code></pre>",
      description: "spoken md fence");
  }

  /// <summary>
  /// The real SV2 failure: canonical HTML can render the same spoken token
  /// across an inline code element and adjacent text. DOM element boundaries
  /// must not make voiced text unselectable.
  /// </summary>
  private static void TestSpokenQuotedMarkupSplitTokenIsSelectable()
  {
    TestMarkupSplitTokenMapping(
      recordNumber: 2,
      nodeId: 502,
      html: "<blockquote><p>Are <code>turn_id</code>s the id guids with author.role user?</p></blockquote>",
      description: "quoted User Context markup");
  }

  private static void TestMarkupSplitTokenMapping(
    int recordNumber,
    long nodeId,
    string html,
    string description)
  {
    const string spoken = "Are turn_ids the id guids with author.role user?";
    using BrowserFixture fixture = BrowserFixture.Create();
    string script = "(() => {" +
      "replaceTranscript(" + JsonSerializer.Serialize(
        $"<span class=\"record-anchor\" data-jsonl-record=\"{recordNumber}\"></span>{html}") +
      ",false,[{NodeId:" + nodeId + ",RecordNumber:" + recordNumber +
      ",Segments:[" + JsonSerializer.Serialize(spoken) + "]}]);" +
      "setSeekableVoiceRanges([{NodeId:" + nodeId +
      ",StartNodeWordIndex:0,WordCount:10}]);" +
      "setVoicePointerSelectMode(true);" +
      "const pieces=[...document.querySelectorAll('.word')].filter(w=>" +
        "w.textContent==='turn_id'||w.textContent==='s');" +
      "return JSON.stringify({pieces:pieces.map(w=>({" +
        "text:w.textContent,selectable:w.classList.contains('voice-selectable')," +
        "excluded:w.classList.contains('voice-excluded')," +
        "nodeId:Number(w.dataset.nodeId||0)," +
        "nodeWordIndex:Number(w.dataset.nodeWordIndex??-1)}))});" +
      "})()";
    JsonElement result = ExecuteJsonProbe(fixture.WebView, script);
    JsonElement pieces = result.GetProperty("pieces");
    Require(pieces.GetArrayLength() == 2,
      $"{description} did not expose the expected turn_id + s DOM pieces.");
    foreach (JsonElement piece in pieces.EnumerateArray())
    {
      Require(piece.GetProperty("selectable").GetBoolean() &&
          !piece.GetProperty("excluded").GetBoolean(),
        $"{description} contains voiced text that is not Ctrl-selectable.");
      Require(piece.GetProperty("nodeId").GetInt64() == nodeId,
        $"{description} split piece has the wrong speech node.");
      Require(piece.GetProperty("nodeWordIndex").GetInt32() == 1,
        $"{description} split piece did not map to spoken token turn_ids at ordinal 1.");
    }
  }

  private static TranscriptRangeProbe[] ReadRanges(object? value)
''')

issue37 = ROOT / "AgentPanelSpeaker" / "Issue37VirtualWindowRegressionTestRunner.cs"
replace_once(
  issue37,
  '''      ("virtual-window/manual-scroll-preserves-disclosure-state",\n        TestManualScrollPreservesDisclosureState)\n''',
  '''      ("virtual-window/manual-scroll-preserves-disclosure-state",\n        TestManualScrollPreservesDisclosureState),\n      ("virtual-window/disclosure-open-state-survives-eviction-and-close-clears-override",\n        TestDisclosureOpenStateSurvivesEvictionAndCloseClearsOverride)\n''')
replace_once(
  issue37,
  '''  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n''',
  r'''  /// <summary>
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
(async () => {
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
  await new Promise(resolve => setTimeout(resolve, 0));

  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:persist"]');
  const openAfterReturn = Boolean(details?.open);

  details.open = false;
  details.dispatchEvent(new Event('toggle'));
  await new Promise(resolve => setTimeout(resolve, 0));
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
''')

print("Staged SV1/SV2 voiced-markup REDs and stronger SV3 disclosure lifecycle RED.")
