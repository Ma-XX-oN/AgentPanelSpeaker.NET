from pathlib import Path

source = Path(".tmp/apply-acceptance-fixes-v4.py").read_text(encoding="utf-8")
exec(compile(source, ".tmp/apply-acceptance-fixes-v4.py", "exec"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  return text.replace(old, new, 1)


# The first repair checked geometry on the next animation frames, while the
# replacement's own programmatic-scroll guard could still be active.  Manual
# ownership is already established by physical input; wait only for that
# programmatic guard to expire, then let measured geometry decide whether the
# next adjacent canonical interval is required.  A later physical reversal is
# represented by userScrollIntentDirection and therefore supersedes the stale
# replacement direction.
view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
view = view_path.read_text(encoding="utf-8")
old_convergence = r'''  virtualShiftPending = false;
  if (renderReason === 'scroll-up' || renderReason === 'scroll-down') {
    const fallbackDirection = renderReason === 'scroll-up' ? -1 : 1;
    // Replacement/anchor restoration is programmatic and never establishes
    // user intent. After layout settles, remeasure the latest physical
    // direction and request only the next adjacent canonical interval.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const convergenceDirection = userScrollIntentDirection !== 0
          ? userScrollIntentDirection
          : fallbackDirection;
        maybeRequestManualVirtualShift(convergenceDirection);
      });
    });
  }
'''
new_convergence = r'''  virtualShiftPending = false;
  if (renderReason === 'scroll-up' || renderReason === 'scroll-down') {
    const fallbackDirection = renderReason === 'scroll-up' ? -1 : 1;
    // Physical input established manual-scroll ownership before this
    // replacement. Replacement/anchor movement is programmatic, so wait for
    // its guard to expire. Geometry then decides whether more adjacent
    // canonical content is required; the short physical-intent timer does not.
    const continueManualScrollConvergence = () => {
      const remainingGuard = programmaticScrollUntil - performance.now();
      if (remainingGuard > 0) {
        setTimeout(
          () => requestAnimationFrame(continueManualScrollConvergence),
          Math.ceil(remainingGuard) + 1);
        return;
      }
      requestAnimationFrame(() => {
        const convergenceDirection = userScrollIntentDirection !== 0
          ? userScrollIntentDirection
          : fallbackDirection;
        maybeRequestManualVirtualShift(convergenceDirection);
      });
    };
    requestAnimationFrame(continueManualScrollConvergence);
  }
'''
view = replace_once(
  view,
  old_convergence,
  new_convergence,
  "post-replacement manual-scroll convergence",
)
view_path.write_text(view, encoding="utf-8")


# The owned-editor RED correctly failed before this call on unfixed production.
# Once MainForm stops consuming the key, the test reaches the native typing
# probe.  Name the actual Win32 export rather than asking user32.dll for the
# managed helper name.
test_path = Path(
  "AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
test = test_path.read_text(encoding="utf-8")
old_import = r'''  [System.Runtime.InteropServices.DllImport(
    "user32.dll",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  private static extern IntPtr SendMessageForEditorAcceptance(
'''
new_import = r'''  [System.Runtime.InteropServices.DllImport(
    "user32.dll",
    EntryPoint = "SendMessageW",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  private static extern IntPtr SendMessageForEditorAcceptance(
'''
test = replace_once(
  test,
  old_import,
  new_import,
  "owned-editor SendMessageW import",
)
test_path.write_text(test, encoding="utf-8")

print("Applied acceptance production repairs v5.")
