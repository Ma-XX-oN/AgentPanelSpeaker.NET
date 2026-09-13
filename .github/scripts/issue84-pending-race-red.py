from pathlib import Path

path = Path("AgentPanelSpeaker/SpeechService.cs")
text = path.read_text(encoding="utf-8")

old = '''      bool pastFirstWord =
        (_pendingHistoryIndex == anchor && _pendingHistoryWordIndex > 0) ||
        (_activeHistoryIndex == anchor && _activeWordIndex > 0);
'''
new = '''      // A queued navigation target is authoritative while the old engine
      // utterance is being cancelled. Its active fragment-relative word offset
      // can otherwise make a second immediate J act on stale pre-cancel state.
      bool pastFirstWord = _pendingHistoryIndex is int pendingHistoryIndex
        ? pendingHistoryIndex == anchor && _pendingHistoryWordIndex > 0
        : _activeHistoryIndex == anchor && _activeWordIndex > 0;
'''
if text.count(old) != 1:
  raise RuntimeError(
    f"pending-cursor rewind sentinel count was {text.count(old)}, expected 1")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
print("Issue #84 pending cursor authority repair staged.")
