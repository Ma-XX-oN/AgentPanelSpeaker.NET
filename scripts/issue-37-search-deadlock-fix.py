from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptSearchIndex.cs")
text = path.read_text(encoding="utf-8")

old = '''    IReadOnlyList<RecordTextMatch> raw = request.Regex
      ? await RegexWorkerClient.SearchAsync(
          records.Select(record => record.Text).ToArray(),
          request.Query,
          request.CaseSensitive,
          request.WholeWord,
          cancellationToken)
      : await Task.Run(
          () => FindLiteral(records, request, cancellationToken),
          cancellationToken);
'''
new = '''    IReadOnlyList<RecordTextMatch> raw = request.Regex
      ? await RegexWorkerClient.SearchAsync(
          records.Select(record => record.Text).ToArray(),
          request.Query,
          request.CaseSensitive,
          request.WholeWord,
          cancellationToken).ConfigureAwait(false)
      : await Task.Run(
          () => FindLiteral(records, request, cancellationToken),
          cancellationToken).ConfigureAwait(false);
'''

count = text.count(old)
if count != 1:
  raise SystemExit(f"SearchAsync await block count={count}")

path.write_text(text.replace(old, new, 1), encoding="utf-8")
