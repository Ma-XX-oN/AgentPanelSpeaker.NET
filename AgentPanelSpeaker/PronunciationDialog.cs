namespace AgentPanelSpeaker;

/// <summary>
/// Edits spelling and IPA pronunciation rules and provides an IPA toolbar.
/// </summary>
internal sealed class PronunciationDialog : Form
{
  private readonly SpeechService _speech;
  private readonly Func<SpeechProfileSettings> _profileProvider;
  private readonly Func<AudioWakeSettings> _wakeProvider;
  private readonly Action<string> _activity;
  private readonly TextBox _spelledTextBox = new();
  private readonly RichTextBox _pronunciationsTextBox = new();
  private readonly Button _toggleToolbarButton = new();
  private readonly Panel _toolbarPanel = new();
  private readonly TableLayoutPanel _pronunciationLayout = new();
  private readonly Label _ipaInformationLabel = new();
  private readonly Label _validationLabel = new();
  private readonly Button _okButton = new();
  private readonly Button _cancelButton = new();
  private readonly ToolTip _toolTip = new();
  private readonly System.Windows.Forms.Timer _hoverTimer = new();
  private readonly List<Button> _ipaButtons = new();

  private IpaSymbolDefinition? _hoveredSymbol;
  private int _savedSelectionStart;
  private int _savedSelectionLength;
  private bool _toolbarOpen;

  /// <summary>
  /// Initializes the spelling and pronunciation editor.
  /// </summary>
  public PronunciationDialog(
    string spelledWords,
    string pronunciations,
    SpeechService speech,
    Func<SpeechProfileSettings> profileProvider,
    Func<AudioWakeSettings> wakeProvider,
    Action<string> activity,
    AppTheme theme)
  {
    _speech = speech;
    _profileProvider = profileProvider;
    _wakeProvider = wakeProvider;
    _activity = activity;

    Text = "Pronunciations";
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    MinimumSize = new Size(760, 620);
    Size = new Size(980, 820);
    KeyPreview = true;
    KeyDown += PronunciationDialogKeyDown;

    var tabs = new TabControl
    {
      Dock = DockStyle.Fill
    };
    tabs.TabPages.Add(CreateSpellingPage(spelledWords));
    tabs.TabPages.Add(CreatePronunciationPage(pronunciations));

    _validationLabel.AutoSize = true;
    _validationLabel.Dock = DockStyle.Fill;
    _validationLabel.ForeColor = Color.Firebrick;

    _okButton.AutoSize = true;
    _okButton.DialogResult = DialogResult.OK;
    _okButton.Text = "OK";
    _cancelButton.AutoSize = true;
    _cancelButton.DialogResult = DialogResult.Cancel;
    _cancelButton.Text = "Cancel";
    AcceptButton = _okButton;
    CancelButton = _cancelButton;

    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false
    };
    buttons.Controls.Add(_cancelButton);
    buttons.Controls.Add(_okButton);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      Padding = new Padding(10),
      RowCount = 3
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.Controls.Add(tabs, 0, 0);
    layout.Controls.Add(_validationLabel, 0, 1);
    layout.Controls.Add(buttons, 0, 2);
    Controls.Add(layout);

    _hoverTimer.Interval = 1000;
    _hoverTimer.Tick += HoverTimerTick;
    FormClosing += PronunciationDialogClosing;
    ThemeManager.Apply(this, theme);
    UpdateIpaButtonState();
  }

  /// <summary>
  /// Gets the spelling-list editor text.
  /// </summary>
  public string SpelledWordsText => _spelledTextBox.Text;

  /// <summary>
  /// Gets the pronunciation-rule editor text.
  /// </summary>
  public string PronunciationsText => _pronunciationsTextBox.Text;

  /// <summary>
  /// Releases owned timers and tooltips.
  /// </summary>
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _hoverTimer.Dispose();
      _toolTip.Dispose();
    }
    base.Dispose(disposing);
  }

  /// <summary>
  /// Creates the one-token-per-line spelling tab.
  /// </summary>
  private TabPage CreateSpellingPage(string spelledWords)
  {
    var page = new TabPage("Spell out");
    var instructions = new Label
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      Text =
        "Enter one word or token per line. Matching is case-insensitive. " +
        "Each matching token is spoken character by character."
    };
    ConfigureEditor(_spelledTextBox, spelledWords);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      Padding = new Padding(8),
      RowCount = 2
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    layout.Controls.Add(instructions, 0, 0);
    layout.Controls.Add(_spelledTextBox, 0, 1);
    page.Controls.Add(layout);
    return page;
  }

  /// <summary>
  /// Creates the IPA-rule tab and its manually toggled toolbar.
  /// </summary>
  private TabPage CreatePronunciationPage(string pronunciations)
  {
    var page = new TabPage("Pronunciations");
    var instructions = new Label
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      Text =
        "Enter name=ipa:pronunciation for an exact-case match or " +
        "name/i=ipa:pronunciation to ignore case. Matching uses whole tokens."
    };

    ConfigureEditor(_pronunciationsTextBox, pronunciations);
    _pronunciationsTextBox.SelectionChanged += (_, _) =>
    {
      SaveEditorSelection();
      UpdateIpaButtonState();
    };
    _pronunciationsTextBox.TextChanged += (_, _) =>
    {
      SaveEditorSelection();
      UpdateIpaButtonState();
    };
    _pronunciationsTextBox.KeyUp += (_, _) =>
    {
      SaveEditorSelection();
      UpdateIpaButtonState();
    };
    _pronunciationsTextBox.MouseUp += (_, _) =>
    {
      SaveEditorSelection();
      UpdateIpaButtonState();
    };

    _toggleToolbarButton.AutoSize = true;
    _toggleToolbarButton.Text = "Show IPA symbols";
    _toggleToolbarButton.Click += (_, _) => ToggleToolbar();

    _toolbarPanel.AutoScroll = true;
    _toolbarPanel.BorderStyle = BorderStyle.FixedSingle;
    _toolbarPanel.Dock = DockStyle.Fill;
    _toolbarPanel.Visible = false;
    _toolbarPanel.Controls.Add(CreateIpaToolbarContents());

    _ipaInformationLabel.AutoEllipsis = true;
    _ipaInformationLabel.BorderStyle = BorderStyle.FixedSingle;
    _ipaInformationLabel.Dock = DockStyle.Fill;
    _ipaInformationLabel.Font = new Font(
      FontFamily.GenericMonospace,
      10.0f);
    _ipaInformationLabel.MinimumSize = new Size(0, 30);
    _ipaInformationLabel.Padding = new Padding(6);
    _ipaInformationLabel.Text =
      "Hover over an IPA symbol to see its sound and example.";

    _pronunciationLayout.ColumnCount = 1;
    _pronunciationLayout.Dock = DockStyle.Fill;
    _pronunciationLayout.Padding = new Padding(8);
    _pronunciationLayout.RowCount = 5;
    _pronunciationLayout.ColumnStyles.Add(
      new ColumnStyle(SizeType.Percent, 100.0f));
    _pronunciationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _pronunciationLayout.RowStyles.Add(
      new RowStyle(SizeType.Percent, 100.0f));
    _pronunciationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _pronunciationLayout.RowStyles.Add(
      new RowStyle(SizeType.Absolute, 0.0f));
    _pronunciationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _pronunciationLayout.Controls.Add(instructions, 0, 0);
    _pronunciationLayout.Controls.Add(_pronunciationsTextBox, 0, 1);
    _pronunciationLayout.Controls.Add(_toggleToolbarButton, 0, 2);
    _pronunciationLayout.Controls.Add(_toolbarPanel, 0, 3);
    _pronunciationLayout.Controls.Add(_ipaInformationLabel, 0, 4);
    page.Controls.Add(_pronunciationLayout);
    return page;
  }

  /// <summary>
  /// Builds grouped symbol buttons from the IPA catalogue.
  /// </summary>
  private Control CreateIpaToolbarContents()
  {
    var groups = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 1,
      Dock = DockStyle.Top,
      Padding = new Padding(5)
    };
    groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));

    int row = 0;
    foreach (IpaSymbolGroup group in IpaSymbolCatalog.Groups)
    {
      groups.RowCount = row + 2;
      groups.Controls.Add(new Label
      {
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Margin = new Padding(3, 8, 3, 3),
        Text = group.Name
      }, 0, row++);

      var symbols = new FlowLayoutPanel
      {
        AutoSize = true,
        Dock = DockStyle.Fill,
        WrapContents = true
      };
      foreach (IpaSymbolDefinition definition in group.Symbols)
      {
        symbols.Controls.Add(CreateIpaButton(definition));
      }
      groups.Controls.Add(symbols, 0, row++);
    }
    return groups;
  }

  /// <summary>
  /// Creates one symbol insertion and hover-preview button.
  /// </summary>
  private Button CreateIpaButton(IpaSymbolDefinition definition)
  {
    var button = new Button
    {
      AutoSize = false,
      Font = new Font("Segoe UI", 13.0f),
      Height = 34,
      Margin = new Padding(2),
      Tag = definition,
      Text = definition.Symbol,
      Width = Math.Max(38, 18 + definition.Symbol.Length * 14)
    };
    _ipaButtons.Add(button);
    _toolTip.SetToolTip(
      button,
      $"{definition.Description}; " +
      IpaSymbolCatalog.GetCodePoints(definition.Symbol));
    button.MouseDown += (_, _) => SaveEditorSelection();
    button.Click += IpaButtonClicked;
    button.MouseEnter += IpaButtonMouseEnter;
    button.MouseLeave += IpaButtonMouseLeave;
    return button;
  }

  /// <summary>
  /// Opens or closes the IPA toolbar only on explicit user request.
  /// </summary>
  private void ToggleToolbar()
  {
    _toolbarOpen = !_toolbarOpen;
    _toolbarPanel.Visible = _toolbarOpen;
    RowStyle editorStyle = _pronunciationLayout.RowStyles[1];
    editorStyle.SizeType = SizeType.Percent;
    editorStyle.Height = _toolbarOpen ? 55.0f : 100.0f;
    RowStyle toolbarStyle = _pronunciationLayout.RowStyles[3];
    toolbarStyle.SizeType = _toolbarOpen
      ? SizeType.Percent
      : SizeType.Absolute;
    toolbarStyle.Height = _toolbarOpen ? 45.0f : 0.0f;
    _toggleToolbarButton.Text = _toolbarOpen
      ? "Hide IPA symbols"
      : "Show IPA symbols";
  }

  /// <summary>
  /// Inserts one symbol, first adding ipa: after equals when necessary.
  /// </summary>
  private void IpaButtonClicked(object? sender, EventArgs eventArgs)
  {
    if (sender is not Button button ||
        button.Tag is not IpaSymbolDefinition definition ||
        !CanInsertIpaAtSavedSelection())
    {
      return;
    }

    _pronunciationsTextBox.Focus();
    _pronunciationsTextBox.Select(
      _savedSelectionStart,
      _savedSelectionLength);

    string text = _pronunciationsTextBox.Text;
    int caret = _pronunciationsTextBox.SelectionStart;
    int selectionLength = _pronunciationsTextBox.SelectionLength;
    GetCurrentLine(text, caret, out int lineStart, out int lineEnd);
    int equals = text.IndexOf('=', lineStart, lineEnd - lineStart);
    int valueStart = equals + 1;
    if (_savedSelectionStart + _savedSelectionLength > lineEnd)
    {
      return;
    }
    string value = text[valueStart..lineEnd];
    if (!value.StartsWith("ipa:", StringComparison.OrdinalIgnoreCase))
    {
      text = text.Insert(valueStart, "ipa:");
      if (caret >= valueStart)
      {
        caret += 4;
      }
      _pronunciationsTextBox.Text = text;
      _pronunciationsTextBox.Select(caret, selectionLength);
    }

    _pronunciationsTextBox.SelectedText = definition.Symbol;
    SaveEditorSelection();
    UpdateIpaButtonState();
  }

  /// <summary>
  /// Displays symbol information and starts delayed or Shift-immediate preview.
  /// </summary>
  private void IpaButtonMouseEnter(object? sender, EventArgs eventArgs)
  {
    if (sender is not Button button ||
        button.Tag is not IpaSymbolDefinition definition)
    {
      return;
    }

    _hoveredSymbol = definition;
    _ipaInformationLabel.Text =
      $"{definition.Symbol} → {definition.ExampleWord} → " +
      $"/{definition.HighlightedExampleIpa}/ → {definition.Position}";
    _hoverTimer.Stop();
    if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
    {
      PreviewHoveredSymbol();
    }
    else
    {
      _hoverTimer.Start();
    }
  }

  /// <summary>
  /// Cancels a pending hover preview.
  /// </summary>
  private void IpaButtonMouseLeave(object? sender, EventArgs eventArgs)
  {
    _hoverTimer.Stop();
    _hoveredSymbol = null;
  }

  /// <summary>
  /// Starts a pending hover preview immediately when Shift is pressed.
  /// </summary>
  private void PronunciationDialogKeyDown(
    object? sender,
    KeyEventArgs eventArgs)
  {
    if (eventArgs.KeyCode != Keys.ShiftKey || _hoveredSymbol is null)
    {
      return;
    }

    _hoverTimer.Stop();
    PreviewHoveredSymbol();
  }

  /// <summary>
  /// Starts the current one-second hover preview.
  /// </summary>
  private void HoverTimerTick(object? sender, EventArgs eventArgs)
  {
    _hoverTimer.Stop();
    PreviewHoveredSymbol();
  }

  /// <summary>
  /// Plays an isolated phone when possible, then its example word.
  /// </summary>
  private void PreviewHoveredSymbol()
  {
    IpaSymbolDefinition? definition = _hoveredSymbol;
    if (definition is null || _speech.IsSpeaking)
    {
      return;
    }

    SpeechProfileSettings profile = _profileProvider().Normalize();
    if (!profile.IsSpoken)
    {
      _activity("IPA preview unavailable: no spoken voice is selected.");
      return;
    }

    _activity(
      $"IPA preview: {definition.Symbol}; " +
      $"example={definition.ExampleWord}.");
    _speech.PreviewIpa(
      definition.CanSoundAlone ? definition.Symbol : null,
      definition.ExampleWord,
      definition.ExampleIpa,
      profile,
      _wakeProvider());
  }

  /// <summary>
  /// Stores the active editor selection before a toolbar button takes focus.
  /// </summary>
  private void SaveEditorSelection()
  {
    _savedSelectionStart = _pronunciationsTextBox.SelectionStart;
    _savedSelectionLength = _pronunciationsTextBox.SelectionLength;
  }

  /// <summary>
  /// Enables symbols only at a valid pronunciation-value insertion point.
  /// </summary>
  private void UpdateIpaButtonState()
  {
    bool enabled = CanInsertIpaAtSavedSelection();
    foreach (Button button in _ipaButtons)
    {
      button.Enabled = enabled;
    }
  }

  /// <summary>
  /// Tests the exact caret boundary required by the pronunciation syntax.
  /// </summary>
  private bool CanInsertIpaAtSavedSelection()
  {
    string text = _pronunciationsTextBox.Text;
    int caret = Math.Clamp(_savedSelectionStart, 0, text.Length);
    GetCurrentLine(text, caret, out int lineStart, out int lineEnd);
    int equals = text.IndexOf('=', lineStart, lineEnd - lineStart);
    if (equals < 0 || caret <= equals)
    {
      return false;
    }

    int valueStart = equals + 1;
    if (_savedSelectionStart + _savedSelectionLength > lineEnd)
    {
      return false;
    }
    string value = text[valueStart..lineEnd];
    if (!value.StartsWith("ipa:", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    int prefixEnd = valueStart + 4;
    return caret >= prefixEnd &&
      _savedSelectionStart >= prefixEnd;
  }

  /// <summary>
  /// Gets current line bounds with an exclusive line end.
  /// </summary>
  private static void GetCurrentLine(
    string text,
    int caret,
    out int lineStart,
    out int lineEnd)
  {
    int safeCaret = Math.Clamp(caret, 0, text.Length);
    int previousNewline = safeCaret == 0
      ? -1
      : text.LastIndexOf('\n', safeCaret - 1);
    lineStart = previousNewline + 1;
    int nextNewline = text.IndexOf('\n', safeCaret);
    lineEnd = nextNewline < 0 ? text.Length : nextNewline;
  }

  /// <summary>
  /// Prevents invalid pronunciation syntax from being accepted.
  /// </summary>
  private void PronunciationDialogClosing(
    object? sender,
    FormClosingEventArgs eventArgs)
  {
    if (DialogResult != DialogResult.OK)
    {
      return;
    }

    PronunciationRuleSet parsed = PronunciationRuleSet.Parse(
      _pronunciationsTextBox.Text);
    if (parsed.Errors.Count == 0)
    {
      return;
    }

    eventArgs.Cancel = true;
    DialogResult = DialogResult.None;
    _validationLabel.Text = string.Join(Environment.NewLine, parsed.Errors);
  }

  private static void ConfigureEditor(TextBoxBase editor, string text)
  {
    editor.AcceptsTab = false;
    editor.Dock = DockStyle.Fill;
    editor.Font = new Font(FontFamily.GenericMonospace, 10.0f);
    editor.Multiline = true;
    editor.Text = text ?? string.Empty;
    editor.WordWrap = false;
    switch (editor)
    {
      case TextBox textBox:
        textBox.ScrollBars = ScrollBars.Both;
        break;

      case RichTextBox richTextBox:
        richTextBox.ScrollBars = RichTextBoxScrollBars.Both;
        break;
    }
  }
}
