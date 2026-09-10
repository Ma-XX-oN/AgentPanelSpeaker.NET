from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

list_anchor = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn)\n'''
list_replacement = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn),\n      ("virtual-window/shift-preserves-entire-physically-visible-range",\n        TestShiftPreservesEntirePhysicallyVisibleRange)\n'''
if text.count(list_anchor) != 1:
  raise SystemExit('test-list anchor mismatch')
text = text.replace(list_anchor, list_replacement, 1)

method_anchor = '''  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n'''
method = r'''  /// <summary>
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

'''
if text.count(method_anchor) != 1:
  raise SystemExit('method insertion anchor mismatch')
text = text.replace(method_anchor, method + method_anchor, 1)
path.write_text(text, encoding='utf-8')
