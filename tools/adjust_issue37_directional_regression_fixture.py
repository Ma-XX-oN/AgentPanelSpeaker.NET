from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''    string path = Path.Combine(root, "rollout-issue37-directional-prefetch.jsonl");
    WriteFixture(path);
'''
new = '''    string path = Path.Combine(root, "rollout-issue37-directional-prefetch.jsonl");
    WriteDirectionalFixture(path);
'''
if text.count(old) != 1:
  raise SystemExit(f"expected one directional fixture call, found {text.count(old)}")
text = text.replace(old, new, 1)

old = '''      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        30,
        "test-precondition",
        null,
        string.Empty,
        null);
'''
new = '''      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        string.Empty,
        null);
'''
if text.count(old) != 1:
  raise SystemExit(f"expected one middle-window precondition, found {text.count(old)}")
text = text.replace(old, new, 1)

old = '''      Require(
        ReadField<int>(view, "_windowStartIndex") > 0,
        "Directional prefetch precondition did not leave unloaded content above.");
'''
new = '''      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Directional prefetch precondition did not leave unloaded content " +
        "on both sides of the materialized window.");
'''
if text.count(old) != 1:
  raise SystemExit(f"expected one directional precondition assertion, found {text.count(old)}")
text = text.replace(old, new, 1)

old = '''      Task resetMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        30,
        "test-precondition",
'''
new = '''      Task resetMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
'''
if text.count(old) != 1:
  raise SystemExit(f"expected one middle-window reset, found {text.count(old)}")
text = text.replace(old, new, 1)

marker = '''  private static void WriteFixture(string path)
  {
'''
fixture = '''  private static void WriteDirectionalFixture(string path)
  {
    const int directionalPairCount = 120;
    string largePayload = new('x', 32_000);
    var records = new List<string>(directionalPairCount * 2);
    for (int index = 1; index <= directionalPairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T02:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Directional issue 37 request {index:D3}. {largePayload}"
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T02:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Directional issue 37 response {index:D3}. {largePayload}"
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

'''
if text.count(marker) != 1:
  raise SystemExit(f"expected one fixture insertion point, found {text.count(marker)}")
text = text.replace(marker, fixture + marker, 1)

path.write_text(text, encoding="utf-8")
