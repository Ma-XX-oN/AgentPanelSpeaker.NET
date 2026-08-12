using System.Drawing.Drawing2D;

namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the custom vector drawing used by a glyph button.
/// </summary>
internal enum GlyphButtonDrawing
{
  Text,
  PreviousSpeakerTurn,
  PreviousNode,
  PreviousSentence,
  Play,
  Pause,
  NextSentence,
  NextNode,
  NextSpeakerTurn,
  ProcessingClock,
  SettingsGear,
  Expand,
  Restore,
  Save,
  Reset,
  Keyboard,
  DiagnosticLog,
  Bluetooth
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
    ThemeManager.LogCustomPaint(
      "GlyphButton.OnPaint",
      "begin",
      this,
      eventArgs.ClipRectangle,
      _drawing.ToString());
    base.OnPaint(eventArgs);
    Color glyphColor = ThemeManager.GetControlForeground(this);
    if (_drawing != GlyphButtonDrawing.Text)
    {
      switch (_drawing)
      {
        case GlyphButtonDrawing.PreviousSpeakerTurn:
        case GlyphButtonDrawing.NextSpeakerTurn:
          DrawSpeakerTurnIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.PreviousNode:
          DrawSkipIcon(eventArgs.Graphics, glyphColor, pointsLeft: true);
          break;
        case GlyphButtonDrawing.PreviousSentence:
          DrawDoubleTriangleIcon(
            eventArgs.Graphics,
            glyphColor,
            pointsLeft: true);
          break;
        case GlyphButtonDrawing.Play:
          DrawPlayIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.Pause:
          DrawPauseIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.NextSentence:
          DrawDoubleTriangleIcon(
            eventArgs.Graphics,
            glyphColor,
            pointsLeft: false);
          break;
        case GlyphButtonDrawing.NextNode:
          DrawSkipIcon(eventArgs.Graphics, glyphColor, pointsLeft: false);
          break;
        case GlyphButtonDrawing.ProcessingClock:
          DrawProcessingClockIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.SettingsGear:
          DrawSettingsGearIcon(eventArgs.Graphics, glyphColor);
          break;
        case GlyphButtonDrawing.Expand:
          DrawChevronIcon(eventArgs.Graphics, glyphColor, pointsUp: true);
          break;
        case GlyphButtonDrawing.Restore:
          DrawChevronIcon(eventArgs.Graphics, glyphColor, pointsUp: false);
          break;
        case GlyphButtonDrawing.Save:
        case GlyphButtonDrawing.Reset:
        case GlyphButtonDrawing.Keyboard:
        case GlyphButtonDrawing.DiagnosticLog:
          UtilityIconAssets.Draw(
            eventArgs.Graphics,
            ClientRectangle,
            _drawing,
            glyphColor);
          break;
        case GlyphButtonDrawing.Bluetooth:
          DrawBluetoothIcon(eventArgs.Graphics, glyphColor);
          break;
      }
      ThemeManager.LogCustomPaint(
        "GlyphButton.OnPaint",
        "end",
        this,
        eventArgs.ClipRectangle,
        _drawing.ToString());
      return;
    }

    if (_useInkBounds && DrawUsingInkBounds(eventArgs.Graphics, glyphColor))
    {
      ThemeManager.LogCustomPaint(
        "GlyphButton.OnPaint",
        "end",
        this,
        eventArgs.ClipRectangle,
        _drawing.ToString());
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
    ThemeManager.LogCustomPaint(
      "GlyphButton.OnPaint",
      "end",
      this,
      eventArgs.ClipRectangle,
      _drawing.ToString());
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
  /// Draws a conventional single play triangle.
  /// </summary>
  private void DrawPlayIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF bounds = GetCompactIconBounds();
    using var path = new GraphicsPath();
    path.AddPolygon(new[]
    {
      new PointF(bounds.Left + bounds.Width * 0.24f, bounds.Top),
      new PointF(bounds.Right, bounds.Top + bounds.Height * 0.5f),
      new PointF(bounds.Left + bounds.Width * 0.24f, bounds.Bottom)
    });
    FillIconPath(graphics, glyphColor, path);
  }

  /// <summary>
  /// Draws two pause bars with generous internal padding.
  /// </summary>
  private void DrawPauseIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF bounds = GetCompactIconBounds();
    float barWidth = bounds.Width * 0.25f;
    float gap = bounds.Width * 0.18f;
    float left = bounds.Left + (bounds.Width - barWidth * 2.0f - gap) / 2.0f;
    using var brush = new SolidBrush(glyphColor);
    graphics.FillRectangle(brush, left, bounds.Top, barWidth, bounds.Height);
    graphics.FillRectangle(
      brush,
      left + barWidth + gap,
      bounds.Top,
      barWidth,
      bounds.Height);
  }

  /// <summary>
  /// Draws two triangles for sentence-level navigation.
  /// </summary>
  private void DrawDoubleTriangleIcon(
    Graphics graphics,
    Color glyphColor,
    bool pointsLeft)
  {
    RectangleF bounds = GetCompactIconBounds();
    float halfWidth = bounds.Width * 0.48f;
    using var path = new GraphicsPath();
    AddTriangle(
      path,
      new RectangleF(bounds.Left, bounds.Top, halfWidth, bounds.Height),
      pointsLeft);
    AddTriangle(
      path,
      new RectangleF(
        bounds.Right - halfWidth,
        bounds.Top,
        halfWidth,
        bounds.Height),
      pointsLeft);
    FillIconPath(graphics, glyphColor, path);
  }

  /// <summary>
  /// Draws a triangle and terminal bar for JSONL-node navigation.
  /// </summary>
  private void DrawSkipIcon(
    Graphics graphics,
    Color glyphColor,
    bool pointsLeft)
  {
    RectangleF bounds = GetCompactIconBounds();
    float barWidth = Math.Max(1.5f, bounds.Width * 0.13f);
    float gap = bounds.Width * 0.10f;
    RectangleF triangleBounds = pointsLeft
      ? new RectangleF(
          bounds.Left + barWidth + gap,
          bounds.Top,
          bounds.Width - barWidth - gap,
          bounds.Height)
      : new RectangleF(
          bounds.Left,
          bounds.Top,
          bounds.Width - barWidth - gap,
          bounds.Height);
    using var path = new GraphicsPath();
    AddTriangle(path, triangleBounds, pointsLeft);
    FillIconPath(graphics, glyphColor, path);
    using var brush = new SolidBrush(glyphColor);
    float barX = pointsLeft ? bounds.Left : bounds.Right - barWidth;
    graphics.FillRectangle(brush, barX, bounds.Top, barWidth, bounds.Height);
  }

  /// <summary>
  /// Draws a compact settings gear independent of the installed symbol font.
  /// </summary>
  private void DrawSettingsGearIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF bounds = GetCompactIconBounds(padding: 5.0f);
    float centreX = bounds.Left + bounds.Width / 2.0f;
    float centreY = bounds.Top + bounds.Height / 2.0f;
    float outer = Math.Min(bounds.Width, bounds.Height) * 0.46f;
    float inner = outer * 0.64f;
    using var path = new GraphicsPath();
    const int toothCount = 8;
    for (int index = 0; index < toothCount * 2; ++index)
    {
      double angle = -Math.PI / 2.0 +
        index * Math.PI / toothCount;
      float radius = index % 2 == 0 ? outer : inner;
      var point = new PointF(
        centreX + (float)Math.Cos(angle) * radius,
        centreY + (float)Math.Sin(angle) * radius);
      if (index == 0)
      {
        path.StartFigure();
      }
      if (index == 0)
      {
        path.AddLine(point, point);
      }
      else
      {
        PointF previous = path.GetLastPoint();
        path.AddLine(previous, point);
      }
    }
    path.CloseFigure();
    path.AddEllipse(
      centreX - outer * 0.28f,
      centreY - outer * 0.28f,
      outer * 0.56f,
      outer * 0.56f);
    FillIconPath(graphics, glyphColor, path, FillMode.Alternate);
  }

  /// <summary>
  /// Draws the expand or restore chevron used by the transcript tabs.
  /// </summary>
  private void DrawChevronIcon(
    Graphics graphics,
    Color glyphColor,
    bool pointsUp)
  {
    RectangleF bounds = GetCompactIconBounds(padding: 6.0f);
    float centreX = bounds.Left + bounds.Width / 2.0f;
    float upperY = bounds.Top + bounds.Height * 0.30f;
    float lowerY = bounds.Bottom - bounds.Height * 0.30f;
    float tipY = pointsUp ? upperY : lowerY;
    float armY = pointsUp ? lowerY : upperY;
    using var pen = new Pen(glyphColor, Math.Max(1.5f, bounds.Height * 0.12f))
    {
      StartCap = LineCap.Round,
      EndCap = LineCap.Round,
      LineJoin = LineJoin.Round
    };
    graphics.DrawLines(pen, new[]
    {
      new PointF(bounds.Left, armY),
      new PointF(centreX, tipY),
      new PointF(bounds.Right, armY)
    });
  }

  /// <summary>
  /// Draws the standard Bluetooth rune without a surrounding frame.
  /// </summary>
  private void DrawBluetoothIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF b = GetCompactIconBounds(padding: 6.0f);
    float centerX = b.Left + b.Width * 0.50f;
    float top = b.Top + b.Height * 0.05f;
    float middleY = b.Top + b.Height * 0.50f;
    float bottom = b.Bottom - b.Height * 0.05f;
    float right = b.Right - b.Width * 0.12f;
    float upperInnerY = b.Top + b.Height * 0.30f;
    float lowerInnerY = b.Bottom - b.Height * 0.30f;
    using var pen = new Pen(
      glyphColor,
      Math.Max(1.7f, b.Width * 0.11f))
    {
      StartCap = LineCap.Round,
      EndCap = LineCap.Round,
      LineJoin = LineJoin.Round
    };

    graphics.DrawLine(pen, centerX, top, centerX, bottom);
    graphics.DrawLines(pen, new[]
    {
      new PointF(centerX, top),
      new PointF(right, upperInnerY),
      new PointF(centerX, middleY),
      new PointF(right, lowerInnerY),
      new PointF(centerX, bottom)
    });
    graphics.DrawLine(
      pen,
      b.Left + b.Width * 0.15f,
      b.Top + b.Height * 0.22f,
      right,
      lowerInnerY);
    graphics.DrawLine(
      pen,
      b.Left + b.Width * 0.15f,
      b.Bottom - b.Height * 0.22f,
      right,
      upperInnerY);
  }

  private void DrawSaveIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF b = GetCompactIconBounds();
    using var pen = new Pen(glyphColor, 1.7f);
    graphics.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
    graphics.DrawRectangle(pen, b.X + b.Width * 0.22f, b.Y,
      b.Width * 0.52f, b.Height * 0.34f);
    graphics.DrawRectangle(pen, b.X + b.Width * 0.2f,
      b.Y + b.Height * 0.58f, b.Width * 0.6f, b.Height * 0.42f);
  }

  private void DrawResetIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF b = GetCompactIconBounds(padding: 6.0f);
    using var pen = new Pen(glyphColor, Math.Max(1.8f, b.Width * 0.12f))
    {
      StartCap = LineCap.Round,
      EndCap = LineCap.Round,
      LineJoin = LineJoin.Round
    };

    RectangleF arc = new(
      b.Left + b.Width * 0.18f,
      b.Top + b.Height * 0.18f,
      b.Width * 0.62f,
      b.Height * 0.62f);
    graphics.DrawArc(pen, arc, 35.0f, 250.0f);
    graphics.DrawLine(
      pen,
      b.Left + b.Width * 0.30f,
      b.Top + b.Height * 0.18f,
      b.Left + b.Width * 0.56f,
      b.Top + b.Height * 0.18f);

    using var path = new GraphicsPath();
    path.AddPolygon(new[]
    {
      new PointF(b.Left + b.Width * 0.12f, b.Top + b.Height * 0.30f),
      new PointF(b.Left + b.Width * 0.34f, b.Top + b.Height * 0.18f),
      new PointF(b.Left + b.Width * 0.34f, b.Top + b.Height * 0.42f)
    });
    FillIconPath(graphics, glyphColor, path);
  }

  private void DrawKeyboardIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF b = GetWideIconBounds(
      horizontalPadding: 4.5f,
      verticalPadding: 7.0f,
      widthRatio: 0.94f,
      heightRatio: 0.58f);
    using var pen = new Pen(glyphColor, Math.Max(1.4f, b.Height * 0.11f))
    {
      LineJoin = LineJoin.Round
    };

    graphics.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

    float keyWidth = b.Width * 0.075f;
    float keyHeight = b.Height * 0.14f;
    float columnGap = b.Width * 0.032f;
    float rowGap = b.Height * 0.11f;
    float left = b.Left + b.Width * 0.08f;
    float top = b.Top + b.Height * 0.16f;

    for (int row = 0; row < 2; row++)
    {
      for (int column = 0; column < 8; column++)
      {
        graphics.DrawRectangle(
          pen,
          left + column * (keyWidth + columnGap),
          top + row * (keyHeight + rowGap),
          keyWidth,
          keyHeight);
      }
    }

    float thirdRowY = top + 2.0f * (keyHeight + rowGap);
    graphics.DrawRectangle(
      pen,
      left,
      thirdRowY,
      keyWidth * 1.35f,
      keyHeight);
    graphics.DrawRectangle(
      pen,
      left + (keyWidth + columnGap) * 1.55f,
      thirdRowY,
      b.Width * 0.42f,
      keyHeight);
    graphics.DrawRectangle(
      pen,
      b.Right - b.Width * 0.17f,
      thirdRowY,
      keyWidth * 1.05f,
      keyHeight);
  }

  private void DrawDiagnosticLogIcon(Graphics graphics, Color glyphColor)
  {
    RectangleF b = GetCompactIconBounds();
    using var pen = new Pen(glyphColor, 1.5f);
    RectangleF page = new(b.Left, b.Top, b.Width * 0.66f, b.Height * 0.9f);
    graphics.DrawRectangle(pen, page.X, page.Y, page.Width, page.Height);
    graphics.DrawLine(pen, page.Left + page.Width * 0.18f,
      page.Top + page.Height * 0.28f, page.Right - page.Width * 0.16f,
      page.Top + page.Height * 0.28f);
    graphics.DrawLine(pen, page.Left + page.Width * 0.18f,
      page.Top + page.Height * 0.48f, page.Right - page.Width * 0.28f,
      page.Top + page.Height * 0.48f);
    RectangleF lens = new(b.Left + b.Width * 0.48f,
      b.Top + b.Height * 0.48f, b.Width * 0.36f, b.Height * 0.36f);
    graphics.DrawEllipse(pen, lens);
    graphics.DrawLine(pen, lens.Right - b.Width * 0.02f,
      lens.Bottom - b.Height * 0.02f, b.Right, b.Bottom);
  }

  private RectangleF GetCompactIconBounds(float padding = 7.0f)
  {
    float width = Math.Max(1.0f, ClientSize.Width - padding * 2.0f);
    float height = Math.Max(1.0f, ClientSize.Height - padding * 2.0f);
    float side = Math.Min(width, height);
    return new RectangleF(
      (ClientSize.Width - side) / 2.0f,
      (ClientSize.Height - side) / 2.0f,
      side,
      side);
  }

  private RectangleF GetWideIconBounds(
    float horizontalPadding,
    float verticalPadding,
    float widthRatio,
    float heightRatio)
  {
    float availableWidth = Math.Max(
      1.0f,
      ClientSize.Width - horizontalPadding * 2.0f);
    float availableHeight = Math.Max(
      1.0f,
      ClientSize.Height - verticalPadding * 2.0f);
    float width = availableWidth * widthRatio;
    float height = availableHeight * heightRatio;

    if (height > availableHeight)
    {
      height = availableHeight;
    }

    if (width > availableWidth)
    {
      width = availableWidth;
    }

    return new RectangleF(
      (ClientSize.Width - width) / 2.0f,
      (ClientSize.Height - height) / 2.0f,
      width,
      height);
  }

  private static void AddTriangle(
    GraphicsPath path,
    RectangleF bounds,
    bool pointsLeft)
  {
    PointF[] points = pointsLeft
      ? new[]
        {
          new PointF(bounds.Left, bounds.Top + bounds.Height * 0.5f),
          new PointF(bounds.Right, bounds.Top),
          new PointF(bounds.Right, bounds.Bottom)
        }
      : new[]
        {
          new PointF(bounds.Left, bounds.Top),
          new PointF(bounds.Right, bounds.Top + bounds.Height * 0.5f),
          new PointF(bounds.Left, bounds.Bottom)
        };
    path.AddPolygon(points);
  }

  private static void FillIconPath(
    Graphics graphics,
    Color glyphColor,
    GraphicsPath path,
    FillMode fillMode = FillMode.Winding)
  {
    SmoothingMode oldSmoothingMode = graphics.SmoothingMode;
    PixelOffsetMode oldPixelOffsetMode = graphics.PixelOffsetMode;
    FillMode oldFillMode = path.FillMode;
    try
    {
      path.FillMode = fillMode;
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
      using var brush = new SolidBrush(glyphColor);
      graphics.FillPath(brush, path);
    }
    finally
    {
      path.FillMode = oldFillMode;
      graphics.SmoothingMode = oldSmoothingMode;
      graphics.PixelOffsetMode = oldPixelOffsetMode;
    }
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
