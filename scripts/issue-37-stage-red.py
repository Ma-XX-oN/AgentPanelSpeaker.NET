from pathlib import Path

view_path = Path('AgentPanelSpeaker/TranscriptView.cs')
program_path = Path('AgentPanelSpeaker/Program.cs')
runner_path = Path('AgentPanelSpeaker/Issue37LargeTranscriptRegressionTestRunner.cs')

view = view_path.read_text(encoding='utf-8')
old = '      TranscriptWindow window = payload.Document.CreateFullWindow();'
new = '      TranscriptWindow window = SelectInitialPresentationWindow(\n        payload.Document,\n        focalIndex);'
if view.count(old) != 1:
  raise SystemExit(f'expected one initial CreateFullWindow call, found {view.count(old)}')
view = view.replace(old, new, 1)

anchor = '''  private int ResolveInitialWindowIndex(\n    TranscriptVirtualDocument document,\n    IReadOnlyList<TranscriptNodeIdentity> identities)\n  {\n    return _pendingPosition is TranscriptPlaybackPosition position &&\n      TryResolvePositionIndex(document, identities, position, out int index)\n        ? index\n        : Math.Max(0, document.Count - 1);\n  }\n'''
helper = anchor + '''\n  /// <summary>\n  /// Selects the transcript materialization used for the first visual\n  /// presentation. Kept as a narrow production seam so the large-transcript\n  /// regression exercises the exact initial-window policy.\n  /// </summary>\n  internal static TranscriptWindow SelectInitialPresentationWindow(\n    TranscriptVirtualDocument document,\n    int focalIndex)\n  {\n    return document.CreateFullWindow();\n  }\n'''
if view.count(anchor) != 1:
  raise SystemExit(f'expected one ResolveInitialWindowIndex anchor, found {view.count(anchor)}')
view = view.replace(anchor, helper, 1)
view_path.write_text(view, encoding='utf-8')

program = program_path.read_text(encoding='utf-8')
program_anchor = '''      if (args.Length == 2 &&\n          string.Equals(args[1], "rolled-back-speech", StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = Issue35SpeechTransitionRegressionTestRunner.Run();\n        return;\n      }\n'''
program_insert = program_anchor + '''\n      if (args.Length == 2 &&\n          string.Equals(args[1], "large-transcript-windowing", StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = Issue37LargeTranscriptRegressionTestRunner.Run();\n        return;\n      }\n'''
if program.count(program_anchor) != 1:
  raise SystemExit(f'expected one Program test-dispatch anchor, found {program.count(program_anchor)}')
program = program.replace(program_anchor, program_insert, 1)
program_path.write_text(program, encoding='utf-8')

runner_path.write_text(r'''using System.Text;

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
''', encoding='utf-8')
