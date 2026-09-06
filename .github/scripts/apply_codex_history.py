from pathlib import Path
import re

CORE = '9ab9e4f5bd5f4e4a02653267ed118c378912617f'

for name in [
  'AgentPanelSpeaker/AIConversationCoreClient.cs',
  'tools/AIConversationCore-worker.mjs',
]:
  path = Path(name)
  text = path.read_text(encoding='utf-8')
  text = text.replace('df58d483956c280d0103bd9ae9f79c642f0d279e', CORE)
  path.write_text(text, encoding='utf-8', newline='\n')

workflow = Path('.github/workflows/core-integration-validation.yml')
text = workflow.read_text(encoding='utf-8')
text = text.replace('b7161f198a204f18d12d896689f6553d76161102', CORE)
workflow.write_text(text, encoding='utf-8', newline='\n')

popup = Path('AgentPanelSpeaker/TranscriptSettingsPopup.cs')
text = popup.read_text(encoding='utf-8')
old_call = 'advancedPopup.SetQueueCapacity(Settings.HighlightQueueCapacity);'
if text.count(old_call) != 3:
  raise SystemExit(f'expected 3 advanced popup queue calls, got {text.count(old_call)}')
text = text.replace(
  old_call,
  'advancedPopup.SetSettings(\n        Settings.HighlightQueueCapacity,\n        Settings.ShowRolledBackHistory);')
old = '''      Settings = (Settings with
      {
        HighlightQueueCapacity = popup.QueueCapacity
      }).Normalize();'''
new = '''      Settings = (Settings with
      {
        HighlightQueueCapacity = popup.QueueCapacity,
        ShowRolledBackHistory = popup.ShowRolledBackHistory
      }).Normalize();'''
if text.count(old) != 1:
  raise SystemExit('advanced popup value-change block did not match')
text = text.replace(old, new, 1)
popup.write_text(text, encoding='utf-8', newline='\n')

locator = Path('AgentPanelSpeaker/SessionLocator.cs')
text = locator.read_text(encoding='utf-8')
start = text.index('  private static string ReadCodexTitle(string sessionPath, string sessionId)')
end = text.index("  /// <summary>\n  /// Reads Claude's session title", start)
replacement = '''  private static string ReadCodexTitle(string sessionPath, string sessionId)
  {
    try
    {
      using var client = new AIConversationCoreClient();
      AIConversationProjection projection = client.Project(
        AgentSource.Codex,
        ReadSharedLines(sessionPath).ToArray(),
        new AIConversationCoreProjectOptions(
          CodexSessionIndexPath: GetCodexSessionIndexPath()));
      string? title = projection.SessionMetadata?.Title;
      return string.IsNullOrWhiteSpace(title)
        ? sessionId
        : LimitTitle(title.Trim());
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      JsonException or
      InvalidOperationException or
      ArgumentException)
    {
      DiagnosticLog.Write("session.codex_title_failed", new
      {
        sessionPath,
        sessionId,
        exception = exception.Message
      });
      return sessionId;
    }
  }

  /// <summary>
  /// Gets the optional caller-discovered Codex session-index path.
  /// </summary>
  internal static string? GetCodexSessionIndexPath()
  {
    string path = Path.Combine(GetCodexHome(), "session_index.jsonl");
    return File.Exists(path) ? path : null;
  }

'''
text = text[:start] + replacement + text[end:]
text = re.sub(
  r'\n  \[GeneratedRegex\(\n    @"## My request for Codex:\\s\*\(\.\*\)",\n    RegexOptions\.Singleline\)\]\n  private static partial Regex CodexRequestPreambleRegex\(\);\n',
  '\n',
  text,
  count=1)
locator.write_text(text, encoding='utf-8', newline='\n')

monitor = Path('AgentPanelSpeaker/JsonlSessionMonitor.cs')
text = monitor.read_text(encoding='utf-8')
old = '''  TimeSpan PollInterval,
  SpeechHistorySnapshot? PreindexedHistory = null);'''
new = '''  TimeSpan PollInterval,
  SpeechHistorySnapshot? PreindexedHistory = null,
  bool IncludeRolledBackTurns = false);'''
if text.count(old) != 1:
  raise SystemExit('MonitorSettings block did not match')
text = text.replace(old, new, 1)

old = '''  public SpeechHistorySnapshot LoadHistoryPreview(
    LocatedSession session,
    bool speakExistingLatestTurn)'''
new = '''  public SpeechHistorySnapshot LoadHistoryPreview(
    LocatedSession session,
    bool speakExistingLatestTurn,
    bool includeRolledBackTurns = false)'''
if text.count(old) != 1:
  raise SystemExit('LoadHistoryPreview signature did not match')
text = text.replace(old, new, 1)

old = '''      preview,
      pendingInputRequests);'''
idx = text.index(old, text.index('public SpeechHistorySnapshot LoadHistoryPreview'))
text = text[:idx] + '''      preview,
      pendingInputRequests,
      includeRolledBackTurns);''' + text[idx + len(old):]

old = '''        _canonicalExtractor.Prime(
          session.Source,
          ReadSharedLines(session.Path));'''
new = '''        _canonicalExtractor.Prime(
          session.Source,
          ReadSharedLines(session.Path),
          ProjectionOptions(session, settings.IncludeRolledBackTurns));'''
if text.count(old) != 1:
  raise SystemExit('preindexed Prime block did not match')
text = text.replace(old, new, 1)

run_start = text.index('  private void Run(MonitorSettings settings')
run_end = text.index('  /// <summary>\n  /// Resolves a fixed or latest initial session.', run_start)
run_text = text[run_start:run_end]
run_text = run_text.replace(
  '            pendingInputRequests);',
  '            pendingInputRequests,\n            settings.IncludeRolledBackTurns);')
text = text[:run_start] + run_text + text[run_end:]

old = '''  private SpeechHistorySnapshot LoadExistingHistory(
    LocatedSession session,
    bool speakExistingLatestTurn,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    IDictionary<string, CodexInputRequest> pendingInputRequests)'''
new = '''  private SpeechHistorySnapshot LoadExistingHistory(
    LocatedSession session,
    bool speakExistingLatestTurn,
    ref long nextNodeId,
    Queue<string> recentFingerprintQueue,
    HashSet<string> recentFingerprintSet,
    Queue<string> preview,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    bool includeRolledBackTurns)'''
if text.count(old) != 1:
  raise SystemExit('LoadExistingHistory signature did not match')
text = text.replace(old, new, 1)
text = text.replace(
  '''    EligibleHistory eligibleHistory = ReadEligibleHistory(
      session,
      pendingInputRequests);''',
  '''    EligibleHistory eligibleHistory = ReadEligibleHistory(
      session,
      pendingInputRequests,
      includeRolledBackTurns);''',
  1)

old = '''  private EligibleHistory ReadEligibleHistory(
    LocatedSession session,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    DateTime? minimumTimestampUtc = null)'''
new = '''  private EligibleHistory ReadEligibleHistory(
    LocatedSession session,
    IDictionary<string, CodexInputRequest> pendingInputRequests,
    bool includeRolledBackTurns,
    DateTime? minimumTimestampUtc = null)'''
if text.count(old) != 1:
  raise SystemExit('ReadEligibleHistory signature did not match')
text = text.replace(old, new, 1)
old = '''    IReadOnlyList<ExtractionResult> results = _canonicalExtractor.Load(
      session.Source,
      ReadSharedLines(session.Path));'''
new = '''    IReadOnlyList<ExtractionResult> results = _canonicalExtractor.Load(
      session.Source,
      ReadSharedLines(session.Path),
      ProjectionOptions(session, includeRolledBackTurns));'''
if text.count(old) != 1:
  raise SystemExit('canonical history load block did not match')
text = text.replace(old, new, 1)

insert_at = text.index('  /// <summary>\n  /// Parses one terminal event timestamp')
helper = '''  /// <summary>
  /// Builds the core projection options for one selected session.
  /// </summary>
  private static AIConversationCoreProjectOptions ProjectionOptions(
    LocatedSession session,
    bool includeRolledBackTurns)
  {
    return new AIConversationCoreProjectOptions(
      IncludeRolledBackTurns: includeRolledBackTurns,
      CodexSessionIndexPath: session.Source == AgentSource.Codex
        ? SessionLocator.GetCodexSessionIndexPath()
        : null);
  }

'''
text = text[:insert_at] + helper + text[insert_at:]
monitor.write_text(text, encoding='utf-8', newline='\n')

main = Path('AgentPanelSpeaker/MainForm.cs')
text = main.read_text(encoding='utf-8')
old = '_monitor.LoadHistoryPreview(session, startAtLatestTurn)'
new = '''_monitor.LoadHistoryPreview(
          session,
          startAtLatestTurn,
          _transcriptSettingsPopup.Settings.ShowRolledBackHistory)'''
if text.count(old) != 1:
  raise SystemExit('paused history preview call did not match')
text = text.replace(old, new, 1)
old = '''        TimeSpan.FromMilliseconds((double)_pollNumeric.Value),
        preindexedHistory));'''
new = '''        TimeSpan.FromMilliseconds((double)_pollNumeric.Value),
        preindexedHistory,
        IncludeRolledBackTurns:
          _transcriptSettingsPopup.Settings.ShowRolledBackHistory));'''
if text.count(old) != 1:
  raise SystemExit('MonitorSettings construction did not match')
text = text.replace(old, new, 1)

old = '''  private void TranscriptSettingsChanged()
  {
    bool dark = ThemeManager.IsDark(GetSelectedTheme());
    TranscriptSettings settings = _transcriptSettingsPopup.Settings;'''
new = '''  private void TranscriptSettingsChanged()
  {
    bool dark = ThemeManager.IsDark(GetSelectedTheme());
    TranscriptSettings settings = _transcriptSettingsPopup.Settings;
    bool historyVisibilityChanged =
      _settingsStore.Current.Transcript.ShowRolledBackHistory !=
      settings.ShowRolledBackHistory;'''
if text.count(old) != 1:
  raise SystemExit('TranscriptSettingsChanged header did not match')
text = text.replace(old, new, 1)
old = '''    _transcriptSettingsSaveTimer.Stop();
    _transcriptSettingsSaveTimer.Start();
  }'''
new = '''    _transcriptSettingsSaveTimer.Stop();
    _transcriptSettingsSaveTimer.Start();
    if (historyVisibilityChanged && !_monitor.IsRunning &&
        !string.IsNullOrWhiteSpace(_sessionPathTextBox.Text))
    {
      try
      {
        LocatedSession session = SessionLocator.FromPath(
          _sessionPathTextBox.Text,
          GetSelectedSource());
        _selectedSessionHistory = null;
        _selectedSessionHistoryPath = null;
        _ = LoadPausedHistoryPreviewAsync(session);
      }
      catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException or
        InvalidDataException or InvalidOperationException or ArgumentException)
      {
        AppendLog($"Unable to rebuild transcript history: {exception.Message}");
      }
    }
  }'''
if text.count(old) != 1:
  raise SystemExit('TranscriptSettingsChanged tail did not match')
text = text.replace(old, new, 1)
main.write_text(text, encoding='utf-8', newline='\n')
