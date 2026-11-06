namespace TSharpVision.Drivers;

/// <summary>How a driver draws the grapheme clusters held by <see cref="TScreenChar"/> lead and trail cells.</summary>
[Flags]
public enum TextCapabilities
{
    /// <summary>Only <see cref="TScreenChar.Character"/> is drawn, one cell each (the lead's fallback character).</summary>
    None = 0,
    /// <summary>A lead and its trails are drawn as one glyph spanning their cells.</summary>
    WideCells = 1 << 0,
    /// <summary>The whole text of a cluster (supplementary characters, combining and ZWJ sequences) reaches the output.</summary>
    ClusterText = 1 << 1,
    /// <summary>Colour glyphs (emoji) keep their own colours.</summary>
    ColorGlyphs = 1 << 2,
}
