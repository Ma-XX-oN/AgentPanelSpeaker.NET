from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')
old = '''  const turn = details.closest('section.transcript-turn');
  const turnId = turn?.getAttribute('data-presentation-id') || '';
  const anchor = details.querySelector('.record-anchor') ||
    turn?.querySelector('.record-anchor');
  const recordNumber = anchor?.getAttribute('data-jsonl-record') || '';
  const sourceId = anchor?.getAttribute('data-source-id') || '';
  return 'fallback:' + turnId + ':' + recordNumber + ':' + sourceId + ':' +
    summaryText;
'''
new = '''  const turn = details.closest('section.transcript-turn');
  const turnId = turn?.getAttribute('data-presentation-id') || '';
  if (turn && turnId) {
    const ordinal = Array.from(turn.querySelectorAll('details')).indexOf(details);
    if (ordinal >= 0) {
      return 'turn-details:' + turnId + ':' + ordinal;
    }
  }
  const anchor = details.querySelector('.record-anchor') ||
    turn?.querySelector('.record-anchor');
  const recordNumber = anchor?.getAttribute('data-jsonl-record') || '';
  return 'fallback:' + recordNumber + ':' + summaryText;
'''
if text.count(old) != 1:
    raise SystemExit(f'Expected exactly one disclosure fallback block, found {text.count(old)}')
path.write_text(text.replace(old, new), encoding='utf-8')
