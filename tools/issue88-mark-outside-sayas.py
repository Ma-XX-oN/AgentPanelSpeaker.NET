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
        XElement? parent = node.Parent;
        bool markOutsideSayAs = local == 0 &&
          parent is not null &&
          string.Equals(
            parent.Name.LocalName,
            "say-as",
            StringComparison.OrdinalIgnoreCase);
        if (markOutsideSayAs)
        {
          parent!.AddBeforeSelf(mark);
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
