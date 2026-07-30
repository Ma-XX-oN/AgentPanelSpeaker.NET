#!/usr/bin/env python3
"""Verify the minimal Claude sub-agent transcript fixture."""

from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
SCRIPT = ROOT / "AI-transcript.py"
FIXTURE = ROOT / "fixtures" / "claude-subagent.jsonl"

result = subprocess.run(
  [sys.executable, str(SCRIPT), "--claude", "--file", str(FIXTURE)],
  check=True,
  capture_output=True,
  text=True,
)
output = result.stdout
assert "## Claude Sub-agent agent-opaque-7" in output
assert "> **Review files**" in output
assert "> Finished review." in output
assert "agentId:" not in output
assert "subagent_tokens" not in output
assert "## Claude Sub-agent child-opaque-9" in output
assert "> **Inspect child output**" in output
assert "> Child review complete." in output
assert output.count("Claude Sub-agent") == 2
print("Claude sub-agent transcript fixture passed.")
