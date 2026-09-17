from pathlib import Path
import argparse

SOURCE = Path("AgentPanelSpeaker/TranscriptView.cs")


def replace_once(text: str, label: str, old: str, new: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def patch_source(text: str) -> str:
  return replace_once(
    text,
    "reconciliation applicability boundary",
    """  for (const node of template.content.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent.trim()) continue;
    if (node.nodeType !== Node.ELEMENT_NODE ||
        !node.classList.contains('virtual-record')) {
      throw new Error(
        'Virtual transcript window contains a non-record top-level node.');
    }
  }

  const incomingRecords = Array.from(template.content.children);
  const existingRecords = Array.from(transcript.children)
    .filter(child => child.classList.contains('virtual-record'));
  if (incomingRecords.some(record => !virtualRecordUnitId(record)) ||
      existingRecords.some(record => !virtualRecordUnitId(record))) {
    return false;
  }
""",
    """  for (const node of template.content.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent.trim()) continue;
    if (node.nodeType !== Node.ELEMENT_NODE ||
        !node.classList.contains('virtual-record')) {
      return false;
    }
  }

  const incomingRecords = Array.from(template.content.children);
  const existingRecords = Array.from(transcript.children)
    .filter(child => child.classList.contains('virtual-record'));
  if (existingRecords.length === 0 ||
      incomingRecords.some(record => !virtualRecordUnitId(record)) ||
      existingRecords.some(record => !virtualRecordUnitId(record))) {
    return false;
  }
""")


def main() -> int:
  parser = argparse.ArgumentParser()
  parser.add_argument("--check", action="store_true")
  args = parser.parse_args()

  original = SOURCE.read_text(encoding="utf-8")
  patched = patch_source(original)
  if patched == original:
    raise RuntimeError("Issue #138 reconciliation-boundary patch made no changes.")

  if args.check:
    print("Issue #138 reconciliation-boundary patch dry-run passed.")
    return 0

  with SOURCE.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(patched)
  print("Issue #138 reconciliation-boundary patch applied.")
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
