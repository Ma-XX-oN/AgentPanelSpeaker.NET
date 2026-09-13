from pathlib import Path

path = Path("AgentPanelSpeaker/SpeechService.cs")
text = path.read_text(encoding="utf-8")

replacements = [
  (
    """  /// <summary>\n  /// Returns whether playback is still inside the first-word rewind grace\n  /// window for the current active history fragment.\n  /// </summary>\n""",
    """  /// <summary>\n  /// Returns whether playback is still inside the rewind reaction window for\n  /// the current active history fragment.\n  /// </summary>\n""",
  ),
  (
    """  /// <summary>\n  /// Prepares first-word rewind grace for a new history playback start.\n  /// The timer itself begins only when a real engine boundary reaches\n  /// fragment-relative word index zero. A PreviousSentence-selected target\n  /// consumes its one-shot suppression instead of re-arming grace.\n  /// </summary>\n""",
    """  /// <summary>\n  /// Prepares the rewind reaction window for a new history playback start.\n  /// The timer itself begins only when a real engine boundary reaches\n  /// fragment-relative word index zero. Every fragment started at word zero,\n  /// including a PreviousSentence destination, gets its own reaction window.\n  /// </summary>\n""",
  ),
  (
    """  /// <summary>\n  /// Starts first-word rewind grace at an actual playback/resume point.\n  /// </summary>\n""",
    """  /// <summary>\n  /// Starts the rewind reaction timer at the actual first-word boundary.\n  /// </summary>\n""",
  ),
]

for old, new in replacements:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"Expected exactly one stale comment block, found {count}")
  text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
print("Issue #84 reaction-window comments corrected.")
