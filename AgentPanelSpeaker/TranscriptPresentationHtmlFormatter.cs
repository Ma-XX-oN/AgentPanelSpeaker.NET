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
