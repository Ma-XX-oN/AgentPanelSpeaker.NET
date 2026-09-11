from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

method_marker = '''    return script;
  }

  private async Task RenderWindowForRecordAsync(
'''
method_replacement = '''    return script;
  }

  private async Task<bool> ExecuteFindWindowReplacementAsync(
    string replacementScript,
    int matchIndex,
    long navigationGeneration)
  {
    try
    {
      CoreWebView2? core = _webView.CoreWebView2;
      if (!_initialized || _webView.IsDisposed || core is null)
      {
        return false;
      }

      string generation = navigationGeneration.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
      string match = matchIndex.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
      string script =
        "(() => {" +
        $"if (findNavigationGeneration !== {generation}) return false;" +
        replacementScript +
        $"void showFindMatch({match},'window-installed',{generation});" +
        "return true;})()";
      string result = await core.ExecuteScriptAsync(script);
      return JsonSerializer.Deserialize<bool>(result);
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or
        ObjectDisposedException or
        JsonException)
    {
      DiagnosticLog.Write("transcript.script_failed", new
      {
        exception = exception.ToString()
      });
      return false;
    }
  }

  private async Task RenderWindowForRecordAsync(
'''
if method_marker not in text:
  raise SystemExit('RenderWindowForRecordAsync insertion point not found')
text = text.replace(method_marker, method_replacement, 1)

old_render = '''      TranscriptWindow window = document.CreateWindow(focalIndex, GetVirtualViewportHeight());
      var timer = Stopwatch.StartNew();
      if (!await ExecuteAsync(BuildReplaceWindowScript(
            window,
            preserve: false,
            focusVirtualIndex: string.Equals(
              reason,
              "search",
              StringComparison.OrdinalIgnoreCase)
                ? null
                : focalIndex)))
      {
        return;
      }
'''
new_render = '''      TranscriptWindow window = document.CreateWindow(focalIndex, GetVirtualViewportHeight());
      var timer = Stopwatch.StartNew();
      bool search = string.Equals(
        reason,
        "search",
        StringComparison.OrdinalIgnoreCase);
      string replacementScript = BuildReplaceWindowScript(
        window,
        preserve: false,
        focusVirtualIndex: search ? null : focalIndex);
      bool replacementApplied;
      if (search &&
          matchIndex is int searchMatchIndex &&
          navigationGeneration is long searchNavigationGeneration)
      {
        replacementApplied = await ExecuteFindWindowReplacementAsync(
          replacementScript,
          searchMatchIndex,
          searchNavigationGeneration);
      }
      else
      {
        replacementApplied = await ExecuteAsync(replacementScript);
      }
      if (!replacementApplied)
      {
        return;
      }
'''
if old_render not in text:
  raise SystemExit('search replacement block not found')
text = text.replace(old_render, new_render, 1)

old_scroll = "  target.scrollIntoView({block:'center', behavior:'smooth'});\n"
new_scroll = '''  target.scrollIntoView({
    block:'center',
    behavior:trigger === 'window-installed' ? 'auto' : 'smooth'
  });
'''
if old_scroll not in text:
  raise SystemExit('Find scrollIntoView call not found')
text = text.replace(old_scroll, new_scroll, 1)

path.write_text(text, encoding='utf-8')
