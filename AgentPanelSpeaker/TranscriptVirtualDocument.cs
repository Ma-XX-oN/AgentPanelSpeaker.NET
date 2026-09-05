using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Stores the complete rendered transcript as independently renderable structural
/// units while retaining estimated heights for unloaded content.
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
  private static readonly Regex DetailsTagRegex = new(
    "</?details\\b[^>]*>",
    RegexOptions.Compiled |
    RegexOptions.CultureInvariant |
    RegexOptions.IgnoreCase);
  private static readonly Regex StructuralUnitMarkerRegex = new(
    "<span\\s+hidden\\s+class=\\\"aicore-structural-unit\\\"\\s+" +
    "data-aicore-unit-id=\\\"[^\\\"]+\\\"\\s+" +
    "data-aicore-source-record-ids=\\\"[^\\\"]*\\\"\\s*></span>",
    RegexOptions.Compiled |
    RegexOptions.CultureInvariant |
    RegexOptions.IgnoreCase);

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
      foreach (TranscriptVirtualIdentity identity in records[index].Identities)
      {
        _recordIndexes[MakeKey(identity.RecordNumber, identity.SourceId)] = index;
      }
    }
  }

  public int Count => _records.Length;

  public static TranscriptVirtualDocument Build(string html)
  {
    MatchCollection anchors = AnchorRegex.Matches(html);
    if (anchors.Count == 0)
    {
      return new TranscriptVirtualDocument(new[]
      {
        new TranscriptVirtualRecord(
          0,
          string.Empty,
          html,
          EstimateHeight(html),
          Array.Empty<TranscriptVirtualIdentity>())
      });
    }

    // AIConversationCore explicitly marks renderer-owned disclosures that must
    // remain atomic. Only details ranges containing one of those declarations
    // suppress ordinary record-anchor boundaries. Generic <details> markup is
    // no longer treated as a shared-core structural decision by this consumer.
    IReadOnlyList<HtmlRange> structuralRanges =
      FindDeclaredStructuralRanges(html);
    var starts = new SortedSet<int>();
    foreach (HtmlRange structuralRange in structuralRanges)
    {
      starts.Add(structuralRange.Start);
    }
    foreach (Match anchor in anchors)
    {
      if (!structuralRanges.Any(range => range.Contains(anchor.Index)))
      {
        starts.Add(anchor.Index);
      }
    }

    int[] unitStarts = starts.ToArray();
    var records = new List<TranscriptVirtualRecord>(unitStarts.Length);
    for (int index = 0; index < unitStarts.Length; ++index)
    {
      int start = index == 0 ? 0 : unitStarts[index];
      int end = index + 1 < unitStarts.Length
        ? unitStarts[index + 1]
        : html.Length;
      var identities = new List<TranscriptVirtualIdentity>();
      foreach (Match anchor in anchors)
      {
        if (anchor.Index < start || anchor.Index >= end)
        {
          continue;
        }
        identities.Add(ReadIdentity(anchor));
      }

      TranscriptVirtualIdentity primary = identities.Count == 0
        ? new TranscriptVirtualIdentity(0, string.Empty)
        : identities[0];
      string recordHtml = html[start..end];
      records.Add(new TranscriptVirtualRecord(
        primary.RecordNumber,
        primary.SourceId,
        recordHtml,
        EstimateHeight(recordHtml),
        identities));
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

  private static TranscriptVirtualIdentity ReadIdentity(Match anchor)
  {
    _ = int.TryParse(
      WebUtility.HtmlDecode(anchor.Groups["record"].Value),
      NumberStyles.Integer,
      CultureInfo.InvariantCulture,
      out int recordNumber);
    string sourceId = WebUtility.HtmlDecode(anchor.Groups["source"].Value);
    return new TranscriptVirtualIdentity(recordNumber, sourceId);
  }

  /// <summary>
  /// Returns only details ranges containing an explicit AIConversationCore
  /// atomic-unit declaration. The generic HTML scan is used solely to locate
  /// the enclosing range for a core marker; it no longer decides atomicity.
  /// </summary>
  private static IReadOnlyList<HtmlRange> FindDeclaredStructuralRanges(string html)
  {
    MatchCollection markers = StructuralUnitMarkerRegex.Matches(html);
    if (markers.Count == 0)
    {
      return Array.Empty<HtmlRange>();
    }

    IReadOnlyList<HtmlRange> detailsRanges = FindOutermostDetailsRanges(html);
    return detailsRanges
      .Where(range => markers.Cast<Match>().Any(marker => range.Contains(marker.Index)))
      .ToArray();
  }

  /// <summary>
  /// Finds complete outermost details elements so a declared atomic marker can
  /// be mapped to the complete HTML container that owns it.
  /// </summary>
  private static IReadOnlyList<HtmlRange> FindOutermostDetailsRanges(string html)
  {
    var ranges = new List<HtmlRange>();
    int depth = 0;
    int outerStart = -1;
    foreach (Match tag in DetailsTagRegex.Matches(html))
    {
      bool closing = tag.Value.StartsWith("</", StringComparison.Ordinal);
      if (!closing)
      {
        if (depth == 0)
        {
          outerStart = tag.Index;
        }
        ++depth;
        continue;
      }

      if (depth == 0)
      {
        continue;
      }
      --depth;
      if (depth == 0 && outerStart >= 0)
      {
        ranges.Add(new HtmlRange(outerStart, tag.Index + tag.Length));
        outerStart = -1;
      }
    }
    return ranges;
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

  private readonly record struct HtmlRange(int Start, int End)
  {
    public bool Contains(int index) => index >= Start && index < End;
  }
}

internal sealed record TranscriptVirtualIdentity(
  int RecordNumber,
  string SourceId);

internal sealed record TranscriptVirtualRecord(
  int RecordNumber,
  string SourceId,
  string Html,
  double EstimatedHeight,
  IReadOnlyList<TranscriptVirtualIdentity> Identities);

internal sealed record TranscriptWindow(
  string Html,
  int StartIndex,
  int EndIndex,
  double TopSpacerHeight,
  double BottomSpacerHeight,
  IReadOnlyList<TranscriptVirtualRecord> Records);
