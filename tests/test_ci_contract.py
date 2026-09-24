import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "ci_contract.py"


def load_module():
  spec = importlib.util.spec_from_file_location("ci_contract", SCRIPT)
  module = importlib.util.module_from_spec(spec)
  assert spec.loader is not None
  spec.loader.exec_module(module)
  return module


def init_clean_clone(root: Path) -> None:
  remote = root.parent / f"{root.name}-origin.git"
  subprocess.run(["git", "init", "--bare", str(remote)], check=True, capture_output=True)
  subprocess.run(["git", "init"], cwd=root, check=True, capture_output=True)
  subprocess.run(["git", "config", "user.email", "ci-test@example.invalid"], cwd=root, check=True)
  subprocess.run(["git", "config", "user.name", "CI test"], cwd=root, check=True)
  subprocess.run(["git", "remote", "add", "origin", str(remote)], cwd=root, check=True)


class CiContractTests(unittest.TestCase):
  def setUp(self):
    self.ci = load_module()

  def test_evaluate_pass_requires_complete_required_matrix(self):
    config = {"environments": [{"id": "windows", "required": True}, {"id": "linux", "required": True}]}
    results = [
      {"schema": 1, "environment": "windows", "version": "1.0.0-issue.7.1", "commit": "abc", "status": "PASS"},
      {"schema": 1, "environment": "linux", "version": "1.0.0-issue.7.1", "commit": "abc", "status": "PASS"},
    ]
    outcome, tag, warnings = self.ci.evaluate_results(config, results, "1.0.0-issue.7.1", "abc")
    self.assertEqual("PASS", outcome)
    self.assertEqual("v1.0.0-issue.7.1", tag)
    self.assertEqual([], warnings)

  def test_evaluate_fail_tags_only_after_complete_matrix(self):
    config = {"environments": [{"id": "windows", "required": True}, {"id": "linux", "required": True}]}
    results = [
      {"schema": 1, "environment": "windows", "version": "1.0.0-issue.7.1", "commit": "abc", "status": "FAIL"},
      {"schema": 1, "environment": "linux", "version": "1.0.0-issue.7.1", "commit": "abc", "status": "PASS"},
    ]
    outcome, tag, _ = self.ci.evaluate_results(config, results, "1.0.0-issue.7.1", "abc")
    self.assertEqual("FAIL", outcome)
    self.assertEqual("v1.0.0-issue.7.1-CI-FAIL", tag)

  def test_incomplete_required_result_never_tags_and_preserves_warning(self):
    config = {"environments": [{"id": "windows", "required": True}]}
    result = {
      "schema": 1, "environment": "windows", "version": "1.0.0-issue.7.1",
      "commit": "abc", "status": "INCOMPLETE", "warnings": ["NuGet unavailable"],
    }
    outcome, tag, warnings = self.ci.evaluate_results(config, [result], "1.0.0-issue.7.1", "abc")
    self.assertEqual("INCOMPLETE", outcome)
    self.assertIsNone(tag)
    self.assertIn("NuGet unavailable", warnings)

  def test_platform_or_runtime_mismatch_is_incomplete(self):
    env = {"platform": "definitely-not-this-platform", "runtimePatterns": {"python": r"^0\."}}
    warnings = self.ci.environment_warnings(env, {"python": sys.version.split()[0]})
    self.assertGreaterEqual(len(warnings), 2)

  def test_clean_clone_requires_origin_full_history_and_clean_tree(self):
    with tempfile.TemporaryDirectory() as td:
      root = Path(td)
      init_clean_clone(root)
      (root / "tracked.txt").write_text("clean\n", encoding="utf-8")
      subprocess.run(["git", "add", "."], cwd=root, check=True)
      subprocess.run(["git", "commit", "-m", "fixture"], cwd=root, check=True, capture_output=True)
      self.ci.assert_clean_clone(root)
      (root / "untracked.txt").write_text("dirty\n", encoding="utf-8")
      with self.assertRaises(self.ci.CiContractError):
        self.ci.assert_clean_clone(root)

  def test_clean_clone_rejects_missing_origin(self):
    with tempfile.TemporaryDirectory() as td:
      root = Path(td)
      subprocess.run(["git", "init"], cwd=root, check=True, capture_output=True)
      subprocess.run(["git", "config", "user.email", "ci-test@example.invalid"], cwd=root, check=True)
      subprocess.run(["git", "config", "user.name", "CI test"], cwd=root, check=True)
      (root / "tracked.txt").write_text("x\n", encoding="utf-8")
      subprocess.run(["git", "add", "."], cwd=root, check=True)
      subprocess.run(["git", "commit", "-m", "fixture"], cwd=root, check=True, capture_output=True)
      with self.assertRaisesRegex(self.ci.CiContractError, "origin"):
        self.ci.assert_clean_clone(root)

  def test_independent_validation_gates_continue_after_failure(self):
    with tempfile.TemporaryDirectory() as td:
      root = Path(td)
      init_clean_clone(root)
      (root / ".ci").mkdir()
      (root / "VERSION").write_text("1.0.0-issue.7.1\n", encoding="utf-8")
      (root / ".ci" / "run-ci-request").write_text("1.0.0-issue.7.1\n", encoding="utf-8")
      subprocess.run(["git", "add", "."], cwd=root, check=True)
      subprocess.run(["git", "commit", "-m", "fixture"], cwd=root, check=True, capture_output=True)
      config = {
        "version": {"kind": "plain", "path": "VERSION"},
        "environments": [{
          "id": "local", "runner": "local", "required": True,
          "steps": [
            {"name": "first fails", "kind": "validation", "command": [sys.executable, "-c", "raise SystemExit(1)"]},
            {"name": "second still runs", "kind": "validation", "command": [sys.executable, "-c", "print('ran')"]},
          ],
        }],
      }
      result_path = root.parent / f"{root.name}-result.json"
      try:
        rc = self.ci.run_environment(config, "local", result_path, root)
        result = json.loads(result_path.read_text(encoding="utf-8"))
      finally:
        result_path.unlink(missing_ok=True)
      self.assertEqual(1, rc)
      self.assertEqual("FAIL", result["status"])
      self.assertEqual(2, len(result["steps"]))
      self.assertEqual("second still runs", result["steps"][1]["name"])

  def test_result_tag_for_iteration_is_single_immutable_outcome(self):
    with tempfile.TemporaryDirectory() as td:
      root = Path(td)
      init_clean_clone(root)
      (root / "fixture").write_text("x\n", encoding="utf-8")
      subprocess.run(["git", "add", "."], cwd=root, check=True)
      subprocess.run(["git", "commit", "-m", "fixture"], cwd=root, check=True, capture_output=True)
      sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, check=True, capture_output=True, text=True).stdout.strip()
      version = "1.0.0-issue.7.1"
      self.ci.create_tag(root, version, f"v{version}-CI-FAIL", sha, False)
      with self.assertRaises(self.ci.CiContractError):
        self.ci.create_tag(root, version, f"v{version}", sha, False)

  def test_workflow_delegates_request_gate_and_windows_matrix_to_repoworkflow(self):
    workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
    shared = (ROOT / "RepoWorkflow" / "repo_workflow" / "github_adapter.py").read_text(encoding="utf-8")
    config = json.loads((ROOT / ".ci" / "repoworkflow.json").read_text(encoding="utf-8"))
    github = json.loads((ROOT / ".ci" / "github.json").read_text(encoding="utf-8"))

    self.assertIn("RepoWorkflow/repo_workflow.py github-request", workflow)
    self.assertIn("RepoWorkflow/repo_workflow.py github-matrix", workflow)
    self.assertIn("actions/setup-dotnet@v4", workflow)
    self.assertIn("matrix.dotnetVersion", workflow)
    self.assertIn(".ci/run-ci-request", shared)

    environments = config["environments"]
    self.assertEqual(1, len(environments))
    environment = environments[0]
    self.assertEqual("windows-dotnet10-python313", environment["id"])
    self.assertTrue(environment["required"])
    self.assertEqual("windows", environment["platform"])
    self.assertEqual(["dotnet-10", "python-3.13"], environment["capabilities"])
    self.assertEqual(
      "windows-latest",
      github["runners"]["windows-dotnet10-python313"],
    )


if __name__ == "__main__":
  unittest.main()
