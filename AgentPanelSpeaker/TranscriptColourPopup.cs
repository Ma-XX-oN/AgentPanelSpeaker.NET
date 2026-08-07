using System.ComponentModel;
using Cyotek.Windows.Forms;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits one transcript highlight colour in a compact nested overlay.
/// </summary>
internal sealed class TranscriptColourPopup : PopupFormBase
{
  private readonly ColorWheel _wheel = new();
  private readonly ColorEditor _rgbEditor = new();
  private readonly ColorEditor _hslEditor = new();
  private readonly ColorEditor _alphaEditor = new();
  private readonly TabControl _modeTabs = new();
  private readonly TableLayoutPanel _editorStack = new();
  private readonly Button _previousSwatch = new();
  private readonly Button _currentSwatch = new();
  private Color _colour = Color.Black;
  private bool _updating;
  private bool _dark;

  public TranscriptColourPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    Size = new Size(500, 330);
    TabStop = false;

    var title = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Highlight Colour",
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(
        SystemFonts.MessageBoxFont ?? Control.DefaultFont,
        FontStyle.Bold)
    };

    _wheel.Dock = DockStyle.Fill;
    _wheel.Margin = new Padding(6, 2, 8, 2);
    _wheel.TabIndex = 0;

    ConfigureEditor(_rgbEditor, showRgb: true, showHex: true,
      showHsl: false, showAlpha: false);
    ConfigureEditor(_hslEditor, showRgb: false, showHex: false,
      showHsl: true, showAlpha: false);
    ConfigureEditor(_alphaEditor, showRgb: false, showHex: false,
      showHsl: false, showAlpha: true);

    var rgbPage = new TabPage("RGB / Hex")
    {
      Padding = new Padding(6)
    };
    var hslPage = new TabPage("HSL")
    {
      Padding = new Padding(6)
    };
    rgbPage.Controls.Add(_rgbEditor);
    hslPage.Controls.Add(_hslEditor);

    _modeTabs.Dock = DockStyle.Fill;
    _modeTabs.Margin = Padding.Empty;
    _modeTabs.TabIndex = 1;
    _modeTabs.TabPages.Add(rgbPage);
    _modeTabs.TabPages.Add(hslPage);

    _alphaEditor.Dock = DockStyle.Fill;
    _alphaEditor.Margin = new Padding(0, 4, 0, 0);
    _alphaEditor.TabIndex = 2;

    ConfigureSwatch(_previousSwatch, "Previous highlight colour");
    ConfigureSwatch(_currentSwatch, "Current highlight colour");
    _previousSwatch.TabIndex = 3;
    _currentSwatch.TabStop = false;
    _previousSwatch.Cursor = Cursors.Hand;
    _previousSwatch.Click += (_, _) => RestorePrevious();

    _wheel.ColorChanged += WheelColorChanged;
    _rgbEditor.ColorChanged += RgbEditorColorChanged;
    _hslEditor.ColorChanged += HslEditorColorChanged;
    _alphaEditor.ColorChanged += AlphaEditorColorChanged;

    _editorStack.ColumnCount = 1;
    _editorStack.RowCount = 2;
    _editorStack.Dock = DockStyle.Fill;
    _editorStack.Margin = new Padding(8, 2, 2, 2);
    _editorStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _editorStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    _editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
    _editorStack.Controls.Add(_modeTabs, 0, 0);
    _editorStack.Controls.Add(_alphaEditor, 0, 1);

    var swatches = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Margin = Padding.Empty
    };
    swatches.Controls.Add(CreateInlineLabel("Previous"));
    swatches.Controls.Add(_previousSwatch);
    swatches.Controls.Add(CreateInlineLabel("Current"));
    swatches.Controls.Add(_currentSwatch);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 3,
      Dock = DockStyle.Fill,
      Padding = new Padding(8)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 2);
    layout.Controls.Add(_wheel, 0, 1);
    layout.Controls.Add(_editorStack, 1, 1);
    layout.Controls.Add(swatches, 0, 2);
    layout.SetColumnSpan(swatches, 2);
    Controls.Add(layout);

    Paint += PaintBorder;
  }

  public event EventHandler? ColourChanged;
  public event EventHandler? DismissRequested;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color Colour => _colour;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color PreviousColour => _previousSwatch.BackColor;

  public void SetColours(Color current, Color previous)
  {
    _updating = true;
    try
    {
      _colour = current;
      SynchronizeControls(current);
      SetSwatchColour(_currentSwatch, current);
      SetSwatchColour(_previousSwatch, previous);
    }
    finally
    {
      _updating = false;
    }
  }

  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    ThemeManager.ApplyPopup(this, dark);
  }

  /// <summary>
  /// Sizes the popup from the actual scaled bounds of both tabbed editors and
  /// the shared alpha editor so no active colour control can be clipped.
  /// </summary>
  public void FitToVisibleControls()
  {
    PerformLayout();

    int selectedIndex = _modeTabs.SelectedIndex;
    int editorWidth = 0;
    int editorHeight = 0;
    for (int index = 0; index < _modeTabs.TabPages.Count; index++)
    {
      _modeTabs.SelectedIndex = index;
      _modeTabs.PerformLayout();
      ColorEditor editor = index == 0 ? _rgbEditor : _hslEditor;
      editor.PerformLayout();
      Size extent = GetVisibleEditorExtent(editor);
      editorWidth = Math.Max(editorWidth, extent.Width);
      editorHeight = Math.Max(editorHeight, extent.Height);
    }
    _modeTabs.SelectedIndex = Math.Max(0, selectedIndex);

    _alphaEditor.PerformLayout();
    Size alphaExtent = GetVisibleEditorExtent(_alphaEditor);
    editorWidth = Math.Max(editorWidth, alphaExtent.Width);

    int tabHeaderAllowance = ScaleLogical(30);
    int alphaRowHeight = alphaExtent.Height + ScaleLogical(8);
    _editorStack.RowStyles[1].SizeType = SizeType.Absolute;
    _editorStack.RowStyles[1].Height = alphaRowHeight;

    int editorColumnWidth = editorWidth + ScaleLogical(24);
    int innerWidth = (int)Math.Ceiling(editorColumnWidth / 0.60);
    int requiredWidth = ScaleLogical(16) + innerWidth;

    int modeHeight = editorHeight + tabHeaderAllowance + ScaleLogical(16);
    _editorStack.RowStyles[0].SizeType = SizeType.Absolute;
    _editorStack.RowStyles[0].Height = modeHeight;

    int wheelHeight = Math.Max(_wheel.PreferredSize.Height, _wheel.MinimumSize.Height);
    int contentHeight = Math.Max(modeHeight + alphaRowHeight, wheelHeight);
    int requiredHeight = ScaleLogical(16 + 28 + 30) + contentHeight;

    Size = new Size(
      Math.Max(ScaleLogical(500), requiredWidth),
      Math.Max(ScaleLogical(300), requiredHeight));
    PerformLayout();
  }

  public void FocusInitialControl()
  {
    _wheel.Focus();
  }

  public bool IsPointerInside()
  {
    return ClientRectangle.Contains(PointToClient(Cursor.Position));
  }


  protected override bool ShowWithoutActivation => true;

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalPopupKey(keyData, this))
    {
      return true;
    }

    if (keyData == Keys.Escape || keyData == (Keys.Alt | Keys.F4))
    {
      DismissRequested?.Invoke(this, EventArgs.Empty);
      return true;
    }
    if (keyData == Keys.Tab && _previousSwatch.ContainsFocus)
    {
      DismissRequested?.Invoke(this, EventArgs.Empty);
      return true;
    }
    if (keyData == (Keys.Shift | Keys.Tab) && _wheel.ContainsFocus)
    {
      DismissRequested?.Invoke(this, EventArgs.Empty);
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private void WheelColorChanged(object? sender, EventArgs eventArgs)
  {
    Color wheelColour = _wheel.Color;
    SetColour(Color.FromArgb(
      _colour.A,
      wheelColour.R,
      wheelColour.G,
      wheelColour.B));
  }

  private void RgbEditorColorChanged(object? sender, EventArgs eventArgs)
  {
    Color edited = _rgbEditor.Color;
    SetColour(Color.FromArgb(_colour.A, edited.R, edited.G, edited.B));
  }

  private void HslEditorColorChanged(object? sender, EventArgs eventArgs)
  {
    Color edited = _hslEditor.Color;
    SetColour(Color.FromArgb(_colour.A, edited.R, edited.G, edited.B));
  }

  private void AlphaEditorColorChanged(object? sender, EventArgs eventArgs)
  {
    SetColour(Color.FromArgb(
      _alphaEditor.Color.A,
      _colour.R,
      _colour.G,
      _colour.B));
  }

  private void SetColour(Color colour)
  {
    if (_updating)
    {
      return;
    }

    _updating = true;
    try
    {
      _colour = colour;
      SynchronizeControls(colour);
      SetSwatchColour(_currentSwatch, colour);
    }
    finally
    {
      _updating = false;
    }
    ColourChanged?.Invoke(this, EventArgs.Empty);
  }

  private void SynchronizeControls(Color colour)
  {
    _wheel.Alpha = colour.A / 255.0;
    _wheel.Color = colour;
    _rgbEditor.Color = colour;
    _hslEditor.Color = colour;
    _alphaEditor.Color = colour;
  }

  private void RestorePrevious()
  {
    Color previous = _previousSwatch.BackColor;
    Color current = _colour;
    SetSwatchColour(_previousSwatch, current);
    SetColour(previous);
  }

  private static void ConfigureEditor(
    ColorEditor editor,
    bool showRgb,
    bool showHex,
    bool showHsl,
    bool showAlpha)
  {
    editor.Dock = DockStyle.Fill;
    editor.Margin = Padding.Empty;
    editor.Orientation = Orientation.Vertical;
    editor.ShowRgb = showRgb;
    editor.ShowHex = showHex;
    editor.ShowHsl = showHsl;
    editor.ShowAlphaChannel = showAlpha;
    editor.PreserveAlphaChannel = true;
  }

  private static Size GetVisibleEditorExtent(ColorEditor editor)
  {
    int right = 0;
    int bottom = 0;
    MeasureVisibleDescendants(editor, editor, ref right, ref bottom);
    return new Size(
      Math.Max(1, right + 4),
      Math.Max(1, bottom + 4));
  }

  private static void MeasureVisibleDescendants(
    Control root,
    Control parent,
    ref int right,
    ref int bottom)
  {
    foreach (Control control in parent.Controls)
    {
      if (!control.Visible)
      {
        continue;
      }

      int x = control.Left;
      int y = control.Top;
      for (Control? ancestor = control.Parent;
           ancestor is not null && !ReferenceEquals(ancestor, root);
           ancestor = ancestor.Parent)
      {
        x += ancestor.Left;
        y += ancestor.Top;
      }

      right = Math.Max(right, x + control.Width);
      bottom = Math.Max(bottom, y + control.Height);
      MeasureVisibleDescendants(root, control, ref right, ref bottom);
    }
  }

  private int ScaleLogical(int value)
  {
    return (int)Math.Ceiling(value * DeviceDpi / 96.0);
  }

  private static void ConfigureSwatch(Button swatch, string name)
  {
    swatch.Size = new Size(46, 20);
    swatch.Margin = new Padding(4, 4, 8, 2);
    swatch.AccessibleName = name;
    swatch.TabStop = true;
    swatch.Text = string.Empty;
    swatch.FlatStyle = FlatStyle.Flat;
    swatch.FlatAppearance.BorderSize = 1;
    swatch.UseVisualStyleBackColor = false;
  }

  private static void SetSwatchColour(Button swatch, Color colour)
  {
    swatch.BackColor = colour;
    swatch.FlatAppearance.MouseOverBackColor = colour;
    swatch.FlatAppearance.MouseDownBackColor = colour;
  }

  private static Label CreateInlineLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Margin = new Padding(0, 6, 0, 0),
      Text = text
    };
  }

  private void PaintBorder(object? sender, PaintEventArgs eventArgs)
  {
    using var pen = new Pen(ThemeManager.GetBorder(_dark));
    Rectangle bounds = ClientRectangle;
    bounds.Width -= 1;
    bounds.Height -= 1;
    eventArgs.Graphics.DrawRectangle(pen, bounds);
  }
}
