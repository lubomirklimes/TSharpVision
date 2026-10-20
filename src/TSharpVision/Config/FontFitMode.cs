namespace TSharpVision.Config;

/// <summary>
/// How a graphical screen driver fits text glyphs into the font's character cell
/// (<c>[graphics] fontFit</c>, <see cref="GraphicsDriverConfiguration.FontFit"/>).
/// The cell size, box drawing, block elements and generated CP437 glyphs are the same in every mode.
/// Currently only the SDL drivers render differently per mode; console and terminal drivers ignore it.
/// </summary>
public enum FontFitMode
{
    /// <summary>
    /// Default. Text keeps the configured font size; only a glyph that overflows the cell is
    /// re-rasterised, condensed along the overflowing axis.
    /// </summary>
    Condense,

    /// <summary>
    /// Ordinary text is rasterised at one common size: the largest whole point size, not above the
    /// configured size, at which the driver's sizing repertoire fits the cell with the aspect ratio
    /// kept. Characters outside that repertoire that still overflow fall back to Condense fitting.
    /// </summary>
    Uniform,
}
