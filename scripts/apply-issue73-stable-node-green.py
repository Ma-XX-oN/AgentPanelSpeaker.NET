from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')
old = '''  const knownNode = knownNodeIds.has(nodeKey);\n  if (knownNode) {\n    postFragmentRangeMiss(\n      text, nodeId, nodeKey, displayKey, lexicalKey, mapped, true);\n    return null;\n  }\n\n  const globalStart = findSequence(\n'''
new = '''  const knownNode = knownNodeIds.has(nodeKey);\n  const stableNodeId = Number(nodeId);\n  if (Number.isFinite(stableNodeId) && stableNodeId > 0) {\n    // A real playback NodeId is authoritative even while virtualization has\n    // evicted that node. Never let duplicate text in another materialized node\n    // impersonate the retained playback position.\n    postFragmentRangeMiss(\n      text, nodeId, nodeKey, displayKey, lexicalKey, mapped, knownNode);\n    return null;\n  }\n\n  const globalStart = findSequence(\n'''
count = text.count(old)
if count != 1:
  raise SystemExit(f'expected one stable-node fallback block, found {count}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
