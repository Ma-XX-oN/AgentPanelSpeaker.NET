from pathlib import Path
from textwrap import dedent

CORE_COMMIT = "6c92799c1b14693001e8b913465f4a12b0b1e1ab"


def replace_once(path, old, new):
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise SystemExit(
      f"{path}: expected one match, found {count}: {old[:80]!r}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


client = Path("AgentPanelSpeaker/AIConversationCoreClient.cs")
replace_once(
  client,
  '    "7eb7f4fca630aa0a132e93799e878120aaf353b9";',
  f'    "{CORE_COMMIT}";')
replace_once(
  client,
  '  [property: JsonPropertyName("units")] CanonicalUnitProjection[] Units,\n'
  '  [property: JsonPropertyName("presentation")]\n',
  '  [property: JsonPropertyName("units")] CanonicalUnitProjection[] Units,\n'
  '  [property: JsonPropertyName("html_units")]\n'
  '    CanonicalHtmlUnitProjection[] HtmlUnits,\n'
  '  [property: JsonPropertyName("presentation")]\n')

worker = Path("tools/AIConversationCore-worker.mjs")
text = worker.read_text(encoding="utf-8")
old_pin = "const CORE_COMMIT = '7eb7f4fca630aa0a132e93799e878120aaf353b9';"
if text.count(old_pin) != 1:
  raise SystemExit("worker Core pin did not match exactly")
text = text.replace(
  old_pin,
  f"const CORE_COMMIT = '{CORE_COMMIT}';",
  1)

marker = "/**\n * Executes one bridge request.\n"
if text.count(marker) != 1:
  raise SystemExit("worker execute marker did not match exactly")
helper = dedent(r'''
/**
 * Adds Core-rendered HTML virtualization units to one structured projection.
 *
 * The worker forwards completed Core HTML and metadata; it never interprets or
 * recreates presentation semantics.
 *
 * @param {Object<string, *>} projection - Structured Core projection.
 * @param {Object<string, boolean>} options - Effective projection options.
 * @returns {Object<string, *>} Projection including ordered Core HTML units.
 */
function withHtmlUnits(projection, options) {
  return {
    ...projection,
    html_units: core.renderCanonicalHtmlUnits(projection?.events ?? [], options)
  };
}

''')
text = text.replace(marker, helper + marker, 1)

session_site = "projection: session.project(options),"
if text.count(session_site) != 1:
  raise SystemExit(
    "worker session_create projection site did not match exactly")
text = text.replace(
  session_site,
  "projection: withHtmlUnits(session.project(options), options),",
  1)

entry_site = "projection: entry.session.project(options),"
if text.count(entry_site) != 2:
  raise SystemExit(
    "worker session_project/session_append sites did not match exactly")
text = text.replace(
  entry_site,
  "projection: withHtmlUnits(entry.session.project(options), options),")

final_block = dedent(r'''
  return {
    ok: true,
    core_commit: CORE_COMMIT,
    projection
  };
''')
if text.count(final_block) != 1:
  raise SystemExit(
    "worker compatibility projection return did not match exactly")
text = text.replace(
  final_block,
  dedent(r'''
  return {
    ok: true,
    core_commit: CORE_COMMIT,
    projection: withHtmlUnits(projection, options)
  };
'''),
  1)
worker.write_text(text, encoding="utf-8")

dom = Path("AgentPanelSpeaker/TranscriptPresentationDomFormatter.cs")
dom.write_text(dedent(r'''
using Markdig;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Obtains completed canonical HTML units directly from AIConversationCore for
/// transcript display and virtualization.
/// </summary>
internal static class TranscriptPresentationDomFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptPresentationDomFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Projects one selected provider JSONL session through AIConversationCore and
  /// returns Core-rendered HTML units without rebuilding semantic HTML in C#.
  /// </summary>
  public static TranscriptPresentationDomResult Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(pipeline);

    IReadOnlyList<string> jsonLines = ReadJsonLines(path, cancellationToken);
    if (jsonLines.Count == 0)
    {
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        string.Empty,
        Array.Empty<CanonicalHtmlUnitProjection>());
    }

    AIConversationProjection projection = CoreClient.Project(
      source,
      jsonLines,
      new AIConversationCoreProjectOptions(IncludeRolledBackTurns: true));
    cancellationToken.ThrowIfCancellationRequested();

    CanonicalHtmlUnitProjection[] units = projection.HtmlUnits ??
      throw new InvalidOperationException(
        "AIConversationCore projection omitted canonical HTML units.");
    foreach (CanonicalHtmlUnitProjection unit in units)
    {
      if (!unit.Atomic ||
          !string.Equals(unit.Kind, "turn", StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "AIConversationCore returned an unsupported HTML virtualization unit.");
      }
    }

    string html = string.Concat(units.Select(unit => unit.Html));
    return new TranscriptPresentationDomResult(
      Array.Empty<TranscriptDomNode>(),
      html,
      units);
  }

  private static IReadOnlyList<string> ReadJsonLines(
    string path,
    CancellationToken cancellationToken)
  {
    var jsonLines = new List<string>();
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is string line)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }
      using JsonDocument document = JsonDocument.Parse(line);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new JsonException("A transcript JSONL record must be an object.");
      }
      jsonLines.Add(line);
    }
    return jsonLines;
  }
}

/// <summary>
/// Completed Core HTML plus the legacy DOM-node slot retained only for API
/// compatibility while callers migrate to Core-owned units.
/// </summary>
internal sealed record TranscriptPresentationDomResult(
  IReadOnlyList<TranscriptDomNode> Nodes,
  string Html,
  IReadOnlyList<CanonicalHtmlUnitProjection> Units);

/// <summary>
/// Legacy browser-DOM instruction shape retained for compatibility. Production
/// transcript rendering no longer constructs semantic nodes through this type.
/// </summary>
internal sealed record TranscriptDomNode(
  string Kind,
  string? Tag,
  IReadOnlyDictionary<string, string>? Attributes,
  string? Text,
  string? Html,
  IReadOnlyList<TranscriptDomNode>? Children);
''').lstrip(), encoding="utf-8")

html_formatter = Path(
  "AgentPanelSpeaker/TranscriptPresentationHtmlFormatter.cs")
html_formatter.write_text(dedent(r'''
using Markdig;

namespace AgentPanelSpeaker;

/// <summary>
/// Compatibility wrapper over AIConversationCore's completed canonical HTML.
/// </summary>
internal static class TranscriptPresentationHtmlFormatter
{
  /// <summary>
  /// Returns the same Core-rendered HTML used by the production transcript path.
  /// No structural or Markdown rendering is performed in AgentPanelSpeaker.
  /// </summary>
  public static string Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default,
    string? structureProbeId = null)
  {
    TranscriptPresentationDomResult result =
      TranscriptPresentationDomFormatter.Format(
        path,
        source,
        pipeline,
        cancellationToken);
    if (!string.IsNullOrWhiteSpace(structureProbeId))
    {
      _ = TranscriptStructureProbe.CaptureHtml(
        structureProbeId,
        "core-canonical-html",
        result.Html);
    }
    return result.Html;
  }
}
''').lstrip(), encoding="utf-8")

virtual = Path("AgentPanelSpeaker/TranscriptVirtualDocument.cs")
text = virtual.read_text(encoding="utf-8")
marker = "  public static TranscriptVirtualDocument Build(string html)\n"
if text.count(marker) != 1:
  raise SystemExit(
    "TranscriptVirtualDocument Build(string) marker did not match")
overload = dedent(r'''
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

''')
text = text.replace(marker, overload + marker, 1)
virtual.write_text(text, encoding="utf-8")

view = Path("AgentPanelSpeaker/TranscriptView.cs")
replace_once(
  view,
  "        TranscriptVirtualDocument document = "
  "TranscriptVirtualDocument.Build(html);",
  "        TranscriptVirtualDocument document = "
  "TranscriptVirtualDocument.Build(\n"
  "          presentation.Units);")
