from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      int shiftsBeforePlayback = windowShiftReasons.Count;
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
'''
new = '''      string fragmentJson = JsonSerializer.Serialize(fragment);
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
          "'.virtual-spacer[data-virtual-spacer=\\\"bottom\\\"]');" +
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
'''

if text.count(old) != 1:
  raise SystemExit("tall-turn active-marker oracle block did not match exactly once")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
