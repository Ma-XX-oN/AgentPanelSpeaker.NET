# CI request and result contract

AgentPanelSpeaker.NET uses RepoWorkflow as the authoritative generic CI lifecycle engine.  Expensive hosted validation remains explicit-request gated rather than running on every development push.

## Development cycle

1. Work on an issue branch using `x.y.z-issue.<issue>.<iteration>`.
2. Run focused repository checks while developing.
3. Before terminalizing an iteration, run the shared direct path on the exact candidate:

```text
python RepoWorkflow/repo_workflow.py verify
```

4. When the exact candidate is ready for hosted validation, set `.ci/run-ci-request` to the authoritative development version and publish that same commit unchanged.
5. The canonical GitHub adapter validates the exact requested commit and finalizes the authoritative result.

The authoritative development version source is the root `VERSION` file.  RepoWorkflow consumer facts are declared in `.ci/repoworkflow.json`, `.ci/github.json`, and `.ci/branch-policy.json`.

## Required Windows environment

The required environment is `windows-dotnet10-python313`:

- Windows;
- .NET 10, provisioned as the 10.0.x SDK family;
- Python 3.13.

The GitHub runner mapping is repository data; the canonical adapter provisions declared runtimes before invoking the repository-owned validator.

## Repository-owned Windows validation

`scripts/repoworkflow_validate.py` remains the authoritative APS validation entry point.  It preserves this repository-specific sequence:

1. NuGet network prerequisite check via `scripts/check_nuget_access.py`;
2. production-project restore using `NuGet.Config`;
3. Release build with `--no-restore`;
4. the Claude sub-agent transcript fixture; and
5. RepoWorkflow adoption/invariant regression coverage.

A missing NuGet/network prerequisite returns the repository hook's prerequisite status so RepoWorkflow classifies the environment as **INCOMPLETE**, not a source failure.  Once prerequisites are available, restore/build/test failures are genuine repository validation failures.

The Windows runner remains capable of the repository's real WinForms, WebView2, speech, and other production integration tests.  Those tests belong in the repository-owned validation path when required; migration to RepoWorkflow does not replace them with headless approximations.

## Result semantics

RepoWorkflow owns the universal request/version guard, clean candidate requirements, result aggregation, and immutable terminal tags:

- **PASS**: all required validation completed and passed; tag `v<version>`.
- **FAIL**: all required validation completed, no required environment was incomplete, and a genuine validation gate failed; tag `v<version>-CI-FAIL`.
- **INCOMPLETE**: a required capability, prerequisite, network service, runner, or other infrastructure requirement prevented a valid result; no terminal tag is created.

Terminal result tags consume the issue iteration.  Source changes after a terminal result require the next development iteration.

## GitHub Actions boundary

`.github/workflows/ci.yml` is the byte-for-byte canonical RepoWorkflow GitHub adapter.  It performs GitHub-specific checkout, runtime provisioning, result-artifact transport, and authorized tag publication.  Validation jobs are read-only; only finalization may publish the tested immutable result tag.

Repository-specific validation semantics stay in checked-in APS scripts and tests rather than in Actions YAML.
