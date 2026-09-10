from pathlib import Path
import re

path = Path(__file__).with_name("apply-issue65-production.py")
lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
out = []
skipping = False
inserted = False
for line in lines:
  if line.startswith("# remove stable word timing phase after node scopes"):
    out.append(line)
    out.append("text = text.replace(\n")
    out.append("  \"  phaseStarted = performance.now();\\n\"\n")
    out.append("  \"  const stableWordScopesMilliseconds = performance.now() - phaseStarted;\\n\",\n")
    out.append("  \"\")\n")
    skipping = True
    inserted = True
    continue
  if skipping:
    if line.startswith("text = text.replace(\"    wordMapCount:"):
      skipping = False
      out.append(line)
    continue
  out.append(line)

if not inserted:
  raise RuntimeError("stable-word timing patch marker was not found")
path.write_text("".join(out), encoding="utf-8", newline="\n")

code = compile(path.read_text(encoding="utf-8"), str(path), "exec")
exec(code, {"__name__": "__main__", "__file__": str(path)})

# Use C# verbatim strings for the structure-probe anchor regex so the regex
# escape sequences are not interpreted as C# string escapes.
probe_path = path.parents[1] / "AgentPanelSpeaker" / "TranscriptStructureProbe.cs"
probe = probe_path.read_text(encoding="utf-8")
pattern = (
  r"  private static readonly Regex RecordAnchorRegex = new\(\n"
  r"    .*?\n"
  r"    .*?\n"
  r"    RegexOptions\.Compiled \| RegexOptions\.CultureInvariant \| RegexOptions\.IgnoreCase\);"
)
replacement = (
  '  private static readonly Regex RecordAnchorRegex = new(\n'
  '    @"<span\\s+class=""record-anchor""[^>]*" +\n'
  '    @"data-jsonl-record=""(?<record>[^""]*)""[^>]*></span>",\n'
  '    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);'
)
probe, count = re.subn(
  pattern,
  lambda _match: replacement,
  probe,
  count=1,
  flags=re.S)
if count != 1:
  raise RuntimeError(f"structure probe anchor-regex repair matched {count} declarations")
probe_path.write_text(probe, encoding="utf-8", newline="\n")

# TranscriptVirtualDocument already has a private IsVisible(int virtualIndex).
# Keep the record-number lookup semantically explicit instead of overloading the
# same int signature.
virtual_path = path.parents[1] / "AgentPanelSpeaker" / "TranscriptVirtualDocument.cs"
virtual = virtual_path.read_text(encoding="utf-8")
old = "  public bool IsVisible(int recordNumber)\n  {\n    return TryGetIndex(recordNumber, out int index) && IsVisible(index);\n  }"
new = "  public bool IsRecordVisible(int recordNumber)\n  {\n    return TryGetIndex(recordNumber, out int index) && IsVisible(index);\n  }"
if virtual.count(old) != 1:
  raise RuntimeError("record visibility method was not found exactly once")
virtual = virtual.replace(old, new, 1)
virtual_path.write_text(virtual, encoding="utf-8", newline="\n")

view_path = path.parents[1] / "AgentPanelSpeaker" / "TranscriptView.cs"
view = view_path.read_text(encoding="utf-8")
view = view.replace(
  "visibleDocument.IsVisible(match.RecordNumber)",
  "visibleDocument.IsRecordVisible(match.RecordNumber)")
view_path.write_text(view, encoding="utf-8", newline="\n")
