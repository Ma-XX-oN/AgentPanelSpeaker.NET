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
probe, count = re.subn(pattern, replacement, probe, count=1, flags=re.S)
if count != 1:
  raise RuntimeError(f"structure probe anchor-regex repair matched {count} declarations")
probe_path.write_text(probe, encoding="utf-8", newline="\n")
