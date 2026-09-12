from pathlib import Path


def replace_count(path: str, old: str, new: str, expected: int) -> None:
  target = Path(path)
  text = target.read_text(encoding='utf-8')
  count = text.count(old)
  if count != expected:
    raise RuntimeError(f'{path}: expected {expected} occurrences, found {count}')
  target.write_text(text.replace(old, new), encoding='utf-8', newline='\n')


def first_id(expression: str, description: str) -> str:
  return (
    f'{expression}.WordIds?.FirstOrDefault() ?? throw new InvalidOperationException('
    f'"{description} has no Core word identity.")'
  )


issue26 = 'AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs'
replace_count(
  issue26,
  'speech.TrySeekToTranscriptWord(context.NodeId, 0, out _)',
  f'speech.TrySeekToTranscriptWord({first_id("context", "User Context fragment")}, out _)',
  3)
replace_count(
  issue26,
  'speech.TrySeekToTranscriptWord(user.NodeId, 0, out _)',
  f'speech.TrySeekToTranscriptWord({first_id("user", "User prompt fragment")}, out _)',
  1)

issue46 = 'AgentPanelSpeaker/Issue46IndependentRegressionOracleTestRunner.cs'
replace_count(
  issue46,
  'speech.TrySeekToTranscriptWord(historical.NodeId, 0, out _)',
  f'speech.TrySeekToTranscriptWord({first_id("historical", "Historical fragment")}, out _)',
  3)
replace_count(
  issue46,
  'speech.TrySeekToTranscriptWord(edited.NodeId, 0, out _)',
  f'speech.TrySeekToTranscriptWord({first_id("edited", "Edited fragment")}, out _)',
  1)
replace_count(
  issue46,
  'speech.TrySeekToTranscriptWord(\n      context.NodeId,\n      0,\n      out _)',
  f'speech.TrySeekToTranscriptWord(\n      {first_id("context", "User Context fragment")},\n      out _)',
  1)
replace_count(
  issue46,
  'speech.TrySeekToTranscriptWord(user.NodeId, 0, out _)',
  f'speech.TrySeekToTranscriptWord({first_id("user", "User prompt fragment")}, out _)',
  1)
