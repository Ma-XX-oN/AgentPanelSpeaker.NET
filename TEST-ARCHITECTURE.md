# Regression Test Architecture

AgentPanelSpeaker uses several different test layers.  A passing lower-layer test
must never be cited as proof of final user-visible behaviour.

## Test layers

| Runner | Layer | What a GREEN result proves |
| --- | --- | --- |
| `RegressionTestRunner` | unit/component | Local parsing, mapping, settings, search, speech, and presentation contracts exercised by its individual tests. |
| `ExtendedRegressionTestRunner` | unit/component | Additional local boundary and failure-path contracts. |
| `AdditionalRegressionTestRunner` | unit/component | Additional mapping, session, bridge, and UI-shape contracts. |
| `CoreRegressionTestRunner` | Core integration/component | The pinned AIConversationCore bridge and projection contracts exercised by the runner. |
| `Issue24SpeechOrdinalRegressionTestRunner` | browser component | Focused list-marker and browser-mapping behaviour.  Some probes deliberately install browser mapping state themselves; they are not provider-to-browser acceptance proof. |
| `Issue24ProductionPathRegressionTestRunner` | composition | Legacy-named composed mapping tests.  They hand-author some HTML, identities, or DOM state and therefore must **not** be cited as complete production-path acceptance despite the historical filename/suite wording. |
| `Issue26UserContextSpeechRegressionTestRunner` | handler/component | Settings persistence, Core extraction, and the direct settings-handler behaviour covered by that runner.  It is not proof that the real checkbox event reaches final playback policy. |
| `Issue30LiveEndRegressionTestRunner` | behavioural component | Speech live-end state-machine behaviour. |
| `Issue35RolledBackVisibilityRegressionTestRunner` | component/contract | In-place policy, retained DOM/history data, spacer, and fragment contracts.  It is not proof of final browser visibility after the real UI toggle. |
| `Issue35SpeechTransitionRegressionTestRunner` | behavioural + contract | Speech transition behaviour plus a small number of API/shape contracts.  Shape checks are not acceptance proof. |
| `Issue44IndependentOutputOracleRegressionTestRunner` | browser-output acceptance | Fixed independent observable browser facts for the #44 cases after real `TranscriptView` production rendering. |
| `Issue46IndependentRegressionOracleTestRunner` | end-to-end observable acceptance | Fixed provider-to-browser/playback facts for the #24, #26, and #35 failure classes through the real production load/control/output paths, plus negative mutations proving the same oracle rejects known-bad results. |

The historical class/file names are retained to preserve repository history and
existing command compatibility.  The classifications above are authoritative;
legacy words such as `ProductionPath` in a filename do not promote a composition
test into an acceptance oracle.

## Acceptance-oracle rule

A test may be called an output or end-to-end acceptance test only when all of the
following are true:

1. A fixed source/provider fixture enters the same production path used by the
   application.
2. Production code computes only the **actual** result.
3. Expected facts are independently authored in the test.  The expected value
   must not be rendered, mapped, normalized, windowed, or otherwise derived by
   the same production helper that creates the actual value.
4. The assertion reaches the observable boundary named by the test: browser
   visibility, highlighted browser word, playback eligibility, or another final
   externally meaningful result.
5. A deliberate negative mutation at the protected boundary makes the **same**
   oracle fail.  This demonstrates that GREEN is capable of detecting the bug
   class rather than merely following production implementation shape.

For browser mapping acceptance, the test must not manually call browser helpers
such as `assignRecordScopes()` or `assignNodeScopes()` to install state that the
production payload is responsible for creating.  Such probes remain useful as
browser-component tests only.

For UI-setting acceptance, the test must operate the actual control/event path.
Calling `TranscriptSettingsChanged()` directly is handler/component coverage,
not evidence that the user-facing checkbox is wired correctly.

## Windows hosted CI capability

The GitHub `windows-latest` runner is a valid production-integration test
environment for UI and speech paths.  Existing tests have demonstrated that it
can:

- show the real production `MainForm` and pump the WinForms event loop;
- initialize and manipulate the real WebView2 control;
- manipulate real WinForms controls and production event handlers;
- execute JavaScript against the production WebView2 DOM;
- run native `System.Speech` and `Windows.Media` speech synthesis;
- receive speech progress and boundary events;
- start the production `SpeechService` and reach active `IsSpeaking`;
- mutate live source data while the application is running; and
- inspect production diagnostics as a behavioural oracle.

Do not reject a CI test merely because it uses WinForms, WebView2, or native
speech synthesis.  Before requiring a real-machine acceptance test, first
determine whether the scenario can use those production paths on Windows CI.

The hosted runner does not currently expose a default WinMM audio output device.
A test that reaches the production WinMM playback path can fail at
`waveOutOpen` with `MMSYSERR_BADDEVICEID` (WinMM error 2).  Keep tests that
require real audio-device playback as explicit hardware suites rather than
weakening them with fake audio.

This limitation does not make native speech timing useless.  Tests that do not
require actual waveOut playback should use sensible timing tolerances; timing
only needs to be close and stable enough to make the protected behaviour
meaningful unless exact timing is itself the requirement.

## Test-process completion is part of correctness

`AgentPanelSpeaker` is a Windows GUI-subsystem executable (`WinExe`).  A shell
launcher returning is not itself evidence that a regression process completed.
Every named suite therefore emits exactly one final machine-readable marker only
after its runner returns:

```text
TEST-SUITE-COMPLETE <suite> exit=<code>
```

Parent processes and CI must require both:

- process exit code `0`; and
- the exact `exit=0` completion marker for the requested suite.

`tools/Invoke-TestSuite.ps1` synchronously waits for built and published
executables and enforces that rule.  Header-only or partially written output is
RED even if the launcher observes a zero exit code.

## UI test lifetime

Acceptance tests that create a `MainForm` must run its production
`MainFormClosing` cleanup path before disposal.  `MainForm` registers
application-wide message/system-event hooks and detaches them in that cleanup.
The test lease invokes that production handler with the noninteractive Windows
shutdown close reason so a save-settings dialog cannot block unattended CI.
Directly calling `Dispose()` is not equivalent and can contaminate a later UI
test in the same process.

## Robot Framework template layer

Robot Framework is an orchestration and reusable-template layer above these test
classifications; it does not change them. A Robot test that invokes a component
probe remains component coverage, while an acceptance claim must still satisfy
the acceptance-oracle rules above.

Reusable project templates live under `tests/robot/resources` and have explicit
compatibility versions. Their mechanics are tested independently under
`tests/robot/template-tests` before permanent regression specifications rely on
them. Issue-specific cases should therefore be expressed as data against an
existing tested template whenever possible instead of introducing another
bespoke runner or workflow.

Permanent Robot regressions live under `tests/robot/regression`, are catalogued
in `tests/robot/TEST-CATALOGUE.md`, and remain after the issue that introduced
them closes. The checked-in task runner selects repository tasks by name; CI
inputs select versioned repository behaviour and do not inject transient test
programs or shell bodies.
