from pathlib import Path

identity_path = Path('AgentPanelSpeaker/TranscriptNodeIdentityMap.cs')
identity = identity_path.read_text(encoding='utf-8')
old = '''  private static bool IsRenderedKind(AgentSource source, string kind)\n  {\n    if (source == AgentSource.Claude)\n    {\n      return kind is "claude.user_text" or\n        "claude.queued_command.context" or "claude.queued_command" or\n        "claude.thinking" or "claude.text" or\n        "claude.subagent.result";\n    }\n\n    return kind == "codex.user_message" ||\n      kind.StartsWith("codex.agent_message", StringComparison.Ordinal);\n  }\n'''
new = '''  private static bool IsRenderedKind(AgentSource source, string kind)\n  {\n    if (source == AgentSource.Claude)\n    {\n      return kind.StartsWith("claude.canonical.", StringComparison.Ordinal) ||\n        kind is "claude.queued_command.context" or\n          "claude.queued_command" or\n          "claude.subagent.result";\n    }\n\n    return kind.StartsWith("codex.canonical.", StringComparison.Ordinal) ||\n      kind == "codex.plan";\n  }\n'''
if old not in identity:
  raise SystemExit('TranscriptNodeIdentityMap IsRenderedKind block not found')
identity_path.write_text(identity.replace(old, new, 1), encoding='utf-8')

program_path = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
program = program_path.read_text(encoding='utf-8')
needle = '''  IReadOnlyList<TranscriptNodeIdentity> identities =\n    TranscriptNodeIdentityMap.Build(path, source);\n  if (identities.Count == 0)\n  {\n    failures.Add($"{label} canonical identity map is empty.");\n    return;\n  }\n'''
replacement = '''  IReadOnlyList<TranscriptNodeIdentity> identities =\n    TranscriptNodeIdentityMap.Build(path, source);\n  if (identities.Count == 0)\n  {\n    failures.Add($"{label} canonical identity map is empty.");\n    return;\n  }\n\n  using (var monitor = new JsonlSessionMonitor())\n  {\n    LocatedSession session = SessionLocator.FromPath(path, source);\n    SpeechHistorySnapshot history = monitor.LoadHistoryPreview(\n      session,\n      speakExistingLatestTurn: false);\n    foreach (IGrouping<long, SpeechFragment> nodeGroup in\n             history.Fragments.GroupBy(fragment => fragment.NodeId))\n    {\n      TranscriptNodeIdentity? identity = identities.FirstOrDefault(\n        item => item.NodeId == nodeGroup.Key);\n      if (identity is null)\n      {\n        failures.Add(\n          $"{label} speech node has no transcript identity: " +\n          $"node={nodeGroup.Key}.");\n        continue;\n      }\n\n      IReadOnlyList<string> expectedSegments = nodeGroup\n        .Select(fragment => fragment.Text)\n        .ToArray();\n      if (expectedSegments.Count != 0 && identity.Segments.Count == 0)\n      {\n        failures.Add(\n          $"{label} rendered speech node lost all DOM segments: " +\n          $"node={nodeGroup.Key}; first={expectedSegments[0]}.");\n      }\n    }\n  }\n'''
if needle not in program:
  raise SystemExit('DisplayParity identity validation insertion point not found')
program_path.write_text(program.replace(needle, replacement, 1), encoding='utf-8')
