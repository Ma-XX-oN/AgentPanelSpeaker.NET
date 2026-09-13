#!/usr/bin/env python3
"""Correct first-token bookmark ownership after the temporary #88 applicator."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "AgentPanelSpeaker" / "SapiSpeechEngine.cs"

text = PATH.read_text(encoding="utf-8")

old_bookmark = '''        if (wordIndex != 0)
        {
          replacement.Add(new XElement(
            ns + "mark",
            new XAttribute("name", $"aps_{word.WordIndex}")));
        }
'''
new_bookmark = '''        replacement.Add(new XElement(
          ns + "mark",
          new XAttribute("name", $"aps_{word.WordIndex}")));
'''
if text.count(old_bookmark) != 1:
  raise RuntimeError("Expected exactly one conditional first-token bookmark block.")
text = text.replace(old_bookmark, new_bookmark, 1)

old_seed = '''    var raw = new List<SpeechWordBoundary>();
    SpeechMarkupWord first = words[0];
    raw.Add(new SpeechWordBoundary(
      TimeSpan.Zero,
      first.WordIndex,
      first.CharacterStart,
      first.CharacterLength,
      first.Text,
      Exact: true));
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
'''
new_seed = '''    var raw = new List<SpeechWordBoundary>();
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
'''
if text.count(old_seed) != 1:
  raise RuntimeError("Expected exactly one synthetic first-token boundary seed.")
text = text.replace(old_seed, new_seed, 1)

PATH.write_text(text, encoding="utf-8")
