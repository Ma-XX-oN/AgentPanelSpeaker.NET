namespace AgentPanelSpeaker;

internal sealed class HotkeySettingsDialog : Form
{
  private readonly Dictionary<HotkeyAction, TextBox> _editors = new();

  public HotkeySettingsDialog(HotkeySettings settings, AppTheme theme)
  {
    Text = "Hotkey remapping";
    StartPosition = FormStartPosition.CenterParent;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MinimizeBox = false;
    MaximizeBox = false;
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    Padding = new Padding(10);

    var table = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 2,
      Dock = DockStyle.Fill
    };
    int row = 0;
    foreach ((HotkeyAction action, string label, string value) in Rows(settings))
    {
      var caption = new Label
      {
        AutoSize = true,
        Text = label,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 12, 3)
      };
      var editor = new TextBox
      {
        Width = 45,
        MaxLength = 1,
        CharacterCasing = CharacterCasing.Upper,
        Text = value,
        TextAlign = HorizontalAlignment.Center
      };
      editor.KeyPress += (_, e) =>
      {
        if (HotkeySettings.ParseKey(e.KeyChar.ToString()) == Keys.None)
        {
          e.Handled = true;
        }
      };
      _editors[action] = editor;
      table.Controls.Add(caption, 0, row);
      table.Controls.Add(editor, 1, row++);
    }

    var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      FlowDirection = FlowDirection.RightToLeft,
      Dock = DockStyle.Fill
    };
    buttons.Controls.Add(cancel);
    buttons.Controls.Add(ok);
    table.Controls.Add(buttons, 0, row);
    table.SetColumnSpan(buttons, 2);
    Controls.Add(table);
    AcceptButton = ok;
    CancelButton = cancel;
    FormClosing += ValidateBeforeClose;
    ThemeManager.Apply(this, theme);
  }

  public HotkeySettings Settings => new HotkeySettings
  {
    PreviousSpeaker = Value(HotkeyAction.PreviousSpeaker),
    PreviousNode = Value(HotkeyAction.PreviousNode),
    PreviousSentence = Value(HotkeyAction.PreviousSentence),
    PlayPause = Value(HotkeyAction.PlayPause),
    NextSentence = Value(HotkeyAction.NextSentence),
    NextNode = Value(HotkeyAction.NextNode),
    NextSpeaker = Value(HotkeyAction.NextSpeaker),
    ProcessingTime = Value(HotkeyAction.ProcessingTime),
    ToggleTranscriptSize = Value(HotkeyAction.ToggleTranscriptSize),
    ToggleFollow = Value(HotkeyAction.ToggleFollow)
  }.Normalize();

  private string Value(HotkeyAction action) => _editors[action].Text;

  private void ValidateBeforeClose(object? sender, FormClosingEventArgs e)
  {
    if (DialogResult != DialogResult.OK)
    {
      return;
    }
    var seen = new HashSet<Keys>();
    foreach (TextBox editor in _editors.Values)
    {
      Keys key = HotkeySettings.ParseKey(editor.Text);
      if (key == Keys.None || !seen.Add(key))
      {
        MessageBox.Show(this, "Every hotkey must be one unique letter or supported punctuation key.", "Invalid hotkeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        editor.Focus();
        e.Cancel = true;
        return;
      }
    }
  }

  private static IEnumerable<(HotkeyAction, string, string)> Rows(HotkeySettings settings)
  {
    yield return (HotkeyAction.PreviousSpeaker, "Previous speaker", settings.PreviousSpeaker);
    yield return (HotkeyAction.PreviousNode, "Previous node", settings.PreviousNode);
    yield return (HotkeyAction.PreviousSentence, "Previous sentence", settings.PreviousSentence);
    yield return (HotkeyAction.PlayPause, "Play / pause", settings.PlayPause);
    yield return (HotkeyAction.NextSentence, "Next sentence", settings.NextSentence);
    yield return (HotkeyAction.NextNode, "Next node", settings.NextNode);
    yield return (HotkeyAction.NextSpeaker, "Next speaker", settings.NextSpeaker);
    yield return (HotkeyAction.ProcessingTime, "Speak processing time", settings.ProcessingTime);
    yield return (HotkeyAction.ToggleTranscriptSize, "Minimize / maximize transcript", settings.ToggleTranscriptSize);
    yield return (HotkeyAction.ToggleFollow, "Toggle follow mode", settings.ToggleFollow);
  }
}
