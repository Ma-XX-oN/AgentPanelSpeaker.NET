using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Production-path regressions for issue #37 large-transcript initial
/// materialization.
/// </summary>
internal static class Issue37LargeTranscriptRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #37 focused regression suite.
  /// </summary>
  public static int Run()
  {
    try
    {
      InitialPresentationIsWindowScoped();
      Console.WriteLine("PASS  issue37/initial-presentation-window-scoped");
      return 0;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(
        "FAIL  issue37/initial-presentation-window-scoped: " +
        exception.Message);
      return 1;
    }
  }

  private static void InitialPresentationIsWindowScoped()
  {
    const int recordCount = 600;
    const int payloadCharacters = 4096;
    var html = new StringBuilder(recordCount * payloadCharacters);
    for (int record = 1; record <= recordCount; ++record)
    {
      html.Append("<span class=\"record-anchor\" data-jsonl-record=\"");
      html.Append(record);
      html.Append("\" data-source-id=\"record-");
      html.Append(record);
      html.Append("\"></span><p>");
      html.Append('x', payloadCharacters);
      html.Append("</p>");
    }

    TranscriptVirtualDocument document =
      TranscriptVirtualDocument.Build(html.ToString());
    if (document.Count < recordCount - 1)
    {
      throw new InvalidOperationException(
        $"Synthetic transcript produced only {document.Count} virtual records.");
    }

    int focalIndex = document.Count / 2;
    TranscriptWindow window = TranscriptView.SelectInitialPresentationWindow(
      document,
      focalIndex);

    if (window.StartIndex > focalIndex || window.EndIndex < focalIndex)
    {
      throw new InvalidOperationException(
        $"Initial window {window.StartIndex}..{window.EndIndex} does not contain focal index {focalIndex}.");
    }
    if (window.Records.Count >= document.Count)
    {
      throw new InvalidOperationException(
        $"Initial production presentation materialized {window.Records.Count} of {document.Count} virtual records; expected a bounded window rather than the complete transcript.");
    }
    if (window.Html.Length >= html.Length)
    {
      throw new InvalidOperationException(
        $"Initial production presentation emitted {window.Html.Length} HTML characters for a {html.Length}-character transcript; expected bounded materialization.");
    }
  }
}
