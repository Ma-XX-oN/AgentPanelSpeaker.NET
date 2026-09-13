namespace AgentPanelSpeaker;

/// <summary>
/// Selects the System.Speech voice and scalar settings for the controlled SSML
/// provider matrix. Values are diagnostic-only and are not persisted.
/// </summary>
internal sealed class SystemSpeechMatrixDialog : Form
{
  private readonly ComboBox _voice = new();
  private readonly NumericUpDown _rate = new();
  private readonly NumericUpDown _volume = new();
  private readonly Button _run = new();
  private readonly Button _cancel = new();

  public SystemSpeechMatrixDialog(IReadOnlyList<InstalledSpeechVoice> voices)
  {
    ArgumentNullException.ThrowIfNull(voices);
    if (voices.Count == 0 || voices.Any(
        voice => voice.Provider != SpeechVoiceProvider.SystemSpeech))
    {
      throw new ArgumentException(
        "The matrix dialog requires one or more System.Speech voices.",
        nameof(voices));
    }

    Text = "System.Speech SSML Matrix";
    StartPosition = FormStartPosition.CenterParent;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    Padding = new Padding(12);

    var layout = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 6,
      Padding = new Padding(0)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));

    var description = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(470, 0),
      Text =
        "Runs five fixed SSML cases directly through System.Speech. " +
        "The diagnostic does not use transcript playback or change the voice cursor."
    };
    layout.Controls.Add(description, 0, 0);
    layout.SetColumnSpan(description, 2);

    _voice.DropDownStyle = ComboBoxStyle.DropDownList;
    _voice.Dock = DockStyle.Fill;
    foreach (InstalledSpeechVoice voice in voices)
    {
      _voice.Items.Add(voice);
    }
    _voice.SelectedIndex = 0;

    _rate.Minimum = -10;
    _rate.Maximum = 10;
    _rate.Value = 0;
    _rate.Width = 80;
    _volume.Minimum = 0;
    _volume.Maximum = 100;
    _volume.Value = 100;
    _volume.Width = 80;

    layout.Controls.Add(new Label { AutoSize = true, Text = "Voice:" }, 0, 1);
    layout.Controls.Add(_voice, 1, 1);
    layout.Controls.Add(new Label { AutoSize = true, Text = "Rate:" }, 0, 2);
    layout.Controls.Add(_rate, 1, 2);
    layout.Controls.Add(new Label { AutoSize = true, Text = "Volume:" }, 0, 3);
    layout.Controls.Add(_volume, 1, 3);

    var cases = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(470, 0),
      Text =
        "1. AI-transcript.py\n" +
        "2. characters(AI)-transcript.py\n" +
        "3. characters(AI) transcript.py\n" +
        "4. characters(AI) + 100 ms break + -transcript.py\n" +
        "5. characters(AI) + sub(alias=transcript) + .py"
    };
    layout.Controls.Add(cases, 0, 4);
    layout.SetColumnSpan(cases, 2);

    ConfigureButton(_run, "Run matrix", DialogResult.OK);
    ConfigureButton(_cancel, "Cancel", DialogResult.Cancel);
    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.RightToLeft,
      Dock = DockStyle.Fill
    };
    buttons.Controls.Add(_cancel);
    buttons.Controls.Add(_run);
    layout.Controls.Add(buttons, 0, 5);
    layout.SetColumnSpan(buttons, 2);

    Controls.Add(layout);
    AcceptButton = _run;
    CancelButton = _cancel;
  }

  /// <summary>
  /// Returns the diagnostic settings selected by the user.
  /// </summary>
  public SystemSpeechMatrixOptions Options => new(
    (InstalledSpeechVoice)_voice.SelectedItem!,
    (int)_rate.Value,
    (int)_volume.Value);

  private static void ConfigureButton(
    Button button,
    string text,
    DialogResult result)
  {
    button.AutoSize = true;
    button.Text = text;
    button.DialogResult = result;
  }
}
