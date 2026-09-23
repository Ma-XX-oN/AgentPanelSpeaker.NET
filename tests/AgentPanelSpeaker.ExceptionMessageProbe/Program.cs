using System.Reflection;
using System.Text.Json;

const int sourceIndex = 7;
const string blockId = "probe:block";
const string expected =
  "Core block probe:block has speech text but no canonical word projection " +
  "for source record 7.";

Assembly app = Assembly.Load("AgentPanelSpeaker");
Type projectionType = app.GetType("AgentPanelSpeaker.AIConversationProjection") ??
  throw new InvalidOperationException("AIConversationProjection type was not found.");
Type sourceType = app.GetType("AgentPanelSpeaker.AgentSource") ??
  throw new InvalidOperationException("AgentSource type was not found.");
Type extractorType = app.GetType("AgentPanelSpeaker.CanonicalProjectionExtractor") ??
  throw new InvalidOperationException("CanonicalProjectionExtractor type was not found.");
MethodInfo extractRecord = extractorType.GetMethod(
  "ExtractRecord",
  BindingFlags.Public | BindingFlags.Static) ??
  throw new InvalidOperationException("ExtractRecord method was not found.");

string projectionJson = JsonSerializer.Serialize(new
{
  schema_version = 2,
  events = new object[]
  {
    new
    {
      source_index = sourceIndex,
      kind = "message",
      role = "assistant",
      channel = "final",
      content_type = "message",
      blocks = new object[]
      {
        new
        {
          id = blockId,
          type = "text",
          text = "Missing canonical words."
        }
      }
    }
  },
  turns = Array.Empty<object>(),
  units = Array.Empty<object>(),
  presentation = (object?)null,
  markdown = string.Empty,
  html_units = Array.Empty<object>()
});
object projection = JsonSerializer.Deserialize(projectionJson, projectionType) ??
  throw new InvalidOperationException("Projection fixture could not be deserialized.");
object source = Enum.Parse(sourceType, "Claude");

try
{
  _ = extractRecord.Invoke(null, new[] { projection, source, sourceIndex });
  Console.Error.WriteLine(
    "FAIL  issue-142/source-record-diagnostic-identifies-exact-record");
  Console.Error.WriteLine(
    "      Expected InvalidDataException was not thrown.");
  return 1;
}
catch (TargetInvocationException exception)
  when (exception.InnerException is InvalidDataException invalidData)
{
  if (!string.Equals(invalidData.Message, expected, StringComparison.Ordinal))
  {
    Console.Error.WriteLine(
      "FAIL  issue-142/source-record-diagnostic-identifies-exact-record");
    Console.Error.WriteLine($"      Expected: {expected}");
    Console.Error.WriteLine($"      Actual:   {invalidData.Message}");
    return 1;
  }
}

Console.WriteLine(
  "PASS  issue-142/source-record-diagnostic-identifies-exact-record");
return 0;
