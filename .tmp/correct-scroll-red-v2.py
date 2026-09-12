from pathlib import Path

path = Path("AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  text = text.replace(old, new, 1)


replace_once(
  '''  const html = [start, start + 1, end].map(index =>
    '<div class="virtual-record" data-virtual-index="' + index + '">' +
    '<span class="record-anchor" data-jsonl-record="' + (index + 1) + '"></span>' +
    '<p>convergence record ' + index + '</p></div>').join('');
  replaceTranscriptWindow(
''',
  '''  const html = [start, start + 1, end].map(index =>
    '<div class="virtual-record" data-virtual-index="' + index + '">' +
    '<span class="record-anchor" data-jsonl-record="' + (index + 1) + '"></span>' +
    '<p>convergence record ' + index + '</p></div>').join('');
  // Keep the replacement in its estimated virtual position. The first
  // materialized records deliberately straddle the viewport's upper edge so
  // geometry, rather than a surviving physical-intent timer, must request the
  // next adjacent canonical batch.
  const topSpacerHeight = Math.max(0, window.scrollY - 100);
  replaceTranscriptWindow(
''',
  "scroll convergence virtual position",
)
replace_once(
  '''    start,
    end,
    0,
    5000,
''',
  '''    start,
    end,
    topSpacerHeight,
    5000,
''',
  "scroll convergence top spacer",
)

replace_once(
  '''  [System.Runtime.InteropServices.DllImport(
    "user32.dll",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  private static extern IntPtr SendMessageForEditorAcceptance(
''',
  '''  [System.Runtime.InteropServices.DllImport(
    "user32.dll",
    EntryPoint = "SendMessageW",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  private static extern IntPtr SendMessageForEditorAcceptance(
''',
  "editor native typing probe",
)

path.write_text(text, encoding="utf-8")
print("Corrected scroll RED geometry and editor native typing probe.")
