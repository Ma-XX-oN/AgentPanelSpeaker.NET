from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

bad_follow = '''      target.scrollIntoView({
    block:'center',
    behavior:trigger === 'window-installed' ? 'auto' : 'smooth'
  });
'''
good_follow = '''      target.scrollIntoView({block:'center', behavior:'smooth'});
'''
if text.count(bad_follow) != 1:
  raise SystemExit(f'expected exactly one bad setFollowSpeech scroll, found {text.count(bad_follow)}')
text = text.replace(bad_follow, good_follow, 1)

old_find = '''  const openedDetailsCount = openAncestors(target);
  programmaticScrollUntil = performance.now() + 1500;
  target.scrollIntoView({block:'center', behavior:'smooth'});
  findCount.textContent = `${match.fileOrdinal} of ${findMatches.length}`;
'''
new_find = '''  const openedDetailsCount = openAncestors(target);
  programmaticScrollUntil = performance.now() + 1500;
  target.scrollIntoView({
    block:'center',
    behavior:trigger === 'window-installed' ? 'auto' : 'smooth'
  });
  findCount.textContent = `${match.fileOrdinal} of ${findMatches.length}`;
'''
if text.count(old_find) != 1:
  raise SystemExit(f'expected exactly one showFindMatch scroll, found {text.count(old_find)}')
text = text.replace(old_find, new_find, 1)
path.write_text(text, encoding='utf-8')
