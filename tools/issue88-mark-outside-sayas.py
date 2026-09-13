from __future__ import annotations

import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
  target = ROOT / path
  text = target.read_text(encoding="utf-8")
  if old not in text:
    raise RuntimeError(f"{path}: expected patch anchor missing")
  target.write_text(text.replace(old, new, 1), encoding="utf-8")


def add_red() -> None:
  old = '''    Require(ssml.Contains("aps_", StringComparison.Ordinal),
      "Spanning spell-out token produced no canonical ownership bookmark.");
'''
  new = '''    Require(ssml.Contains("aps_", StringComparison.Ordinal),
      "Spanning spell-out token produced no canonical ownership bookmark.");

    var document = System.Xml.Linq.XDocument.Parse(ssml);
    System.Xml.Linq.XElement mark = document
      .Descendants()
      .Single(element =>
        element.Name.LocalName == "mark" &&
        string.Equals(
          element.Attribute("name")?.Value,
          "aps_0",
          StringComparison.Ordinal));
    System.Xml.Linq.XElement sayAs = document
      .Descendants()
      .Single(element => element.Name.LocalName == "say-as");
    Require(mark.Parent == sayAs.Parent,
      "Spell-out ownership mark is not outside the say-as element.");
    System.Xml.Linq.XElement? nextElement = mark
      .NodesAfterSelf()
      .OfType<System.Xml.Linq.XElement>()
      .FirstOrDefault();
    Require(ReferenceEquals(nextElement, sayAs),
      "Spell-out ownership mark is not immediately before say-as.");
    Require(!sayAs.Descendants().Any(element => element.Name.LocalName == "mark"),
      "Spell-out say-as still contains a nested ownership mark.");
'''
  replace_once(
    "AgentPanelSpeaker/Issue88SpeechOwnershipRegressionTestRunner.cs",
    old,
    new)

  old = '''    string service = ReadSource("SpeechService.cs");
    Require(service.Contains("TranscriptPlaybackHighlightMode.Fragment", StringComparison.Ordinal),
      "SpeechService does not explicitly degrade to fragment highlighting.");
'''
  new = '''    string service = ReadSource("SpeechService.cs");
    Require(service.Contains("TranscriptPlaybackHighlightMode.Fragment", StringComparison.Ordinal),
      "SpeechService does not explicitly degrade to fragment highlighting.");
    int degradationStart = service.IndexOf(
      "private void EngineWordTrackingUnavailable(",
      StringComparison.Ordinal);
    int completedStart = service.IndexOf(
      "private void EngineCompleted()",
      degradationStart,
      StringComparison.Ordinal);
    Require(degradationStart >= 0 && completedStart > degradationStart,
      "SpeechService degradation handler could not be isolated.");
    string degradationHandler = service[degradationStart..completedStart];
    Require(degradationHandler.Contains("Activity?.Invoke(", StringComparison.Ordinal),
      "Fragment-level speech degradation is not visible in Activity.");
    Require(degradationHandler.Contains("degradation.Reason", StringComparison.Ordinal),
      "Activity degradation warning does not include the actual reason.");
'''
  replace_once(
    "AgentPanelSpeaker/Issue88SpeechOwnershipRegressionTestRunner.cs",
    old,
    new)


def apply_green() -> None:
  old = '''        var replacement = new List<object>();
        if (prefix.Length != 0)
        {
          replacement.Add(new XText(prefix));
        }
        replacement.Add(new XElement(
          ns + "mark",
          new XAttribute("name", $"aps_{word.WordIndex}")));
        if (spoken.Length != 0)
        {
          replacement.Add(new XText(spoken));
        }
        if (suffix.Length != 0)
        {
          replacement.Add(new XText(suffix));
        }
        node.ReplaceWith(replacement);
'''
  new = '''        var mark = new XElement(
          ns + "mark",
          new XAttribute("name", $"aps_{word.WordIndex}"));
        XElement? sayAs = node
          .Ancestors()
          .FirstOrDefault(element => string.Equals(
            element.Name.LocalName,
            "say-as",
            StringComparison.OrdinalIgnoreCase));
        bool markOutsideSayAs = sayAs is not null;
        if (markOutsideSayAs)
        {
          sayAs!.AddBeforeSelf(mark);
        }

        var replacement = new List<object>();
        if (prefix.Length != 0)
        {
          replacement.Add(new XText(prefix));
        }
        if (!markOutsideSayAs)
        {
          replacement.Add(mark);
        }
        if (spoken.Length != 0)
        {
          replacement.Add(new XText(spoken));
        }
        if (suffix.Length != 0)
        {
          replacement.Add(new XText(suffix));
        }
        node.ReplaceWith(replacement);
'''
  replace_once("AgentPanelSpeaker/SapiSpeechEngine.cs", old, new)

  old = '''      DiagnosticLog.Write("speech.word_tracking_degraded", new
      {
        degradation.Backend,
        degradation.VoiceName,
        degradation.Reason,
        fragment.FragmentId,
        fragment.NodeId,
        fragment.Text,
        highlightMode = "fragment"
      });
      ReportPlaybackPositionLocked(
'''
  new = '''      DiagnosticLog.Write("speech.word_tracking_degraded", new
      {
        degradation.Backend,
        degradation.VoiceName,
        degradation.Reason,
        fragment.FragmentId,
        fragment.NodeId,
        fragment.Text,
        highlightMode = "fragment"
      });
      Activity?.Invoke(
        "Speech word highlighting unavailable; highlighting the full " +
        $"fragment. Backend: {degradation.Backend}; " +
        $"voice: {degradation.VoiceName}; reason: {degradation.Reason}.");
      ReportPlaybackPositionLocked(
'''
  replace_once("AgentPanelSpeaker/SpeechService.cs", old, new)


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("stage", choices=("red", "green"))
  args = parser.parse_args()
  if args.stage == "red":
    add_red()
  else:
    apply_green()


if __name__ == "__main__":
  main()
