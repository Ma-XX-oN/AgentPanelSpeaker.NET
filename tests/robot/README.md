# Robot regression layer

Robot Framework is the repository's reusable data-driven regression layer.  It
does not replace the test-layer classifications or acceptance-oracle rules in
`TEST-ARCHITECTURE.md`; it standardizes how repeated test mechanics are
expressed and executed.

## Contract

- Robot Framework is pinned in `requirements-test.txt`.
- Reusable project templates live in `tests/robot/resources`.
- Template APIs are versioned.  Version 1 is `TemplatesV1.resource`, and its
  public keywords end in `V1`.
- Template implementations are proved by `tests/robot/template-tests` before
  project regressions rely on them.
- Durable project regressions live in `tests/robot/regression` and remain after
  the issue that introduced them closes.
- Prefer data rows against an existing template.  Add executable test code only
  when an existing tested template cannot express the required behaviour.
- If a missing capability is reusable, add it to a new or compatible template
  API and test the template itself first.

## Running

From the repository root:

```powershell
& tools/Invoke-RepositoryTask.ps1 -Task robot
```

To run the full branch verification path:

```powershell
& tools/Invoke-RepositoryTask.ps1 -Task verify
```

Robot output is written outside the working tree by the checked-in task, so test
execution does not create untracked report files in the repository.

## Data-push model

The permanent GitHub Actions runner receives only a checked-in task name and an
optional ref.  The selected task, Robot templates, and test specifications all
come from that checked-out ref.  CI inputs therefore select versioned repository
behaviour; they do not inject transient scripts or issue-specific commands.
