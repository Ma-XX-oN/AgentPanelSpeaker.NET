from pathlib import Path

path = Path(__file__).resolve().parents[1] / "AgentPanelSpeaker" / "Issue37VirtualWindowRegressionTestRunner.cs"
text = path.read_text(encoding="utf-8")
old = '''      """\n(async () => {\n  const withDetails =\n'''
new = '''      """\n(() => {\n  const withDetails =\n'''
if text.count(old) != 1:
  raise RuntimeError(f"expected one async SV3 probe, found {text.count(old)}")
text = text.replace(old, new)
for line in (
    "  await new Promise(resolve => setTimeout(resolve, 0));\n",
):
  count = text.count(line)
  if count != 2:
    raise RuntimeError(f"expected two SV3 await lines, found {count}")
  text = text.replace(line, "")
path.write_text(text, encoding="utf-8")
print("Corrected SV3 RED probe to execute synchronously.")
