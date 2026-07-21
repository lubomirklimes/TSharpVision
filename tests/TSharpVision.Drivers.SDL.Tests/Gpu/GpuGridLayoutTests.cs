// Tests for the pixel-exact GPU grid layout (see docs/sdl-rendering.md).
//
// GpuGridLayout is deliberately SDL-independent, so the placement decision that the shader
// path, the CPU-compositing fallback and the diagnostics all share can be verified as pure maths —
// no GPU device, window or swapchain is created anywhere in this file.
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

public sealed class GpuGridLayoutTests
{
    // The demo's measured configuration: JetBrains Mono 18pt -> 11x24 cells, 120x37 grid.
    private const int CellW = 11;
    private const int CellH = 24;
    private const int Cols  = 120;
    private const int Rows  = 37;
    private const int GridW = Cols * CellW;   // 1320
    private const int GridH = Rows * CellH;   //  888

    // ── Exact multiple ─────────────────────────────────────────────────────────

    [Fact]
    public void ExactMultiple_UsesTheWholeTargetWithNoRemainder()
    {
        var layout = GpuGridLayout.Compute(GridW, GridH, Cols, Rows, CellW, CellH);

        Assert.Equal(GridW, layout.GridWidth);
        Assert.Equal(GridH, layout.GridHeight);
        Assert.Equal(GridW, layout.ViewportWidth);
        Assert.Equal(GridH, layout.ViewportHeight);
        Assert.Equal(0, layout.ViewportX);
        Assert.Equal(0, layout.ViewportY);
        Assert.Equal(0, layout.RemainderX);
        Assert.Equal(0, layout.RemainderY);
        Assert.True(layout.IsPixelExact);
        Assert.False(layout.IsClipped);
    }

    // ── Remainders ─────────────────────────────────────────────────────────────

    [Fact]
    public void HorizontalRemainder_LeavesTheViewportGridSized()
    {
        var layout = GpuGridLayout.Compute(1325, GridH, Cols, Rows, CellW, CellH);

        Assert.Equal(GridW, layout.GridWidth);
        Assert.Equal(GridW, layout.ViewportWidth);     // NOT stretched to 1325
        Assert.Equal(GridH, layout.ViewportHeight);
        Assert.Equal(5, layout.RemainderX);
        Assert.Equal(0, layout.RemainderY);
        Assert.True(layout.IsPixelExact);              // a remainder is not an error
    }

    [Fact]
    public void VerticalRemainder_LeavesTheViewportGridSized()
    {
        var layout = GpuGridLayout.Compute(GridW, 895, Cols, Rows, CellW, CellH);

        Assert.Equal(GridH, layout.GridHeight);
        Assert.Equal(GridH, layout.ViewportHeight);    // NOT stretched to 895
        Assert.Equal(0, layout.RemainderX);
        Assert.Equal(7, layout.RemainderY);
        Assert.True(layout.IsPixelExact);
    }

    [Fact]
    public void BothRemainders_LeaveTheViewportExactlyGridSized()
    {
        var layout = GpuGridLayout.Compute(1325, 895, Cols, Rows, CellW, CellH);

        Assert.Equal(1320, layout.GridWidth);
        Assert.Equal(888,  layout.GridHeight);
        Assert.Equal(1320, layout.ViewportWidth);
        Assert.Equal(888,  layout.ViewportHeight);
        Assert.Equal(5, layout.RemainderX);
        Assert.Equal(7, layout.RemainderY);
        Assert.True(layout.IsPixelExact);
        Assert.False(layout.IsClipped);
    }

    [Fact]
    public void GridIsAnchoredTopLeft_SoLeftoverPixelsAreOnTheRightAndBottom()
    {
        var layout = GpuGridLayout.Compute(1325, 895, Cols, Rows, CellW, CellH);

        // Top-left anchor keeps cell (0,0) at window pixel (0,0), which is what
        // SdlMouseTranslator.PixelToCell already assumes.
        Assert.Equal(0, layout.ViewportX);
        Assert.Equal(0, layout.ViewportY);
        Assert.Equal(layout.SwapchainWidth  - layout.GridWidth,  layout.RemainderX);
        Assert.Equal(layout.SwapchainHeight - layout.GridHeight, layout.RemainderY);
    }

    // ── Representative cell sizes from the diagnostics runs ────────────────────

    public static TheoryData<int, int> RepresentativeCells => new()
    {
        { 11, 20 },   // Consolas 20pt
        { 12, 23 },   // Courier New 20pt
        { 12, 24 },   // Cascadia Mono 20pt / JetBrains Mono 18pt
        { 24, 43 },   // Consolas 43pt — the 2px stroke run
    };

    [Theory]
    [MemberData(nameof(RepresentativeCells))]
    public void ViewportAlwaysEqualsTheGrid_ForRepresentativeCellSizes(int cellW, int cellH)
    {
        // Sweep a range of swapchain sizes around several exact multiples, so most cases have a
        // remainder on one or both axes.
        for (int cols = 1; cols <= 40; cols += 7)
        {
            for (int rows = 1; rows <= 40; rows += 9)
            {
                for (int extraW = 0; extraW < cellW; extraW += 3)
                {
                    for (int extraH = 0; extraH < cellH; extraH += 3)
                    {
                        int swapW = cols * cellW + extraW;
                        int swapH = rows * cellH + extraH;

                        var layout = GpuGridLayout.Compute(swapW, swapH, cols, rows, cellW, cellH);

                        Assert.Equal(cols * cellW, layout.ViewportWidth);
                        Assert.Equal(rows * cellH, layout.ViewportHeight);
                        Assert.Equal(extraW, layout.RemainderX);
                        Assert.Equal(extraH, layout.RemainderY);
                        Assert.True(layout.IsPixelExact);
                    }
                }
            }
        }
    }

    // ── Mathematical invariants ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RepresentativeCells))]
    public void ViewportMatchesColsRowsTimesCellSize_AndFitsInsideTheSwapchain(int cellW, int cellH)
    {
        for (int swapW = 1; swapW <= 1400; swapW += 97)
        {
            for (int swapH = 1; swapH <= 950; swapH += 103)
            {
                // The driver's own floor policy: at least 2 columns and 1 row.
                int cols = Math.Max(2, swapW / cellW);
                int rows = Math.Max(1, swapH / cellH);

                var layout = GpuGridLayout.Compute(swapW, swapH, cols, rows, cellW, cellH);

                Assert.Equal(cols * cellW, layout.GridWidth);
                Assert.Equal(rows * cellH, layout.GridHeight);

                // The viewport never exceeds the target — that would be a GPU validation error.
                Assert.True(layout.ViewportWidth  <= layout.SwapchainWidth);
                Assert.True(layout.ViewportHeight <= layout.SwapchainHeight);
                Assert.True(layout.ViewportWidth  >= 0);
                Assert.True(layout.ViewportHeight >= 0);

                // Remainder is never negative and is always strictly less than one cell whenever
                // the grid came from the floor policy without hitting the minimum.
                Assert.True(layout.RemainderX >= 0);
                Assert.True(layout.RemainderY >= 0);

                if (!layout.IsClipped)
                {
                    Assert.True(layout.RemainderX < cellW);
                    Assert.True(layout.RemainderY < cellH);
                    Assert.True(layout.IsPixelExact);
                }
            }
        }
    }

    [Fact]
    public void FloorDivision_KeepsTheGridInsideTheSwapchain()
    {
        // The invariant the driver's HandleWindowResize is expected to satisfy:
        //   0 <= swap - grid < cell   (whenever the minimum floor is not in play)
        for (int swapW = 100; swapW <= 1400; swapW += 37)
        {
            int cols = swapW / CellW;
            if (cols < 2) continue;

            var layout = GpuGridLayout.Compute(swapW, GridH, cols, Rows, CellW, CellH);

            Assert.True(layout.GridWidth <= layout.SwapchainWidth);
            Assert.InRange(layout.SwapchainWidth - layout.GridWidth, 0, CellW - 1);
        }
    }

    // ── Tiny / degenerate sizes ────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(-10, -10)]
    public void TinyOrNegativeSwapchain_ProducesASafeLayout(int swapW, int swapH)
    {
        // Uses the driver's minimum floor, which can demand a grid larger than the window.
        var layout = GpuGridLayout.Compute(swapW, swapH, cols: 2, rows: 1, CellW, CellH);

        Assert.True(layout.ViewportWidth  >= 0);
        Assert.True(layout.ViewportHeight >= 0);
        Assert.True(layout.ViewportWidth  <= layout.SwapchainWidth);
        Assert.True(layout.ViewportHeight <= layout.SwapchainHeight);
        Assert.True(layout.RemainderX >= 0);
        Assert.True(layout.RemainderY >= 0);
    }

    [Fact]
    public void SwapchainSmallerThanOneCell_IsReportedAsClippedAndNotPixelExact()
    {
        var layout = GpuGridLayout.Compute(
            swapchainWidth: 5, swapchainHeight: 5, cols: 2, rows: 1, CellW, CellH);

        Assert.True(layout.IsClipped);
        Assert.False(layout.IsPixelExact);   // the grid cannot be shown 1:1 at this size
        Assert.Equal(5, layout.ViewportWidth);
        Assert.Equal(5, layout.ViewportHeight);
        Assert.Equal(0, layout.RemainderX);  // nothing left over — the grid overflows instead
        Assert.Equal(0, layout.RemainderY);
    }

    [Fact]
    public void ZeroSizedSwapchain_IsNotRenderable()
    {
        var layout = GpuGridLayout.Compute(0, 0, Cols, Rows, CellW, CellH);

        Assert.False(layout.IsRenderable);
        Assert.Equal(0, layout.ViewportWidth);
        Assert.Equal(0, layout.ViewportHeight);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(-1, -1)]
    public void NonPositiveColsRowsOrCellSize_CollapseSafely(int cols, int rows)
    {
        var layout = GpuGridLayout.Compute(1320, 888, cols, rows, CellW, CellH);

        Assert.True(layout.GridWidth  >= 0);
        Assert.True(layout.GridHeight >= 0);
        Assert.True(layout.ViewportWidth  >= 0);
        Assert.True(layout.ViewportHeight >= 0);
    }

    [Fact]
    public void ZeroCellSize_DoesNotDivideByZeroOrThrow()
    {
        var layout = GpuGridLayout.Compute(1320, 888, Cols, Rows, cellWidth: 0, cellHeight: 0);

        Assert.Equal(0, layout.GridWidth);
        Assert.Equal(0, layout.GridHeight);
        Assert.False(layout.IsRenderable);
        Assert.False(layout.IsPixelExact);
    }

    // ── CPU-compositing fallback: 1:1 copy rectangles ─────────────────────────

    [Fact]
    public void FallbackCopy_UsesEqualSourceAndDestinationSizes_SoNothingIsScaled()
    {
        // The CPU fallback's screen texture is always grid-sized.
        var layout = GpuGridLayout.Compute(1325, 895, Cols, Rows, CellW, CellH);

        (int w, int h) = layout.ComputeOneToOneCopySize(layout.GridWidth, layout.GridHeight);

        // One size drives both rectangles, so source and destination are equal by construction.
        Assert.Equal(layout.GridWidth,  w);
        Assert.Equal(layout.GridHeight, h);

        // And the destination never spills into the letterbox.
        Assert.True(w <= layout.SwapchainWidth);
        Assert.True(h <= layout.SwapchainHeight);
    }

    [Theory]
    [MemberData(nameof(RepresentativeCells))]
    public void FallbackCopy_NeverRequestsScaling_WhenTheGridFits(int cellW, int cellH)
    {
        for (int cols = 2; cols <= 30; cols += 7)
        {
            for (int rows = 1; rows <= 30; rows += 9)
            {
                int gridW = cols * cellW;
                int gridH = rows * cellH;

                // Swapchain at least as large as the grid, with an arbitrary remainder.
                var layout = GpuGridLayout.Compute(
                    gridW + cellW - 1, gridH + cellH - 1, cols, rows, cellW, cellH);

                (int w, int h) = layout.ComputeOneToOneCopySize(gridW, gridH);

                // Copy size equals the source size => the blit is 1:1, never a stretch.
                Assert.Equal(gridW, w);
                Assert.Equal(gridH, h);
            }
        }
    }

    [Fact]
    public void FallbackCopy_ClampsToTheSmallerOfViewportAndSource()
    {
        var layout = GpuGridLayout.Compute(1325, 895, Cols, Rows, CellW, CellH);

        // A stale, smaller screen texture must not be sampled out of bounds.
        (int w, int h) = layout.ComputeOneToOneCopySize(100, 50);
        Assert.Equal(100, w);
        Assert.Equal(50,  h);

        // A larger one must not be written out of bounds.
        (int w2, int h2) = layout.ComputeOneToOneCopySize(99999, 99999);
        Assert.Equal(layout.ViewportWidth,  w2);
        Assert.Equal(layout.ViewportHeight, h2);
    }

    [Fact]
    public void FallbackCopy_IsSkippableWhenEitherDimensionIsZero()
    {
        var layout = GpuGridLayout.Compute(0, 0, Cols, Rows, CellW, CellH);

        (int w, int h) = layout.ComputeOneToOneCopySize(1320, 888);

        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void FallbackBlitFilter_IsNearest_ForOneToOnePixelGraphics()
    {
        // Linear would resample; with equal rectangles it should be a no-op, but Nearest makes
        // the 1:1 intent explicit and removes any driver-dependent edge interpolation.
        Assert.Equal(SDL3.SDL.GPUFilter.Nearest, SDLGpuRenderer.OneToOneBlitFilter);
    }

    // ── Diagnostics text ───────────────────────────────────────────────────────

    [Fact]
    public void Describe_ReportsPixelExactYes_EvenWithANonZeroRemainder()
    {
        var layout = GpuGridLayout.Compute(1325, 895, Cols, Rows, CellW, CellH);

        string text = layout.Describe(CellW, CellH, Cols, Rows);

        Assert.Contains("swapchain=1325x895", text);
        Assert.Contains("grid=1320x888", text);
        Assert.Contains("viewport=0,0 1320x888", text);
        Assert.Contains("remainder=5x7", text);
        Assert.Contains("letterbox=right:5 bottom:7", text);
        Assert.Contains("pixelExact=yes", text);
    }

    [Fact]
    public void Describe_FlagsTheClippedDegenerateCase()
    {
        var layout = GpuGridLayout.Compute(5, 5, cols: 2, rows: 1, CellW, CellH);

        string text = layout.Describe(CellW, CellH, 2, 1);

        Assert.Contains("pixelExact=no", text);
        Assert.Contains("clipped=yes", text);
    }
}

/// <summary>
/// Proves the NDC-to-device-pixel mapping is exact under the grid-sized viewport, as pure maths —
/// no GPU device is needed to verify the arithmetic the vertex shader and viewport transform
/// perform together.
/// </summary>
public sealed class GpuNdcMappingTests
{
    /// <summary>
    /// Reproduces <c>TerminalGpuPipeline.RenderFrame</c>'s vertex maths for a cell boundary,
    /// then applies the standard viewport transform the GPU performs:
    /// <c>deviceX = (ndcX + 1) / 2 * viewportWidth + viewportX</c>.
    /// </summary>
    private static double BoundaryToDevicePixelX(int col, int cellWidth, int cols, int viewportWidth)
    {
        float invW = 2f / (cols * cellWidth);          // exactly as the pipeline computes it
        float ndcX = col * cellWidth * invW - 1f;      // exactly as the pipeline computes it
        return (ndcX + 1.0) / 2.0 * viewportWidth;
    }

    private static double BoundaryToDevicePixelY(int row, int cellHeight, int rows, int viewportHeight)
    {
        float invH = 2f / (rows * cellHeight);
        float ndcY = 1f - row * cellHeight * invH;
        // Y is inverted by the viewport transform: deviceY = (1 - ndcY) / 2 * height.
        return (1.0 - ndcY) / 2.0 * viewportHeight;
    }

    public static TheoryData<int, int> CellAndGrid => new()
    {
        { 11, 120 },   // odd cell width — the case most at risk of accumulating error
        { 12, 120 },
        { 23, 37 },
        { 24, 37 },
        { 43, 20 },
    };

    [Theory]
    [MemberData(nameof(CellAndGrid))]
    public void EveryColumnBoundaryLandsOnItsExactIntegerPixel(int cellWidth, int cols)
    {
        int viewportWidth = cols * cellWidth;   // the viewport invariant

        for (int col = 0; col <= cols; col++)
        {
            double device   = BoundaryToDevicePixelX(col, cellWidth, cols, viewportWidth);
            double expected = col * cellWidth;

            // Float32 invW introduces at most ~1e-4 px of error at these magnitudes — far below
            // the 0.5 px distance from any pixel centre, so rasterisation cannot pick a different
            // pixel. Assert well inside that margin.
            Assert.True(Math.Abs(device - expected) < 0.01,
                $"col {col}: device {device}, expected {expected}");
        }
    }

    [Theory]
    [MemberData(nameof(CellAndGrid))]
    public void EveryRowBoundaryLandsOnItsExactIntegerPixel(int cellHeight, int rows)
    {
        int viewportHeight = rows * cellHeight;

        for (int row = 0; row <= rows; row++)
        {
            double device   = BoundaryToDevicePixelY(row, cellHeight, rows, viewportHeight);
            double expected = row * cellHeight;

            Assert.True(Math.Abs(device - expected) < 0.01,
                $"row {row}: device {device}, expected {expected}");
        }
    }

    [Fact]
    public void CellWidthsDoNotAlternate_SoNoColumnIsWiderThanAnother()
    {
        // The visual symptom of fractional scaling is neighbouring columns rendering at different
        // widths. With a grid-sized viewport every consecutive boundary gap is exactly cellWidth.
        const int cellWidth = 11, cols = 120;
        int viewportWidth = cols * cellWidth;

        for (int col = 0; col < cols; col++)
        {
            double left  = BoundaryToDevicePixelX(col,     cellWidth, cols, viewportWidth);
            double right = BoundaryToDevicePixelX(col + 1, cellWidth, cols, viewportWidth);

            Assert.True(Math.Abs((right - left) - cellWidth) < 0.01,
                $"col {col} width {right - left}, expected {cellWidth}");
        }
    }

    /// <summary>
    /// Demonstrates the defect this phase fixes: with the old full-swapchain mapping, a swapchain
    /// that is not an exact multiple of the cell size put boundaries on fractional pixels.
    /// </summary>
    [Fact]
    public void OldFullSwapchainMapping_WasFractional_WhereTheNewOneIsExact()
    {
        const int cellWidth = 11, cols = 120;
        const int gridWidth = cols * cellWidth;   // 1320
        const int swapchainWidth = 1325;          // 5 px remainder

        // Old behaviour: NDC spanned the whole swapchain.
        double oldBoundary = BoundaryToDevicePixelX(1, cellWidth, cols, swapchainWidth);
        Assert.True(Math.Abs(oldBoundary - cellWidth) > 0.01,
            "the old mapping should have been fractional here");

        // New behaviour: NDC is confined to a grid-sized viewport.
        double newBoundary = BoundaryToDevicePixelX(1, cellWidth, cols, gridWidth);
        Assert.True(Math.Abs(newBoundary - cellWidth) < 0.01);
    }
}
