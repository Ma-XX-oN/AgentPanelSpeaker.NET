from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      int focalIndex = -1;
      for (int candidate = 0; candidate < document.Count; ++candidate)
      {
        TranscriptWindow window = document.CreateWindow(candidate);
        if (window.StartIndex > 0 &&
            window.EndIndex < document.Count - 1 &&
            tallIndex >= window.StartIndex &&
            tallIndex <= window.EndIndex &&
            window.EndIndex - tallIndex <= 2)
        {
          focalIndex = candidate;
          break;
        }
      }
      Require(
        focalIndex >= 0,
        "Could not position the compact tall-turn fixture near a physical " +
        "materialized-window edge without oversized synthetic turns.");

      Task positionedWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        focalIndex,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(positionedWindow, "tall-turn edge-positioned window");
'''
new = '''      int physicalStart = Math.Max(0, tallIndex - 1);
      int physicalEnd = Math.Min(document.Count - 2, tallIndex + 1);
      Require(
        physicalStart < tallIndex &&
        physicalEnd > tallIndex &&
        physicalEnd < document.Count - 1,
        "Tall-turn physical fixture could not retain materialized neighbours " +
        "and unloaded content below the tall Core unit.");

      TranscriptVirtualRecord[] physicalRecords = document.Records
        .Skip(physicalStart)
        .Take(physicalEnd - physicalStart + 1)
        .ToArray();
      string physicalHtml = string.Concat(
        physicalRecords.Select((record, offset) =>
          "<section class=\\\"virtual-record\\\" data-virtual-index=\\\"" +
          (physicalStart + offset) + "\\\">" + record.Html + "</section>"));
      double topSpacerHeight = document.Records
        .Take(physicalStart)
        .Sum(record => record.EstimatedHeight);
      double bottomSpacerHeight = document.Records
        .Skip(physicalEnd + 1)
        .Sum(record => record.EstimatedHeight);
      var physicalWindow = new TranscriptWindow(
        physicalHtml,
        physicalStart,
        physicalEnd,
        topSpacerHeight,
        bottomSpacerHeight,
        physicalRecords);

      MethodInfo? buildWindowScript = typeof(TranscriptView).GetMethod(
        "BuildReplaceWindowScript",
        BindingFlags.Instance | BindingFlags.NonPublic);
      Require(
        buildWindowScript is not null,
        "Could not access the production virtual-window replacement builder.");
      string script = buildWindowScript!.Invoke(
        view,
        new object?[]
        {
          physicalWindow,
          false,
          null,
          null,
          null,
          null,
          null,
          null,
          null
        }) as string ?? string.Empty;
      Require(
        !string.IsNullOrWhiteSpace(script),
        "Production virtual-window replacement builder returned no script.");
      ExecuteVoidScript(webView, script);
      SetField(view, "_windowStartIndex", physicalStart);
      SetField(view, "_windowEndIndex", physicalEnd);
      PumpMessages(250);
'''
if text.count(old) != 1:
  raise SystemExit("tall-turn record-count placement block did not match exactly once")
text = text.replace(old, new, 1)

helper_anchor = '''  private static T ReadField<T>(object target, string fieldName)
'''
helper = '''  private static void SetField<T>(
    object target,
    string fieldName,
    T value)
  {
    FieldInfo? field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
      throw new InvalidOperationException(
        $"Field {fieldName} was not found on {target.GetType().Name}.");
    }
    field.SetValue(target, value);
  }

'''
if text.count(helper_anchor) != 1:
  raise SystemExit("test reflection helper anchor did not match exactly once")
text = text.replace(helper_anchor, helper + helper_anchor, 1)

path.write_text(text, encoding="utf-8")
