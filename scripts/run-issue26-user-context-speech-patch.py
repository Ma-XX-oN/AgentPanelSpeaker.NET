from pathlib import Path
import runpy

root = Path(__file__).resolve().parents[1]
main_path = root / "scripts/patch-issue26-user-context-speech.py"
main = main_path.read_text(encoding="utf-8")

ambiguous_preview = '''# LoadHistoryPreview -> LoadExistingHistory
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """      pendingInputRequests,\\n      includeRolledBackTurns);""",
  """      pendingInputRequests,\\n      includeRolledBackTurns,\\n      includeUserContext);""")
'''
specific_preview = '''# LoadHistoryPreview -> LoadExistingHistory
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """    return LoadExistingHistory(\\n      session,\\n      speakExistingLatestTurn,\\n      ref nextNodeId,\\n      recentFingerprintQueue,\\n      recentFingerprintSet,\\n      preview,\\n      pendingInputRequests,\\n      includeRolledBackTurns);""",
  """    return LoadExistingHistory(\\n      session,\\n      speakExistingLatestTurn,\\n      ref nextNodeId,\\n      recentFingerprintQueue,\\n      recentFingerprintSet,\\n      preview,\\n      pendingInputRequests,\\n      includeRolledBackTurns,\\n      includeUserContext);""")
'''
if main.count(ambiguous_preview) != 1:
  raise RuntimeError("Expected one ambiguous history-preview patch block.")
main = main.replace(ambiguous_preview, specific_preview)

old_history_block = '''# Two Run() history calls.
old_history = """            pendingInputRequests,\\n            settings.IncludeRolledBackTurns);"""
new_history = """            pendingInputRequests,\\n            settings.IncludeRolledBackTurns,\\n            settings.IncludeUserContext);"""
monitor_path = ROOT / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
monitor_text = monitor_path.read_text(encoding="utf-8")
count = monitor_text.count(old_history)
if count != 2:
  raise RuntimeError(f"JsonlSessionMonitor.cs: expected two Run history targets, found {count}")
monitor_path.write_text(monitor_text.replace(old_history, new_history), encoding="utf-8", newline="\\n")
'''
new_history_block = '''# Run() history calls use different indentation at the initial and switched-session sites.
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """          pendingInputRequests,\\n          settings.IncludeRolledBackTurns);""",
  """          pendingInputRequests,\\n          settings.IncludeRolledBackTurns,\\n          settings.IncludeUserContext);""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """              pendingInputRequests,\\n              settings.IncludeRolledBackTurns);""",
  """              pendingInputRequests,\\n              settings.IncludeRolledBackTurns,\\n              settings.IncludeUserContext);""")
'''
if main.count(old_history_block) != 1:
  raise RuntimeError("Expected one issue-26 Run-history patch block.")
main = main.replace(old_history_block, new_history_block)

main_path.write_text(main, encoding="utf-8", newline="\n")
runpy.run_path(str(main_path), run_name="__main__")
