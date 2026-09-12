from pathlib import Path

source = Path(".tmp/apply-acceptance-fixes-v4.py").read_text(encoding="utf-8")
exec(compile(source, ".tmp/apply-acceptance-fixes-v4.py", "exec"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  return text.replace(old, new, 1)


# Continue a legitimate manual-scroll transaction after replacement. Physical
# input owns the transaction/direction; replacement and anchor restoration do
# not. Wait until the replacement's programmatic-scroll guard has expired,
# then remeasure actual rendered geometry and request only the next adjacent
# canonical interval when headroom is still insufficient.
view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
view = view_path.read_text(encoding="utf-8")
old = r'''  virtualShiftPending = false;
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
new = r'''  virtualShiftPending = false;
  if (renderReason === 'scroll-up' || renderReason === 'scroll-down') {
    const fallbackDirection = renderReason === 'scroll-up' ? -1 : 1;
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
view = replace_once(view, old, new, "manual-scroll convergence scheduling")
view_path.write_text(view, encoding="utf-8")

print("Applied acceptance production repairs v7.")
