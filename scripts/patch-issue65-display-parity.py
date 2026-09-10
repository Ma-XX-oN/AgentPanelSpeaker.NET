from pathlib import Path

path = Path("tools/AgentPanelSpeaker.DisplayParity/Program.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"Expected exactly one match, found {count}: {old[:80]!r}")
  text = text.replace(old, new, 1)


replace_once(
  '''    TranscriptNodeIdentity[] thoughtIdentities = identities
      .Where(identity =>
        identity.SourceId is "thought-one" or "thought-two" or "thought-three")
      .ToArray();''',
  '''    TranscriptNodeIdentity[] thoughtIdentities = identities
      .Where(identity => identity.RecordNumber is 2 or 3 or 4)
      .ToArray();''')

replace_once(
  '''      if (!document.TryGetIndex(
            identity.RecordNumber,
            identity.SourceId,
            out int virtualIndex))
      {
        failures.Add(
          $"Claude thought identity has no virtual-document unit: " +
          $"{identity.RecordNumber}/{identity.SourceId}.");''',
  '''      if (!document.TryGetIndex(identity.RecordNumber, out int virtualIndex))
      {
        failures.Add(
          $"Claude thought identity has no virtual-document unit: " +
          $"record={identity.RecordNumber}.");''')

replace_once(
  '''  return left.NodeId == right.NodeId &&
    left.RecordNumber == right.RecordNumber &&
    string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal) &&
    left.Segments.SequenceEqual(right.Segments, StringComparer.Ordinal);''',
  '''  return left.NodeId == right.NodeId &&
    left.RecordNumber == right.RecordNumber &&
    left.Segments.SequenceEqual(right.Segments, StringComparer.Ordinal);''')

replace_once(
  '''  return $"node={identity.NodeId},record={identity.RecordNumber}," +
    $"source={identity.SourceId},segments=[{string.Join("|", identity.Segments)}]";''',
  '''  return $"node={identity.NodeId},record={identity.RecordNumber}," +
    $"segments=[{string.Join("|", identity.Segments)}]";''')

replace_once(
  '''    string anchor =
      $"data-jsonl-record=\\"{identity.RecordNumber}\\" data-source-id=\\"" +
      $"{identity.SourceId}\\"";
    if (!markdown.Contains(anchor, StringComparison.Ordinal))
    {
      failures.Add(
        $"{label} canonical identity has no matching DOM anchor: " +
        $"record={identity.RecordNumber} source={identity.SourceId}.");''',
  '''    string anchor = $"data-jsonl-record=\\"{identity.RecordNumber}\\"";
    if (!markdown.Contains(anchor, StringComparison.Ordinal))
    {
      failures.Add(
        $"{label} canonical identity has no matching DOM anchor: " +
        $"record={identity.RecordNumber}.");''')

replace_once(
  '''        if (!searchIndex.TryResolveVoiceOrigin(
              identity.NodeId,
              nodeWordIndex,
              out int recordNumber,
              out string sourceId,
              out int recordWordIndex))''',
  '''        if (!searchIndex.TryResolveVoiceOrigin(
              identity.NodeId,
              nodeWordIndex,
              out int recordNumber,
              out int recordWordIndex))''')

replace_once(
  '''        else if (recordNumber != identity.RecordNumber ||
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
            $"actual={recordNumber}/{sourceId}/{recordWordIndex}.");''',
  '''        else if (recordNumber != identity.RecordNumber ||
                 recordWordIndex < 0)
        {
          failures.Add(
            $"{label} speech/highlight provenance mismatch: " +
            $"node={identity.NodeId} word={nodeWordIndex} " +
            $"expectedRecord={identity.RecordNumber} " +
            $"actual={recordNumber}/{recordWordIndex}.");''')

path.write_text(text, encoding="utf-8", newline="\n")
