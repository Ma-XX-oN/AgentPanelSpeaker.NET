from pathlib import Path
import runpy

root = Path(__file__).resolve().parents[1]
main_path = root / "scripts/patch-issue26-user-context-speech.py"
main = main_path.read_text(encoding="utf-8")
old = '''# Two Run() history calls.
old_history = """            pendingInputRequests,\\n            settings.IncludeRolledBackTurns);"""
new_history = """            pendingInputRequests,\\n            settings.IncludeRolledBackTurns,\\n            settings.IncludeUserContext);"""
monitor_path = ROOT / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
monitor_text = monitor_path.read_text(encoding="utf-8")
count = monitor_text.count(old_history)
if count != 2:
  raise RuntimeError(f"JsonlSessionMonitor.cs: expected two Run history targets, found {count}")
monitor_path.write_text(monitor_text.replace(old_history, new_history), encoding="utf-8", newline="\\n")
'''
new = '''# Run() history calls use different indentation at the initial and switched-session sites.
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """          pendingInputRequests,\\n          settings.IncludeRolledBackTurns);""",
  """          pendingInputRequests,\\n          settings.IncludeRolledBackTurns,\\n          settings.IncludeUserContext);""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """              pendingInputRequests,\\n              settings.IncludeRolledBackTurns);""",
  """              pendingInputRequests,\\n              settings.IncludeRolledBackTurns,\\n              settings.IncludeUserContext);""")
'''
if main.count(old) != 1:
  raise RuntimeError("Expected one issue-26 Run-history patch block.")
main_path.write_text(main.replace(old, new), encoding="utf-8", newline="\n")
runpy.run_path(str(main_path), run_name="__main__")
