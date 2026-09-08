from pathlib import Path


def replace_once(text, old, new, name):
  if old not in text:
    raise SystemExit(f'{name}: target not found')
  return text.replace(old, new, 1)


path = Path('AgentPanelSpeaker/TranscriptNodeIdentityMap.cs')
text = path.read_text(encoding='utf-8')

text = replace_once(
  text,
  '''  public static IReadOnlyList<TranscriptNodeIdentity> Build(
    string path,
    AgentSource source,
    CancellationToken cancellationToken = default)
''',
  '''  public static IReadOnlyList<TranscriptNodeIdentity> Build(
    string path,
    AgentSource source,
    CancellationToken cancellationToken = default,
    bool includeRolledBackTurns = false)
''',
  'identity-map signature')

text = replace_once(
  text,
  '''    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      client.Project(source, jsonLines));
''',
  '''    using var client = new AIConversationCoreClient();
    var projectOptions = new AIConversationCoreProjectOptions(
      IncludeRolledBackTurns: includeRolledBackTurns,
      CodexSessionIndexPath: source == AgentSource.Codex
        ? SessionLocator.GetCodexSessionIndexPath()
        : null,
      // User Context is always part of the indexed speech-history namespace.
      // The UI setting controls playback eligibility, not node numbering.
      IncludeUserContext: true);
    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      client.Project(source, jsonLines, projectOptions));
''',
  'identity-map Core projection options')

path.write_text(text, encoding='utf-8')

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

text = replace_once(
  text,
  '''      AgentSource source = _source;
      CancellationToken token = cancellation.Token;
''',
  '''      AgentSource source = _source;
      bool includeRolledBackTurns = _settings.ShowRolledBackHistory;
      CancellationToken token = cancellation.Token;
''',
  'capture transcript projection setting')

text = replace_once(
  text,
  '''          () => identities = TranscriptNodeIdentityMap.Build(
            path,
            source,
            token),
''',
  '''          () => identities = TranscriptNodeIdentityMap.Build(
            path,
            source,
            token,
            includeRolledBackTurns),
''',
  'transcript identity-map call')

path.write_text(text, encoding='utf-8')
