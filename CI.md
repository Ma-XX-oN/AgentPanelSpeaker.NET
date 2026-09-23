# CI request and result contract

AgentPanelSpeaker.NET uses an explicit CI request instead of running expensive validation on every development push.

## Development cycle

1. Work on an issue branch using `x.y.z-issue.<issue>.<iteration>`.
2. Make ordinary source/documentation commits without requesting hosted CI.
3. Run focused checks locally while developing.
4. When the candidate is ready, set `.ci/run-ci-request` to the exact authoritative development version and push that change.
5. GitHub Actions validates the exact requested commit.

The authoritative development version source is the root `VERSION` file.

## Clean-clone prerequisite

Full CI is valid only from a real clean clone. `scripts/ci_contract.py` fails preflight unless:

- the checkout is a Git working tree;
- an `origin` remote exists;
- the repository is non-shallow so historical validation remains available; and
- there are no tracked or untracked working-tree changes.

GitHub checkout therefore uses `fetch-depth: 0`. Local full validation should also be run from a clean, full clone rather than an exported archive or synthetic working tree.

## Local validation

The same repository-owned contract used by Actions is available locally:

```text
python scripts/ci_contract.py preflight
python scripts/ci_contract.py matrix
python scripts/ci_contract.py run --environment windows-dotnet10-python313 --result <outside-repo-result.json>
```

Store result JSON outside the repository so finalization can continue to assert a clean clone.

The currently required environment is declared in `.ci/test-matrix.json`:

- Windows
- .NET 10.x
- Python 3.13

The runtime records the actual OS, Python version, and .NET version. A platform or runtime mismatch makes the environment **INCOMPLETE**, not a source failure.

## Windows validation

The required Windows environment performs:

1. a NuGet network prerequisite check;
2. production-project restore using `NuGet.Config`;
3. Release build with warnings treated as errors by the project;
4. the existing Claude sub-agent transcript fixture; and
5. CI-contract regression tests.

The network check is a prerequisite. If NuGet cannot be reached, validation is INCOMPLETE and no result tag is allowed. Once the prerequisite is available, restore/build/test failures are genuine repository validation results.

Future production integration tests can be added to the repository-owned matrix without moving their semantics into Actions YAML.

## Matrix and tagging

Collect result files from every required environment, then run:

```text
python scripts/ci_contract.py finalize --results-dir <results-directory>
```

`--tag` authorizes tagging only after the complete required matrix has reported for the same commit and version:

```text
python scripts/ci_contract.py finalize --results-dir <results-directory> --tag
```

`--push` additionally publishes the tag and requires `--tag`.

- **PASS**: every required environment reported and passed. Tag `v<version>`.
- **FAIL**: every required environment reported, no required environment was incomplete, and at least one genuine validation gate failed. Tag `v<version>-CI-FAIL`.
- **INCOMPLETE**: a required result is missing or infrastructure/platform/runtime/network requirements prevented valid execution. Emit warnings and create no tag.

Result tags are immutable. Once either PASS or CI-FAIL exists for an issue iteration, source changes require the next development iteration.

A GitHub runner/service failure is not a CI result for the source. Re-run the existing workflow/jobs against the same commit rather than changing source or consuming another issue iteration.

## GitHub Actions boundary

The workflow is orchestration only: clean checkout, runtime setup, invoking `scripts/ci_contract.py`, collecting result artifacts, and final tag publication. Test semantics live in repository files. Validation jobs have read-only repository permission; only the finalizer can write tags.

The repository additionally enforces `docs/GITHUB-ACTIONS-POLICY.md` through
`scripts/check_actions_policy.py` and `tests/test_actions_policy.py`.  Changes to
workflow definitions or the policy checker/tests run a lightweight Linux policy
job.  That policy-only path does **not** request the expensive Windows matrix;
the matrix still requires a `.ci/run-ci-request` change or manual dispatch.

Maintained Actions workflows must remain within the policy allow-list.  Do not
create issue-specific or one-shot workflows that patch, repair, migrate,
instrument, commit, or push source, tests, or documentation.  Result-tag
publication through `scripts/ci_contract.py finalize ... --tag --push` is the
only current main-line repository-write exception.
