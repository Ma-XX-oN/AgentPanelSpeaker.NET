from pathlib import Path

view_path = Path('AgentPanelSpeaker/TranscriptView.cs')
program_path = Path('AgentPanelSpeaker/Program.cs')

view = view_path.read_text(encoding='utf-8')
old_helper = '''  internal static TranscriptWindow SelectInitialPresentationWindow(\n    TranscriptVirtualDocument document,\n    int focalIndex)\n  {\n    return document.CreateFullWindow();\n  }\n'''
new_helper = '''  internal static TranscriptWindow SelectInitialPresentationWindow(\n    TranscriptVirtualDocument document,\n    int focalIndex)\n  {\n    return document.CreateWindow(focalIndex);\n  }\n'''
if view.count(old_helper) != 1:
  raise SystemExit(f'expected one full-window helper, found {view.count(old_helper)}')
view = view.replace(old_helper, new_helper, 1)

old_render = '''      string script = BuildReplaceDomScript(\n        window,\n        payload.DomNodes,\n        preserve: !force,\n        structureProbeId: structureProbeId,\n        expectedStructure: virtualStructure);\n'''
new_render = '''      DiagnosticLog.Write("transcript.initial_window_selected", new\n      {\n        focalIndex,\n        window.StartIndex,\n        window.EndIndex,\n        recordCount = window.Records.Count,\n        totalRecordCount = payload.Document.Count,\n        htmlCharacters = window.Html.Length,\n        window.TopSpacerHeight,\n        window.BottomSpacerHeight,\n        preparationMilliseconds\n      });\n      string script = BuildReplaceWindowScript(\n        window,\n        preserve: !force,\n        focusVirtualIndex: force ? focalIndex : null,\n        structureProbeId: structureProbeId,\n        expectedStructure: virtualStructure);\n'''
if view.count(old_render) != 1:
  raise SystemExit(f'expected one full-DOM initial render call, found {view.count(old_render)}')
view = view.replace(old_render, new_render, 1)

old_mode = '''      _windowStartIndex = window.StartIndex;\n      _windowEndIndex = window.EndIndex;\n      _domPresentationMode = true;\n'''
new_mode = '''      _windowStartIndex = window.StartIndex;\n      _windowEndIndex = window.EndIndex;\n      _domPresentationMode = false;\n'''
if view.count(old_mode) != 1:
  raise SystemExit(f'expected one initial DOM presentation mode assignment, found {view.count(old_mode)}')
view = view.replace(old_mode, new_mode, 1)
view_path.write_text(view, encoding='utf-8')

program = program_path.read_text(encoding='utf-8')
old_runs = '''      int rolledBackVisibility = RunIsolatedTestSuite("rolled-back-visibility");\n      int rolledBackSpeech = RunIsolatedTestSuite("rolled-back-speech");\n\n      Environment.ExitCode = primary == 0 &&\n'''
new_runs = '''      int rolledBackVisibility = RunIsolatedTestSuite("rolled-back-visibility");\n      int rolledBackSpeech = RunIsolatedTestSuite("rolled-back-speech");\n      int largeTranscriptWindowing = RunIsolatedTestSuite(\n        "large-transcript-windowing");\n\n      Environment.ExitCode = primary == 0 &&\n'''
if program.count(old_runs) != 1:
  raise SystemExit(f'expected one default-suite run anchor, found {program.count(old_runs)}')
program = program.replace(old_runs, new_runs, 1)

old_condition = '''                             liveEnd == 0 &&\n                             rolledBackVisibility == 0 &&\n                             rolledBackSpeech == 0\n        ? 0\n'''
new_condition = '''                             liveEnd == 0 &&\n                             rolledBackVisibility == 0 &&\n                             rolledBackSpeech == 0 &&\n                             largeTranscriptWindowing == 0\n        ? 0\n'''
if program.count(old_condition) != 1:
  raise SystemExit(f'expected one default-suite condition anchor, found {program.count(old_condition)}')
program = program.replace(old_condition, new_condition, 1)
program_path.write_text(program, encoding='utf-8')
