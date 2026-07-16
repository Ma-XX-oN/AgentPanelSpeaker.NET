namespace AgentPanelSpeaker;

/// <summary>
/// Draws one symbol in the exact centre of a standard Windows Forms button.
/// </summary>
internal sealed class GlyphButton : Button
{
  private string _glyph = string.Empty;

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
  /// Draws themed button chrome first, then the centred symbol.
  /// </summary>
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    base.OnPaint(eventArgs);
    Color glyphColor = Enabled ? ForeColor : SystemColors.GrayText;
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
}
