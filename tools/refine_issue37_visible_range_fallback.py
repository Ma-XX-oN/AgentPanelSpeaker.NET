from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptVirtualDocument.cs')
text = path.read_text(encoding='utf-8')

old = '''  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight,
    int? protectedStartIndex = null,
    int? protectedEndIndex = null)
  {
'''
new = '''  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight)
  {
    return CreateShiftedWindowCore(
      focalIndex,
      currentStartIndex,
      currentEndIndex,
      direction,
      viewportHeight,
      protectedStartIndex: null,
      protectedEndIndex: null,
      inferProtectedRangeWhenMissing: true);
  }

  /// <summary>
  /// Slides an existing physical window while protecting the exact atomic Core
  /// units that the browser reports as intersecting the physical viewport.
  /// </summary>
  public TranscriptWindow CreateShiftedWindow(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight,
    int? protectedStartIndex,
    int? protectedEndIndex)
  {
    return CreateShiftedWindowCore(
      focalIndex,
      currentStartIndex,
      currentEndIndex,
      direction,
      viewportHeight,
      protectedStartIndex,
      protectedEndIndex,
      inferProtectedRangeWhenMissing: false);
  }

  private TranscriptWindow CreateShiftedWindowCore(
    int focalIndex,
    int currentStartIndex,
    int currentEndIndex,
    int direction,
    double viewportHeight,
    int? protectedStartIndex,
    int? protectedEndIndex,
    bool inferProtectedRangeWhenMissing)
  {
'''
if text.count(old) != 1:
  raise SystemExit('shift overload refinement anchor mismatch')
text = text.replace(old, new, 1)

old_call = '''      protectedStartIndex,
      protectedEndIndex);
    double targetHeight = normalizedViewportHeight *
'''
new_call = '''      protectedStartIndex,
      protectedEndIndex,
      inferProtectedRangeWhenMissing);
    double targetHeight = normalizedViewportHeight *
'''
if text.count(old_call) != 1:
  raise SystemExit('protected-range resolver call anchor mismatch')
text = text.replace(old_call, new_call, 1)

old_helper = '''    double viewportHeight,
    int? protectedStartIndex,
    int? protectedEndIndex)
  {
    if (protectedStartIndex is int requestedStart ||
        protectedEndIndex is int requestedEnd)
'''
new_helper = '''    double viewportHeight,
    int? protectedStartIndex,
    int? protectedEndIndex,
    bool inferProtectedRangeWhenMissing)
  {
    if (protectedStartIndex.HasValue || protectedEndIndex.HasValue)
'''
if text.count(old_helper) != 1:
  raise SystemExit('protected-range helper signature anchor mismatch')
text = text.replace(old_helper, new_helper, 1)

fallback_anchor = '''      return (
        Math.Min(start, focalIndex),
        Math.Max(end, focalIndex));
    }

    int conservativeStart = focalIndex;
'''
fallback_replacement = '''      return (
        Math.Min(start, focalIndex),
        Math.Max(end, focalIndex));
    }

    if (!inferProtectedRangeWhenMissing)
    {
      return (focalIndex, focalIndex);
    }

    int conservativeStart = focalIndex;
'''
if text.count(fallback_anchor) != 1:
  raise SystemExit('protected-range fallback anchor mismatch')
text = text.replace(fallback_anchor, fallback_replacement, 1)

path.write_text(text, encoding='utf-8')
