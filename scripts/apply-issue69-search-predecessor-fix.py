from pathlib import Path


def replace_once(text: str, old: str, new: str, description: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected exactly one {description} occurrence, found {count}.")
  return text.replace(old, new, 1)


virtual_path = Path("AgentPanelSpeaker/TranscriptVirtualDocument.cs")
virtual_text = virtual_path.read_text(encoding="utf-8")
virtual_anchor = """    return BuildWindow(left, right);\n  }\n\n  /// <summary>\n  /// Slides an existing physical window in one direction.  The newly exposed\n"""
virtual_replacement = """    return BuildWindow(left, right);\n  }\n\n  /// <summary>\n  /// Creates a Find-navigation window while retaining the immediately preceding\n  /// visible atomic Core unit as local context, even when the searched unit alone\n  /// exceeds the normal physical-height target.\n  /// </summary>\n  public TranscriptWindow CreateSearchWindow(\n    int focalIndex,\n    double viewportHeight)\n  {\n    TranscriptWindow window = CreateWindow(focalIndex, viewportHeight);\n    if (_records.Length == 0)\n    {\n      return window;\n    }\n\n    int resolvedFocalIndex = ResolveVisibleFocalIndex(\n      Math.Clamp(focalIndex, 0, _records.Length - 1));\n    int previousVisibleIndex = FindPreviousVisibleIndex(resolvedFocalIndex);\n    return previousVisibleIndex >= 0 && window.StartIndex > previousVisibleIndex\n      ? BuildWindow(previousVisibleIndex, window.EndIndex)\n      : window;\n  }\n\n  /// <summary>\n  /// Slides an existing physical window in one direction.  The newly exposed\n"""
virtual_text = replace_once(
  virtual_text,
  virtual_anchor,
  virtual_replacement,
  "CreateSearchWindow insertion anchor")
virtual_path.write_text(virtual_text, encoding="utf-8", newline="\n")

view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
view_text = view_path.read_text(encoding="utf-8")
method_marker = "  private async Task RenderWindowForRecordAsync(\n"
method_index = view_text.find(method_marker)
if method_index < 0:
  raise RuntimeError("RenderWindowForRecordAsync() was not found.")
window_call = (
  "      TranscriptWindow window = document.CreateWindow("\
  "focalIndex, GetVirtualViewportHeight());")
call_index = view_text.find(window_call, method_index)
if call_index < 0:
  raise RuntimeError("Find materialization CreateWindow() call was not found.")
replacement_call = """      bool searchNavigation = string.Equals(\n        reason,\n        \"search\",\n        StringComparison.OrdinalIgnoreCase);\n      TranscriptWindow window = searchNavigation\n        ? document.CreateSearchWindow(focalIndex, GetVirtualViewportHeight())\n        : document.CreateWindow(focalIndex, GetVirtualViewportHeight());"""
view_text = (
  view_text[:call_index] +
  replacement_call +
  view_text[call_index + len(window_call):])
view_path.write_text(view_text, encoding="utf-8", newline="\n")

# Diagnostic-only refinement for the failed GREEN attempt.  It leaves the
# permanent invariant unchanged and only reports the observed indexes.
test_path = Path("AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
test_text = test_path.read_text(encoding="utf-8")
test_old = """    Require(end >= focalIndex,\n      \"Find materialization lost the searched Core unit while retaining context.\");\n"""
test_new = """    Require(end >= focalIndex,\n      $\"Find materialization lost the searched Core unit while retaining context. \" +\n      $\"Observed start={start}, focal={focalIndex}, end={end}.\");\n"""
test_text = replace_once(
  test_text,
  test_old,
  test_new,
  "issue 69 target-retention diagnostic")
test_path.write_text(test_text, encoding="utf-8", newline="\n")
