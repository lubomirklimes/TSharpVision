namespace TSharpVision.Graphics;

/// <summary>
/// The logical coordinate space (10000 × 7500) of the original TVGraph 1.21 library (TVGRAPH.INT) and its
/// 16-colour table. <see cref="GObject"/>s keep their geometry in this space; <see cref="GlobalToPhysical"/>
/// maps it onto the pixel rectangle of the drawing view.
/// </summary>
public static class GraphConstants
{
    /// <summary>Exclusive upper bound of the logical x axis.</summary>
    public const int GraphXMax = 10000;

    /// <summary>Exclusive upper bound of the logical y axis.</summary>
    public const int GraphYMax = 7500;

    /// <summary>
    /// The 16-colour VGA table as 0xRRGGBBAA, indexed by the CGA/EGA attribute (0 = black … 15 = white).
    /// </summary>
    public static ReadOnlySpan<uint> CgaPalette =>
    [
        0x000000FF, // 0  black
        0x0000AAFF, // 1  blue
        0x00AA00FF, // 2  green
        0x00AAAAFF, // 3  cyan
        0xAA0000FF, // 4  red
        0xAA00AAFF, // 5  magenta
        0xAA5500FF, // 6  brown
        0xAAAAAAFF, // 7  light gray
        0x555555FF, // 8  dark gray
        0x5555FFFF, // 9  light blue
        0x55FF55FF, // 10 light green
        0x55FFFFFF, // 11 light cyan
        0xFF5555FF, // 12 light red
        0xFF55FFFF, // 13 light magenta
        0xFFFF55FF, // 14 yellow
        0xFFFFFFFF, // 15 white
    ];

    /// <summary>
    /// Maps a logical point (0..GraphXMax, 0..GraphYMax) to a pixel inside the given pixel rectangle.
    /// </summary>
    public static void GlobalToPhysical(
        int pixelRectX, int pixelRectY,
        int pixelRectW, int pixelRectH,
        int gx, int gy,
        out int px, out int py)
    {
        px = pixelRectX + (int)((long)gx * pixelRectW / GraphXMax);
        py = pixelRectY + (int)((long)gy * pixelRectH / GraphYMax);
    }
}
