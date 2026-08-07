using System.ComponentModel;
using Cyotek.Windows.Forms;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits one transcript highlight colour in a compact nested overlay.
/// </summary>
internal sealed class TranscriptColourPopup : UserControl
{
  private readonly ColorWheel _wheel = new();
  private readonly ColorEditor _editor = new();
  private readonly Button _previousSwatch = new();
  private readonly Button _currentSwatch = new();
  private bool _updating;
  private bool _dark;

  public TranscriptColourPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(430, 330);
    TabStop = false;
    Visible = false;

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
    _editor.Dock = DockStyle.Fill;
    _editor.Margin = new Padding(8, 2, 2, 2);
    _editor.Orientation = Orientation.Vertical;
    _editor.TabIndex = 1;
    ConfigureSwatch(_previousSwatch, "Previous highlight colour");
    ConfigureSwatch(_currentSwatch, "Current highlight colour");
    _previousSwatch.TabIndex = 2;
    _currentSwatch.TabStop = false;
    _previousSwatch.Cursor = Cursors.Hand;
    _previousSwatch.Click += (_, _) => RestorePrevious();
    _wheel.ColorChanged += WheelColorChanged;
    _editor.ColorChanged += EditorColorChanged;

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
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 2);
    layout.Controls.Add(_wheel, 0, 1);
    layout.Controls.Add(_editor, 1, 1);
    layout.Controls.Add(swatches, 0, 2);
    layout.SetColumnSpan(swatches, 2);
    Controls.Add(layout);

    Paint += PaintBorder;
  }

  public event EventHandler? ColourChanged;
  public event EventHandler? DismissRequested;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color Colour => _editor.Color;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color PreviousColour => _previousSwatch.BackColor;

  public void SetColours(Color current, Color previous)
  {
    _updating = true;
    try
    {
      _wheel.Color = current;
      _editor.Color = current;
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
  /// Sizes the popup from the actual scaled bounds of every visible editor
  /// control so that keyboard-focusable fields are never clipped.
  /// </summary>
  public void FitToVisibleControls()
  {
    PerformLayout();
    _editor.PerformLayout();

    int editorRight = 0;
    int editorBottom = 0;
    foreach (Control control in _editor.Controls)
    {
      if (!control.Visible)
      {
        continue;
      }
      editorRight = Math.Max(editorRight, control.Right);
      editorBottom = Math.Max(editorBottom, control.Bottom);
    }

    int editorWidth = Math.Max(_editor.PreferredSize.Width, editorRight + 4);
    int editorHeight = Math.Max(_editor.PreferredSize.Height, editorBottom + 4);
    int editorColumnWidth = editorWidth + _editor.Margin.Horizontal;
    int innerWidth = (int)Math.Ceiling(editorColumnWidth / 0.58);
    int requiredWidth = 16 + innerWidth;

    int wheelHeight = Math.Max(_wheel.PreferredSize.Height, _wheel.MinimumSize.Height);
    int contentHeight = Math.Max(
      editorHeight + _editor.Margin.Vertical,
      wheelHeight + _wheel.Margin.Vertical);
    int requiredHeight = 16 + 28 + contentHeight + 30;

    Size = new Size(
      Math.Max(430, requiredWidth),
      Math.Max(330, requiredHeight));
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
    SynchronizeColour(_wheel.Color, updateWheel: false);
  }

  private void EditorColorChanged(object? sender, EventArgs eventArgs)
  {
    SynchronizeColour(_editor.Color, updateWheel: true);
  }

  private void SynchronizeColour(Color colour, bool updateWheel)
  {
    if (_updating)
    {
      return;
    }

    _updating = true;
    try
    {
      if (updateWheel)
      {
        _wheel.Color = colour;
      }
      if (_editor.Color != colour)
      {
        _editor.Color = colour;
      }
      SetSwatchColour(_currentSwatch, colour);
    }
    finally
    {
      _updating = false;
    }
    ColourChanged?.Invoke(this, EventArgs.Empty);
  }

  private void RestorePrevious()
  {
    Color previous = _previousSwatch.BackColor;
    Color current = _editor.Color;
    SetSwatchColour(_previousSwatch, current);
    SynchronizeColour(previous, updateWheel: true);
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
