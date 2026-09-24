# GitHub Actions Policy

GitHub Actions in AgentPanelSpeaker.NET exist to validate checked-in repository state and publish immutable CI result tags.  They are not a remote editor for production source, tests, or documentation.

## Authority

RepoWorkflow owns the common GitHub Actions allow-list, write-permission, repository-mutation, request-gating, and result-tagging policy.  The repository enforces that policy with:

```text
python RepoWorkflow/repo_workflow.py repository-policy
```

The canonical main-line workflow is `.github/workflows/ci.yml`, copied byte-for-byte from the pinned RepoWorkflow GitHub adapter.  Repository-specific validation remains in APS-owned scripts and tests.

## Workflow and write boundaries

The maintained main line must not acquire issue-specific, temporary, migration, repair, patch, instrumentation, or other one-shot workflows as permanent repository machinery.  Development changes must be made through a normal working tree or repository API/connector, not by Actions that rewrite and commit source, tests, or documentation.

Validation jobs remain read-only.  The canonical finalizer is the only generic repository-write path and may write only the tested immutable result tag after RepoWorkflow has aggregated a complete authoritative result.

Direct `git add`, source-editing `git commit`, arbitrary `git push`, or direct mutating GitHub/cURL API calls do not belong in validation workflows.

## Repository-specific integration validation

AgentPanelSpeaker's Windows validation surface remains repository-owned.  Real WinForms, WebView2, speech, Release-build, transcript-fixture, and related production integration checks can run on the Windows runner through the repository validation hook without moving their semantics into Actions YAML.

## Enforcement behaviour

RepoWorkflow repository policy is deterministic and fails visibly when the maintained workflow set or write boundary violates the declared contract.  The canonical GitHub policy job runs before expensive validation; `.ci/run-ci-request` or explicit manual dispatch remains the gate for the full Windows environment.

Historical workflow-run metadata is separate from Git history.  Removing obsolete run history does not rewrite repository commits or alter the migration evidence.
