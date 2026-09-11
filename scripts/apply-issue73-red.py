from pathlib import Path

path = Path('AgentPanelSpeaker/Program.cs')
text = path.read_text(encoding='utf-8')

named_anchor = '''      if (args.Length == 2 &&\n          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))\n'''
named_insert = '''      if (args.Length == 2 &&\n          string.Equals(\n            args[1],\n            "ctrl-click-voice-pointer",\n            StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = RunNamedSuite(\n          "ctrl-click-voice-pointer",\n          () => RunWithWinFormsMessageLoop(\n            Issue73CtrlClickVoicePointerRegressionTestRunner.Run));\n        return;\n      }\n\n'''
if named_insert not in text:
  if named_anchor not in text:
    raise SystemExit('Named-suite insertion anchor not found.')
  text = text.replace(named_anchor, named_insert + named_anchor, 1)

run_anchor = '''      int findOrigin = RunIsolatedTestSuite("find-origin");\n\n      Environment.ExitCode = primary == 0 &&\n'''
run_replace = '''      int findOrigin = RunIsolatedTestSuite("find-origin");\n      int ctrlClickVoicePointer = RunIsolatedTestSuite(\n        "ctrl-click-voice-pointer");\n\n      Environment.ExitCode = primary == 0 &&\n'''
if run_replace not in text:
  if run_anchor not in text:
    raise SystemExit('All-suite run insertion anchor not found.')
  text = text.replace(run_anchor, run_replace, 1)

condition_anchor = '''                             redundancy == 0 &&\n                             findOrigin == 0\n'''
condition_replace = '''                             redundancy == 0 &&\n                             findOrigin == 0 &&\n                             ctrlClickVoicePointer == 0\n'''
if condition_replace not in text:
  if condition_anchor not in text:
    raise SystemExit('All-suite condition insertion anchor not found.')
  text = text.replace(condition_anchor, condition_replace, 1)

path.write_text(text, encoding='utf-8')
