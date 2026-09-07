from pathlib import Path
import runpy

root = Path(__file__).resolve().parents[1]
path = root / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
text = path.read_text(encoding="utf-8")
old = """    EligibleHistory eligibleHistory = ReadEligibleHistory(\n      session,\n      pendingInputRequests,\n      includeRolledBackTurns);"""
new = """    EligibleHistory eligibleHistory = ReadEligibleHistory(\n      session,\n      pendingInputRequests,\n      includeRolledBackTurns,\n      includeUserContext);"""
if text.count(old) != 1:
  raise RuntimeError("Expected one LoadExistingHistory -> ReadEligibleHistory call.")
text = text.replace(old, new)
old = """  private EligibleHistory ReadEligibleHistory(\n    LocatedSession session,\n    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns,\n    DateTime? minimumTimestampUtc = null)"""
new = """  private EligibleHistory ReadEligibleHistory(\n    LocatedSession session,\n    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns,\n    bool includeUserContext,\n    DateTime? minimumTimestampUtc = null)"""
if text.count(old) != 1:
  raise RuntimeError("Expected one ReadEligibleHistory signature.")
path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
runpy.run_path(str(root / "scripts/patch-issue26-user-context-speech.py"), run_name="__main__")
