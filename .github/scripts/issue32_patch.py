from pathlib import Path


def replace_once(text, old, new, label):
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


main_path = Path("AgentPanelSpeaker/TranscriptSettingsPopup.cs")
text = main_path.read_text(encoding="utf-8")
text = replace_once(
  text,
  "internal sealed class TranscriptSettingsPopup : PopupFormBase\n{\n",
  """internal sealed class TranscriptSettingsPopup : PopupFormBase
{
  private const string RolledBackDescription =
    "Shows Codex turns that were rolled back or superseded. Historical " +
    "revisions are hidden by default; enabling this includes original, " +
    "superseded, edited, and aborted revision state in the transcript.";
  private const string UserContextDescription =
    "Includes Core-identified User/IDE context in speech before the actual " +
    "User prompt. The context uses the User Context voice profile. This does " +
    "not change whether the context is shown in the transcript.";
""",
  "main descriptions")
text = replace_once(
  text,
  "  private readonly Label _trackingValue = new();\n  private readonly Button _advancedButton = new();\n",
  """  private readonly Label _trackingValue = new();
  private readonly CheckBox _showRolledBackCheckBox = new();
  private readonly CheckBox _speakUserContextCheckBox = new();
  private readonly Button _advancedButton = new();
""",
  "main checkbox fields")
text = replace_once(
  text,
  "    Size = new Size(430, 196);\n",
  "    Size = new Size(430, 256);\n",
  "main size")
text = replace_once(
  text,
  """    ConfigureSlider(_trackingSlider, 1, 8, 1);
    _trackingSlider.TabIndex = 3;
    ConfigureValueLabel(_trackingValue);

    _advancedButton.AutoSize = false;
""",
  """    ConfigureSlider(_trackingSlider, 1, 8, 1);
    _trackingSlider.TabIndex = 3;
    ConfigureValueLabel(_trackingValue);

    ConfigureCheckBox(
      _showRolledBackCheckBox,
      "Show rolled-back Codex history",
      "Show rolled-back Codex history",
      RolledBackDescription,
      tabIndex: 4);
    ConfigureCheckBox(
      _speakUserContextCheckBox,
      "Speak User / IDE context",
      "Speak User or IDE context",
      UserContextDescription,
      tabIndex: 5);

    _advancedButton.AutoSize = false;
""",
  "main checkbox configuration")
text = replace_once(
  text,
  "    _advancedButton.TabIndex = 4;\n",
  "    _advancedButton.TabIndex = 6;\n",
  "main advanced tab index")
text = replace_once(
  text,
  """      ColumnCount = 3,
      RowCount = 5,
      Dock = DockStyle.Fill,
""",
  """      ColumnCount = 3,
      RowCount = 7,
      Dock = DockStyle.Fill,
""",
  "main row count")
text = replace_once(
  text,
  """    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.Controls.Add(title, 0, 0);
""",
  """    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.Controls.Add(title, 0, 0);
""",
  "main row styles")
text = replace_once(
  text,
  """    AddSliderRow(layout, 2, "Fade Duration", _fadeSlider, _fadeValue);
    AddSliderRow(layout, 3, "Tracking Update", _trackingSlider, _trackingValue);
    layout.Controls.Add(_advancedButton, 0, 4);
    layout.SetColumnSpan(_advancedButton, 3);
""",
  """    AddSliderRow(layout, 2, "Fade Duration", _fadeSlider, _fadeValue);
    AddSliderRow(layout, 3, "Tracking Update", _trackingSlider, _trackingValue);
    layout.Controls.Add(_showRolledBackCheckBox, 0, 4);
    layout.SetColumnSpan(_showRolledBackCheckBox, 3);
    layout.Controls.Add(_speakUserContextCheckBox, 0, 5);
    layout.SetColumnSpan(_speakUserContextCheckBox, 3);
    layout.Controls.Add(_advancedButton, 0, 6);
    layout.SetColumnSpan(_advancedButton, 3);
""",
  "main control placement")
text = replace_once(
  text,
  """    _previousSwatch.Click += (_, _) => RestorePreviousColour();
    _fadeSlider.ValueChanged += ValueChanged;
    _trackingSlider.ValueChanged += ValueChanged;

""",
  """    _previousSwatch.Click += (_, _) => RestorePreviousColour();
    _fadeSlider.ValueChanged += ValueChanged;
    _trackingSlider.ValueChanged += ValueChanged;
    _showRolledBackCheckBox.CheckedChanged += ValueChanged;
    _speakUserContextCheckBox.CheckedChanged += ValueChanged;

""",
  "main checkbox events")
text = replace_once(
  text,
  """      _trackingSlider.Value = Math.Clamp(
        Settings.HighlightUpdateMilliseconds / 5,
        1,
        8);
      if (_advancedPopup is { IsDisposed: false } advancedPopup)
      {
        advancedPopup.SetSettings(
        Settings.HighlightQueueCapacity,
        Settings.ShowRolledBackHistory,
        Settings.SpeakUserContext);
      }
""",
  """      _trackingSlider.Value = Math.Clamp(
        Settings.HighlightUpdateMilliseconds / 5,
        1,
        8);
      _showRolledBackCheckBox.Checked = Settings.ShowRolledBackHistory;
      _speakUserContextCheckBox.Checked = Settings.SpeakUserContext;
      if (_advancedPopup is { IsDisposed: false } advancedPopup)
      {
        advancedPopup.SetQueueCapacity(Settings.HighlightQueueCapacity);
      }
""",
  "main set settings")
text = replace_once(
  text,
  """    if (_advancedPopup is { IsDisposed: false } advancedPopup)
    {
      advancedPopup.ApplyTheme(dark);
      advancedPopup.SetSettings(
        Settings.HighlightQueueCapacity,
        Settings.ShowRolledBackHistory,
        Settings.SpeakUserContext);
    }
""",
  """    if (_advancedPopup is { IsDisposed: false } advancedPopup)
    {
      advancedPopup.ApplyTheme(dark);
      advancedPopup.SetQueueCapacity(Settings.HighlightQueueCapacity);
    }
""",
  "main apply theme")
text = replace_once(
  text,
  """    popup.ApplyTheme(_dark);
    popup.SetSettings(
      Settings.HighlightQueueCapacity,
      Settings.ShowRolledBackHistory,
      Settings.SpeakUserContext);
    PositionAdvancedPopup(popup);
""",
  """    popup.ApplyTheme(_dark);
    popup.SetQueueCapacity(Settings.HighlightQueueCapacity);
    PositionAdvancedPopup(popup);
""",
  "main show advanced")
text = replace_once(
  text,
  """      Settings = (Settings with
      {
        HighlightQueueCapacity = popup.QueueCapacity,
        ShowRolledBackHistory = popup.ShowRolledBackHistory,
        SpeakUserContext = popup.SpeakUserContext
      }).Normalize();
""",
  """      Settings = (Settings with
      {
        HighlightQueueCapacity = popup.QueueCapacity
      }).Normalize();
""",
  "main advanced value handler")
text = replace_once(
  text,
  """      FadeMilliseconds = FadeMillisecondsFromStep(_fadeSlider.Value),
      HighlightUpdateMilliseconds = _trackingSlider.Value * 5,
      HighlightQueueCapacity = Settings.HighlightQueueCapacity
""",
  """      FadeMilliseconds = FadeMillisecondsFromStep(_fadeSlider.Value),
      HighlightUpdateMilliseconds = _trackingSlider.Value * 5,
      HighlightQueueCapacity = Settings.HighlightQueueCapacity,
      ShowRolledBackHistory = _showRolledBackCheckBox.Checked,
      SpeakUserContext = _speakUserContextCheckBox.Checked
""",
  "main value update")
text = replace_once(
  text,
  """  private static void ConfigureValueLabel(Label label)
  {
    label.AutoSize = false;
    label.Dock = DockStyle.Fill;
    label.TextAlign = ContentAlignment.MiddleRight;
  }

""",
  """  private static void ConfigureValueLabel(Label label)
  {
    label.AutoSize = false;
    label.Dock = DockStyle.Fill;
    label.TextAlign = ContentAlignment.MiddleRight;
  }

  private static void ConfigureCheckBox(
    CheckBox checkBox,
    string text,
    string accessibleName,
    string accessibleDescription,
    int tabIndex)
  {
    checkBox.AutoSize = true;
    checkBox.Dock = DockStyle.Fill;
    checkBox.Margin = new Padding(0, 4, 0, 0);
    checkBox.Text = text;
    checkBox.TabIndex = tabIndex;
    checkBox.AccessibleName = accessibleName;
    checkBox.AccessibleDescription = accessibleDescription;
  }

""",
  "main checkbox helper")
main_path.write_text(text, encoding="utf-8", newline="\n")

advanced_path = Path("AgentPanelSpeaker/TranscriptAdvancedSettingsPopup.cs")
text = advanced_path.read_text(encoding="utf-8")
text = replace_once(
  text,
  """  private const string RolledBackDescription =
    "Shows Codex turns that were rolled back or superseded. Historical " +
    "revisions are hidden by default; enabling this includes original, " +
    "superseded, edited, and aborted revision state in the transcript.";
  private const string UserContextDescription =
    "Includes Core-identified User/IDE context in speech before the actual " +
    "User prompt. The context uses the User Context voice profile. This does " +
    "not change whether the context is shown in the transcript.";

""",
  "",
  "advanced descriptions")
text = replace_once(
  text,
  """  private readonly Label _queueCapacityValue = new();
  private readonly CheckBox _showRolledBackCheckBox = new();
  private readonly CheckBox _speakUserContextCheckBox = new();
""",
  """  private readonly Label _queueCapacityValue = new();
""",
  "advanced checkbox fields")
start = text.index("    _showRolledBackCheckBox.AutoSize = true;")
end = text.index("    var scaleLabels = new TableLayoutPanel", start)
text = text[:start] + text[end:]
text = replace_once(text, "      RowCount = 7,\n", "      RowCount = 5,\n", "advanced row count")
text = replace_once(
  text,
  """    _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
""",
  """    _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
""",
  "advanced row styles")
text = replace_once(
  text,
  """    _layout.Controls.Add(scaleLabels, 0, 4);
    _layout.Controls.Add(_showRolledBackCheckBox, 0, 5);
    _layout.Controls.Add(_speakUserContextCheckBox, 0, 6);
""",
  """    _layout.Controls.Add(scaleLabels, 0, 4);
""",
  "advanced controls")
text = replace_once(
  text,
  """    _showRolledBackCheckBox.CheckedChanged += (_, _) => PublishValueChanged();
    _speakUserContextCheckBox.CheckedChanged += (_, _) => PublishValueChanged();

""",
  "",
  "advanced checkbox events")
text = replace_once(
  text,
  """  public int QueueCapacity => _queueCapacitySlider.Value;
  public bool ShowRolledBackHistory => _showRolledBackCheckBox.Checked;
  public bool SpeakUserContext => _speakUserContextCheckBox.Checked;

  public void SetSettings(
    int capacity,
    bool showRolledBackHistory,
    bool speakUserContext)
  {
    _updating = true;
    try
    {
      _queueCapacitySlider.Value = Math.Clamp(capacity, 1, 16);
      _showRolledBackCheckBox.Checked = showRolledBackHistory;
      _speakUserContextCheckBox.Checked = speakUserContext;
      UpdateValueText();
    }
    finally
    {
      _updating = false;
    }
  }

  public void SetQueueCapacity(int capacity)
  {
    SetSettings(capacity, ShowRolledBackHistory, SpeakUserContext);
  }
""",
  """  public int QueueCapacity => _queueCapacitySlider.Value;

  public void SetQueueCapacity(int capacity)
  {
    _updating = true;
    try
    {
      _queueCapacitySlider.Value = Math.Clamp(capacity, 1, 16);
      UpdateValueText();
    }
    finally
    {
      _updating = false;
    }
  }
""",
  "advanced settings API")
advanced_path.write_text(text, encoding="utf-8", newline="\n")

test_path = Path("AgentPanelSpeaker/AdditionalRegressionTestRunner.cs")
text = test_path.read_text(encoding="utf-8")
text = replace_once(
  text,
  """      ("mapping/word-id-round-trip", TestWordIdRoundTrip),
      ("mapping/unknown-word-id-rejected", TestUnknownWordId)
""",
  """      ("mapping/word-id-round-trip", TestWordIdRoundTrip),
      ("mapping/unknown-word-id-rejected", TestUnknownWordId),
      ("ui/transcript-settings-placement", TestTranscriptSettingsPlacement)
""",
  "test registration")
marker = "  private static TranscriptSearchIndex BuildSearchIndex(\n"
test = r'''  private static void TestTranscriptSettingsPlacement()
  {
    using var main = new TranscriptSettingsPopup();
    using var advanced = new TranscriptAdvancedSettingsPopup();

    System.Windows.Forms.CheckBox? rolledBack = Descendants(main)
      .OfType<System.Windows.Forms.CheckBox>()
      .SingleOrDefault(control => string.Equals(
        control.Text,
        "Show rolled-back Codex history",
        StringComparison.Ordinal));
    System.Windows.Forms.CheckBox? userContext = Descendants(main)
      .OfType<System.Windows.Forms.CheckBox>()
      .SingleOrDefault(control => string.Equals(
        control.Text,
        "Speak User / IDE context",
        StringComparison.Ordinal));
    Require(rolledBack is not null,
      "Main Transcript Settings is missing rolled-back-history control.");
    Require(userContext is not null,
      "Main Transcript Settings is missing User/IDE-context control.");

    string[] advancedCheckBoxes = Descendants(advanced)
      .OfType<System.Windows.Forms.CheckBox>()
      .Select(control => control.Text)
      .ToArray();
    Require(!advancedCheckBoxes.Contains(
        "Show rolled-back Codex history",
        StringComparer.Ordinal),
      "Advanced Transcript Settings still contains rolled-back-history control.");
    Require(!advancedCheckBoxes.Contains(
        "Speak User / IDE context",
        StringComparer.Ordinal),
      "Advanced Transcript Settings still contains User/IDE-context control.");

    main.SetSettings(
      TranscriptSettings.Default with
      {
        ShowRolledBackHistory = false,
        SpeakUserContext = false
      },
      dark: false);
    rolledBack!.Checked = true;
    userContext!.Checked = true;
    Require(main.Settings.ShowRolledBackHistory,
      "Moved rolled-back-history control did not update TranscriptSettings.");
    Require(main.Settings.SpeakUserContext,
      "Moved User/IDE-context control did not update TranscriptSettings.");

    advanced.SetQueueCapacity(7);
    Require(advanced.QueueCapacity == 7,
      "Advanced highlight buffering no longer updates its queue capacity.");
  }

  private static IEnumerable<System.Windows.Forms.Control> Descendants(
    System.Windows.Forms.Control root)
  {
    foreach (System.Windows.Forms.Control child in root.Controls)
    {
      yield return child;
      foreach (System.Windows.Forms.Control descendant in Descendants(child))
      {
        yield return descendant;
      }
    }
  }

'''
text = replace_once(text, marker, test + marker, "permanent UI regression")
test_path.write_text(text, encoding="utf-8", newline="\n")
