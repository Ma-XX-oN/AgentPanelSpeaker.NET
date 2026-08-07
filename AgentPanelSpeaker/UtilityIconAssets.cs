using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AgentPanelSpeaker;

/// <summary>
/// Loads and draws the approved raster artwork for utility buttons.
/// </summary>
internal static class UtilityIconAssets
{
  private static readonly ConcurrentDictionary<string, Bitmap> SourceCache = new();
  private static readonly ConcurrentDictionary<string, Rectangle> InkBoundsCache = new();
  private static readonly ConcurrentDictionary<(string Name, int Argb), Bitmap>
    TintedCache = new();

  /// <summary>
  /// Draws one approved utility icon centred in the supplied client bounds.
  /// </summary>
  public static void Draw(
    Graphics graphics,
    Rectangle clientBounds,
    GlyphButtonDrawing drawing,
    Color colour)
  {
    string? name = GetAssetName(drawing);
    if (name is null || clientBounds.Width <= 0 || clientBounds.Height <= 0)
    {
      return;
    }

    Bitmap? source = LoadSource(name);
    if (source is null)
    {
      return;
    }

    Rectangle inkBounds = InkBoundsCache.GetOrAdd(
      name,
      _ => FindInkBounds(source));
    if (inkBounds.Width <= 0 || inkBounds.Height <= 0)
    {
      return;
    }

    Bitmap tinted = TintedCache.GetOrAdd(
      (name, colour.ToArgb()),
      key => Tint(source, Color.FromArgb(key.Argb)));

    float targetExtent = Math.Max(
      1.0f,
      Math.Min(clientBounds.Width, clientBounds.Height) * 0.60f);
    float scale = Math.Min(
      targetExtent / inkBounds.Width,
      targetExtent / inkBounds.Height);
    float width = inkBounds.Width * scale;
    float height = inkBounds.Height * scale;
    float left = clientBounds.Left + (clientBounds.Width - width) / 2.0f;
    float top = clientBounds.Top + (clientBounds.Height - height) / 2.0f;
    var destination = new RectangleF(left, top, width, height);

    SmoothingMode oldSmoothing = graphics.SmoothingMode;
    InterpolationMode oldInterpolation = graphics.InterpolationMode;
    PixelOffsetMode oldPixelOffset = graphics.PixelOffsetMode;
    CompositingQuality oldCompositing = graphics.CompositingQuality;
    try
    {
      graphics.SmoothingMode = SmoothingMode.HighQuality;
      graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
      graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
      graphics.CompositingQuality = CompositingQuality.HighQuality;
      graphics.DrawImage(
        tinted,
        destination,
        inkBounds,
        GraphicsUnit.Pixel);
    }
    finally
    {
      graphics.SmoothingMode = oldSmoothing;
      graphics.InterpolationMode = oldInterpolation;
      graphics.PixelOffsetMode = oldPixelOffset;
      graphics.CompositingQuality = oldCompositing;
    }
  }

  private static string? GetAssetName(GlyphButtonDrawing drawing)
  {
    return drawing switch
    {
      GlyphButtonDrawing.Save => "Save",
      GlyphButtonDrawing.Reset => "Reset",
      GlyphButtonDrawing.Keyboard => "Keyboard",
      GlyphButtonDrawing.DiagnosticLog => "DiagnosticLog",
      _ => null
    };
  }

  private static Bitmap? LoadSource(string name)
  {
    try
    {
      return SourceCache.GetOrAdd(name, key =>
      {
        string path = Path.Combine(
          AppContext.BaseDirectory,
          "Assets",
          "UtilityIcons",
          $"{key}.png");
        return new Bitmap(path);
      });
    }
    catch (ArgumentException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
  }

  private static Rectangle FindInkBounds(Bitmap bitmap)
  {
    int left = bitmap.Width;
    int top = bitmap.Height;
    int right = -1;
    int bottom = -1;

    for (int y = 0; y < bitmap.Height; ++y)
    {
      for (int x = 0; x < bitmap.Width; ++x)
      {
        if (bitmap.GetPixel(x, y).A == 0)
        {
          continue;
        }

        left = Math.Min(left, x);
        top = Math.Min(top, y);
        right = Math.Max(right, x);
        bottom = Math.Max(bottom, y);
      }
    }

    return right < left || bottom < top
      ? Rectangle.Empty
      : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
  }

  private static Bitmap Tint(Bitmap source, Color colour)
  {
    var tinted = new Bitmap(
      source.Width,
      source.Height,
      PixelFormat.Format32bppArgb);

    for (int y = 0; y < source.Height; ++y)
    {
      for (int x = 0; x < source.Width; ++x)
      {
        byte alpha = source.GetPixel(x, y).A;
        tinted.SetPixel(
          x,
          y,
          Color.FromArgb(alpha, colour.R, colour.G, colour.B));
      }
    }
    return tinted;
  }
}
