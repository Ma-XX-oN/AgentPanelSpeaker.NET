using AgentPanelSpeaker;
using Markdig;

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
    $"FAIL: {failures.Count} canonical display/identity/highlight/growth " +
    "validation difference(s)");
  foreach (string failure in failures)
  {
    Console.Error.WriteLine(failure);
  }
  return 1;
}

Console.WriteLine(
  "PASS: transcript display, node identity, speech/highlight mapping, and " +
  "live-growth stability are canonical.");
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
  ValidateGrowth(path, AgentSource.Claude, "Claude", failures);
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
  ValidateGrowth(path, AgentSource.Codex, "Codex", failures);
}

static void ValidateGrowth(
  string sourcePath,
  AgentSource source,
  string label,
  ICollection<string> failures)
{
  string[] lines = File.ReadLines(sourcePath)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .ToArray();
  string tempPath = Path.Combine(
    Path.GetTempPath(),
    $"AgentPanelSpeaker-growth-{Guid.NewGuid():N}.jsonl");
  IReadOnlyList<TranscriptNodeIdentity> previous =
    Array.Empty<TranscriptNodeIdentity>();

  try
  {
    File.WriteAllText(tempPath, string.Empty);
    for (int prefixLength = 1; prefixLength <= lines.Length; ++prefixLength)
    {
      File.AppendAllText(tempPath, lines[prefixLength - 1] + Environment.NewLine);
      IReadOnlyList<TranscriptNodeIdentity> current =
        TranscriptNodeIdentityMap.Build(tempPath, source);

      if (current.Count < previous.Count)
      {
        failures.Add(
          $"{label} live growth removed identities at prefix {prefixLength}: " +
          $"previous={previous.Count} current={current.Count}.");
      }
      else
      {
        for (int index = 0; index < previous.Count; ++index)
        {
          if (!IdentityEquals(previous[index], current[index]))
          {
            failures.Add(
              $"{label} live growth changed prior identity at prefix " +
              $"{prefixLength}, node {index + 1}: " +
              $"before={FormatIdentity(previous[index])} " +
              $"after={FormatIdentity(current[index])}.");
          }
        }
      }

      string markdown = TranscriptMarkdownFormatter.Format(tempPath, source);
      ValidateIdentityChain(
        tempPath,
        source,
        markdown,
        $"{label} growth prefix {prefixLength}",
        failures);
      previous = current;
    }
  }
  finally
  {
    try
    {
      File.Delete(tempPath);
    }
    catch (IOException)
    {
      // Best-effort cleanup of a temporary parity fixture.
    }
  }
}

static bool IdentityEquals(
  TranscriptNodeIdentity left,
  TranscriptNodeIdentity right)
{
  return left.NodeId == right.NodeId &&
    left.RecordNumber == right.RecordNumber &&
    string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal) &&
    left.Segments.SequenceEqual(right.Segments, StringComparer.Ordinal);
}

static string FormatIdentity(TranscriptNodeIdentity identity)
{
  return $"node={identity.NodeId},record={identity.RecordNumber}," +
    $"source={identity.SourceId},segments=[{string.Join("|", identity.Segments)}]";
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

  string html = Markdown.ToHtml(
    markdown,
    new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
  TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
    html,
    identities,
    CancellationToken.None);

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

    int nodeWordIndex = 0;
    foreach (string segment in identity.Segments)
    {
      int wordCount = SpeechTokenization.Matches(segment).Count;
      for (int index = 0; index < wordCount; ++index)
      {
        if (!searchIndex.TryResolveVoiceOrigin(
              identity.NodeId,
              nodeWordIndex,
              out int recordNumber,
              out string sourceId,
              out int recordWordIndex))
        {
          failures.Add(
            $"{label} speech word is not mapped to rendered transcript: " +
            $"node={identity.NodeId} word={nodeWordIndex}.");
        }
        else if (recordNumber != identity.RecordNumber ||
                 !string.Equals(
                   sourceId,
                   identity.SourceId,
                   StringComparison.Ordinal) ||
                 recordWordIndex < 0)
        {
          failures.Add(
            $"{label} speech/highlight provenance mismatch: " +
            $"node={identity.NodeId} word={nodeWordIndex} " +
            $"expected={identity.RecordNumber}/{identity.SourceId} " +
            $"actual={recordNumber}/{sourceId}/{recordWordIndex}.");
        }
        nodeWordIndex++;
      }
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
