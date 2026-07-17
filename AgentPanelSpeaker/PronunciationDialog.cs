using System.Globalization;
using System.Text;

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
  private readonly Button _pronounceButton = new();
  private readonly Panel _toolbarPanel = new();
  private readonly SplitContainer _pronunciationSplitContainer = new();
  private readonly RichTextBox _ipaInformationBox = new();
  private readonly Label _validationLabel = new();
  private readonly Button _okButton = new();
  private readonly Button _cancelButton = new();
  private readonly ToolTip _toolTip = new();
  private readonly System.Windows.Forms.Timer _hoverTimer = new();
  private readonly System.Windows.Forms.Timer _informationTimer = new();
  private readonly List<Button> _ipaButtons = new();

  private static readonly string[] IpaInformationSentences =
  {
    "Hover over an IPA symbol to see its sound and example.",
    "Press Shift while hovering for an immediate sound example."
  };

  private const int InformationRotationMilliseconds = 7000;

  private IpaSymbolDefinition? _hoveredSymbol;
  private int _savedSelectionStart;
  private int _savedSelectionLength;
  private int _informationSentenceIndex;
  private int _savedToolbarHeight = 300;
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
    _informationTimer.Interval = InformationRotationMilliseconds;
    _informationTimer.Tick += InformationTimerTick;
    _informationTimer.Start();
    _speech.SpeakingStateChanged += SpeechSpeakingStateChanged;
    FormClosing += PronunciationDialogClosing;
    ThemeManager.Apply(this, theme);
    RefreshIdleInformationText();
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
      _speech.SpeakingStateChanged -= SpeechSpeakingStateChanged;
      _hoverTimer.Dispose();
      _informationTimer.Dispose();
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

    _pronounceButton.AutoSize = true;
    _pronounceButton.Text = "Pronounce";
    _pronounceButton.MouseDown += (_, _) => SaveEditorSelection();
    _pronounceButton.Click += PronounceButtonClicked;

    var pronunciationButtons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    pronunciationButtons.Controls.Add(_toggleToolbarButton);
    pronunciationButtons.Controls.Add(_pronounceButton);

    _toolbarPanel.AutoScroll = true;
    _toolbarPanel.BorderStyle = BorderStyle.FixedSingle;
    _toolbarPanel.Dock = DockStyle.Fill;
    _toolbarPanel.Controls.Add(CreateIpaToolbarContents());

    _ipaInformationBox.AutoWordSelection = false;
    _ipaInformationBox.BorderStyle = BorderStyle.FixedSingle;
    _ipaInformationBox.Cursor = Cursors.Default;
    _ipaInformationBox.DetectUrls = false;
    _ipaInformationBox.Dock = DockStyle.Fill;
    _ipaInformationBox.Font = new Font(
      FontFamily.GenericMonospace,
      10.0f);
    _ipaInformationBox.Multiline = true;
    _ipaInformationBox.ReadOnly = true;
    _ipaInformationBox.ScrollBars = RichTextBoxScrollBars.None;
    _ipaInformationBox.ShortcutsEnabled = false;
    _ipaInformationBox.TabStop = false;
    _ipaInformationBox.WordWrap = false;
    _ipaInformationBox.SizeChanged += (_, _) =>
      RefreshIdleInformationText();
    SetIpaInformationText(IpaInformationSentences[0]);

    var editorAndButtons = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      RowCount = 2
    };
    editorAndButtons.ColumnStyles.Add(
      new ColumnStyle(SizeType.Percent, 100.0f));
    editorAndButtons.RowStyles.Add(
      new RowStyle(SizeType.Percent, 100.0f));
    editorAndButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    editorAndButtons.Controls.Add(_pronunciationsTextBox, 0, 0);
    editorAndButtons.Controls.Add(pronunciationButtons, 0, 1);

    _pronunciationSplitContainer.Dock = DockStyle.Fill;
    _pronunciationSplitContainer.FixedPanel = FixedPanel.Panel2;
    _pronunciationSplitContainer.IsSplitterFixed = false;
    _pronunciationSplitContainer.Orientation = Orientation.Horizontal;
    _pronunciationSplitContainer.Panel1MinSize = 150;
    _pronunciationSplitContainer.Panel2MinSize = 120;
    _pronunciationSplitContainer.Panel2Collapsed = true;
    _pronunciationSplitContainer.SplitterWidth = 6;
    _pronunciationSplitContainer.Panel1.Controls.Add(editorAndButtons);
    _pronunciationSplitContainer.Panel2.Controls.Add(_toolbarPanel);
    _pronunciationSplitContainer.SplitterMoved += (_, _) =>
    {
      if (!_pronunciationSplitContainer.Panel2Collapsed)
      {
        _savedToolbarHeight =
          _pronunciationSplitContainer.Panel2.ClientSize.Height;
      }
    };

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      Padding = new Padding(8),
      RowCount = 3
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40.0f));
    layout.Controls.Add(instructions, 0, 0);
    layout.Controls.Add(_pronunciationSplitContainer, 0, 1);
    layout.Controls.Add(_ipaInformationBox, 0, 2);
    page.Controls.Add(layout);
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
    var font = new Font("Segoe UI", 14.0f);
    Size textSize = TextRenderer.MeasureText(
      definition.Symbol,
      font,
      Size.Empty,
      TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    var button = new GlyphButton
    {
      AutoSize = false,
      Font = font,
      Glyph = definition.Symbol,
      Height = Math.Max(42, textSize.Height + 14),
      Margin = new Padding(2),
      Tag = definition,
      Text = string.Empty,
      Width = Math.Max(42, textSize.Width + 18)
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
    if (_toolbarOpen)
    {
      _pronunciationSplitContainer.Panel2Collapsed = false;
      BeginInvoke((Action)RestoreToolbarHeight);
    }
    else
    {
      _savedToolbarHeight =
        _pronunciationSplitContainer.Panel2.ClientSize.Height;
      _pronunciationSplitContainer.Panel2Collapsed = true;
      ResumeInformationRotation();
    }
    _toggleToolbarButton.Text = _toolbarOpen
      ? "Hide IPA symbols"
      : "Show IPA symbols";
  }

  /// <summary>
  /// Restores the user-sized toolbar height after its panel is reopened.
  /// </summary>
  private void RestoreToolbarHeight()
  {
    if (!_toolbarOpen || _pronunciationSplitContainer.Panel2Collapsed)
    {
      return;
    }

    int totalHeight = _pronunciationSplitContainer.ClientSize.Height;
    int maximumToolbarHeight =
      totalHeight -
      _pronunciationSplitContainer.SplitterWidth -
      _pronunciationSplitContainer.Panel1MinSize;
    if (maximumToolbarHeight < _pronunciationSplitContainer.Panel2MinSize)
    {
      return;
    }

    int toolbarHeight = Math.Clamp(
      _savedToolbarHeight,
      _pronunciationSplitContainer.Panel2MinSize,
      maximumToolbarHeight);
    _pronunciationSplitContainer.SplitterDistance =
      totalHeight -
      _pronunciationSplitContainer.SplitterWidth -
      toolbarHeight;
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
        !button.Enabled ||
        button.Tag is not IpaSymbolDefinition definition)
    {
      return;
    }

    _informationTimer.Stop();
    _hoveredSymbol = definition;
    SetIpaInformationText(definition);
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
  /// Cancels a pending hover preview and restores idle instructions.
  /// </summary>
  private void IpaButtonMouseLeave(object? sender, EventArgs eventArgs)
  {
    _hoverTimer.Stop();
    _hoveredSymbol = null;
    ResumeInformationRotation();
  }

  /// <summary>
  /// Alternates idle instructions when both do not fit on one line.
  /// </summary>
  private void InformationTimerTick(object? sender, EventArgs eventArgs)
  {
    if (_hoveredSymbol is not null)
    {
      return;
    }

    _informationSentenceIndex =
      (_informationSentenceIndex + 1) % IpaInformationSentences.Length;
    RefreshIdleInformationText();
  }

  /// <summary>
  /// Restores idle instructions after the pointer leaves an active key.
  /// </summary>
  private void ResumeInformationRotation()
  {
    _hoverTimer.Stop();
    _hoveredSymbol = null;
    RefreshIdleInformationText();
  }

  /// <summary>
  /// Shows both idle instructions when space permits, otherwise rotates them.
  /// </summary>
  private void RefreshIdleInformationText()
  {
    if (_hoveredSymbol is not null || _ipaInformationBox.IsDisposed)
    {
      return;
    }

    string combined = string.Join("  ", IpaInformationSentences);
    Size combinedSize = TextRenderer.MeasureText(
      combined,
      _ipaInformationBox.Font,
      Size.Empty,
      TextFormatFlags.NoPadding |
      TextFormatFlags.NoPrefix |
      TextFormatFlags.SingleLine);
    int availableWidth = Math.Max(
      0,
      _ipaInformationBox.ClientSize.Width - 12);

    _informationTimer.Stop();
    if (availableWidth >= combinedSize.Width)
    {
      SetIpaInformationText(combined);
      return;
    }

    SetIpaInformationText(
      IpaInformationSentences[_informationSentenceIndex]);
    _informationTimer.Start();
  }

  /// <summary>
  /// Shows one symbol using a consistent isolated-and-example layout.
  /// </summary>
  private void SetIpaInformationText(IpaSymbolDefinition definition)
  {
    ArgumentNullException.ThrowIfNull(definition);

    var text = new StringBuilder();
    var emphasizedRanges = new List<TextRange>();
    string displaySymbol =
      IpaSymbolCatalog.GetDisplaySymbol(definition.Symbol);
    AppendEmphasizedText(text, emphasizedRanges, displaySymbol);
    text.Append(" → ");

    if (definition.StandaloneIpa is not null)
    {
      text.Append("isolated /");
      AppendIpaText(
        text,
        emphasizedRanges,
        definition.StandaloneIpa,
        definition.Symbol);
      text.Append("/ → ");
    }

    text.Append(definition.ExampleWord);
    text.Append(" → /");
    AppendIpaText(
      text,
      emphasizedRanges,
      definition.ExampleIpa,
      definition.Symbol);
    text.Append("/ → ");
    text.Append(definition.Position);

    SetIpaInformationText(text.ToString(), emphasizedRanges);
  }

  /// <summary>
  /// Appends text and records the complete appended range for emphasis.
  /// </summary>
  private static void AppendEmphasizedText(
    StringBuilder text,
    ICollection<TextRange> emphasizedRanges,
    string value)
  {
    int start = text.Length;
    text.Append(value);
    emphasizedRanges.Add(new TextRange(start, value.Length));
  }

  /// <summary>
  /// Appends IPA and records every occurrence of the selected symbol.
  /// </summary>
  private static void AppendIpaText(
    StringBuilder text,
    ICollection<TextRange> emphasizedRanges,
    string ipa,
    string symbol)
  {
    int textStart = text.Length;
    text.Append(ipa);
    foreach (TextRange range in GetIpaEmphasisRanges(ipa, symbol))
    {
      emphasizedRanges.Add(
        new TextRange(textStart + range.Start, range.Length));
    }
  }

  /// <summary>
  /// Finds every selected symbol and includes its carrier when necessary.
  /// </summary>
  private static IEnumerable<TextRange> GetIpaEmphasisRanges(
    string ipa,
    string symbol)
  {
    int searchStart = 0;
    while (searchStart < ipa.Length)
    {
      int symbolStart = ipa.IndexOf(
        symbol,
        searchStart,
        StringComparison.Ordinal);
      if (symbolStart < 0)
      {
        yield break;
      }

      int start = symbolStart;
      int end = symbolStart + symbol.Length;
      if (symbol is "͡" or "͜")
      {
        start = PreviousRuneStart(ipa, start);
        end = NextRuneEnd(ipa, end);
      }
      else
      {
        UnicodeCategory category =
          CharUnicodeInfo.GetUnicodeCategory(symbol, 0);
        if (category is
          UnicodeCategory.NonSpacingMark or
          UnicodeCategory.SpacingCombiningMark or
          UnicodeCategory.EnclosingMark)
        {
          start = PreviousRuneStart(ipa, start);
        }
        else if (category == UnicodeCategory.ModifierLetter)
        {
          if (IsPrefixModifier(symbol))
          {
            end = NextRuneEnd(ipa, end);
          }
          else
          {
            start = PreviousRuneStart(ipa, start);
          }
        }
      }

      yield return new TextRange(start, end - start);
      searchStart = symbolStart + Math.Max(1, symbol.Length);
    }
  }

  /// <summary>
  /// Returns whether a modifier visually belongs to the following phone.
  /// </summary>
  private static bool IsPrefixModifier(string symbol)
  {
    return symbol is "ˈ" or "ˌ" or "ꜛ" or "ꜜ" or "↗" or "↘";
  }

  /// <summary>
  /// Finds the UTF-16 start of the rune immediately before an index.
  /// </summary>
  private static int PreviousRuneStart(string text, int index)
  {
    if (index <= 0)
    {
      return 0;
    }

    int start = index - 1;
    if (start > 0 &&
        char.IsLowSurrogate(text[start]) &&
        char.IsHighSurrogate(text[start - 1]))
    {
      start--;
    }

    return start;
  }

  /// <summary>
  /// Finds the UTF-16 end of the rune immediately after an index.
  /// </summary>
  private static int NextRuneEnd(string text, int index)
  {
    if (index >= text.Length)
    {
      return text.Length;
    }

    int end = index + 1;
    if (char.IsHighSurrogate(text[index]) &&
        end < text.Length &&
        char.IsLowSurrogate(text[end]))
    {
      end++;
    }

    return end;
  }

  /// <summary>
  /// Replaces the information line and bolds every requested range.
  /// </summary>
  private void SetIpaInformationText(
    string text,
    IReadOnlyCollection<TextRange>? emphasizedRanges = null)
  {
    ArgumentNullException.ThrowIfNull(text);
    if (_ipaInformationBox.IsDisposed)
    {
      return;
    }

    _ipaInformationBox.SuspendLayout();
    try
    {
      _ipaInformationBox.Text = text;
      _ipaInformationBox.Select(0, text.Length);
      _ipaInformationBox.SelectionColor = _ipaInformationBox.ForeColor;
      _ipaInformationBox.SelectionFont = _ipaInformationBox.Font;

      if (emphasizedRanges is not null && emphasizedRanges.Count > 0)
      {
        using var emphasizedFont = new Font(
          _ipaInformationBox.Font.FontFamily,
          _ipaInformationBox.Font.Size + 1.0f,
          FontStyle.Bold,
          _ipaInformationBox.Font.Unit);
        foreach (TextRange range in emphasizedRanges)
        {
          int start = Math.Clamp(range.Start, 0, text.Length);
          int length = Math.Clamp(range.Length, 0, text.Length - start);
          if (length == 0)
          {
            continue;
          }

          _ipaInformationBox.Select(start, length);
          _ipaInformationBox.SelectionFont = emphasizedFont;
        }
      }

      _ipaInformationBox.Select(0, 0);
    }
    finally
    {
      _ipaInformationBox.ResumeLayout();
    }
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
      definition.StandaloneIpa,
      definition.ExampleWord,
      definition.ExampleIpa,
      profile,
      _wakeProvider());
  }

  /// <summary>
  /// Pronounces the rule on the caret's current line.
  /// </summary>
  private void PronounceButtonClicked(object? sender, EventArgs eventArgs)
  {
    if (_speech.IsSpeaking ||
        !TryGetCurrentPronunciationPreview(
          out string token,
          out string? ipa))
    {
      return;
    }

    SpeechProfileSettings profile = _profileProvider().Normalize();
    if (!profile.IsSpoken)
    {
      _activity(
        "Pronunciation preview unavailable: no spoken voice is selected.");
      return;
    }

    if (ipa is null)
    {
      _activity(
        $"Pronunciation preview: {token}; standard voice pronunciation.");
      _speech.PreviewText(token, profile, _wakeProvider());
    }
    else
    {
      _activity($"Pronunciation preview: {token}; /{ipa}/.");
      _speech.PreviewIpa(
        isolatedIpa: null,
        token,
        ipa,
        profile,
        _wakeProvider());
    }
    UpdatePronounceButtonState();
  }

  /// <summary>
  /// Reads the preview token and optional IPA value from the caret line.
  /// </summary>
  private bool TryGetCurrentPronunciationPreview(
    out string token,
    out string? ipa)
  {
    string text = _pronunciationsTextBox.Text;
    int caret = Math.Clamp(_savedSelectionStart, 0, text.Length);
    GetCurrentLine(text, caret, out int lineStart, out int lineEnd);
    string line = text[lineStart..lineEnd].Trim();
    int equals = line.IndexOf('=');
    if (equals <= 0)
    {
      token = string.Empty;
      ipa = null;
      return false;
    }

    string left = line[..equals].Trim();
    if (left.EndsWith("/i", StringComparison.OrdinalIgnoreCase))
    {
      left = left[..^2].Trim();
    }
    if (left.Length == 0)
    {
      token = string.Empty;
      ipa = null;
      return false;
    }

    token = left;
    string right = line[(equals + 1)..].Trim();
    if (!right.StartsWith("ipa:", StringComparison.OrdinalIgnoreCase))
    {
      ipa = null;
      return true;
    }

    ipa = right[4..].Trim();
    return ipa.Length != 0;
  }

  /// <summary>
  /// Refreshes the Pronounce button when speech starts or stops.
  /// </summary>
  private void SpeechSpeakingStateChanged(bool speaking)
  {
    if (IsDisposed || Disposing || !IsHandleCreated)
    {
      return;
    }

    if (InvokeRequired)
    {
      try
      {
        BeginInvoke((Action)UpdatePronounceButtonState);
      }
      catch (InvalidOperationException)
      {
      }
      return;
    }
    UpdatePronounceButtonState();
  }

  /// <summary>
  /// Enables Pronounce for a current-line token while speech is idle.
  /// </summary>
  private void UpdatePronounceButtonState()
  {
    _pronounceButton.Enabled =
      !_speech.IsSpeaking &&
      TryGetCurrentPronunciationPreview(out _, out _);
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
    UpdatePronounceButtonState();
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

  /// <summary>
  /// Describes one rich-text emphasis range.
  /// </summary>
  private readonly record struct TextRange(int Start, int Length);

}
