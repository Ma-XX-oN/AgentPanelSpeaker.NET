using System.Drawing.Drawing2D;

namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the custom vector drawing used by a glyph button.
/// </summary>
internal enum GlyphButtonDrawing
{
  Text,
  PreviousSpeakerTurn,
  NextSpeakerTurn,
  ProcessingClock
}

/// <summary>
/// Draws one symbol or transport icon in the centre of a standard button.
/// </summary>
internal sealed class GlyphButton : Button
{
  private string _glyph = string.Empty;
  private bool _useInkBounds;
  private GlyphButtonDrawing _drawing;

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
  /// Gets or sets whether text or a custom speaker-turn icon is drawn.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public GlyphButtonDrawing Drawing
  {
    get => _drawing;
    set
    {
      _drawing = value;
      Invalidate();
    }
  }

  /// <summary>
  /// Draws themed button chrome first, then the centred symbol or icon.
  /// </summary>
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    base.OnPaint(eventArgs);
    Color glyphColor = Enabled ? ForeColor : SystemColors.GrayText;
    if (_drawing != GlyphButtonDrawing.Text)
    {
      switch (_drawing)
      {
        case GlyphButtonDrawing.PreviousSpeakerTurn:
        case GlyphButtonDrawing.NextSpeakerTurn:
          DrawSpeakerTurnIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.ProcessingClock:
          DrawProcessingClockIcon(eventArgs.Graphics, glyphColor);
          break;
      }
      return;
    }

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

  /// <summary>
  /// Draws two alternating speech bubbles and a navigation arrow.
  /// </summary>
  private void DrawSpeakerTurnIcon(Graphics graphics, Color glyphColor)
  {
    if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
    {
      return;
    }

    const float designWidth = 36.0f;
    const float designHeight = 22.0f;
    float scale = Math.Min(
      ClientSize.Width / (designWidth + 8.0f),
      ClientSize.Height / (designHeight + 12.0f));
    scale = Math.Max(0.5f, scale);
    float originX = (ClientSize.Width - designWidth * scale) / 2.0f;
    float originY = (ClientSize.Height - designHeight * scale) / 2.0f;

    SmoothingMode oldSmoothingMode = graphics.SmoothingMode;
    PixelOffsetMode oldPixelOffsetMode = graphics.PixelOffsetMode;
    try
    {
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
      using var pen = new Pen(glyphColor, 1.7f * scale)
      {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
      };

      bool previous = _drawing == GlyphButtonDrawing.PreviousSpeakerTurn;
      if (previous)
      {
        DrawArrow(graphics, pen, originX, originY, scale, pointsLeft: true);
        DrawSpeechBubble(graphics, pen, originX, originY, scale, 15.0f, 2.0f);
        DrawSpeechBubble(graphics, pen, originX, originY, scale, 21.0f, 10.0f);
      }
      else
      {
        DrawSpeechBubble(graphics, pen, originX, originY, scale, 1.0f, 2.0f);
        DrawSpeechBubble(graphics, pen, originX, originY, scale, 7.0f, 10.0f);
        DrawArrow(graphics, pen, originX, originY, scale, pointsLeft: false);
      }
    }
    finally
    {
      graphics.SmoothingMode = oldSmoothingMode;
      graphics.PixelOffsetMode = oldPixelOffsetMode;
    }
  }

  /// <summary>
  /// Draws a clock used to request the selected turn's processing duration.
  /// </summary>
  private void DrawProcessingClockIcon(Graphics graphics, Color glyphColor)
  {
    if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
    {
      return;
    }

    float scale = Math.Min(ClientSize.Width, ClientSize.Height) / 30.0f;
    scale = Math.Max(0.5f, scale);
    float radius = 9.0f * scale;
    float centreX = ClientSize.Width / 2.0f;
    float centreY = ClientSize.Height / 2.0f;
    SmoothingMode oldSmoothingMode = graphics.SmoothingMode;
    PixelOffsetMode oldPixelOffsetMode = graphics.PixelOffsetMode;
    try
    {
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
      using var pen = new Pen(glyphColor, 1.8f * scale)
      {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
      };
      graphics.DrawEllipse(
        pen,
        centreX - radius,
        centreY - radius,
        radius * 2.0f,
        radius * 2.0f);
      graphics.DrawLine(
        pen,
        centreX,
        centreY,
        centreX,
        centreY - 5.2f * scale);
      graphics.DrawLine(
        pen,
        centreX,
        centreY,
        centreX + 4.5f * scale,
        centreY + 2.8f * scale);
    }
    finally
    {
      graphics.SmoothingMode = oldSmoothingMode;
      graphics.PixelOffsetMode = oldPixelOffsetMode;
    }
  }

  private static void DrawArrow(
    Graphics graphics,
    Pen pen,
    float originX,
    float originY,
    float scale,
    bool pointsLeft)
  {
    float tailX = pointsLeft ? 13.0f : 23.0f;
    float tipX = pointsLeft ? 2.0f : 34.0f;
    float centreY = 11.0f;
    float headX = pointsLeft ? 7.0f : 29.0f;
    graphics.DrawLine(
      pen,
      originX + tailX * scale,
      originY + centreY * scale,
      originX + tipX * scale,
      originY + centreY * scale);
    graphics.DrawLine(
      pen,
      originX + tipX * scale,
      originY + centreY * scale,
      originX + headX * scale,
      originY + 6.0f * scale);
    graphics.DrawLine(
      pen,
      originX + tipX * scale,
      originY + centreY * scale,
      originX + headX * scale,
      originY + 16.0f * scale);
  }

  private static void DrawSpeechBubble(
    Graphics graphics,
    Pen pen,
    float originX,
    float originY,
    float scale,
    float x,
    float y)
  {
    using GraphicsPath path = CreateSpeechBubblePath(
      originX + x * scale,
      originY + y * scale,
      13.0f * scale,
      9.0f * scale,
      2.2f * scale);
    graphics.DrawPath(pen, path);
  }

  private static GraphicsPath CreateSpeechBubblePath(
    float x,
    float y,
    float width,
    float height,
    float radius)
  {
    float diameter = radius * 2.0f;
    float bodyHeight = height - radius;
    var path = new GraphicsPath();
    path.AddArc(x, y, diameter, diameter, 180.0f, 90.0f);
    path.AddArc(
      x + width - diameter,
      y,
      diameter,
      diameter,
      270.0f,
      90.0f);
    path.AddArc(
      x + width - diameter,
      y + bodyHeight - diameter,
      diameter,
      diameter,
      0.0f,
      90.0f);
    path.AddLine(
      x + width - radius,
      y + bodyHeight,
      x + width - 4.0f * radius,
      y + bodyHeight);
    path.AddLine(
      x + width - 4.0f * radius,
      y + bodyHeight,
      x + width - 5.0f * radius,
      y + height);
    path.AddLine(
      x + width - 5.0f * radius,
      y + height,
      x + width - 5.2f * radius,
      y + bodyHeight);
    path.AddArc(
      x,
      y + bodyHeight - diameter,
      diameter,
      diameter,
      90.0f,
      90.0f);
    path.CloseFigure();
    return path;
  }
}
