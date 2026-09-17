from pathlib import Path
import argparse

SOURCE = Path("AgentPanelSpeaker/TranscriptView.cs")


def replace_once(text: str, label: str, old: str, new: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


def patched(text: str) -> str:
  return replace_once(
    text,
    "raw transcript compatibility decision",
    """  for (const node of template.content.childNodes) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent.trim()) continue;
    if (node.nodeType !== Node.ELEMENT_NODE ||
        !node.classList.contains('virtual-record')) {
      throw new Error(
        'Virtual transcript window contains a non-record top-level node.');
    }
  }

  const incomingRecords = Array.from(template.content.children);
""",
    """  const topLevelNodes = Array.from(template.content.childNodes)
    .filter(node =>
      node.nodeType !== Node.TEXT_NODE || node.textContent.trim());
  if (topLevelNodes.some(node =>
      node.nodeType !== Node.ELEMENT_NODE ||
      !node.classList.contains('virtual-record'))) {
    return false;
  }

  const incomingRecords = Array.from(template.content.children);
""")


def main() -> int:
  parser = argparse.ArgumentParser()
  parser.add_argument("--check", action="store_true")
  args = parser.parse_args()

  original = SOURCE.read_text(encoding="utf-8")
  result = patched(original)
  if result == original:
    raise RuntimeError("Issue #138 raw transcript patch made no changes.")
  if args.check:
    print("Issue #138 raw transcript patch dry-run passed.")
    return 0

  with SOURCE.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(result)
  print("Issue #138 raw transcript patch applied.")
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
