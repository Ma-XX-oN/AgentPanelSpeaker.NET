from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str, description: str) -> None:
  text = path.read_text(encoding="utf-8-sig")
  if new in text:
    return
  if old not in text:
    raise SystemExit(f"{description} anchor was not found in {path}.")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


# FollowSpeechChanged now carries the reason for the transition as well as the
# resulting state. Keep the existing issue #37 oracle subscribed to that
# production event rather than weakening or removing the test.
replace_once(
  ROOT / "AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs",
  "      view.FollowSpeechChanged += enabled => notifiedFollowState = enabled;\n",
  "      view.FollowSpeechChanged += (enabled, _) => notifiedFollowState = enabled;\n",
  "issue #37 FollowSpeechChanged subscription")

view_path = ROOT / "AgentPanelSpeaker/TranscriptView.cs"

# Preserve the browser-side evidence that establishes whether the applied
# canonical playback marker was actually visible while Follow was active. This
# deliberately records observable state rather than inferring success from a
# posted marker alone.
replace_once(
  view_path,
  """    data.follow,
    data.wordId);
  chrome.webview.postMessage({
    type: 'playback-applied',
""",
  """    data.follow,
    data.wordId);
  const appliedTarget = data.wordId
    ? document.getElementById(`word-${data.wordId}`)
    : document.querySelector('.word.speaking,.word.paused');
  const appliedRect = appliedTarget?.getBoundingClientRect();
  chrome.webview.postMessage({
    type: 'playback-applied',
""",
  "playback applied target diagnostics")

replace_once(
  view_path,
  """    boundaryWordIndex: currentBoundaryWordIndex,
    boundaryTimestamp: data.boundaryTimestamp,
    javascriptTimestamp: String(performance.now())
""",
  """    boundaryWordIndex: currentBoundaryWordIndex,
    boundaryTimestamp: data.boundaryTimestamp,
    followSpeech,
    targetVisible: !!appliedRect &&
      appliedRect.bottom > 0 && appliedRect.top < window.innerHeight,
    windowStartIndex,
    windowEndIndex,
    scrollY: window.scrollY,
    viewportHeight: window.innerHeight,
    javascriptTimestamp: String(performance.now())
""",
  "browser playback-applied state fields")

replace_once(
  view_path,
  """          boundaryWordIndex = ReadOptionalInt32(root, \"boundaryWordIndex\"),
          boundaryTimestamp = ReadOptionalInt64(root, \"boundaryTimestamp\"),
          javascriptTimestamp = ReadOptionalString(root, \"javascriptTimestamp\"),
          receivedTimestamp = Stopwatch.GetTimestamp()
""",
  """          boundaryWordIndex = ReadOptionalInt32(root, \"boundaryWordIndex\"),
          boundaryTimestamp = ReadOptionalInt64(root, \"boundaryTimestamp\"),
          followSpeech = ReadOptionalBoolean(root, \"followSpeech\"),
          targetVisible = ReadOptionalBoolean(root, \"targetVisible\"),
          windowStartIndex = ReadOptionalInt32(root, \"windowStartIndex\"),
          windowEndIndex = ReadOptionalInt32(root, \"windowEndIndex\"),
          scrollY = ReadOptionalDouble(root, \"scrollY\"),
          viewportHeight = ReadOptionalDouble(root, \"viewportHeight\"),
          javascriptTimestamp = ReadOptionalString(root, \"javascriptTimestamp\"),
          receivedTimestamp = Stopwatch.GetTimestamp()
""",
  "C# playback-applied state logging")
