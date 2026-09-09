namespace AgentPanelSpeaker;

/// <summary>
/// Selects the one production strategy used for initial transcript presentation.
/// Output acceptance tests consume the same plan but compare its browser result
/// to independently-authored expectations.
/// </summary>
internal sealed record TranscriptInitialPresentationPlan(
  TranscriptWindow Window,
  IReadOnlyList<TranscriptDomNode>? DomNodes)
{
  /// <summary>
  /// Creates the initial production presentation. The safe baseline uses the
  /// complete canonical DOM model; a future bounded strategy must first satisfy
  /// the independent browser-output acceptance suite before changing this plan.
  /// </summary>
  public static TranscriptInitialPresentationPlan Create(
    TranscriptVirtualDocument document,
    IReadOnlyList<TranscriptDomNode> domNodes,
    int focalIndex)
  {
    ArgumentNullException.ThrowIfNull(document);
    ArgumentNullException.ThrowIfNull(domNodes);
    _ = focalIndex;
    return new TranscriptInitialPresentationPlan(
      document.CreateFullWindow(),
      domNodes);
  }
}
