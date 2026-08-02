using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Stores the complete rendered transcript as independently renderable records
/// while retaining an estimated height for unloaded records.
/// </summary>
internal sealed class TranscriptVirtualDocument
{
  private const int RegionRecordCount = 20;
  private const int LoadedRegionRadius = 2;
  private const int MaximumHtmlCharacters = 1_000_000;
  private const double MinimumEstimatedHeight = 72.0;
  private static readonly Regex AnchorRegex = new(
    "<span class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*data-source-id=\\\"(?<source>[^\\\"]*)\\\"[^>]*></span>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex TagRegex = new(
    "<[^>]+>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private readonly TranscriptVirtualRecord[] _records;
  private readonly Dictionary<string, int> _recordIndexes;
  private readonly double[] _heights;

  private TranscriptVirtualDocument(TranscriptVirtualRecord[] records)
  {
    _records = records;
    _heights = records.Select(record => record.EstimatedHeight).ToArray();
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
        new TranscriptVirtualRecord(0, string.Empty, html, EstimateHeight(html))
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
      string recordHtml = html[start..end];
      records.Add(new TranscriptVirtualRecord(
        recordNumber,
        sourceId,
        recordHtml,
        EstimateHeight(recordHtml)));
    }
    return new TranscriptVirtualDocument(records.ToArray());
  }

  public bool TryGetIndex(int recordNumber, string sourceId, out int index)
  {
    return _recordIndexes.TryGetValue(MakeKey(recordNumber, sourceId), out index);
  }

  public void UpdateMeasuredHeights(IReadOnlyDictionary<int, double> measurements)
  {
    foreach ((int index, double height) in measurements)
    {
      if (index >= 0 && index < _heights.Length &&
          double.IsFinite(height) && height >= MinimumEstimatedHeight)
      {
        _heights[index] = height;
      }
    }
  }

  public TranscriptWindow CreateWindow(int focalIndex)
  {
    if (_records.Length == 0)
    {
      return new TranscriptWindow(
        string.Empty,
        0,
        -1,
        0,
        0,
        Array.Empty<TranscriptVirtualRecord>());
    }

    focalIndex = Math.Clamp(focalIndex, 0, _records.Length - 1);
    int focalRegion = focalIndex / RegionRecordCount;
    int firstRegion = Math.Max(0, focalRegion - LoadedRegionRadius);
    int lastRegion = Math.Min(
      (_records.Length - 1) / RegionRecordCount,
      focalRegion + LoadedRegionRadius);
    int start = firstRegion * RegionRecordCount;
    int end = Math.Min(
      _records.Length - 1,
      ((lastRegion + 1) * RegionRecordCount) - 1);

    int characters = 0;
    int left = focalIndex;
    int right = focalIndex;
    characters += _records[focalIndex].Html.Length;
    while (true)
    {
      bool added = false;
      if (left > start &&
          characters + _records[left - 1].Html.Length <= MaximumHtmlCharacters)
      {
        --left;
        characters += _records[left].Html.Length;
        added = true;
      }
      if (right < end &&
          characters + _records[right + 1].Html.Length <= MaximumHtmlCharacters)
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
    string windowHtml = string.Concat(records.Select((record, offset) =>
      "<section class=\"virtual-record\" data-virtual-index=\"" +
      (left + offset).ToString(CultureInfo.InvariantCulture) + "\">" +
      record.Html + "</section>"));
    return new TranscriptWindow(
      windowHtml,
      left,
      right,
      SumHeights(0, left),
      SumHeights(right + 1, _records.Length),
      records);
  }

  private double SumHeights(int start, int end)
  {
    double result = 0;
    for (int index = start; index < end; ++index)
    {
      result += _heights[index];
    }
    return result;
  }

  private static double EstimateHeight(string html)
  {
    string text = WebUtility.HtmlDecode(TagRegex.Replace(html, " "));
    int explicitBlocks = Regex.Matches(
      html,
      "<(?:p|pre|li|h[1-6]|details|blockquote|tr)\\b",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
    int wrappedLines = Math.Max(1, (text.Length + 89) / 90);
    return Math.Max(
      MinimumEstimatedHeight,
      24.0 + (wrappedLines * 22.0) + (explicitBlocks * 10.0));
  }

  private static string MakeKey(int recordNumber, string sourceId)
  {
    return sourceId + "\0" + recordNumber.ToString(CultureInfo.InvariantCulture);
  }
}

internal sealed record TranscriptVirtualRecord(
  int RecordNumber,
  string SourceId,
  string Html,
  double EstimatedHeight);

internal sealed record TranscriptWindow(
  string Html,
  int StartIndex,
  int EndIndex,
  double TopSpacerHeight,
  double BottomSpacerHeight,
  IReadOnlyList<TranscriptVirtualRecord> Records);
