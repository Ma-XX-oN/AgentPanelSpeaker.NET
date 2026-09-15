# Robot test catalogue

This file records the permanent Robot suites and reusable template contracts.
Robot's own suite discovery remains the executable source of truth.

## Template API v1

| Keyword | Purpose | Self-tested |
| --- | --- | --- |
| `Command Should Succeed V1` | Run a command and require exit code 0; process-start failures are labelled infrastructure errors. | Yes |
| `Command Should Fail For Reason V1` | Require nonzero exit plus an expected diagnostic; unexpected success and wrong-reason failures are rejected. | Yes |
| `Path Should Exist V1` | Require an expected file. | Yes |
| `File Should Contain Text V1` | Require exact text in a file. | Yes |
| `Text File Should Match Regex V1` | Require a regex match in a file. | Yes |
| `Relative Difference Should Be At Most V1` | Compare numeric values by relative difference. | Yes |

## Permanent suites

| Suite | Tags | Protects |
| --- | --- | --- |
| `template-tests/templates_v1.robot` | `template-selftest`, `template-v1`, `issue-108` | The reusable template implementation, including expected-RED and infrastructure-error semantics. |
| `regression/repository_contracts.robot` | `regression`, `infrastructure`, `permanent` | Development-version shape, project-version declaration, task-dispatch rejection, development-version automation, and the existing GUI test-process completion guard. |
| `regression/issue_94_rate_match.robot` | `regression`, `issue-94`, `permanent`, `speech` | Matched/native System.Speech rate mapping, the default-on persisted rate-match setting and selective change tracking, checkbox text, and the explanatory native-range tooltip. |

When a new permanent regression is added, add it to an appropriate `.robot`
suite and update this catalogue when it introduces a new behaviour category or
template contract.
