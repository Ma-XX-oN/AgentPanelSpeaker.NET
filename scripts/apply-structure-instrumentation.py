from pathlib import Path

# Instrument TranscriptView at the exact renderer -> virtual window -> WebView2 DOM
# boundaries.  Every stage uses the same probe id so one log identifies the first
# changed source-to-details relationship.
path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

old = '''    var renderTimer = Stopwatch.StartNew();

    try
'''
new = '''    var renderTimer = Stopwatch.StartNew();
    string structureProbeId = $"{generation}:{Guid.NewGuid():N}";

    try
'''
if old not in text:
  raise SystemExit('render timer insertion point not found')
text = text.replace(old, new, 1)

old = '''        string html = TranscriptHtmlRenderer.ToHtml(markdown, _pipeline);
        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
          html,
          identities,
          token);
        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
        return new TranscriptRenderPayload(document, identities, searchIndex);'''
new = '''        string html = TranscriptHtmlRenderer.ToHtml(markdown, _pipeline);
        TranscriptStructureSnapshot rendererStructure =
          TranscriptStructureProbe.CaptureHtml(
            structureProbeId,
            "renderer-html",
            html);
        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
          html,
          identities,
          token);
        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
        return new TranscriptRenderPayload(
          document,
          identities,
          searchIndex,
          rendererStructure);'''
if old not in text:
  raise SystemExit('renderer snapshot insertion point not found')
text = text.replace(old, new, 1)

old = '''      TranscriptWindow window = payload.Document.CreateWindow(focalIndex);
      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: focalIndex);'''
new = '''      TranscriptWindow window = payload.Document.CreateWindow(focalIndex);
      TranscriptStructureSnapshot virtualStructure =
        TranscriptStructureProbe.CaptureHtml(
          structureProbeId,
          "virtual-window-html",
          window.Html);
      TranscriptStructureProbe.Compare(
        payload.RendererStructure,
        virtualStructure);
      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: focalIndex);'''
if old not in text:
  raise SystemExit('initial virtual snapshot insertion point not found')
text = text.replace(old, new, 1)

old = '''      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;

      TranscriptPlaybackPosition? renderAnchor = null;'''
new = '''      _windowStartIndex = window.StartIndex;
      _windowEndIndex = window.EndIndex;
      TranscriptStructureSnapshot? webViewStructure =
        await CaptureWebViewStructureAsync(structureProbeId);
      if (webViewStructure is not null)
      {
        TranscriptStructureProbe.Compare(virtualStructure, webViewStructure);
      }

      TranscriptPlaybackPosition? renderAnchor = null;'''
if old not in text:
  raise SystemExit('initial WebView snapshot insertion point not found')
text = text.replace(old, new, 1)

old = '''        window = payload.Document.CreateWindow(latestIndex);
        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex)))
        {
          ShowLoading("Unable to position transcript at the voice marker. " +
            "See diagnostic log.");
          return;
        }
        _windowStartIndex = window.StartIndex;
        _windowEndIndex = window.EndIndex;
        focalIndex = latestIndex;'''
new = '''        window = payload.Document.CreateWindow(latestIndex);
        virtualStructure = TranscriptStructureProbe.CaptureHtml(
          structureProbeId,
          "virtual-window-html-positioned",
          window.Html);
        TranscriptStructureProbe.Compare(
          payload.RendererStructure,
          virtualStructure);
        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex)))
        {
          ShowLoading("Unable to position transcript at the voice marker. " +
            "See diagnostic log.");
          return;
        }
        _windowStartIndex = window.StartIndex;
        _windowEndIndex = window.EndIndex;
        webViewStructure = await CaptureWebViewStructureAsync(structureProbeId);
        if (webViewStructure is not null)
        {
          TranscriptStructureProbe.Compare(virtualStructure, webViewStructure);
        }
        focalIndex = latestIndex;'''
if old not in text:
  raise SystemExit('positioned virtual snapshot insertion point not found')
text = text.replace(old, new, 1)

marker = '''  private async Task<bool> ExecuteAsync(string script)
'''
method = '''  private async Task<TranscriptStructureSnapshot?> CaptureWebViewStructureAsync(
    string probeId)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (!_initialized || _webView.IsDisposed || core is null)
      {
        return null;
      }
      string result = await core.ExecuteScriptAsync(
        TranscriptStructureProbe.BuildWebViewProbeScript());
      return TranscriptStructureProbe.CaptureWebViewResult(probeId, result);
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or ObjectDisposedException or JsonException)
    {
      DiagnosticLog.Write("transcript.structure_probe_failed", new
      {
        probeId,
        stage = "webview-dom",
        exception = exception.ToString()
      });
      return null;
    }
  }

'''
if marker not in text:
  raise SystemExit('ExecuteAsync insertion point not found')
text = text.replace(marker, method + marker, 1)

old = '''  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    TranscriptSearchIndex SearchIndex);'''
new = '''  private sealed record TranscriptRenderPayload(
    TranscriptVirtualDocument Document,
    IReadOnlyList<TranscriptNodeIdentity> Identities,
    TranscriptSearchIndex SearchIndex,
    TranscriptStructureSnapshot RendererStructure);'''
if old not in text:
  raise SystemExit('payload record insertion point not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')

# When the direct presentation formatter is active, add the canonical tree as a
# fourth boundary using the same probe id.
path = Path('AgentPanelSpeaker/TranscriptPresentationHtmlFormatter.cs')
text = path.read_text(encoding='utf-8')
old = '''    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default)
'''
new = '''    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default,
    string? structureProbeId = null)
'''
if old not in text:
  raise SystemExit('direct formatter signature insertion point not found')
text = text.replace(old, new, 1)
old = '''    if (tree.ValueKind != JsonValueKind.Object ||
        GetString(tree, "kind") != "conversation")
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted the canonical presentation tree.");
    }

    var output = new StringBuilder();'''
new = '''    if (tree.ValueKind != JsonValueKind.Object ||
        GetString(tree, "kind") != "conversation")
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted the canonical presentation tree.");
    }
    if (!string.IsNullOrWhiteSpace(structureProbeId))
    {
      TranscriptStructureProbe.CapturePresentationTree(structureProbeId, tree);
    }

    var output = new StringBuilder();'''
if old not in text:
  raise SystemExit('presentation tree probe insertion point not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')

# Keep the pending direct-migration script compatible so that when that migration
# is retried it retains this instrumentation rather than silently dropping it.
path = Path('scripts/apply-direct-presentation-migration.py')
if path.exists():
  text = path.read_text(encoding='utf-8')
  text = text.replace(
    '''          () => html = TranscriptPresentationHtmlFormatter.Format(
            path,
            source,
            _pipeline,
            token));''',
    '''          () => html = TranscriptPresentationHtmlFormatter.Format(
            path,
            source,
            _pipeline,
            token,
            structureProbeId));''')
  path.write_text(text, encoding='utf-8')
