using System.ComponentModel;
using Cyotek.Windows.Forms;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits one transcript highlight colour in a compact nested overlay.
/// </summary>
internal sealed class TranscriptColourPopup : UserControl
{
  private readonly ColorWheel _wheel = new();
  private readonly Button _previousSwatch = new();
  private readonly Button _currentSwatch = new();
  private bool _updating;
  private bool _dark;

  public TranscriptColourPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(220, 220);
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
    _wheel.Margin = new Padding(8, 2, 8, 2);
    _wheel.TabIndex = 0;
    ConfigureSwatch(_previousSwatch, "Previous highlight colour");
    ConfigureSwatch(_currentSwatch, "Current highlight colour");
    _previousSwatch.TabIndex = 1;
    _currentSwatch.TabStop = false;
    _previousSwatch.Cursor = Cursors.Hand;
    _previousSwatch.Click += (_, _) => RestorePrevious();
    _wheel.ColorChanged += WheelColorChanged;

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
      ColumnCount = 1,
      RowCount = 3,
      Dock = DockStyle.Fill,
      Padding = new Padding(8)
    };
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.Controls.Add(title, 0, 0);
    layout.Controls.Add(_wheel, 0, 1);
    layout.Controls.Add(swatches, 0, 2);
    Controls.Add(layout);

    Paint += PaintBorder;
    WireHoverEvents(this);
  }

  public event EventHandler? ColourChanged;
  public event EventHandler? DismissRequested;
  public event EventHandler? PointerEntered;
  public event EventHandler? PointerLeft;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color Colour => _wheel.Color;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public Color PreviousColour => _previousSwatch.BackColor;

  public void SetColours(Color current, Color previous)
  {
    _updating = true;
    try
    {
      _wheel.Color = current;
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
    Color background = dark
      ? Color.FromArgb(47, 47, 50)
      : Color.FromArgb(250, 250, 250);
    Color foreground = dark
      ? Color.FromArgb(240, 240, 240)
      : Color.FromArgb(24, 24, 24);
    ApplyThemeRecursive(this, background, foreground);
    Invalidate(true);
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
    SetSwatchColour(_currentSwatch, _wheel.Color);
    if (!_updating)
    {
      ColourChanged?.Invoke(this, EventArgs.Empty);
    }
  }

  private void RestorePrevious()
  {
    Color previous = _previousSwatch.BackColor;
    SetSwatchColour(_previousSwatch, _wheel.Color);
    _wheel.Color = previous;
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
    using var pen = new Pen(_dark
      ? Color.FromArgb(105, 105, 110)
      : Color.FromArgb(110, 110, 110));
    Rectangle bounds = ClientRectangle;
    bounds.Width -= 1;
    bounds.Height -= 1;
    eventArgs.Graphics.DrawRectangle(pen, bounds);
  }

  private void WireHoverEvents(Control control)
  {
    control.MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
    control.MouseLeave += (_, _) =>
    {
      if (!IsPointerInside())
      {
        PointerLeft?.Invoke(this, EventArgs.Empty);
      }
    };
    foreach (Control child in control.Controls)
    {
      WireHoverEvents(child);
    }
  }

  private static void ApplyThemeRecursive(
    Control control,
    Color background,
    Color foreground)
  {
    control.BackColor = background;
    control.ForeColor = foreground;
    foreach (Control child in control.Controls)
    {
      ApplyThemeRecursive(child, background, foreground);
    }
  }
}
