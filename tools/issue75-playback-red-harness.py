from pathlib import Path

PATH = Path('AgentPanelSpeaker/Program.cs')
text = PATH.read_text(encoding='utf-8')

old = '''        Environment.ExitCode = RunNamedSuite(\n          "core-word-id-migration",\n          Issue75CoreWordIdMigrationRegressionTestRunner.Run);\n'''
new = '''        Environment.ExitCode = RunNamedSuite(\n          "core-word-id-migration",\n          () => RunWithWinFormsMessageLoop(\n            Issue75CoreWordIdMigrationRegressionTestRunner.Run));\n'''
if text.count(old) != 1:
  raise RuntimeError('Expected one named issue #75 suite registration.')
text = text.replace(old, new)

old = '''      int ctrlClickVoicePointer = RunIsolatedTestSuite(\n        "ctrl-click-voice-pointer");\n\n      Environment.ExitCode = primary == 0 &&\n'''
new = '''      int ctrlClickVoicePointer = RunIsolatedTestSuite(\n        "ctrl-click-voice-pointer");\n      int coreWordIdMigration = RunIsolatedTestSuite(\n        "core-word-id-migration");\n\n      Environment.ExitCode = primary == 0 &&\n'''
if text.count(old) != 1:
  raise RuntimeError('Expected one full-suite issue #75 insertion point.')
text = text.replace(old, new)

old = '''                             findOrigin == 0 &&\n                             ctrlClickVoicePointer == 0\n        ? 0\n'''
new = '''                             findOrigin == 0 &&\n                             ctrlClickVoicePointer == 0 &&\n                             coreWordIdMigration == 0\n        ? 0\n'''
if text.count(old) != 1:
  raise RuntimeError('Expected one full-suite issue #75 condition point.')
text = text.replace(old, new)

PATH.write_text(text, encoding='utf-8', newline='\n')
