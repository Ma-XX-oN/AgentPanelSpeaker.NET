from pathlib import Path

path = Path("AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

test_name = '"real-session/search-window-retains-preceding-unit"'
if test_name not in text:
  old_list = '''      ("real-session/directional-shift-keeps-prefetch-headroom",\n        TestDirectionalShiftKeepsPrefetchHeadroom),\n      ("real-session/live-end-window-retains-preceding-unit",\n        TestLiveEndWindowRetainsPrecedingUnit)'''
  new_list = '''      ("real-session/directional-shift-keeps-prefetch-headroom",\n        TestDirectionalShiftKeepsPrefetchHeadroom),\n      ("real-session/search-window-retains-preceding-unit",\n        TestSearchWindowRetainsPrecedingUnit),\n      ("real-session/live-end-window-retains-preceding-unit",\n        TestLiveEndWindowRetainsPrecedingUnit)'''
  if text.count(old_list) != 1:
    raise SystemExit("Could not locate issue #54 test inventory insertion point.")
  text = text.replace(old_list, new_list, 1)

  marker = '''  /// <summary>\n  /// Reproduces issue #58 without splitting Core atomic units. A final turn can\n'''
  method = '''  /// <summary>\n  /// Reproduces issue #69 through the production Find materialization path. A\n  /// tall searched assistant turn must not materialize alone when a visible\n  /// predecessor Core turn exists.\n  /// </summary>\n  private static void TestSearchWindowRetainsPrecedingUnit()\n  {\n    var units = new[]\n    {\n      CreateUnit(0, "search-preceding",\n        "<p>Issue 69 preceding user turn.</p>"),\n      CreateUnit(1, "search-target",\n        "<p>Issue 69 searched assistant turn.</p>"),\n      CreateUnit(2, "search-following",\n        "<p>Issue 69 following turn.</p>")\n    };\n    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);\n    document.SetLayoutGeneration(1);\n    document.UpdateMeasuredHeights(\n      new Dictionary<int, double>\n      {\n        [0] = 120.0,\n        [1] = TranscriptVirtualDocument.DefaultViewportHeight * 6.0,\n        [2] = 120.0\n      },\n      1);\n    Require(document.TryGetIndex(2, out int focalIndex),\n      "Issue #69 fixture target record did not resolve.");\n\n    using var host = CreateOffscreenHost();\n    using var view = new TranscriptView { Dock = DockStyle.Fill };\n    host.Controls.Add(view);\n    host.Show();\n    _ = host.Handle;\n    _ = view.Handle;\n    WaitForViewInitialization(view);\n    PumpUntil(\n      () => ReadField<bool>(view, "_initialized"),\n      "issue #69 transcript shell initialization");\n\n    FieldInfo documentField = typeof(TranscriptView).GetField(\n      "_virtualDocument",\n      BindingFlags.Instance | BindingFlags.NonPublic) ??\n      throw new InvalidOperationException(\n        "TranscriptView._virtualDocument field was not found.");\n    documentField.SetValue(view, document);\n\n    MethodInfo render = typeof(TranscriptView).GetMethod(\n      "RenderWindowForRecordAsync",\n      BindingFlags.Instance | BindingFlags.NonPublic) ??\n      throw new InvalidOperationException(\n        "TranscriptView.RenderWindowForRecordAsync() was not found.");\n    object? invoked = render.Invoke(view, new object?[]\n    {\n      2,\n      "search",\n      0,\n      null\n    });\n    Task task = invoked as Task ??\n      throw new InvalidOperationException(\n        "Search-window materialization did not return a Task.");\n    PumpUntilCompleted(task, "issue #69 search-window materialization");\n\n    int start = ReadField<int>(view, "_windowStartIndex");\n    int end = ReadField<int>(view, "_windowEndIndex");\n    Require(start < focalIndex,\n      $"Find materialized the tall searched Core unit without its immediately " +\n      $"preceding visible turn. Observed start={start}, focal={focalIndex}, end={end}.");\n    Require(end >= focalIndex,\n      $"Find materialization lost the searched Core unit while retaining context. " +\n      $"Observed start={start}, focal={focalIndex}, end={end}.");\n  }\n\n'''
  if text.count(marker) != 1:
    raise SystemExit("Could not locate issue #58 method insertion point.")
  text = text.replace(marker, method + marker, 1)
else:
  init_old = '''    WaitForViewInitialization(view);\n\n    FieldInfo documentField = typeof(TranscriptView).GetField(\n'''
  init_new = '''    WaitForViewInitialization(view);\n    PumpUntil(\n      () => ReadField<bool>(view, "_initialized"),\n      "issue #69 transcript shell initialization");\n\n    FieldInfo documentField = typeof(TranscriptView).GetField(\n'''
  if '"issue #69 transcript shell initialization"' not in text:
    if text.count(init_old) != 1:
      raise SystemExit("Could not locate issue #69 shell-initialization insertion point.")
    text = text.replace(init_old, init_new, 1)

  first_old = '''    Require(start < focalIndex,\n      "Find materialized the tall searched Core unit without its immediately " +\n      "preceding visible turn.");\n'''
  first_new = '''    Require(start < focalIndex,\n      $"Find materialized the tall searched Core unit without its immediately " +\n      $"preceding visible turn. Observed start={start}, focal={focalIndex}, end={end}.");\n'''
  if first_old in text:
    text = text.replace(first_old, first_new, 1)

  second_old = '''    Require(end >= focalIndex,\n      "Find materialization lost the searched Core unit while retaining context.");\n'''
  second_new = '''    Require(end >= focalIndex,\n      $"Find materialization lost the searched Core unit while retaining context. " +\n      $"Observed start={start}, focal={focalIndex}, end={end}.");\n'''
  if second_old in text:
    text = text.replace(second_old, second_new, 1)

path.write_text(text, encoding="utf-8", newline="\n")
