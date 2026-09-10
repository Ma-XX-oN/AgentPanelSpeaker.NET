from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptVirtualDocument.cs")
text = path.read_text(encoding="utf-8")

old_trim = """    TrimToMinimumHeight(
      ref left,
      ref right,
      focalIndex,
      focalIndex,
      targetHeight,
      ref totalHeight,
      trimBothEdges: true);
    return BuildWindow(left, right);
"""
new_trim = """    int protectedStartIndex = focalIndex;
    if (IsLastVisibleIndex(focalIndex))
    {
      int previousVisibleIndex = FindPreviousVisibleIndex(focalIndex);
      if (previousVisibleIndex >= 0 && left > previousVisibleIndex)
      {
        left = previousVisibleIndex;
        totalHeight = SumHeights(left, right + 1);
        protectedStartIndex = previousVisibleIndex;
      }
    }

    TrimToMinimumHeight(
      ref left,
      ref right,
      protectedStartIndex,
      focalIndex,
      targetHeight,
      ref totalHeight,
      trimBothEdges: true);
    return BuildWindow(left, right);
"""
if text.count(old_trim) != 1:
    raise SystemExit("Expected exactly one initial-window trim block.")
text = text.replace(old_trim, new_trim, 1)

old_resolver = """  private int ResolveVisibleFocalIndex(int focalIndex)
  {
"""
new_resolver = """  /// <summary>
  /// Returns whether the supplied unit is the final currently visible Core
  /// unit, ignoring hidden historical revisions after it.
  /// </summary>
  private bool IsLastVisibleIndex(int index)
  {
    for (int candidate = index + 1; candidate < _records.Length; ++candidate)
    {
      if (IsVisible(candidate))
      {
        return false;
      }
    }
    return true;
  }

  /// <summary>
  /// Finds the immediately preceding currently visible Core unit.
  /// </summary>
  private int FindPreviousVisibleIndex(int index)
  {
    for (int candidate = index - 1; candidate >= 0; --candidate)
    {
      if (IsVisible(candidate))
      {
        return candidate;
      }
    }
    return -1;
  }

  private int ResolveVisibleFocalIndex(int focalIndex)
  {
"""
if text.count(old_resolver) != 1:
    raise SystemExit("Expected exactly one visible-focal resolver.")
text = text.replace(old_resolver, new_resolver, 1)

path.write_text(text, encoding="utf-8", newline="")
