# Agent Panel Speaker diagnostics

This document records interpretation rules for Agent Panel Speaker diagnostic
JSONL.  These rules matter when correlating activity across worker, UI, speech,
and WebView threads.

## JSONL record order and `Utc`

The physical JSONL line order is the order in which `DiagnosticLog` appended
records to the file.  Preserve that order during analysis.  Do not sort records
by `Utc` in an attempt to reconstruct a more accurate chronology.

`DiagnosticLog.Write()` currently captures `DateTime.UtcNow` and serializes the
record before it acquires the shared append lock.  The lock serializes only the
file append.  Two threads can therefore produce this valid sequence:

```text
thread A captures an earlier Utc value
thread A is delayed before acquiring the append lock
thread B captures a later Utc value and appends first
thread A appends afterward
```

A small `Utc` reversal between adjacent lines is consequently expected when the
records came from different threads.  The accepted #97 real-machine log
contained this pattern: every observed adjacent timestamp reversal crossed
threads, with no same-thread reversal.

This application-side ordering effect is separate from the operating-system
clock.  `Utc` is wall-clock time, not a monotonic elapsed-time counter.  Windows
can adjust system time forward or backward while synchronizing the time-of-day
clock.  Use the event's monotonic timestamp field, `Stopwatch`, provider audio
position, or another explicit monotonic measurement when elapsed time or strict
causal timing is required.

The presence of a small wall-clock reversal is therefore not sufficient evidence
that an event occurred out of file order.  Keep both the original JSONL order
and the recorded `Utc` value; each describes a different property of the event.

Microsoft documents the distinction here:

- System Time (https://learn.microsoft.com/en-us/windows/win32/sysinfo/system-time)
- GetSystemTimePreciseAsFileTime (https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemtimepreciseasfiletime)

## System.Speech progress and punctuation

The System.Speech path is driven by the provider's `SpeakProgress` callbacks.
Agent Panel Speaker maps each callback from its provider source range back to
canonical transcript ownership.  It does not invent an independent timing
boundary merely because a punctuation token is visible in the source.

The resulting rule is:

- punctuation that receives its own `SpeakProgress` source range can produce an
  independent playback/highlight boundary;
- punctuation that receives no provider progress callback is skipped naturally;
- no interpolation or synthetic punctuation timing is inserted to fill a
  missing callback;
- later reported words remain mapped from their actual provider source ranges,
  rather than being shifted to compensate for punctuation the provider omitted.

This is intentional provider behaviour, not a missing-boundary defect.  It is
consistent with the existing transcript invariant that unspoken punctuation
does not advance the marker.

For example, a provider can report progress for the lexical pieces of a phrase
while omitting a comma or terminal period as a separate event.  The transcript
must preserve the punctuation visually, but the speech cursor advances only on
boundaries actually reported by the provider.

## Application and Core version identity

`AgentPanelSpeaker/AgentPanelSpeaker.csproj` is the single writable source of the
Agent Panel Speaker semantic version.  Development versions use
`x.y.z-issue.<issue>.<iteration>` and release versions use `x.y.z`.

`ApplicationIdentity.Version` reads the generated assembly informational-version
metadata.  The main-window title is composed from `ApplicationIdentity.WindowTitle`,
and `app.start.Data.version` is written from `ApplicationIdentity.Version`.
Neither consumer carries its own version literal.

The build/publish regression gate verifies that the project version, executable
`ProductVersion`, window-title source, and diagnostic version source remain in
sync.  A drift back to independent UI or diagnostic literals is therefore a test
failure.

AIConversationCore has two distinct identities and both are preserved:

- the exact pinned Core commit identifies the source revision Agent Panel Speaker
  requires;
- the Core semantic version is obtained from the loaded Core's public
  `getVersion()` API and is returned by the persistent worker as `core_version`.

The worker reports both `core_commit` and `core_version`.  Agent Panel Speaker
verifies the exact commit pin and records both values in `core.worker_started`.
The integration workflow verifies `core_version` against the pinned/bundled
Core's own `package.json`; Agent Panel Speaker does not maintain a duplicate Core
semantic-version literal.

Semantic version and exact commit identity answer different questions.  Use the
application semantic version for product/release identity and the repository/Core
commit identities when exact source provenance is required.
