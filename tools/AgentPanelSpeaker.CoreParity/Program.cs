using AgentPanelSpeaker;

if (args.Length != 2)
{
  Console.Error.WriteLine(
    "Usage: AgentPanelSpeaker.CoreParity <AIConversationCore-root> <worker-path>");
  return 2;
}

string coreRoot = Path.GetFullPath(args[0]);
string workerPath = Path.GetFullPath(args[1]);
Environment.SetEnvironmentVariable("AI_CONVERSATION_CORE", coreRoot);

var fixtures = new[]
{
  new Fixture(
    AgentSource.Claude,
    "claude/adaptive-fence.jsonl"),
  new Fixture(
    AgentSource.Claude,
    "claude/claude-subagent.jsonl"),
  new Fixture(
    AgentSource.Codex,
    "codex/codex-orphan-patch.jsonl")
};

using var client = new AIConversationCoreClient(workerPath);
var failures = new List<string>();
foreach (Fixture fixture in fixtures)
{
  string path = Path.Combine(coreRoot, "tests", "fixtures", fixture.RelativePath);
  string[] lines = File.ReadLines(path)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .ToArray();
  AIConversationProjection projection = client.Project(fixture.Source, lines);

  for (int index = 0; index < lines.Length; ++index)
  {
    ExtractionResult legacy = JsonlRecordExtractor.Extract(
      fixture.Source,
      lines[index]);
    ExtractionResult canonical = CanonicalProjectionExtractor.ExtractRecord(
      projection,
      fixture.Source,
      index);
    CompareResult(
      fixture.RelativePath,
      index + 1,
      legacy,
      canonical,
      failures);
  }
}

if (failures.Count != 0)
{
  Console.Error.WriteLine(
    $"FAIL: {failures.Count} canonical extraction parity difference(s)");
  foreach (string failure in failures)
  {
    Console.Error.WriteLine(failure);
  }
  return 1;
}

Console.WriteLine(
  $"PASS: canonical extraction matches v212 semantics for {fixtures.Length} fixtures.");
return 0;

static void CompareResult(
  string fixture,
  int recordNumber,
  ExtractionResult legacy,
  ExtractionResult canonical,
  ICollection<string> failures)
{
  string prefix = $"{fixture}:record {recordNumber}";
  CompareNodes(prefix, legacy.Nodes, canonical.Nodes, failures);
  CompareValue(
    prefix,
    "CompletionTimestamp",
    legacy.CompletionTimestamp,
    canonical.CompletionTimestamp,
    failures);
  CompareInputRequest(
    prefix,
    legacy.InputRequest,
    canonical.InputRequest,
    failures);
  CompareInputResponse(
    prefix,
    legacy.InputResponse,
    canonical.InputResponse,
    failures);
  CompareBackgroundWork(
    prefix,
    legacy.BackgroundWorkEvents ?? Array.Empty<BackgroundWorkEvent>(),
    canonical.BackgroundWorkEvents ?? Array.Empty<BackgroundWorkEvent>(),
    failures);
}

static void CompareNodes(
  string prefix,
  IReadOnlyList<ExtractedNode> legacy,
  IReadOnlyList<ExtractedNode> canonical,
  ICollection<string> failures)
{
  if (legacy.Count != canonical.Count)
  {
    failures.Add(
      $"{prefix}: node count legacy={legacy.Count} canonical={canonical.Count}");
    return;
  }

  for (int index = 0; index < legacy.Count; ++index)
  {
    ExtractedNode left = legacy[index];
    ExtractedNode right = canonical[index];
    string nodePrefix = $"{prefix}:node {index + 1}";
    CompareValue(nodePrefix, "Category", left.Category, right.Category, failures);
    CompareValue(nodePrefix, "Text", left.Text, right.Text, failures);
    CompareValue(nodePrefix, "Timestamp", left.Timestamp, right.Timestamp, failures);
    CompareValue(
      nodePrefix,
      "StartsUserTurn",
      left.StartsUserTurn,
      right.StartsUserTurn,
      failures);
  }
}

static void CompareInputRequest(
  string prefix,
  CodexInputRequest? legacy,
  CodexInputRequest? canonical,
  ICollection<string> failures)
{
  if (legacy is null || canonical is null)
  {
    if (legacy is not null || canonical is not null)
    {
      failures.Add(
        $"{prefix}: InputRequest presence legacy={legacy is not null} " +
        $"canonical={canonical is not null}");
    }
    return;
  }

  CompareValue(prefix, "InputRequest.CallId", legacy.CallId, canonical.CallId, failures);
  if (legacy.Questions.Count != canonical.Questions.Count)
  {
    failures.Add(
      $"{prefix}: InputRequest question count legacy={legacy.Questions.Count} " +
      $"canonical={canonical.Questions.Count}");
    return;
  }

  for (int index = 0; index < legacy.Questions.Count; ++index)
  {
    CodexInputQuestion left = legacy.Questions[index];
    CodexInputQuestion right = canonical.Questions[index];
    string questionPrefix = $"{prefix}:question {index + 1}";
    CompareValue(questionPrefix, "Id", left.Id, right.Id, failures);
    CompareValue(questionPrefix, "IsSecret", left.IsSecret, right.IsSecret, failures);
    if (left.Options.Count != right.Options.Count)
    {
      failures.Add(
        $"{questionPrefix}: option count legacy={left.Options.Count} " +
        $"canonical={right.Options.Count}");
      continue;
    }
    for (int optionIndex = 0; optionIndex < left.Options.Count; ++optionIndex)
    {
      CodexInputOption leftOption = left.Options[optionIndex];
      CodexInputOption rightOption = right.Options[optionIndex];
      string optionPrefix = $"{questionPrefix}:option {optionIndex + 1}";
      CompareValue(optionPrefix, "Label", leftOption.Label, rightOption.Label, failures);
      CompareValue(
        optionPrefix,
        "Description",
        leftOption.Description,
        rightOption.Description,
        failures);
    }
  }
}

static void CompareInputResponse(
  string prefix,
  CodexInputResponse? legacy,
  CodexInputResponse? canonical,
  ICollection<string> failures)
{
  if (legacy is null || canonical is null)
  {
    if (legacy is not null || canonical is not null)
    {
      failures.Add(
        $"{prefix}: InputResponse presence legacy={legacy is not null} " +
        $"canonical={canonical is not null}");
    }
    return;
  }

  CompareValue(prefix, "InputResponse.CallId", legacy.CallId, canonical.CallId, failures);
  CompareValue(
    prefix,
    "InputResponse.Timestamp",
    legacy.Timestamp,
    canonical.Timestamp,
    failures);
  string[] legacyKeys = legacy.Answers.Keys.OrderBy(value => value).ToArray();
  string[] canonicalKeys = canonical.Answers.Keys.OrderBy(value => value).ToArray();
  if (!legacyKeys.SequenceEqual(canonicalKeys, StringComparer.Ordinal))
  {
    failures.Add(
      $"{prefix}: InputResponse keys legacy=[{string.Join(",", legacyKeys)}] " +
      $"canonical=[{string.Join(",", canonicalKeys)}]");
    return;
  }
  foreach (string key in legacyKeys)
  {
    if (!legacy.Answers[key].SequenceEqual(
          canonical.Answers[key],
          StringComparer.Ordinal))
    {
      failures.Add(
        $"{prefix}: InputResponse[{key}] legacy=" +
        $"[{string.Join(",", legacy.Answers[key])}] canonical=" +
        $"[{string.Join(",", canonical.Answers[key])}]");
    }
  }
}

static void CompareBackgroundWork(
  string prefix,
  IReadOnlyList<BackgroundWorkEvent> legacy,
  IReadOnlyList<BackgroundWorkEvent> canonical,
  ICollection<string> failures)
{
  if (legacy.Count != canonical.Count)
  {
    failures.Add(
      $"{prefix}: background-work count legacy={legacy.Count} " +
      $"canonical={canonical.Count}");
    return;
  }

  for (int index = 0; index < legacy.Count; ++index)
  {
    BackgroundWorkEvent left = legacy[index];
    BackgroundWorkEvent right = canonical[index];
    string workPrefix = $"{prefix}:background {index + 1}";
    CompareValue(workPrefix, "Id", left.Id, right.Id, failures);
    CompareValue(
      workPrefix,
      "Description",
      left.Description,
      right.Description,
      failures);
    CompareValue(workPrefix, "StartUtc", left.StartUtc, right.StartUtc, failures);
    CompareValue(workPrefix, "EndUtc", left.EndUtc, right.EndUtc, failures);
  }
}

static void CompareValue<T>(
  string prefix,
  string name,
  T legacy,
  T canonical,
  ICollection<string> failures)
{
  if (!EqualityComparer<T>.Default.Equals(legacy, canonical))
  {
    failures.Add(
      $"{prefix}: {name} legacy={Format(legacy)} canonical={Format(canonical)}");
  }
}

static string Format<T>(T value)
{
  return value is null ? "<null>" : value.ToString() ?? "<null>";
}

internal sealed record Fixture(AgentSource Source, string RelativePath);
