#!/usr/bin/env python3
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
PYTHON = sys.executable
PROJECT = "AgentPanelSpeaker/AgentPanelSpeaker.csproj"


def run(command: list[str], *, env: dict[str, str]) -> int:
  result = subprocess.run(command, cwd=ROOT, env=env)
  return result.returncode


def main() -> int:
  env = dict(os.environ)
  env["PYTHONDONTWRITEBYTECODE"] = "1"

  if run([PYTHON, "scripts/check_nuget_access.py"], env=env) != 0:
    print("Prerequisite unavailable: NuGet network access", file=sys.stderr)
    return 2

  if run([
    "dotnet",
    "restore",
    PROJECT,
    "--configfile",
    "NuGet.Config",
  ], env=env) != 0:
    return 1

  failures = 0
  for command in (
    ["dotnet", "build", PROJECT, "-c", "Release", "--no-restore"],
    [PYTHON, "tools/test-subagents.py"],
    [PYTHON, "-m", "unittest", "tests/test_repoworkflow_adoption.py"],
    [
      PYTHON,
      "-m",
      "unittest",
      "discover",
      "-s",
      "tests",
      "-p",
      "test_ci_contract.py",
    ],
  ):
    if run(command, env=env) != 0:
      failures += 1
  return 1 if failures else 0


if __name__ == "__main__":
  raise SystemExit(main())
