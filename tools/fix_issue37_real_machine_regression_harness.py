from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

text = text.replace(
  'File.WriteAllLines(path, records.Select(JsonSerializer.Serialize));',
  'File.WriteAllLines(path, records.Select(item => JsonSerializer.Serialize(item)));')

anchor = '  private static void TestBrowserWindowBehaviour()\n'
helpers = '''  private static Form CreateOffscreenHost()\n  {\n    return new Form\n    {\n      Width = 900,\n      Height = 700,\n      ShowInTaskbar = false,\n      StartPosition = FormStartPosition.Manual,\n      Location = new Point(-30000, -30000)\n    };\n  }\n\n  private static void WaitForViewInitialization(TranscriptView view)\n  {\n    WebView2 webView = ReadField<WebView2>(view, "_webView");\n    PumpUntil(\n      () => webView.CoreWebView2 is not null,\n      "WebView2 core initialization");\n  }\n\n  private static void WaitForTranscriptRender(TranscriptView view)\n  {\n    WebView2 webView = ReadField<WebView2>(view, "_webView");\n    PumpUntil(\n      () =>\n        ReadField<int>(view, "_windowStartIndex") >= 0 &&\n        ReadField<int>(view, "_windowEndIndex") >=\n          ReadField<int>(view, "_windowStartIndex") &&\n        webView.Visible,\n      "production transcript window to finish rendering");\n  }\n\n'''
if helpers not in text:
  if anchor not in text:
    raise SystemExit('browser-test anchor not found')
  text = text.replace(anchor, helpers + anchor, 1)

path.write_text(text, encoding='utf-8')
