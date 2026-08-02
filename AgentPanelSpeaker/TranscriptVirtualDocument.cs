using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Stores the complete rendered transcript as independently renderable records.
/// </summary>
internal sealed class TranscriptVirtualDocument
{
  private const int Radius = 40;
  private const int MaximumHtmlCharacters = 1_000_000;
  private static readonly Regex AnchorRegex = new(
    "<span class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*data-source-id=\\\"(?<source>[^\\\"]*)\\\"[^>]*></span>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private readonly TranscriptVirtualRecord[] _records;
  private readonly Dictionary<string, int> _recordIndexes;

  private TranscriptVirtualDocument(TranscriptVirtualRecord[] records)
  {
    _records = records;
    _recordIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int index = 0; index < records.Length; ++index)
    {
      _recordIndexes[MakeKey(records[index].RecordNumber, records[index].SourceId)] = index;
    }
  }

  public int Count => _records.Length;

  public static TranscriptVirtualDocument Build(string html)
  {
    MatchCollection matches = AnchorRegex.Matches(html);
    if (matches.Count == 0)
    {
      return new TranscriptVirtualDocument(new[]
      {
        new TranscriptVirtualRecord(0, string.Empty, html)
      });
    }
    var records = new List<TranscriptVirtualRecord>(matches.Count);
    for (int index = 0; index < matches.Count; ++index)
    {
      Match match = matches[index];
      int start = index == 0 ? 0 : match.Index;
      int end = index + 1 < matches.Count ? matches[index + 1].Index : html.Length;
      _ = int.TryParse(
        WebUtility.HtmlDecode(match.Groups["record"].Value),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int recordNumber);
      string sourceId = WebUtility.HtmlDecode(match.Groups["source"].Value);
      records.Add(new TranscriptVirtualRecord(
        recordNumber,
        sourceId,
        html[start..end]));
    }
    return new TranscriptVirtualDocument(records.ToArray());
  }

  public bool TryGetIndex(int recordNumber, string sourceId, out int index)
  {
    return _recordIndexes.TryGetValue(MakeKey(recordNumber, sourceId), out index);
  }

  public TranscriptWindow CreateWindow(int focalIndex)
  {
    if (_records.Length == 0)
    {
      return new TranscriptWindow(string.Empty, 0, -1, Array.Empty<TranscriptVirtualRecord>());
    }
    focalIndex = Math.Clamp(focalIndex, 0, _records.Length - 1);
    int start = Math.Max(0, focalIndex - Radius);
    int end = Math.Min(_records.Length - 1, focalIndex + Radius);
    int characters = 0;
    int left = focalIndex;
    int right = focalIndex;
    characters += _records[focalIndex].Html.Length;
    while (true)
    {
      bool added = false;
      if (left > start && characters + _records[left - 1].Html.Length <= MaximumHtmlCharacters)
      {
        --left;
        characters += _records[left].Html.Length;
        added = true;
      }
      if (right < end && characters + _records[right + 1].Html.Length <= MaximumHtmlCharacters)
      {
        ++right;
        characters += _records[right].Html.Length;
        added = true;
      }
      if (!added)
      {
        break;
      }
    }
    TranscriptVirtualRecord[] records = _records[left..(right + 1)];
    return new TranscriptWindow(
      string.Concat(records.Select(record => record.Html)),
      left,
      right,
      records);
  }

  private static string MakeKey(int recordNumber, string sourceId)
  {
    return sourceId + "\0" + recordNumber.ToString(CultureInfo.InvariantCulture);
  }
}

internal sealed record TranscriptVirtualRecord(
  int RecordNumber,
  string SourceId,
  string Html);

internal sealed record TranscriptWindow(
  string Html,
  int StartIndex,
  int EndIndex,
  IReadOnlyList<TranscriptVirtualRecord> Records);
