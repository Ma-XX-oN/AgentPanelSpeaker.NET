using AgentPanelSpeaker;

if (args.Length != 2)
{
  Console.Error.WriteLine(
    "Usage: AgentPanelSpeaker.DisplayParity <AIConversationCore-root> <worker-path>");
  return 2;
}

string coreRoot = Path.GetFullPath(args[0]);
string workerPath = Path.GetFullPath(args[1]);
Environment.SetEnvironmentVariable("AI_CONVERSATION_CORE", coreRoot);

string deployedWorkerDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
Directory.CreateDirectory(deployedWorkerDirectory);
File.Copy(
  workerPath,
  Path.Combine(deployedWorkerDirectory, "AIConversationCore-worker.mjs"),
  overwrite: true);

var failures = new List<string>();
ValidateClaude(coreRoot, failures);
ValidateCodex(coreRoot, failures);

if (failures.Count != 0)
{
  Console.Error.WriteLine(
    $"FAIL: {failures.Count} canonical display/identity validation difference(s)");
  foreach (string failure in failures)
  {
    Console.Error.WriteLine(failure);
  }
  return 1;
}

Console.WriteLine(
  "PASS: transcript display and node identity are sourced from canonical projection.");
return 0;

static void ValidateClaude(string coreRoot, ICollection<string> failures)
{
  string path = Path.Combine(
    coreRoot,
    "tests",
    "fixtures",
    "claude",
    "adaptive-fence.jsonl");
  string markdown = TranscriptMarkdownFormatter.Format(path, AgentSource.Claude);

  Require(markdown, "[claude]", "Claude session header", failures);
  Require(markdown, "data-jsonl-record=\"1\"", "Claude first record anchor", failures);
  Require(markdown, "Run the command.", "Claude User message", failures);
  Require(markdown, "Print hello", "canonical Claude Bash description", failures);
  Require(markdown, "echo hello", "canonical Claude Bash command", failures);
  Require(
    markdown,
    "Output with ```triple backticks``` inside the result.",
    "canonical Claude Bash result",
    failures);
  Require(markdown, "Done.", "Claude final message", failures);
  Reject(markdown, "record_index=", "raw provenance comment", failures);
  ValidateIdentityChain(
    path,
    AgentSource.Claude,
    markdown,
    "Claude",
    failures);
}

static void ValidateCodex(string coreRoot, ICollection<string> failures)
{
  string path = Path.Combine(
    coreRoot,
    "tests",
    "fixtures",
    "codex",
    "codex-orphan-patch.jsonl");
  string markdown = TranscriptMarkdownFormatter.Format(path, AgentSource.Codex);

  Require(markdown, "[codex]", "Codex session header", failures);
  Require(markdown, "data-jsonl-record=\"1\"", "Codex first record anchor", failures);
  Require(markdown, "Fix the corners order.", "Codex User message", failures);
  Require(
    markdown,
    "I found the ordering bug in corners().",
    "Codex commentary",
    failures);
  Require(markdown, "*** Update File: foo.py", "canonical Codex patch display", failures);
  Reject(markdown, "record_index=", "raw provenance comment", failures);
  ValidateIdentityChain(
    path,
    AgentSource.Codex,
    markdown,
    "Codex",
    failures);
}

static void ValidateIdentityChain(
  string path,
  AgentSource source,
  string markdown,
  string label,
  ICollection<string> failures)
{
  IReadOnlyList<TranscriptNodeIdentity> identities =
    TranscriptNodeIdentityMap.Build(path, source);
  if (identities.Count == 0)
  {
    failures.Add($"{label} canonical identity map is empty.");
    return;
  }

  long expectedNodeId = 1;
  foreach (TranscriptNodeIdentity identity in identities)
  {
    if (identity.NodeId != expectedNodeId)
    {
      failures.Add(
        $"{label} node sequence expected {expectedNodeId}, found {identity.NodeId}.");
    }
    expectedNodeId++;

    string anchor =
      $"data-jsonl-record=\"{identity.RecordNumber}\" data-source-id=\"" +
      $"{identity.SourceId}\"";
    if (!markdown.Contains(anchor, StringComparison.Ordinal))
    {
      failures.Add(
        $"{label} canonical identity has no matching DOM anchor: " +
        $"record={identity.RecordNumber} source={identity.SourceId}.");
    }
  }
}

static void Require(
  string text,
  string expected,
  string description,
  ICollection<string> failures)
{
  if (!text.Contains(expected, StringComparison.Ordinal))
  {
    failures.Add($"Missing {description}: {expected}");
  }
}

static void Reject(
  string text,
  string unexpected,
  string description,
  ICollection<string> failures)
{
  if (text.Contains(unexpected, StringComparison.Ordinal))
  {
    failures.Add($"Unexpected {description}: {unexpected}");
  }
}
