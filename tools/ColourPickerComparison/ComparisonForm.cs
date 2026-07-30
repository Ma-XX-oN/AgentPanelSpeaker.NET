using Cyotek.Windows.Forms;
using System.Globalization;

namespace ColourPickerComparison;

internal sealed class ComparisonForm : Form
{
  private readonly TabControl _tabs = new();
  private readonly ColorWheel _wheel = new();
  private readonly ColorEditor _editor = new();
  private readonly Panel _selectedSwatch = new();
  private readonly Label _selectedValue = new();
  private readonly Button _copyButton = new();
  private readonly TrackBar[] _sliders = new TrackBar[4];
  private readonly NumericUpDown[] _numbers = new NumericUpDown[4];
  private readonly Panel _dialogSwatch = new();
  private Color _selected = Color.FromArgb(255, 61, 83, 132);
  private bool _updating;

  public ComparisonForm()
  {
    Text = "Highlight Colour Picker Comparison";
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(880, 650);
    Size = new Size(1040, 760);
    AutoScaleMode = AutoScaleMode.Dpi;

    var description = new Label
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      Padding = new Padding(8, 8, 8, 4),
      Text =
        "Each tab selects the same colour using a different interface.  " +
        "The selected ARGB value is shown at the bottom."
    };

    _tabs.Dock = DockStyle.Fill;
    _tabs.TabPages.Add(CreateCurrentPickerTab());
    _tabs.TabPages.Add(CreatePaletteTab());
    _tabs.TabPages.Add(CreateSliderTab());
    _tabs.TabPages.Add(CreateWindowsDialogTab());

    ConfigureSwatch(_selectedSwatch);
    _selectedSwatch.Size = new Size(92, 34);
    _selectedValue.AutoSize = true;
    _selectedValue.Margin = new Padding(12, 8, 12, 0);
    _copyButton.AutoSize = true;
    _copyButton.Text = "Copy ARGB";
    _copyButton.Click += (_, _) => Clipboard.SetText(_selectedValue.Text);

    var footer = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Dock = DockStyle.Fill,
      Padding = new Padding(8),
      WrapContents = false
    };
    footer.Controls.Add(new Label
    {
      AutoSize = true,
      Margin = new Padding(0, 8, 8, 0),
      Text = "Selected"
    });
    footer.Controls.Add(_selectedSwatch);
    footer.Controls.Add(_selectedValue);
    footer.Controls.Add(_copyButton);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      RowCount = 3,
      Dock = DockStyle.Fill
    };
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.Controls.Add(description, 0, 0);
    layout.Controls.Add(_tabs, 0, 1);
    layout.Controls.Add(footer, 0, 2);
    Controls.Add(layout);

    SetSelectedColour(_selected);
  }

  private TabPage CreateCurrentPickerTab()
  {
    var page = new TabPage("Current wheel + editor");
    _wheel.Dock = DockStyle.Fill;
    _wheel.Margin = new Padding(12);
    _editor.Dock = DockStyle.Fill;
    _editor.Orientation = Orientation.Horizontal;
    _editor.Margin = new Padding(12);
    _wheel.ColorChanged += (_, _) =>
    {
      if (!_updating)
      {
        SetSelectedColour(_wheel.Color);
      }
    };
    _editor.ColorChanged += (_, _) =>
    {
      if (!_updating)
      {
        SetSelectedColour(_editor.Color);
      }
    };

    var layout = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 1,
      Dock = DockStyle.Fill,
      Padding = new Padding(12)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
    layout.Controls.Add(_wheel, 0, 0);
    layout.Controls.Add(_editor, 1, 0);
    page.Controls.Add(layout);
    return page;
  }

  private TabPage CreatePaletteTab()
  {
    var page = new TabPage("Preset swatches");
    var panel = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      AutoScroll = true,
      Padding = new Padding(16),
      WrapContents = true
    };

    foreach (Color colour in PaletteColours())
    {
      var button = new Button
      {
        BackColor = colour,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(5),
        Size = new Size(72, 52),
        TabStop = true,
        AccessibleName = ToHex(colour),
        UseVisualStyleBackColor = false
      };
      button.FlatAppearance.BorderColor = Color.DimGray;
      button.Click += (_, _) => SetSelectedColour(colour);
      panel.Controls.Add(button);
    }

    page.Controls.Add(panel);
    return page;
  }

  private TabPage CreateSliderTab()
  {
    var page = new TabPage("RGBA sliders");
    string[] names = { "Red", "Green", "Blue", "Alpha" };
    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = 5,
      Dock = DockStyle.Top,
      Padding = new Padding(24),
      AutoSize = true
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

    for (int index = 0; index < names.Length; ++index)
    {
      var label = new Label
      {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = names[index],
        TextAlign = ContentAlignment.MiddleLeft
      };
      var slider = new TrackBar
      {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 255,
        TickFrequency = 16,
        SmallChange = 1,
        LargeChange = 16,
        Height = 44,
        Tag = index
      };
      var number = new NumericUpDown
      {
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 255,
        Tag = index
      };
      slider.ValueChanged += SliderValueChanged;
      number.ValueChanged += NumberValueChanged;
      _sliders[index] = slider;
      _numbers[index] = number;
      layout.Controls.Add(label, 0, index);
      layout.Controls.Add(slider, 1, index);
      layout.Controls.Add(number, 2, index);
    }

    page.Controls.Add(layout);
    return page;
  }

  private TabPage CreateWindowsDialogTab()
  {
    var page = new TabPage("Windows colour dialog");
    ConfigureSwatch(_dialogSwatch);
    _dialogSwatch.Size = new Size(180, 90);

    var button = new Button
    {
      AutoSize = true,
      Text = "Open Windows colour dialog..."
    };
    button.Click += (_, _) =>
    {
      using var dialog = new ColorDialog
      {
        AllowFullOpen = true,
        AnyColor = true,
        Color = Color.FromArgb(_selected.R, _selected.G, _selected.B),
        FullOpen = true
      };
      if (dialog.ShowDialog(this) == DialogResult.OK)
      {
        SetSelectedColour(Color.FromArgb(
          _selected.A,
          dialog.Color.R,
          dialog.Color.G,
          dialog.Color.B));
      }
    };

    var layout = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      Padding = new Padding(28),
      WrapContents = false
    };
    layout.Controls.Add(new Label
    {
      AutoSize = true,
      MaximumSize = new Size(720, 0),
      Text =
        "This is the standard Windows picker.  It is familiar and compact, " +
        "but it does not expose alpha, so the existing alpha value is kept."
    });
    layout.Controls.Add(button);
    layout.Controls.Add(_dialogSwatch);
    page.Controls.Add(layout);
    return page;
  }

  private void SliderValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating || sender is not TrackBar slider || slider.Tag is not int index)
    {
      return;
    }
    _updating = true;
    try
    {
      _numbers[index].Value = slider.Value;
      SetSelectedColourFromChannels();
    }
    finally
    {
      _updating = false;
    }
    RefreshDisplays();
  }

  private void NumberValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating || sender is not NumericUpDown number ||
        number.Tag is not int index)
    {
      return;
    }
    _updating = true;
    try
    {
      _sliders[index].Value = Decimal.ToInt32(number.Value);
      SetSelectedColourFromChannels();
    }
    finally
    {
      _updating = false;
    }
    RefreshDisplays();
  }

  private void SetSelectedColourFromChannels()
  {
    _selected = Color.FromArgb(
      _sliders[3].Value,
      _sliders[0].Value,
      _sliders[1].Value,
      _sliders[2].Value);
  }

  private void SetSelectedColour(Color colour)
  {
    _selected = colour;
    _updating = true;
    try
    {
      _wheel.Color = colour;
      _editor.Color = colour;
      int[] channels = { colour.R, colour.G, colour.B, colour.A };
      for (int index = 0; index < channels.Length; ++index)
      {
        _sliders[index].Value = channels[index];
        _numbers[index].Value = channels[index];
      }
    }
    finally
    {
      _updating = false;
    }
    RefreshDisplays();
  }

  private void RefreshDisplays()
  {
    _selectedSwatch.BackColor = _selected;
    _dialogSwatch.BackColor = _selected;
    _selectedValue.Text = string.Create(
      CultureInfo.InvariantCulture,
      $"ARGB({_selected.A}, {_selected.R}, {_selected.G}, {_selected.B})  " +
      $"{ToHex(_selected)}");
  }

  private static void ConfigureSwatch(Panel panel)
  {
    panel.BorderStyle = BorderStyle.FixedSingle;
    panel.Margin = new Padding(4);
  }

  private static string ToHex(Color colour)
  {
    return $"#{colour.A:X2}{colour.R:X2}{colour.G:X2}{colour.B:X2}";
  }

  private static IReadOnlyList<Color> PaletteColours()
  {
    return new[]
    {
      Color.FromArgb(255, 255, 222, 149),
      Color.FromArgb(255, 122, 83, 26),
      Color.FromArgb(255, 61, 83, 132),
      Color.FromArgb(255, 41, 98, 255),
      Color.FromArgb(255, 0, 122, 204),
      Color.FromArgb(255, 0, 153, 188),
      Color.FromArgb(255, 0, 128, 128),
      Color.FromArgb(255, 16, 124, 16),
      Color.FromArgb(255, 78, 154, 6),
      Color.FromArgb(255, 181, 206, 168),
      Color.FromArgb(255, 255, 196, 0),
      Color.FromArgb(255, 255, 140, 0),
      Color.FromArgb(255, 202, 80, 16),
      Color.FromArgb(255, 209, 52, 56),
      Color.FromArgb(255, 232, 17, 35),
      Color.FromArgb(255, 194, 57, 179),
      Color.FromArgb(255, 136, 23, 152),
      Color.FromArgb(255, 104, 33, 122),
      Color.FromArgb(255, 118, 118, 118),
      Color.FromArgb(255, 160, 160, 160),
      Color.FromArgb(255, 210, 210, 210),
      Color.FromArgb(255, 245, 245, 245),
      Color.FromArgb(192, 61, 83, 132),
      Color.FromArgb(128, 61, 83, 132),
      Color.FromArgb(96, 255, 222, 149),
      Color.FromArgb(64, 255, 255, 255)
    };
  }
}
