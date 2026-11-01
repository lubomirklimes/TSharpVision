using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// Pointer hit-testing while Curvature is on screen: the driver maps a drawable pixel through the
/// shader's destination-to-source formula before choosing a cell, and only while the warp is presented.
/// </summary>
[Collection("SDL initialization")]
public sealed class SDLGpuDriverCurvaturePointerTests
{
    // 80x25 cells of 9x18 at 1x: a 720x450 grid in a window of the same size.
    private const int Cols = 80, Rows = 25, CellW = 9, CellH = 18;
    private static readonly SdlDisplayScale Scale = SdlDisplayScale.Create(Cols * CellW, Rows * CellH, Cols * CellW, Rows * CellH, 1f);

    private static SDLGpuDriver Attached(GpuDisplayEffect? presented)
    {
        var d = new SDLGpuDriver { PresentedEffectOverrideForTesting = presented };
        d.AttachForTest(SdlCellMetrics.ForDensity(CellW, CellH, 1f), Scale, Cols, Rows);
        return d;
    }

    private static TPoint Expected(int px, int py)
    {
        GpuCurvature.MapPixelToSourcePixel(px, py, Cols * CellW, Rows * CellH, out int sx, out int sy);
        return SdlMouseTranslator.PixelToCell(sx, sy, CellW, CellH);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Scanlines")]
    [InlineData("Glow")]
    [InlineData("ShadowMask")]
    public void OtherEffectsKeepTheDirectMapping(string presentedId)
    {
        Assert.True(GpuDisplayEffects.TryParse(presentedId, out GpuDisplayEffect presented));
        using var d = Attached(presented);
        for (int px = 0; px < Cols * CellW; px += 37)
            for (int py = 0; py < Rows * CellH; py += 29)
                Assert.Equal(SdlMouseTranslator.PixelToCell(px, py, CellW, CellH), d.WindowToCellForTest(px + 0.5f, py + 0.5f));
    }

    [Fact]
    public void CurvatureMapsEveryPointerPositionThroughTheWarp()
    {
        using var d = Attached(GpuDisplayEffect.Curvature);
        int displaced = 0;
        for (int px = 0; px < Cols * CellW; px += 7)
            for (int py = 0; py < Rows * CellH; py += 5)
            {
                TPoint cell = d.WindowToCellForTest(px + 0.5f, py + 0.5f);
                Assert.Equal(Expected(px, py), cell);
                Assert.InRange(cell.x, 0, Cols - 1);
                Assert.InRange(cell.y, 0, Rows - 1);
                if (cell != SdlMouseTranslator.PixelToCell(px, py, CellW, CellH)) displaced++;
            }
        Assert.True(displaced > 0, "the warp must move some hits away from the unwarped cell");
    }

    [Fact]
    public void CentreIsUnchangedAndCornersClampToTheEdgeCell()
    {
        using var d = Attached(GpuDisplayEffect.Curvature);
        Assert.Equal(new TPoint(Cols / 2, Rows / 2), d.WindowToCellForTest(Cols * CellW / 2f + 0.5f, Rows * CellH / 2f + 0.5f));
        Assert.Equal(new TPoint(0, 0), d.WindowToCellForTest(0.5f, 0.5f));
        Assert.Equal(new TPoint(Cols - 1, Rows - 1), d.WindowToCellForTest(Cols * CellW - 0.5f, Rows * CellH - 0.5f));
    }

    [Fact]
    public void FallbackToDefaultRestoresTheDirectMapping()
    {
        using var d = Attached(GpuDisplayEffect.Curvature);
        Assert.Equal(Expected(30, 20), d.WindowToCellForTest(30.5f, 20.5f));
        d.PresentedEffectOverrideForTesting = GpuDisplayEffect.Default; // e.g. CPU fallback or failed pipeline
        Assert.Equal(SdlMouseTranslator.PixelToCell(30, 20, CellW, CellH), d.WindowToCellForTest(30.5f, 20.5f));
    }

    [Fact]
    public void DetachedDriverReportsDefault()
    {
        using var d = new SDLGpuDriver();
        Assert.Equal(GpuDisplayEffect.Default, d.PresentedEffect);
    }
}
