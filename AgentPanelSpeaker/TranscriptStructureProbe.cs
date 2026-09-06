using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Captures stable source-to-container relationships at each rendering boundary
/// so the first structural divergence can be identified from one diagnostic log.
/// </summary>
internal static class TranscriptStructureProbe
{
  private static readonly Regex RecordAnchorRegex = new(
    "<span\\s+class=\\\"record-anchor\\\"[^>]*" +
    "data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*" +
    "data-source-id=\\\"(?<source>[^\\\"]*)\\\"[^>]*></span>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex DetailsTagRegex = new(
    "</?details\\b[^>]*>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex SectionTagRegex = new(
    "</?section\\b[^>]*>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex SummaryRegex = new(
    "<summary\\b[^>]*>(?<text>.*?)</summary>",
    RegexOptions.Compiled |
    RegexOptions.CultureInvariant |
    RegexOptions.IgnoreCase |
    RegexOptions.Singleline);

  private static readonly Regex StructuralUnitRegex = new(
    "data-aicore-unit-id=\\\"(?<id>[^\\\"]+)\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex PresentationIdRegex = new(
    "data-presentation-id=\\\"(?<id>[^\\\"]+)\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex ClassRegex = new(
    "class=\\\"(?<value>[^\\\"]*)\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

  private static readonly Regex TagRegex = new(
    "<[^>]+>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

  /// <summary>
  /// Captures the exact HTML containment relationships before or after a host
  /// transformation.
  /// </summary>
  public static TranscriptStructureSnapshot CaptureHtml(
    string probeId,
    string stage,
    string html)
  {
    ArgumentNullException.ThrowIfNull(probeId);
    ArgumentNullException.ThrowIfNull(stage);
    ArgumentNullException.ThrowIfNull(html);

    IReadOnlyList<HtmlContainerRange> details = FindDetailsRanges(html);
    IReadOnlyList<HtmlContainerRange> turns = FindTurnRanges(html);
    var entries = new List<TranscriptStructureEntry>();

    foreach (Match anchor in RecordAnchorRegex.Matches(html))
    {
      _ = int.TryParse(
        WebUtility.HtmlDecode(anchor.Groups["record"].Value),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int recordNumber);
      string sourceId = WebUtility.HtmlDecode(anchor.Groups["source"].Value);
      string[] detailsChain = details
        .Where(range => range.Contains(anchor.Index))
        .OrderBy(range => range.Start)
        .Select(range => range.Key)
        .ToArray();
      string turnId = turns
        .Where(range => range.Contains(anchor.Index))
        .OrderByDescending(range => range.Start)
        .Select(range => range.Key)
        .FirstOrDefault() ?? string.Empty;
      entries.Add(new TranscriptStructureEntry(
        recordNumber,
        sourceId,
        turnId,
        detailsChain));
    }

    var snapshot = new TranscriptStructureSnapshot(
      probeId,
      stage,
      entries.ToArray());
    LogSnapshot(snapshot, details.Count, turns.Count);
    return snapshot;
  }

  /// <summary>
  /// Captures source membership in the canonical presentation tree.
  /// </summary>
  public static TranscriptStructureSnapshot CapturePresentationTree(
    string probeId,
    JsonElement tree)
  {
    ArgumentNullException.ThrowIfNull(probeId);
    var entries = new Dictionary<string, TranscriptStructureEntry>(
      StringComparer.Ordinal);

    if (tree.ValueKind == JsonValueKind.Object &&
        tree.TryGetProperty("turns", out JsonElement turns) &&
        turns.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement turn in turns.EnumerateArray())
      {
        string turnId = ReadString(turn, "id");
        VisitPresentationNode(
          turn,
          turnId,
          Array.Empty<string>(),
          entries);
      }
    }

    var snapshot = new TranscriptStructureSnapshot(
      probeId,
      "presentation-tree",
      entries.Values
        .OrderBy(entry => entry.RecordNumber)
        .ThenBy(entry => entry.SourceId, StringComparer.Ordinal)
        .ToArray());
    LogSnapshot(snapshot, detailsCount: null, turnCount: null);
    return snapshot;
  }

  /// <summary>
  /// Parses a WebView2 DOM probe result into the same snapshot representation.
  /// </summary>
  public static TranscriptStructureSnapshot CaptureWebViewResult(
    string probeId,
    string json)
  {
    ArgumentNullException.ThrowIfNull(probeId);
    ArgumentNullException.ThrowIfNull(json);
    using JsonDocument document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;
    if (root.ValueKind == JsonValueKind.String)
    {
      string nested = root.GetString() ?? "{}";
      using JsonDocument nestedDocument = JsonDocument.Parse(nested);
      return CaptureWebViewRoot(probeId, nestedDocument.RootElement);
    }
    return CaptureWebViewRoot(probeId, root);
  }

  /// <summary>
  /// Compares two structural snapshots and logs every changed record relationship.
  /// </summary>
  public static void Compare(
    TranscriptStructureSnapshot before,
    TranscriptStructureSnapshot after)
  {
    ArgumentNullException.ThrowIfNull(before);
    ArgumentNullException.ThrowIfNull(after);
    if (!string.Equals(before.ProbeId, after.ProbeId, StringComparison.Ordinal))
    {
      throw new ArgumentException("Structure snapshots belong to different probes.");
    }

    Dictionary<string, TranscriptStructureEntry> beforeByKey = before.Entries
      .ToDictionary(EntryKey, StringComparer.Ordinal);
    Dictionary<string, TranscriptStructureEntry> afterByKey = after.Entries
      .ToDictionary(EntryKey, StringComparer.Ordinal);
    string[] commonKeys = beforeByKey.Keys
      .Intersect(afterByKey.Keys, StringComparer.Ordinal)
      .OrderBy(key => key, StringComparer.Ordinal)
      .ToArray();
    var differences = new List<object>();

    foreach (string key in commonKeys)
    {
      TranscriptStructureEntry left = beforeByKey[key];
      TranscriptStructureEntry right = afterByKey[key];
      bool detailsChanged = !left.DetailsChain.SequenceEqual(
        right.DetailsChain,
        StringComparer.Ordinal);
      bool turnChanged =
        left.TurnId.Length != 0 &&
        right.TurnId.Length != 0 &&
        !string.Equals(left.TurnId, right.TurnId, StringComparison.Ordinal);
      if (!detailsChanged && !turnChanged)
      {
        continue;
      }
      differences.Add(new
      {
        left.RecordNumber,
        left.SourceId,
        beforeTurn = left.TurnId,
        afterTurn = right.TurnId,
        beforeDetails = left.DetailsChain,
        afterDetails = right.DetailsChain
      });
    }

    DiagnosticLog.Write(
      differences.Count == 0
        ? "transcript.structure_equivalent"
        : "transcript.structure_divergence",
      new
      {
        probeId = before.ProbeId,
        fromStage = before.Stage,
        toStage = after.Stage,
        comparedRecordCount = commonKeys.Length,
        differenceCount = differences.Count,
        differences
      });
  }

  /// <summary>
  /// Builds JavaScript that returns the browser's actual record-to-container DOM
  /// relationships after WebView2 has parsed and repaired the supplied HTML.
  /// </summary>
  public static string BuildWebViewProbeScript()
  {
    return """
      (() => {
        const keyForDetails = (details) => {
          const presentation = details.getAttribute('data-presentation-id');
          if (presentation) return 'presentation:' + presentation;
          const marker = details.querySelector('[data-aicore-unit-id]');
          if (marker) return 'core-unit:' + marker.getAttribute('data-aicore-unit-id');
          const summary = Array.from(details.children).find(
            child => child.tagName === 'SUMMARY');
          const text = summary ? summary.textContent.trim().replace(/\s+/g, ' ') : '';
          return 'summary:' + text;
        };
        const allDetails = Array.from(document.querySelectorAll('details'));
        const detailsKeys = new Map(
          allDetails.map(details => [details, keyForDetails(details)]));
        const entries = Array.from(document.querySelectorAll('.record-anchor')).map(anchor => {
          const chain = [];
          let element = anchor.parentElement;
          while (element) {
            if (element.tagName === 'DETAILS') {
              chain.unshift(detailsKeys.get(element) ?? 'details:?');
            }
            element = element.parentElement;
          }
          const turn = anchor.closest('section.transcript-turn');
          return {
            recordNumber: Number(anchor.getAttribute('data-jsonl-record') || 0),
            sourceId: anchor.getAttribute('data-source-id') || '',
            turnId: turn ? ('presentation:' + (turn.getAttribute('data-presentation-id') || '')) : '',
            detailsChain: chain,
            connected: anchor.isConnected
          };
        });
        return {
          entries,
          detailsCount: allDetails.length,
          turnCount: document.querySelectorAll('section.transcript-turn').length
        };
      })()
      """;
  }

  private static TranscriptStructureSnapshot CaptureWebViewRoot(
    string probeId,
    JsonElement root)
  {
    var entries = new List<TranscriptStructureEntry>();
    if (root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("entries", out JsonElement array) &&
        array.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement item in array.EnumerateArray())
      {
        var chain = new List<string>();
        if (item.TryGetProperty("detailsChain", out JsonElement chainElement) &&
            chainElement.ValueKind == JsonValueKind.Array)
        {
          foreach (JsonElement value in chainElement.EnumerateArray())
          {
            if (value.ValueKind == JsonValueKind.String)
            {
              chain.Add(value.GetString() ?? string.Empty);
            }
          }
        }
        entries.Add(new TranscriptStructureEntry(
          ReadInt32(item, "recordNumber"),
          ReadString(item, "sourceId"),
          ReadString(item, "turnId"),
          chain.ToArray()));
      }
    }

    var snapshot = new TranscriptStructureSnapshot(
      probeId,
      "webview-dom",
      entries.ToArray());
    int? detailsCount = ReadNullableInt32(root, "detailsCount");
    int? turnCount = ReadNullableInt32(root, "turnCount");
    LogSnapshot(snapshot, detailsCount, turnCount);
    return snapshot;
  }

  private static void VisitPresentationNode(
    JsonElement node,
    string turnId,
    IReadOnlyList<string> reasoningChain,
    Dictionary<string, TranscriptStructureEntry> entries)
  {
    string kind = ReadString(node, "kind");
    string[] nextChain = reasoningChain.ToArray();
    if (kind == "reasoning_group")
    {
      nextChain = reasoningChain
        .Append("presentation:" + ReadString(node, "id"))
        .ToArray();
    }

    if (node.TryGetProperty("source", out JsonElement sources) &&
        sources.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement source in sources.EnumerateArray())
      {
        int recordIndex = ReadInt32(source, "record_index");
        if (recordIndex < 0)
        {
          continue;
        }
        int recordNumber = recordIndex + 1;
        string sourceId = ReadString(source, "record_id");
        if (sourceId.Length == 0)
        {
          sourceId = recordNumber.ToString(CultureInfo.InvariantCulture);
        }
        var entry = new TranscriptStructureEntry(
          recordNumber,
          sourceId,
          turnId.Length == 0 ? string.Empty : "presentation:" + turnId,
          nextChain);
        string key = EntryKey(entry);
        if (!entries.TryGetValue(key, out TranscriptStructureEntry? existing) ||
            entry.DetailsChain.Length >= existing.DetailsChain.Length)
        {
          entries[key] = entry;
        }
      }
    }

    if (node.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement child in children.EnumerateArray())
      {
        VisitPresentationNode(child, turnId, nextChain, entries);
      }
    }
  }

  private static IReadOnlyList<HtmlContainerRange> FindDetailsRanges(string html)
  {
    var stack = new Stack<(int Start, string OpeningTag, int Ordinal)>();
    var ranges = new List<HtmlContainerRange>();
    int ordinal = 0;
    foreach (Match tag in DetailsTagRegex.Matches(html))
    {
      bool closing = tag.Value.StartsWith("</", StringComparison.Ordinal);
      if (!closing)
      {
        stack.Push((tag.Index, tag.Value, ordinal++));
        continue;
      }
      if (stack.Count == 0)
      {
        continue;
      }
      (int start, string openingTag, int rangeOrdinal) = stack.Pop();
      int end = tag.Index + tag.Length;
      string fragment = html[start..end];
      string key = DetailsKey(openingTag, fragment, rangeOrdinal);
      ranges.Add(new HtmlContainerRange(start, end, key));
    }
    return ranges.OrderBy(range => range.Start).ToArray();
  }

  private static IReadOnlyList<HtmlContainerRange> FindTurnRanges(string html)
  {
    var stack = new Stack<(int Start, string OpeningTag)>();
    var ranges = new List<HtmlContainerRange>();
    foreach (Match tag in SectionTagRegex.Matches(html))
    {
      bool closing = tag.Value.StartsWith("</", StringComparison.Ordinal);
      if (!closing)
      {
        stack.Push((tag.Index, tag.Value));
        continue;
      }
      if (stack.Count == 0)
      {
        continue;
      }
      (int start, string openingTag) = stack.Pop();
      Match classMatch = ClassRegex.Match(openingTag);
      string classValue = classMatch.Success
        ? WebUtility.HtmlDecode(classMatch.Groups["value"].Value)
        : string.Empty;
      if (!classValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Contains("transcript-turn", StringComparer.Ordinal))
      {
        continue;
      }
      string id = AttributeValue(PresentationIdRegex, openingTag);
      ranges.Add(new HtmlContainerRange(
        start,
        tag.Index + tag.Length,
        id.Length == 0 ? "presentation:" : "presentation:" + id));
    }
    return ranges.OrderBy(range => range.Start).ToArray();
  }

  private static string DetailsKey(
    string openingTag,
    string fragment,
    int ordinal)
  {
    string presentationId = AttributeValue(PresentationIdRegex, openingTag);
    if (presentationId.Length != 0)
    {
      return "presentation:" + presentationId;
    }
    string unitId = AttributeValue(StructuralUnitRegex, fragment);
    if (unitId.Length != 0)
    {
      return "core-unit:" + unitId;
    }
    Match summary = SummaryRegex.Match(fragment);
    string summaryText = summary.Success
      ? NormalizeText(summary.Groups["text"].Value)
      : string.Empty;
    return "summary:" + summaryText;
  }

  private static string AttributeValue(Regex regex, string text)
  {
    Match match = regex.Match(text);
    return match.Success
      ? WebUtility.HtmlDecode(match.Groups["id"].Value)
      : string.Empty;
  }

  private static string NormalizeText(string html)
  {
    string text = WebUtility.HtmlDecode(TagRegex.Replace(html, " "));
    return Regex.Replace(text, "\\s+", " ").Trim();
  }

  private static void LogSnapshot(
    TranscriptStructureSnapshot snapshot,
    int? detailsCount,
    int? turnCount)
  {
    DiagnosticLog.Write("transcript.structure_probe", new
    {
      snapshot.ProbeId,
      snapshot.Stage,
      anchorCount = snapshot.Entries.Count,
      detailsCount,
      turnCount,
      entries = snapshot.Entries.Select(entry => new
      {
        entry.RecordNumber,
        entry.SourceId,
        entry.TurnId,
        entry.DetailsChain
      }).ToArray()
    });
  }

  private static string EntryKey(TranscriptStructureEntry entry)
  {
    return entry.SourceId + "\0" +
      entry.RecordNumber.ToString(CultureInfo.InvariantCulture);
  }

  private static string ReadString(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  private static int ReadInt32(JsonElement element, string propertyName)
  {
    return ReadNullableInt32(element, propertyName) ?? -1;
  }

  private static int? ReadNullableInt32(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.TryGetInt32(out int result)
        ? result
        : null;
  }

  private sealed record HtmlContainerRange(int Start, int End, string Key)
  {
    public bool Contains(int index) => index >= Start && index < End;
  }
}

internal sealed record TranscriptStructureSnapshot(
  string ProbeId,
  string Stage,
  IReadOnlyList<TranscriptStructureEntry> Entries);

internal sealed record TranscriptStructureEntry(
  int RecordNumber,
  string SourceId,
  string TurnId,
  string[] DetailsChain);
