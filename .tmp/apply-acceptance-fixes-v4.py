from pathlib import Path

source = Path(".tmp/apply-acceptance-fixes-v3.py").read_text(encoding="utf-8")
old_guard = "if main.count(old_name) != 2:\n"
new_guard = "if main.count(old_name) != 4:\n"
if source.count(old_guard) != 1:
  raise RuntimeError("v3 policy-call count guard did not match exactly once.")
source = source.replace(old_guard, new_guard, 1)
old_message = "f\"MainForm: expected two {old_name} calls, found {main.count(old_name)}.\")"
new_message = "f\"MainForm: expected four {old_name} calls, found {main.count(old_name)}.\")"
if source.count(old_message) != 1:
  raise RuntimeError("v3 policy-call diagnostic did not match exactly once.")
source = source.replace(old_message, new_message, 1)
exec(compile(source, ".tmp/apply-acceptance-fixes-v3.py", "exec"))
