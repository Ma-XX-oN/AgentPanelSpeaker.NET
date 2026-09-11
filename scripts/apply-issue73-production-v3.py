from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
source_path = ROOT / "scripts" / "apply-issue73-production.py"
source = source_path.read_text(encoding="utf-8")

# The original helper's SpeechService / TranscriptView / event-args edits are
# already guarded and reached MainForm successfully in prior runs.  Execute
# only that proven prefix, then apply MainForm edits with explicit semantic
# contexts instead of ambiguous global snippets.
main_marker = '\nmain = ROOT / "AgentPanelSpeaker" / "MainForm.cs"\n'
parts = source.split(main_marker, 1)
if len(parts) != 2:
  raise SystemExit("production helper: MainForm boundary was not found exactly once")
exec(
  compile(parts[0], str(source_path), "exec"),
  {
    "__name__": "__main__",
    "__file__": str(source_path),
  })

main = ROOT / "AgentPanelSpeaker" / "MainForm.cs"
text = main.read_text(encoding="utf-8")


def replace_once(old: str, new: str, description: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise SystemExit(
      f"{main}: {description}: expected exactly one match, found {count}")
  text = text.replace(old, new, 1)


replace_once(
  "    _masterSpeechProfile.ProfileChanged += (_, _) =>\n"
  "      SaveControlsToSettings();\n",
  "    _masterSpeechProfile.ProfileChanged += (_, _) =>\n"
  "    {\n"
  "      SaveControlsToSettings();\n"
  "      RefreshTranscriptVoiceSelectability();\n"
  "    };\n",
  "master speech profile eligibility refresh")

replace_once(
  "    SaveControlsToSettings();\n"
  "    AppendLog(\n"
  "      \"Spoken fenced-code types updated: \" +\n",
  "    SaveControlsToSettings();\n"
  "    RefreshTranscriptVoiceSelectability();\n"
  "    AppendLog(\n"
  "      \"Spoken fenced-code types updated: \" +\n",
  "fenced-code eligibility refresh")

replace_once(
  "    SaveControlsToSettings();\n"
  "    ScheduleVoiceSettingsPreview(role, context);\n",
  "    SaveControlsToSettings();\n"
  "    RefreshTranscriptVoiceSelectability();\n"
  "    ScheduleVoiceSettingsPreview(role, context);\n",
  "voice-row eligibility refresh")

replace_once(
  "      _speech.SetShowRolledBackHistory(\n"
  "        settings.Transcript.ShowRolledBackHistory);\n"
  "      _transcriptView.ApplySettings(settings.Transcript, transcriptDark);\n",
  "      _speech.SetShowRolledBackHistory(\n"
  "        settings.Transcript.ShowRolledBackHistory);\n"
  "      _transcriptView.ApplySettings(settings.Transcript, transcriptDark);\n"
  "      RefreshTranscriptVoiceSelectability();\n",
  "loaded-settings eligibility refresh")

replace_once(
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n"
  "    _transcriptView.ApplySettings(settings, dark);\n",
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n"
  "    _transcriptView.ApplySettings(settings, dark);\n"
  "    RefreshTranscriptVoiceSelectability();\n",
  "transcript-settings eligibility refresh")

load_history = (
  "      _speech.LoadHistory(\n"
  "        snapshot.Fragments,\n"
  "        snapshot.Completions,\n"
  "        snapshot.BackgroundWorkEvents,\n"
  "        snapshot.StartMode);\n")
load_history_count = text.count(load_history)
if load_history_count != 2:
  raise SystemExit(
    f"{main}: expected two history-load sites, found {load_history_count}")
text = text.replace(
  load_history,
  load_history + "      RefreshTranscriptVoiceSelectability();\n")

replace_once(
  "      _speech.SpeakLive(fragment);\n"
  "      AppendLog($\"Queued {fragment.Category}: {fragment.Text}\");\n",
  "      _speech.SpeakLive(fragment);\n"
  "      RefreshTranscriptVoiceSelectability();\n"
  "      AppendLog($\"Queued {fragment.Category}: {fragment.Text}\");\n",
  "live-fragment eligibility refresh")

# Every BeginLiveSession() clears retained speech history, so every such reset
# must immediately clear the browser's eligibility ranges too.  Preserve each
# call site's indentation instead of relying on one textual context.
def add_live_session_refresh(match: re.Match[str]) -> str:
  indent = match.group("indent")
  return (
    f"{indent}_speech.BeginLiveSession();\n"
    f"{indent}RefreshTranscriptVoiceSelectability();\n")

text, begin_live_count = re.subn(
  r"^(?P<indent>[ \t]*)_speech\.BeginLiveSession\(\);\n",
  add_live_session_refresh,
  text,
  flags=re.MULTILINE)
if begin_live_count != 3:
  raise SystemExit(
    f"{main}: expected three BeginLiveSession reset sites, found {begin_live_count}")

seek_handler = '''  /// <summary>
  /// Moves the paused speech marker to the voiced word selected by Find.
  /// </summary>
  private void TranscriptFindSeekRequested(
'''
seek_handler_new = '''  /// <summary>
  /// Publishes current speech eligibility to the transcript. Ctrl itself only
  /// toggles one page-level CSS class; individual word classes change only
  /// when mapping or speech eligibility changes.
  /// </summary>
  private void RefreshTranscriptVoiceSelectability()
  {
    _transcriptView.SetSeekableVoiceRanges(
      _speech.GetSeekableTranscriptWordRanges());
  }

  /// <summary>
  /// Moves the paused speech marker to a voiced word selected by Find or by
  /// direct Ctrl+click transcript navigation.
  /// </summary>
  private void TranscriptFindSeekRequested(
'''
replace_once(
  seek_handler,
  seek_handler_new,
  "voice-selectability publisher insertion")

replace_once(
  "      AppendLog($\"Find moved speech marker: {text}\");\n",
  "      AppendLog(eventArgs.Source == \"ctrl-click\"\n"
  "        ? $\"Ctrl+click moved speech marker: {text}\"\n"
  "        : $\"Find moved speech marker: {text}\");\n",
  "Ctrl+click activity text")

main.write_text(text, encoding="utf-8")
print("Applied issue #73 production implementation with context-specific MainForm edits.")
