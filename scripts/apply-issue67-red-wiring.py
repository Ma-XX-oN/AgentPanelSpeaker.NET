from pathlib import Path

path = Path("AgentPanelSpeaker/Program.cs")
text = path.read_text(encoding="utf-8")

if 'string.Equals(args[1], "find-origin"' not in text:
  marker = '''      if (args.Length == 2 &&\n          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))\n'''
  block = '''      if (args.Length == 2 &&\n          string.Equals(args[1], "find-origin", StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = RunNamedSuite(\n          "find-origin",\n          () => RunWithWinFormsMessageLoop(\n            Issue67FindOriginRegressionTestRunner.Run));\n        return;\n      }\n\n'''
  if marker not in text:
    raise RuntimeError("named-suite insertion marker not found")
  text = text.replace(marker, block + marker, 1)

if 'int findOrigin = RunIsolatedTestSuite("find-origin");' not in text:
  marker = '      int redundancy = RunIsolatedTestSuite("redundancy");\n'
  replacement = marker + '      int findOrigin = RunIsolatedTestSuite("find-origin");\n'
  if marker not in text:
    raise RuntimeError("all-suite invocation marker not found")
  text = text.replace(marker, replacement, 1)

if 'redundancy == 0 &&\n                             findOrigin == 0' not in text:
  marker = '                             redundancy == 0\n'
  replacement = '                             redundancy == 0 &&\n                             findOrigin == 0\n'
  if marker not in text:
    raise RuntimeError("all-suite result marker not found")
  text = text.replace(marker, replacement, 1)

path.write_text(text, encoding="utf-8", newline="\n")
