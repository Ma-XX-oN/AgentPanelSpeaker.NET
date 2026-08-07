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
  private readonly TabPage _rgbPage = new("RGB / Hex");
  private readonly TabPage _hslPage = new("HSL");
  private readonly Panel _alphaViewport = new();
  private readonly TableLayoutPanel _editorStack = new();
  private readonly TableLayoutPanel _layout = new();
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

    _rgbPage.Padding = new Padding(6);
    _hslPage.Padding = new Padding(6);
    _rgbPage.Controls.Add(_rgbEditor);
    _hslPage.Controls.Add(_hslEditor);

    _modeTabs.Dock = DockStyle.Fill;
    _modeTabs.Margin = Padding.Empty;
    _modeTabs.TabIndex = 1;
    _modeTabs.TabPages.Add(_rgbPage);
    _modeTabs.TabPages.Add(_hslPage);

    _alphaViewport.Dock = DockStyle.Fill;
    _alphaViewport.Margin = new Padding(0, 4, 0, 0);
    _alphaViewport.TabStop = false;
    _alphaViewport.Controls.Add(_alphaEditor);
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
    _editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
    _editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    _editorStack.Controls.Add(_modeTabs, 0, 0);
    _editorStack.Controls.Add(_alphaViewport, 0, 1);

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

    _layout.ColumnCount = 2;
    _layout.RowCount = 3;
    _layout.Dock = DockStyle.Fill;
    _layout.Padding = new Padding(8);
    _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
    _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
    _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    _layout.Controls.Add(title, 0, 0);
    _layout.SetColumnSpan(title, 2);
    _layout.Controls.Add(_wheel, 0, 1);
    _layout.Controls.Add(_editorStack, 1, 1);
    _layout.Controls.Add(swatches, 0, 2);
    _layout.SetColumnSpan(swatches, 2);
    Controls.Add(_layout);

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
  /// Sizes each editor from an unconstrained probe layout, then crops its
  /// viewport to the known Cyotek controls for that editor group.  The popup is
  /// still hidden when this runs, so Control.Visible cannot be used: WinForms
  /// reports descendants of a hidden Form as not visible even when their local
  /// Visible state is enabled.
  /// </summary>
  public void FitToVisibleControls()
  {
    // Width is a design constraint, not something to infer from ColorEditor
    // child bounds: the Cyotek colour bars stretch to whatever probe width we
    // give them, so measuring their right edge would simply feed the probe
    // width back into the popup width.
    int editorColumnWidth = ScaleLogical(300);
    int probeWidth = Math.Max(
      ScaleLogical(260),
      editorColumnWidth - ScaleLogical(18));
    int probeHeight = ScaleLogical(300);

    Rectangle rgbBounds = ProbeEditorGroupBounds(
      _rgbEditor,
      probeWidth,
      probeHeight,
      "rgbHeaderLabel", "rLabel", "rNumericUpDown", "rColorBar",
      "gLabel", "gNumericUpDown", "gColorBar",
      "bLabel", "bNumericUpDown", "bColorBar",
      "hexLabel", "hexTextBox");
    Rectangle hslBounds = ProbeEditorGroupBounds(
      _hslEditor,
      probeWidth,
      probeHeight,
      "hslLabel", "hLabel", "hNumericUpDown", "hColorBar",
      "sLabel", "sNumericUpDown", "sColorBar",
      "lLabel", "lNumericUpDown", "lColorBar");
    Rectangle alphaBounds = ProbeEditorGroupBounds(
      _alphaEditor,
      probeWidth,
      probeHeight,
      "aLabel", "aNumericUpDown", "aColorBar");

    int measuredEditorWidth = Math.Max(
      Math.Max(rgbBounds.Width, hslBounds.Width),
      alphaBounds.Width);
    int tabContentHeight = Math.Max(rgbBounds.Height, hslBounds.Height);
    int tabChrome = ScaleLogical(38);
    int tabHeight = tabContentHeight + tabChrome;
    int alphaHeight = alphaBounds.Height + ScaleLogical(8);
    int editorHeight = tabHeight + alphaHeight;

    _editorStack.RowStyles[0].SizeType = SizeType.Absolute;
    _editorStack.RowStyles[0].Height = tabHeight;
    _editorStack.RowStyles[1].SizeType = SizeType.Absolute;
    _editorStack.RowStyles[1].Height = alphaHeight;

    int rightWidth = editorColumnWidth;
    int leftWidth = ScaleLogical(235);
    int contentHeight = Math.Max(ScaleLogical(240), editorHeight);
    _layout.ColumnStyles[0].SizeType = SizeType.Absolute;
    _layout.ColumnStyles[0].Width = leftWidth;
    _layout.RowStyles[1].SizeType = SizeType.Absolute;
    _layout.RowStyles[1].Height = contentHeight;

    int requiredWidth = ScaleLogical(16) + leftWidth + rightWidth;
    int requiredHeight = ScaleLogical(16 + 28 + 30) + contentHeight;
    Size = new Size(requiredWidth, requiredHeight);
    PerformLayout();

    PlaceEditorInViewport(_rgbEditor, _rgbPage, rgbBounds, probeWidth, probeHeight);
    PlaceEditorInViewport(_hslEditor, _hslPage, hslBounds, probeWidth, probeHeight);
    PlaceEditorInViewport(
      _alphaEditor,
      _alphaViewport,
      alphaBounds,
      probeWidth,
      probeHeight);

    DiagnosticLog.Write("popup.colour_layout", new
    {
      popupSize = Size,
      rgbBounds,
      hslBounds,
      alphaBounds,
      tabHeight,
      alphaHeight,
      measuredEditorWidth,
      editorColumnWidth,
      contentHeight,
      rgbEditor = new { _rgbEditor.Location, _rgbEditor.Size },
      hslEditor = new { _hslEditor.Location, _hslEditor.Size },
      alphaEditor = new { _alphaEditor.Location, _alphaEditor.Size }
    });
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
    editor.Dock = DockStyle.None;
    editor.Margin = Padding.Empty;
    editor.AutoSize = false;
    editor.Orientation = Orientation.Vertical;
    editor.ShowRgb = showRgb;
    editor.ShowHex = showHex;
    editor.ShowHsl = showHsl;
    editor.ShowAlphaChannel = showAlpha;
    editor.PreserveAlphaChannel = true;
  }

  private static Rectangle ProbeEditorGroupBounds(
    ColorEditor editor,
    int width,
    int height,
    params string[] controlNames)
  {
    editor.Size = new Size(width, height);
    editor.PerformLayout();

    var names = new HashSet<string>(controlNames, StringComparer.Ordinal);
    int left = int.MaxValue;
    int top = int.MaxValue;
    int right = int.MinValue;
    int bottom = int.MinValue;
    foreach (Control control in editor.Controls)
    {
      if (!names.Contains(control.Name))
      {
        continue;
      }

      left = Math.Min(left, control.Left);
      top = Math.Min(top, control.Top);
      right = Math.Max(right, control.Right);
      bottom = Math.Max(bottom, control.Bottom);
    }

    if (left == int.MaxValue)
    {
      throw new InvalidOperationException(
        "The Cyotek ColorEditor layout did not contain the expected controls.");
    }
    return Rectangle.FromLTRB(left, top, right, bottom);
  }

  private static void PlaceEditorInViewport(
    ColorEditor editor,
    Control viewport,
    Rectangle contentBounds,
    int probeWidth,
    int probeHeight)
  {
    int inset = viewport is TabPage ? 6 : 0;
    editor.Size = new Size(
      Math.Max(probeWidth, viewport.ClientSize.Width + contentBounds.Left),
      probeHeight);
    editor.Location = new Point(
      inset - contentBounds.Left,
      inset - contentBounds.Top);
    editor.BringToFront();
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
