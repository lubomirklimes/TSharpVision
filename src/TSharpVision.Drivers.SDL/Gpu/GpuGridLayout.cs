namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Where the logical terminal grid sits inside an acquired swapchain texture, in device pixels.
/// <para>
/// Pure and SDL-independent so the placement decision is unit-testable and is computed in exactly
/// one place — the shader path, the CPU-compositing fallback and the diagnostics all use this.
/// </para>
/// <para>
/// The invariant this type exists to enforce: <b>one generated terminal pixel maps to one device
/// pixel</b>. The grid is never stretched to fill the swapchain. When the swapchain is not an
/// exact multiple of the cell size, the leftover pixels stay as a letterbox strip on the right and
/// bottom instead of being distributed across cells.
/// </para>
/// </summary>
/// <param name="SwapchainWidth">Width of the acquired swapchain texture, in device pixels.</param>
/// <param name="SwapchainHeight">Height of the acquired swapchain texture, in device pixels.</param>
/// <param name="GridWidth">Logical grid width, <c>cols * cellWidth</c>.</param>
/// <param name="GridHeight">Logical grid height, <c>rows * cellHeight</c>.</param>
/// <param name="ViewportX">Left edge of the terminal rectangle. Always 0 — top-left anchored.</param>
/// <param name="ViewportY">Top edge of the terminal rectangle. Always 0 — top-left anchored.</param>
/// <param name="ViewportWidth">
/// Width actually given to the GPU viewport. Equal to <paramref name="GridWidth"/> in every
/// non-degenerate case; clamped to the swapchain only when the grid does not fit at all.
/// </param>
/// <param name="ViewportHeight">Height given to the GPU viewport; see <paramref name="ViewportWidth"/>.</param>
/// <param name="RemainderX">Letterbox width on the right, in device pixels. Never negative.</param>
/// <param name="RemainderY">Letterbox height along the bottom, in device pixels. Never negative.</param>
/// <param name="IsPixelExact">
/// <c>true</c> when the viewport is exactly grid-sized, i.e. no cell is fractionally scaled. A
/// non-zero remainder does <b>not</b> make this false — letterboxing is the intended outcome.
/// </param>
/// <param name="IsClipped">
/// <c>true</c> only in the degenerate case where the grid is larger than the swapchain, so the
/// viewport had to be clamped. See <see cref="Compute"/>.
/// </param>
internal readonly record struct GpuGridLayout(
    int  SwapchainWidth,
    int  SwapchainHeight,
    int  GridWidth,
    int  GridHeight,
    int  ViewportX,
    int  ViewportY,
    int  ViewportWidth,
    int  ViewportHeight,
    int  RemainderX,
    int  RemainderY,
    bool IsPixelExact,
    bool IsClipped)
{
    /// <summary><c>true</c> when there is anything to draw at all.</summary>
    internal bool IsRenderable => ViewportWidth > 0 && ViewportHeight > 0;

    /// <summary>
    /// Computes the pixel-exact placement of a <paramref name="cols"/> x <paramref name="rows"/>
    /// grid of <paramref name="cellWidth"/> x <paramref name="cellHeight"/> cells inside a
    /// <paramref name="swapchainWidth"/> x <paramref name="swapchainHeight"/> target.
    /// <para>
    /// Placement policy: the grid is anchored <b>top-left</b>, and every leftover pixel is left as
    /// letterbox on the right and bottom. Top-left was chosen because the driver already treats
    /// cell (0,0) as the window origin everywhere else — mouse hit-testing in
    /// <c>SdlMouseTranslator.PixelToCell</c> divides raw window pixels by the cell size with no
    /// offset, so centring the grid would silently break mouse coordinates.
    /// </para>
    /// <para>
    /// Degenerate inputs are handled without throwing, dividing by zero, or producing a negative
    /// viewport: non-positive dimensions collapse to an unrenderable layout, and a grid larger
    /// than the swapchain (only reachable when the driver's minimum column/row floor exceeds what
    /// the window can show) clamps the viewport and reports <see cref="IsClipped"/>. Clamping does
    /// compress those cells, which is why <see cref="IsPixelExact"/> becomes <c>false</c> there.
    /// </para>
    /// </summary>
    internal static GpuGridLayout Compute(
        int swapchainWidth,
        int swapchainHeight,
        int cols,
        int rows,
        int cellWidth,
        int cellHeight)
    {
        int swapW = Math.Max(0, swapchainWidth);
        int swapH = Math.Max(0, swapchainHeight);

        // A non-positive cell size or column/row count cannot produce a grid at all.
        int gridW = cols > 0 && cellWidth  > 0 ? cols * cellWidth  : 0;
        int gridH = rows > 0 && cellHeight > 0 ? rows * cellHeight : 0;

        int viewportW = Math.Min(gridW, swapW);
        int viewportH = Math.Min(gridH, swapH);

        bool clipped = gridW > swapW || gridH > swapH;

        // Letterbox is what the grid does NOT cover; never negative.
        int remainderX = Math.Max(0, swapW - gridW);
        int remainderY = Math.Max(0, swapH - gridH);

        bool pixelExact =
            viewportW == gridW && viewportH == gridH && gridW > 0 && gridH > 0;

        return new GpuGridLayout(
            SwapchainWidth:  swapW,
            SwapchainHeight: swapH,
            GridWidth:       gridW,
            GridHeight:      gridH,
            ViewportX:       0,
            ViewportY:       0,
            ViewportWidth:   viewportW,
            ViewportHeight:  viewportH,
            RemainderX:      remainderX,
            RemainderY:      remainderY,
            IsPixelExact:    pixelExact,
            IsClipped:       clipped);
    }

    /// <summary>
    /// Source/destination copy size for a 1:1 blit of a <paramref name="sourceWidth"/> x
    /// <paramref name="sourceHeight"/> screen texture into this layout's viewport rectangle.
    /// <para>
    /// The same value is used for both rectangles — that is what makes the copy unscaled. It is
    /// the smaller of the viewport and the source so the blit can never sample outside the screen
    /// texture nor write outside the swapchain.
    /// </para>
    /// </summary>
    internal (int Width, int Height) ComputeOneToOneCopySize(int sourceWidth, int sourceHeight) =>
        (Math.Max(0, Math.Min(ViewportWidth,  sourceWidth)),
         Math.Max(0, Math.Min(ViewportHeight, sourceHeight)));

    /// <summary>Diagnostic line describing this layout. Format is stable enough to grep.</summary>
    internal string Describe(int cellWidth, int cellHeight, int cols, int rows) =>
        $"swapchain={SwapchainWidth}x{SwapchainHeight} grid={GridWidth}x{GridHeight} " +
        $"viewport={ViewportX},{ViewportY} {ViewportWidth}x{ViewportHeight} " +
        $"cell={cellWidth}x{cellHeight} cols/rows={cols}x{rows} " +
        $"remainder={RemainderX}x{RemainderY} " +
        $"letterbox=right:{RemainderX} bottom:{RemainderY} " +
        $"pixelExact={(IsPixelExact ? "yes" : "no")}" +
        (IsClipped ? " clipped=yes (grid larger than swapchain)" : "");
}
