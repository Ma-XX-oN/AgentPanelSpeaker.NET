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
ValidateClaudeThoughtGroup(failures);
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

static void ValidateClaudeThoughtGroup(ICollection<string> failures)
{
  string tempPath = Path.Combine(
    Path.GetTempPath(),
    $"AgentPanelSpeaker-thoughts-{Guid.NewGuid():N}.jsonl");
  string[] records =
  {
    "{\"type\":\"user\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:01.000Z\",\"uuid\":\"thought-user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Please reason through this.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:02.000Z\",\"uuid\":\"thought-one\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"First thought has unique alpha words.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:03.000Z\",\"uuid\":\"thought-two\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Second thought has unique beta words.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:04.000Z\",\"uuid\":\"thought-three\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Third thought has unique gamma words.\"}]}}",
    "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:05.000Z\",\"uuid\":\"thought-final\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Final Claude answer after thoughts.\"}]}}"
  };

  try
  {
    File.WriteAllLines(tempPath, records);
    string markdown = TranscriptMarkdownFormatter.Format(
      tempPath,
      AgentSource.Claude);
    Require(markdown, "<details>", "Claude thought details opening", failures);
    Require(
      markdown,
      "<summary>Having 3 thoughts</summary>",
      "Claude thought details summary",
      failures);
    Require(markdown, "</details>", "Claude thought details closing", failures);

    var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    string html = TranscriptHtmlRenderer.ToHtml(markdown, pipeline);
    ValidateDetailsRange(html, "rendered Claude thought HTML", failures);
    Reject(
      html,
      "&gt;",
      "literal Markdown quote marker in grouped Claude thoughts",
      failures);
    Reject(
      html,
      "***",
      "literal Markdown thought separator in grouped Claude thoughts",
      failures);
    Require(
      html,
      "<hr",
      "rendered Markdown thought separator in grouped Claude thoughts",
      failures);

    IReadOnlyList<TranscriptNodeIdentity> identities =
      TranscriptNodeIdentityMap.Build(tempPath, AgentSource.Claude);
    TranscriptNodeIdentity[] thoughtIdentities = identities
      .Where(identity =>
        identity.SourceId is "thought-one" or "thought-two" or "thought-three")
      .ToArray();
    if (thoughtIdentities.Length != 3)
    {
      failures.Add(
        $"Claude grouped-thought fixture expected 3 thought identities, " +
        $"found {thoughtIdentities.Length}.");
    }

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
    var thoughtIndexes = new List<int>();
    foreach (TranscriptNodeIdentity identity in thoughtIdentities)
    {
      if (!document.TryGetIndex(
            identity.RecordNumber,
            identity.SourceId,
            out int virtualIndex))
      {
        failures.Add(
          $"Claude thought identity has no virtual-document unit: " +
          $"{identity.RecordNumber}/{identity.SourceId}.");
        continue;
      }
      thoughtIndexes.Add(virtualIndex);
    }

    // All source records inside one disclosure must resolve to the same
    // structural virtual unit. Otherwise CreateWindow() would wrap fragments
    // of one <details> element in separate <section> elements and the browser
    // would repair the invalid cross-section DOM by ending the disclosure early.
    if (thoughtIndexes.Count == 3 && thoughtIndexes.Distinct().Count() != 1)
    {
      failures.Add(
        "Claude details disclosure was split across multiple virtual-document units: " +
        string.Join(",", thoughtIndexes));
    }
    if (thoughtIndexes.Count != 0)
    {
      TranscriptWindow window = document.CreateWindow(thoughtIndexes[0]);
      ValidateDetailsRange(window.Html, "virtualized Claude thought HTML", failures);
      int detailsStart = window.Html.IndexOf("<details>", StringComparison.Ordinal);
      int detailsEnd = detailsStart < 0
        ? -1
        : window.Html.IndexOf("</details>", detailsStart, StringComparison.Ordinal);
      if (detailsStart >= 0 && detailsEnd >= 0)
      {
        int nestedSection = window.Html.IndexOf(
          "<section class=\"virtual-record\"",
          detailsStart + 1,
          StringComparison.Ordinal);
        if (nestedSection >= 0 && nestedSection < detailsEnd)
        {
          failures.Add(
            "Virtualization inserted a record section boundary inside Claude details.");
        }
        int containingSectionClose = window.Html.IndexOf(
          "</section>",
          detailsStart,
          StringComparison.Ordinal);
        if (containingSectionClose >= 0 && containingSectionClose < detailsEnd)
        {
          failures.Add(
            "Virtualization closed the containing record section before Claude details ended.");
        }
      }
    }

    ValidateIdentityChain(
      tempPath,
      AgentSource.Claude,
      markdown,
      "Claude grouped thoughts",
      failures);
  }
  finally
  {
    try
    {
      File.Delete(tempPath);
    }
    catch (IOException)
    {
      // Best-effort cleanup of a temporary regression fixture.
    }
  }
}

static void ValidateDetailsRange(
  string html,
  string label,
  ICollection<string> failures)
{
  int detailsStart = html.IndexOf("<details>", StringComparison.Ordinal);
  int detailsEnd = detailsStart < 0
    ? -1
    : html.IndexOf("</details>", detailsStart, StringComparison.Ordinal);
  if (detailsStart < 0 || detailsEnd < 0)
  {
    failures.Add($"{label} does not contain one complete details disclosure.");
    return;
  }

  foreach (string thought in new[]
           {
             "First thought has unique alpha words.",
             "Second thought has unique beta words.",
             "Third thought has unique gamma words."
           })
  {
    int thoughtIndex = html.IndexOf(thought, StringComparison.Ordinal);
    if (thoughtIndex <= detailsStart || thoughtIndex >= detailsEnd)
    {
      failures.Add($"{label}: thought text escaped details: {thought}");
    }
  }

  int blockquoteStart = html.IndexOf(
    "<blockquote>",
    detailsStart,
    StringComparison.Ordinal);
  if (blockquoteStart < 0 || blockquoteStart >= detailsEnd)
  {
    failures.Add($"{label}: Claude thought quote styling is outside details.");
  }
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

  using (var monitor = new JsonlSessionMonitor())
  {
    LocatedSession session = SessionLocator.FromPath(path, source);
    SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
      session,
      speakExistingLatestTurn: false);
    foreach (IGrouping<long, SpeechFragment> nodeGroup in
             history.Fragments.GroupBy(fragment => fragment.NodeId))
    {
      TranscriptNodeIdentity? identity = identities.FirstOrDefault(
        item => item.NodeId == nodeGroup.Key);
      if (identity is null)
      {
        failures.Add(
          $"{label} speech node has no transcript identity: " +
          $"node={nodeGroup.Key}.");
        continue;
      }

      IReadOnlyList<string> expectedSegments = nodeGroup
        .Select(fragment => fragment.Text)
        .ToArray();
      if (expectedSegments.Count != 0 && identity.Segments.Count == 0)
      {
        failures.Add(
          $"{label} rendered speech node lost all DOM segments: " +
          $"node={nodeGroup.Key}; first={expectedSegments[0]}.");
      }
    }
  }

  string html = TranscriptHtmlRenderer.ToHtml(
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
