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
  private int _heightGeneration = -1;
  private bool _showRolledBackHistory;

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

  /// <summary>
  /// Returns the complete rendered record inventory.
  /// </summary>
  public IReadOnlyList<TranscriptVirtualRecord> Records => _records;

  /// <summary>
  /// Returns the complete transcript as one unsplit window. This is used by the
  /// canonical DOM renderer so structural elements are created as DOM objects
  /// and are never divided across independently parsed HTML fragments.
  /// </summary>
  public TranscriptWindow CreateFullWindow()
  {
    string html = string.Concat(_records.Select(record => record.Html));
    return new TranscriptWindow(
      html,
      0,
      _records.Length - 1,
      0,
      0,
      _records);
  }


/// <summary>
/// Builds virtualization records directly from Core-owned complete HTML units.
/// Unit boundaries and source identities come from Core metadata; this path
/// does not discover semantic boundaries from completed HTML.
/// </summary>
public static TranscriptVirtualDocument Build(
  IReadOnlyList<CanonicalHtmlUnitProjection> units)
{
  ArgumentNullException.ThrowIfNull(units);
  var records = new List<TranscriptVirtualRecord>(units.Count);
  foreach (CanonicalHtmlUnitProjection unit in units)
  {
    if (!unit.Atomic ||
        !string.Equals(unit.Kind, "turn", StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        "Unsupported AIConversationCore HTML virtualization unit.");
    }

    var identities = new List<TranscriptVirtualIdentity>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (CanonicalHtmlSourceProjection source in unit.Source ??
        Array.Empty<CanonicalHtmlSourceProjection>())
    {
      if (source.RecordIndex is not int sourceIndex || sourceIndex < 0)
      {
        continue;
      }
      int recordNumber = sourceIndex + 1;
      string sourceId = string.IsNullOrWhiteSpace(source.RecordId)
        ? recordNumber.ToString(CultureInfo.InvariantCulture)
        : source.RecordId;
      if (seen.Add(MakeKey(recordNumber, sourceId)))
      {
        identities.Add(new TranscriptVirtualIdentity(
          recordNumber,
          sourceId));
      }
    }

    TranscriptVirtualIdentity primary = identities.Count == 0
      ? new TranscriptVirtualIdentity(0, string.Empty)
      : identities[0];
    string html = unit.Html ?? string.Empty;
    records.Add(new TranscriptVirtualRecord(
      primary.RecordNumber,
      primary.SourceId,
      html,
      EstimateHeight(html),
      identities,
      html.Contains(
        "data-revision-historical=\"true\"",
        StringComparison.OrdinalIgnoreCase)));
  }

  DiagnosticLog.Write("transcript.virtual-core-units", new
  {
    unitCount = units.Count,
    sourceIdentityCount = records.Sum(
      record => record.Identities.Count)
  });
  return new TranscriptVirtualDocument(records.ToArray());
}

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
          Array.Empty<TranscriptVirtualIdentity>(),
          IsHistoricalRevisionHtml(html))
      });
    }

    // Prefer AIConversationCore's explicit atomic-unit declarations.  As a
    // compatibility guard, also keep any complete <details> that owns multiple
    // canonical record anchors atomic.  The latter is not a provider-semantic
    // inference: the record anchors are injected from canonical provenance and
    // prove that splitting this HTML container would put one DOM element across
    // multiple virtual <section> siblings, which WebView2 repairs by ending the
    // disclosure early.
    IReadOnlyList<HtmlRange> declaredRanges =
      FindDeclaredStructuralRanges(html);
    IReadOnlyList<HtmlRange> fallbackRanges =
      FindMultiRecordDetailsRanges(html, anchors, declaredRanges);
    HtmlRange[] structuralRanges = declaredRanges
      .Concat(fallbackRanges)
      .OrderBy(range => range.Start)
      .ToArray();

    DiagnosticLog.Write("transcript.virtual-structure", new
    {
      anchorCount = anchors.Count,
      declaredDetailsCount = declaredRanges.Count,
      fallbackDetailsCount = fallbackRanges.Count,
      structuralDetailsCount = structuralRanges.Length
    });

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
        identities,
        IsHistoricalRevisionHtml(recordHtml)));
    }

    return new TranscriptVirtualDocument(records.ToArray());
  }

  public bool TryGetIndex(int recordNumber, string sourceId, out int index)
  {
    return _recordIndexes.TryGetValue(MakeKey(recordNumber, sourceId), out index);
  }

  /// <summary>
  /// Changes effective historical visibility without changing canonical record
  /// indexes or measured natural heights.
  /// </summary>
  public void SetShowRolledBackHistory(bool show)
  {
    _showRolledBackHistory = show;
  }

  /// <summary>
  /// Returns whether one canonical source identity is currently visible.
  /// </summary>
  public bool IsVisible(int recordNumber, string sourceId)
  {
    return TryGetIndex(recordNumber, sourceId, out int index) &&
      IsVisible(index);
  }

  /// <summary>
  /// Invalidates natural-height measurements when width/DPI/font layout changes.
  /// </summary>
  public void SetLayoutGeneration(int layoutGeneration)
  {
    if (_heightGeneration == layoutGeneration)
    {
      return;
    }
    _heightGeneration = layoutGeneration;
    for (int index = 0; index < _heights.Length; ++index)
    {
      _heights[index] = _records[index].EstimatedHeight;
    }
  }

  /// <summary>
  /// Applies measured natural heights only to the layout generation that
  /// produced them.
  /// </summary>
  public void UpdateMeasuredHeights(
    IReadOnlyDictionary<int, double> measurements,
    int layoutGeneration)
  {
    SetLayoutGeneration(layoutGeneration);
    foreach ((int index, double height) in measurements)
    {
      if (index >= 0 && index < _heights.Length &&
          double.IsFinite(height) && height >= 0)
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

    focalIndex = ResolveVisibleFocalIndex(
      Math.Clamp(focalIndex, 0, _records.Length - 1));
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
      result += EffectiveHeight(index);
    }
    return result;
  }

  private bool IsVisible(int index)
  {
    return index >= 0 && index < _records.Length &&
      (_showRolledBackHistory || !_records[index].HistoricalRevision);
  }

  private double EffectiveHeight(int index)
  {
    return IsVisible(index) ? _heights[index] : 0.0;
  }

  private int ResolveVisibleFocalIndex(int focalIndex)
  {
    if (IsVisible(focalIndex))
    {
      return focalIndex;
    }
    for (int offset = 1; offset < _records.Length; ++offset)
    {
      int right = focalIndex + offset;
      if (right < _records.Length && IsVisible(right))
      {
        return right;
      }
      int left = focalIndex - offset;
      if (left >= 0 && IsVisible(left))
      {
        return left;
      }
    }
    return focalIndex;
  }

  private static bool IsHistoricalRevisionHtml(string html)
  {
    return html.Contains(
        "data-revision-historical=\"true\"",
        StringComparison.OrdinalIgnoreCase) ||
      html.Contains("revision-original", StringComparison.OrdinalIgnoreCase) ||
      html.Contains("revision-superseded", StringComparison.OrdinalIgnoreCase);
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
  /// Returns details ranges containing an explicit AIConversationCore
  /// atomic-unit declaration.
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
  /// Returns undeclared details ranges that contain multiple canonical record
  /// anchors.  Such a range cannot be split into independently wrapped virtual
  /// sections without changing the browser DOM structure.
  /// </summary>
  private static IReadOnlyList<HtmlRange> FindMultiRecordDetailsRanges(
    string html,
    MatchCollection anchors,
    IReadOnlyList<HtmlRange> declaredRanges)
  {
    var result = new List<HtmlRange>();
    foreach (HtmlRange range in FindOutermostDetailsRanges(html))
    {
      if (declaredRanges.Any(declared => declared.Equals(range)))
      {
        continue;
      }

      int anchorCount = 0;
      foreach (Match anchor in anchors)
      {
        if (!range.Contains(anchor.Index))
        {
          continue;
        }
        ++anchorCount;
        if (anchorCount >= 2)
        {
          result.Add(range);
          break;
        }
      }
    }
    return result;
  }

  /// <summary>
  /// Finds complete outermost details elements so a declared or proven
  /// multi-record atomic range can be mapped to the complete HTML container.
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
  IReadOnlyList<TranscriptVirtualIdentity> Identities,
  bool HistoricalRevision = false);

internal sealed record TranscriptWindow(
  string Html,
  int StartIndex,
  int EndIndex,
  double TopSpacerHeight,
  double BottomSpacerHeight,
  IReadOnlyList<TranscriptVirtualRecord> Records);
