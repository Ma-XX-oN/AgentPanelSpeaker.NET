from pathlib import Path


def replace_once(text, old, new, label):
  count = text.count(old)
  if count != 1:
    raise SystemExit(f'{label}: expected exactly one match, found {count}')
  return text.replace(old, new)


main_path = Path('AgentPanelSpeaker/MainForm.cs')
main = main_path.read_text(encoding='utf-8')

main = replace_once(
  main,
  '''  private const int WmKeyDown = 0x0100;\n''',
  '''  private const int WmKeyDown = 0x0100;\n  private const int WmKeyUp = 0x0101;\n''',
  'MainForm WmKeyUp constant')
main = replace_once(
  main,
  '''  private const int WmSystemKeyDown = 0x0104;\n''',
  '''  private const int WmSystemKeyDown = 0x0104;\n  private const int WmSystemKeyUp = 0x0105;\n''',
  'MainForm WmSystemKeyUp constant')

profile_anchor = '''  private IEnumerable<SpeechProfileCompactControl> GetProfileControls()\n  {\n    yield return _masterSpeechProfile;\n    foreach (VoiceRowControls row in _voiceRows.Values)\n    {\n      yield return row.MainProfile;\n      yield return row.ContextProfile;\n    }\n  }\n\n'''
resolver = '''  /// <summary>\n  /// Resolves host Ctrl key messages into transcript voice-pointer selection\n  /// state without consuming the original key message.\n  /// </summary>\n  internal static bool TryGetVoicePointerSelectModeMessage(\n    int message,\n    IntPtr wordParameter,\n    out bool enabled)\n  {\n    enabled = false;\n    if (message is not (WmKeyDown or WmSystemKeyDown or\n        WmKeyUp or WmSystemKeyUp))\n    {\n      return false;\n    }\n\n    Keys keyCode = (Keys)(int)wordParameter & Keys.KeyCode;\n    if (keyCode != Keys.ControlKey)\n    {\n      return false;\n    }\n\n    enabled = message is WmKeyDown or WmSystemKeyDown;\n    return true;\n  }\n\n'''
main = replace_once(
  main,
  profile_anchor,
  profile_anchor + resolver,
  'MainForm Ctrl resolver insertion')

filter_anchor = '''    if (GetAncestor(message.HWnd, GaRoot) != Handle)\n    {\n      return false;\n    }\n\n    if (message.Msg is not (WmKeyDown or WmSystemKeyDown))\n'''
filter_replacement = '''    if (GetAncestor(message.HWnd, GaRoot) != Handle)\n    {\n      return false;\n    }\n\n    if (TryGetVoicePointerSelectModeMessage(\n          message.Msg,\n          message.WParam,\n          out bool voicePointerSelectMode))\n    {\n      _transcriptView.SetVoicePointerSelectMode(voicePointerSelectMode);\n    }\n\n    if (message.Msg is not (WmKeyDown or WmSystemKeyDown))\n'''
main = replace_once(
  main,
  filter_anchor,
  filter_replacement,
  'MainForm host Ctrl routing')

activation_anchor = '''    _processingTimeButton.Click += ProcessingTimeButtonClicked;\n    Deactivate += (_, _) =>\n    {\n      PopupFormBase.WriteActivationDiagnostics(\n        "mainform-deactivate-event",\n        this);\n      HoverPopupController.HandleOwnerDeactivated(this);\n    };\n'''
activation_replacement = '''    _processingTimeButton.Click += ProcessingTimeButtonClicked;\n    Activated += (_, _) =>\n      _transcriptView.SetVoicePointerSelectMode(\n        (Control.ModifierKeys & Keys.Control) != 0);\n    Deactivate += (_, _) =>\n    {\n      _transcriptView.SetVoicePointerSelectMode(false);\n      PopupFormBase.WriteActivationDiagnostics(\n        "mainform-deactivate-event",\n        this);\n      HoverPopupController.HandleOwnerDeactivated(this);\n    };\n'''
main = replace_once(
  main,
  activation_anchor,
  activation_replacement,
  'MainForm activation/deactivation sync')
main_path.write_text(main, encoding='utf-8')

view_path = Path('AgentPanelSpeaker/TranscriptView.cs')
view = view_path.read_text(encoding='utf-8')

range_anchor = '''  public void SetSeekableVoiceRanges(\n    IReadOnlyList<SeekableTranscriptWordRange> ranges)\n  {\n    ArgumentNullException.ThrowIfNull(ranges);\n    _seekableVoiceRanges = ranges.ToArray();\n    PostSeekableVoiceRanges();\n  }\n\n'''
set_mode_method = '''  /// <summary>\n  /// Updates the page-level Ctrl voice-pointer selection mode from the host\n  /// window without walking or mutating individual transcript words.\n  /// </summary>\n  public void SetVoicePointerSelectMode(bool enabled)\n  {\n    PostMessage(new\n    {\n      type = "voice-pointer-select-mode",\n      enabled\n    });\n  }\n\n'''
view = replace_once(
  view,
  range_anchor,
  range_anchor + set_mode_method,
  'TranscriptView host selection-mode API')

message_anchor = '''  if (data.type === 'seekable-voice-ranges') {\n    setSeekableVoiceRanges(data.ranges ?? data.Ranges ?? []);\n    return;\n  }\n  if (data.type === 'settings') {\n'''
message_replacement = '''  if (data.type === 'seekable-voice-ranges') {\n    setSeekableVoiceRanges(data.ranges ?? data.Ranges ?? []);\n    return;\n  }\n  if (data.type === 'voice-pointer-select-mode') {\n    setVoicePointerSelectMode(Boolean(data.enabled ?? data.Enabled));\n    return;\n  }\n  if (data.type === 'settings') {\n'''
view = replace_once(
  view,
  message_anchor,
  message_replacement,
  'TranscriptView host message handling')

view = replace_once(
  view,
  '''window.addEventListener('blur', () => setVoicePointerSelectMode(false));\n''',
  '',
  'TranscriptView browser blur removal')
view_path.write_text(view, encoding='utf-8')
