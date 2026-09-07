from pathlib import Path
import runpy

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
    raise RuntimeError(f"Expected one history propagation target, found {count}: {old[:80]!r}")
  text = text.replace(old, new)

path.write_text(text, encoding="utf-8", newline="\n")
runpy.run_path(str(root / "scripts/patch-issue26-user-context-speech.py"), run_name="__main__")
