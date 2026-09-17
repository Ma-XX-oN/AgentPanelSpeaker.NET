from pathlib import Path
import argparse

SOURCE = Path("AgentPanelSpeaker/TranscriptView.cs")


def replace_once(text: str, label: str, old: str, new: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def patched(text: str) -> str:
  text = replace_once(
    text,
    "snapshot incoming records",
    """  const existingByUnitId = new Map();
""",
    """  const incomingRecords = Array.from(template.content.children);
  const existingByUnitId = new Map();
""")
  text = replace_once(
    text,
    "iterate stable incoming record snapshot",
    """  for (const incoming of template.content.children) {
""",
    """  for (const incoming of incomingRecords) {
""")
  return text


def main() -> int:
  parser = argparse.ArgumentParser()
  parser.add_argument("--check", action="store_true")
  args = parser.parse_args()
  original = SOURCE.read_text(encoding="utf-8")
  result = patched(original)
  if result == original:
    raise RuntimeError("Issue #138 follow-up patch made no changes.")
  if args.check:
    print("Issue #138 follow-up dry-run passed.")
    return 0
  with SOURCE.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(result)
  print("Issue #138 follow-up patch applied.")
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
