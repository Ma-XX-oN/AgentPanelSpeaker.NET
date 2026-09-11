from pathlib import Path

root = Path(__file__).resolve().parents[1]
source_path = root / "scripts" / "apply-issue73-production.py"
source = source_path.read_text(encoding="utf-8")

old = '''replace_once(
  main,
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\\n    _transcriptView.ApplySettings(settings, dark);\\n",
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\\n    _transcriptView.ApplySettings(settings, dark);\\n    RefreshTranscriptVoiceSelectability();\\n")
'''
new = '''text = main.read_text(encoding="utf-8")
old_policy_apply = (
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\\n"
  "    _transcriptView.ApplySettings(settings, dark);\\n")
new_policy_apply = old_policy_apply + "    RefreshTranscriptVoiceSelectability();\\n"
policy_apply_count = text.count(old_policy_apply)
if policy_apply_count != 2:
  raise SystemExit(
    f"{main}: expected two speech-policy apply sites, found {policy_apply_count}")
main.write_text(
  text.replace(old_policy_apply, new_policy_apply),
  encoding="utf-8")
'''

count = source.count(old)
if count != 1:
  raise SystemExit(
    f"production helper: expected one ambiguous policy block, found {count}")
source = source.replace(old, new, 1)
exec(compile(source, str(source_path), "exec"), {"__name__": "__main__"})
