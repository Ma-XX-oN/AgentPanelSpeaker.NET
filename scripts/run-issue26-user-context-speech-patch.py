from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
text = path.read_text(encoding="utf-8")

replacements = [
  (
    """          pendingInputRequests,\n          settings.IncludeRolledBackTurns);""",
    """          pendingInputRequests,\n          settings.IncludeRolledBackTurns,\n          settings.IncludeUserContext);"""
  ),
  (
    """              pendingInputRequests,\n              settings.IncludeRolledBackTurns);""",
    """              pendingInputRequests,\n              settings.IncludeRolledBackTurns,\n              settings.IncludeUserContext);"""
  ),
  (
    """    EligibleHistory eligibleHistory = ReadEligibleHistory(\n      session,\n      pendingInputRequests,\n      includeRolledBackTurns);""",
    """    EligibleHistory eligibleHistory = ReadEligibleHistory(\n      session,\n      pendingInputRequests,\n      includeRolledBackTurns,\n      includeUserContext);"""
  ),
  (
    """  private EligibleHistory ReadEligibleHistory(\n    LocatedSession session,\n    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns,\n    DateTime? minimumTimestampUtc = null)""",
    """  private EligibleHistory ReadEligibleHistory(\n    LocatedSession session,\n    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns,\n    bool includeUserContext,\n    DateTime? minimumTimestampUtc = null)"""
  )
]

for old, new in replacements:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected one history propagation target, found {count}: {old[:80]!r}")
  text = text.replace(old, new)

path.write_text(text, encoding="utf-8", newline="\n")

main_path = root / "scripts/patch-issue26-user-context-speech.py"
main = main_path.read_text(encoding="utf-8")
brittle = '''count = monitor_text.count(old_history)
if count != 2:
  raise RuntimeError(f"JsonlSessionMonitor.cs: expected two Run history targets, found {count}")
monitor_path.write_text(monitor_text.replace(old_history, new_history), encoding="utf-8", newline="\\n")'''
replacement = '''count = monitor_text.count(old_history)
if count not in (0, 2):
  raise RuntimeError(f"JsonlSessionMonitor.cs: expected zero or two Run history targets, found {count}")
if count:
  monitor_path.write_text(monitor_text.replace(old_history, new_history), encoding="utf-8", newline="\\n")'''
if main.count(brittle) != 1:
  raise RuntimeError("Expected one prehandled history replacement guard.")
main = main.replace(brittle, replacement)
exec(compile(main, str(main_path), "exec"), {"__name__": "__main__", "__file__": str(main_path)})
