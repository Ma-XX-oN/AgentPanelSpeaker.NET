from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
REPOWORKFLOW_SHA = "755f1449db56ccdfefe4f15eb91cb6d6c300174c"
DEVELOPMENT_VERSION = re.compile(r"^\d+\.\d+\.\d+-issue\.\d+\.\d+$")


def read_text(relative: str) -> str:
  return (ROOT / relative).read_text(encoding="utf-8")


def read_json(relative: str) -> dict:
  return json.loads(read_text(relative))


class RepoWorkflowAdoptionTests(unittest.TestCase):
  def test_repoworkflow_is_pinned_real_submodule(self) -> None:
    modules = read_text(".gitmodules")
    self.assertIn('[submodule "RepoWorkflow"]', modules)
    self.assertIn("path = RepoWorkflow", modules)
    self.assertIn("url = https://github.com/Ma-XX-oN/RepoWorkflow.git", modules)
    tree = subprocess.run(
      ["git", "ls-tree", "HEAD", "RepoWorkflow"],
      cwd=ROOT,
      check=True,
      text=True,
      capture_output=True,
    ).stdout.strip()
    self.assertEqual(f"160000 commit {REPOWORKFLOW_SHA}\tRepoWorkflow", tree)

  def test_consumer_configuration_preserves_windows_environment(self) -> None:
    self.assertEqual({
      "schema": 1,
      "versionCommand": ["python", "scripts/workflow_version.py"],
      "repository": {
        "integrationBranch": "main",
        "authoritativeRemote": "origin",
      },
      "environments": [{
        "id": "windows-dotnet10-python313",
        "required": True,
        "platform": "win32",
        "capabilities": ["dotnet-10", "python-3.13"],
        "validationCommand": ["python", "scripts/repoworkflow_validate.py"],
      }],
    }, read_json(".ci/repoworkflow.json"))

  def test_github_mapping_and_branch_policy_are_repository_facts(self) -> None:
    self.assertEqual({
      "schema": 1,
      "prepareRunner": "ubuntu-latest",
      "runners": {"windows-dotnet10-python313": "windows-latest"},
    }, read_json(".ci/github.json"))
    self.assertEqual({
      "schema": 1,
      "integrationBranch": "main",
      "branches": {
        "issue-154-repoworkflow-adoption": {
          "parent": "main",
          "allowedDependencies": [],
          "integrationTarget": "main",
        }
      },
      "patterns": [{
        "pattern": "issue-*",
        "parent": "main",
        "allowedDependencies": [],
      }],
    }, read_json(".ci/branch-policy.json"))

  def test_version_hook_matches_authoritative_version_file(self) -> None:
    expected = read_text("VERSION").strip()
    self.assertRegex(expected, DEVELOPMENT_VERSION)
    result = subprocess.run(
      [sys.executable, "scripts/workflow_version.py"],
      cwd=ROOT,
      text=True,
      capture_output=True,
    )
    self.assertEqual(0, result.returncode, result.stderr or result.stdout)
    self.assertEqual(expected, result.stdout.strip())

  def test_validation_hook_preserves_windows_production_chain(self) -> None:
    hook = read_text("scripts/repoworkflow_validate.py")
    self.assertIn("scripts/check_nuget_access.py", hook)
    self.assertIn("return 2", hook)
    self.assertIn("dotnet", hook)
    self.assertIn("restore", hook)
    self.assertIn("AgentPanelSpeaker/AgentPanelSpeaker.csproj", hook)
    self.assertIn("NuGet.Config", hook)
    self.assertIn("build", hook)
    self.assertIn("Release", hook)
    self.assertIn("--no-restore", hook)
    self.assertIn("tools/test-subagents.py", hook)
    self.assertIn("tests/test_repoworkflow_adoption.py", hook)
    self.assertIn("tests/test_ci_contract.py", hook)

  def test_github_ci_is_canonical_repoworkflow_adapter(self) -> None:
    self.assertEqual(
      read_text("RepoWorkflow/templates/github/ci.yml"),
      read_text(".github/workflows/ci.yml"),
    )
    workflow = read_text(".github/workflows/ci.yml")
    self.assertIn("actions/setup-dotnet@v4", workflow)
    self.assertIn("matrix.dotnetVersion", workflow)

  def test_legacy_comparison_machinery_remains_until_equivalence(self) -> None:
    for relative in (
      ".ci/ci-config.json",
      ".ci/test-matrix.json",
      "scripts/ci_contract.py",
      "tests/test_ci_contract.py",
      "scripts/check_actions_policy.py",
      "tests/test_actions_policy.py",
    ):
      self.assertTrue((ROOT / relative).exists(), relative)


if __name__ == "__main__":
  unittest.main()
