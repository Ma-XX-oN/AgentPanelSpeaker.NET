from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

old_handler = '''      if (type == "find-cancel")
      {
        CancelFindSearch();
        return;
      }
'''
new_handler = '''      if (type == "find-navigation-invalidated")
      {
        long? navigationGeneration = ReadOptionalInt64(
          root,
          "navigationGeneration");
        if (navigationGeneration is long generation)
        {
          _latestFindWindowNavigationGeneration = Math.Max(
            _latestFindWindowNavigationGeneration,
            generation);
        }
        return;
      }
      if (type == "find-cancel")
      {
        CancelFindSearch();
        return;
      }
'''
if old_handler not in text:
  raise RuntimeError('find-cancel handler marker not found')
text = text.replace(old_handler, new_handler, 1)

old_js = '''function cancelFindSearch(updateStatus) {
  ++findGeneration;
  ++findNavigationGeneration;
  if (findSearchPending) {
    chrome.webview.postMessage({type:'find-cancel'});
'''
new_js = '''function cancelFindSearch(updateStatus) {
  ++findGeneration;
  ++findNavigationGeneration;
  chrome.webview.postMessage({
    type:'find-navigation-invalidated',
    navigationGeneration:findNavigationGeneration
  });
  if (findSearchPending) {
    chrome.webview.postMessage({type:'find-cancel'});
'''
if old_js not in text:
  raise RuntimeError('cancelFindSearch marker not found')
text = text.replace(old_js, new_js, 1)

path.write_text(text, encoding='utf-8', newline='\n')
