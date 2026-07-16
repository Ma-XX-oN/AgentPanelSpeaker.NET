using System.Drawing.Drawing2D;

namespace AgentPanelSpeaker;

/// <summary>
/// Draws one symbol in the centre of a standard Windows Forms button.
/// </summary>
internal sealed class GlyphButton : Button
{
  private string _glyph = string.Empty;
  private bool _useInkBounds;

  /// <summary>
  /// Gets or sets the symbol drawn in the button centre.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public string Glyph
  {
    get => _glyph;
    set
    {
      _glyph = value ?? string.Empty;
      Invalidate();
    }
  }

  /// <summary>
  /// Gets or sets whether the visible glyph outline, rather than the font line
  /// box, is centred.  Transport symbols use this to avoid appearing high or
  /// low inside a short button.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public bool UseInkBounds
  {
    get => _useInkBounds;
    set
    {
      _useInkBounds = value;
      Invalidate();
    }
  }

  /// <summary>
  /// Draws themed button chrome first, then the centred symbol.
  /// </summary>
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    base.OnPaint(eventArgs);
    Color glyphColor = Enabled ? ForeColor : SystemColors.GrayText;
    if (_useInkBounds && DrawUsingInkBounds(eventArgs.Graphics, glyphColor))
    {
      return;
    }

    TextRenderer.DrawText(
      eventArgs.Graphics,
      _glyph,
      Font,
      ClientRectangle,
      glyphColor,
      TextFormatFlags.HorizontalCenter |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.SingleLine |
      TextFormatFlags.NoPadding |
      TextFormatFlags.NoPrefix);
  }

  /// <summary>
  /// Centres the actual vector outline of a transport glyph.
  /// </summary>
  private bool DrawUsingInkBounds(Graphics graphics, Color glyphColor)
  {
    if (_glyph.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
    {
      return false;
    }

    using var path = new GraphicsPath();
    using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
    format.FormatFlags |= StringFormatFlags.NoWrap;
    float emSize = graphics.DpiY * Font.SizeInPoints / 72.0f;
    path.AddString(
      _glyph,
      Font.FontFamily,
      (int)Font.Style,
      emSize,
      PointF.Empty,
      format);
    RectangleF inkBounds = path.GetBounds();
    if (inkBounds.Width <= 0.0f || inkBounds.Height <= 0.0f)
    {
      return false;
    }

    float x = (ClientSize.Width - inkBounds.Width) / 2.0f - inkBounds.X;
    float y = (ClientSize.Height - inkBounds.Height) / 2.0f - inkBounds.Y;
    using var transform = new Matrix();
    transform.Translate(x, y);
    path.Transform(transform);

    SmoothingMode oldSmoothingMode = graphics.SmoothingMode;
    PixelOffsetMode oldPixelOffsetMode = graphics.PixelOffsetMode;
    try
    {
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
      using var brush = new SolidBrush(glyphColor);
      graphics.FillPath(brush, path);
    }
    finally
    {
      graphics.SmoothingMode = oldSmoothingMode;
      graphics.PixelOffsetMode = oldPixelOffsetMode;
    }
    return true;
  }
}
