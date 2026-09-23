# AgentPanelSpeaker versioning protocol

`AgentPanelSpeaker/AgentPanelSpeaker.csproj` is the authoritative application
version source.  `ApplicationIdentity.Version`, the window title, executable
ProductVersion, and `app.start.version` must derive from that value.

## Development identity

All issue development builds use:

```text
x.y.z-issue.<issue>.<iteration>
```

Examples:

```text
1.0.0-issue.94.1
1.0.0-issue.94.2
```

The issue suffix remains present through:

1. implementation;
2. RED/GREEN regression work;
3. complete automated validation;
4. integration-branch fast-forward;
5. installed/real-machine testing.

Automated CI success is **not** acceptance.  Integration into a shared branch
is **not** acceptance.  A development build must remain identifiable until the
user explicitly accepts that exact installed build.

If code, behaviour, or the testable build identity changes after failed or
partial acceptance, increment `<iteration>` before the next installed test.

## Release identity

Plain `x.y.z` is restored only after explicit real-machine acceptance of the
issue build.  Do not promote a development version merely because repository
CI is green.

## DEVELOPMENT-VERSION guard

When `DEVELOPMENT-VERSION` exists at the repository root, it is an acceptance
guard, not a second version authority.  Its one line must contain an
issue-development version and must exactly match the authoritative `<Version>`
in `AgentPanelSpeaker.csproj`.

`tools/Verify-VersionConsistency.ps1` enforces that match for both built and
published application validation.  The guard file must not be removed or
changed to a plain release version until explicit real-machine acceptance.

After acceptance, remove `DEVELOPMENT-VERSION`, restore the intended plain
release version in `AgentPanelSpeaker.csproj`, and run the complete final
validation again before release/integration completion.

This protocol supersedes the premature-promotion sequence recorded in issue
#103.  Issue #107 records the correction.
