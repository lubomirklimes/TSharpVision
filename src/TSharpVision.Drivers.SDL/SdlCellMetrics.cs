namespace TSharpVision.Drivers.SDL;

/// <summary>
/// One terminal cell in logical size (what the configured font produces at its configured point
/// size, i.e. at 1×) and in drawable pixels (what is actually rendered at the window's pixel
/// density). Shared by SDLDriver and SDLGpuDriver so both back-ends lay out the grid identically.
/// <para>
/// <b>Rounding policy.</b> The renderers address whole pixels, so a cell is an integral number of
/// drawable pixels on each axis: <c>PixelWidth = round(LogicalWidth × Density)</c> (at least 1,
/// midpoint away from zero). Every cell of the grid gets that same size — the grid never mixes
/// widths — so there is no per-cell drift; a non-integer density can only leave a strip of less
/// than one cell as letterbox, exactly as an odd window size already does at 1×.
/// </para>
/// <para>
/// Columns and rows always come from drawable pixels divided by the drawable cell
/// (<see cref="GridForDrawable"/>), and mouse positions are converted to drawable pixels before
/// they are divided by the same cell, so rendering and hit-testing agree at any density.
/// </para>
/// </summary>
/// <param name="LogicalWidth">Cell width at 1×, from the font at its configured size.</param>
/// <param name="LogicalHeight">Cell height at 1×.</param>
/// <param name="Density">Pixel density the drawable size was derived for.</param>
/// <param name="PixelWidth">Cell width in drawable pixels.</param>
/// <param name="PixelHeight">Cell height in drawable pixels.</param>
internal readonly record struct SdlCellMetrics(
    int   LogicalWidth,
    int   LogicalHeight,
    float Density,
    int   PixelWidth,
    int   PixelHeight)
{
    /// <summary>Logical underline-cursor thickness, in 1× pixels.</summary>
    public const int LogicalUnderlineHeight = 2;

    /// <summary>Cell metrics for <paramref name="density"/>; 1× when the density is 1.</summary>
    public static SdlCellMetrics ForDensity(int logicalWidth, int logicalHeight, float density)
    {
        density = SdlDisplayScale.NormalizeDensity(density);
        logicalWidth  = Math.Max(1, logicalWidth);
        logicalHeight = Math.Max(1, logicalHeight);
        return new SdlCellMetrics(
            logicalWidth, logicalHeight, density,
            ScalePixels(logicalWidth, density), ScalePixels(logicalHeight, density));
    }

    /// <summary><c>round(value × density)</c>, never below 1.</summary>
    public static int ScalePixels(int value, float density) =>
        Math.Max(1, (int)MathF.Round(value * density, MidpointRounding.AwayFromZero));

    /// <summary><c>true</c> when drawable and logical cells are the same size (1×).</summary>
    public bool IsUnscaled => PixelWidth == LogicalWidth && PixelHeight == LogicalHeight;

    /// <summary>Point size glyphs are rasterised at: the configured size times the density.</summary>
    public float RasterPointSize(float configuredPointSize) => configuredPointSize * Density;

    /// <summary>Underline-cursor thickness in drawable pixels, clamped to the cell.</summary>
    public int UnderlineHeight => Math.Min(PixelHeight, ScalePixels(LogicalUnderlineHeight, Density));

    /// <summary>
    /// Size of one dot of the CP437 shading dither in drawable pixels: the integral part of the
    /// density, so <c>░ ▒ ▓</c> keep the dot pitch they have at 1× instead of turning into a much
    /// finer pattern. Falls back to 1 when the drawable cell is not divisible by it, which keeps the
    /// pattern continuous across cell boundaries (see <c>Cp437GlyphGenerator.PhaseForCell</c>).
    /// </summary>
    public int ShadingDotSize
    {
        get
        {
            int dot = Math.Max(1, (int)MathF.Round(Density, MidpointRounding.AwayFromZero));
            return PixelWidth % dot == 0 && PixelHeight % dot == 0 ? dot : 1;
        }
    }

    /// <summary>Columns and rows that fit a drawable of <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>; at least 2 × 1.</summary>
    public (ushort Cols, ushort Rows) GridForDrawable(int pixelWidth, int pixelHeight) =>
        ((ushort)Math.Clamp(pixelWidth  / PixelWidth,  2, ushort.MaxValue),
         (ushort)Math.Clamp(pixelHeight / PixelHeight, 1, ushort.MaxValue));

    /// <summary>
    /// Window size, in window coordinates, whose drawable holds exactly
    /// <paramref name="cols"/> × <paramref name="rows"/> drawable cells at <paramref name="scale"/>.
    /// Rounds up so that rounding never loses the last column or row; at an integer density this
    /// is exactly <c>cols × LogicalWidth</c> by <c>rows × LogicalHeight</c>.
    /// </summary>
    public (int Width, int Height) WindowSizeForGrid(int cols, int rows, in SdlDisplayScale scale)
    {
        float sx = scale.ScaleX > 0f ? scale.ScaleX : Density;
        float sy = scale.ScaleY > 0f ? scale.ScaleY : Density;
        return (CeilToWindow(cols * PixelWidth, sx), CeilToWindow(rows * PixelHeight, sy));
    }

    // A ratio such as 2880/1440 is exact in float, but 1.25-style ratios are not; shave a hair
    // before rounding up so 1800/1.25 stays 1440 rather than becoming 1441.
    private static int CeilToWindow(int pixels, float scale) =>
        Math.Max(1, (int)Math.Ceiling(pixels / (double)scale - 1e-4));

    /// <summary>Diagnostic line. Format is stable enough to grep.</summary>
    public string Describe() =>
        $"cell logical={LogicalWidth}x{LogicalHeight} drawable={PixelWidth}x{PixelHeight} " +
        $"density={Density.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} " +
        $"underline={UnderlineHeight} shadingDot={ShadingDotSize}";
}
