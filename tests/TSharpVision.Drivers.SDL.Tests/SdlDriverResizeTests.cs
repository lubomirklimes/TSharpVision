// SDL driver resize tests.
// All tests run headless (TSHARPVISION_NO_SDL=1) or use pure static helpers.
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class SdlDriverResizeTests
{
    // ── CalculateGridSize (pure, no SDL required) ─────────────────────────

    [Fact]
    public void CalculateGridSize_TypicalWindow_ReturnsCorrectGrid()
    {
        // 120 cols × 37 rows at cell 14×26
        var (cols, rows) = SDLDriver.CalculateGridSize(120 * 14, 37 * 26, 14, 26);
        Assert.Equal(120, cols);
        Assert.Equal(37,  rows);
    }

    [Fact]
    public void CalculateGridSize_LargerWindow_ReturnsMoreCells()
    {
        var (cols, rows) = SDLDriver.CalculateGridSize(1920, 1080, 14, 26);
        Assert.Equal(1920 / 14, cols);
        Assert.Equal(1080 / 26, rows);
    }

    [Fact]
    public void CalculateGridSize_SmallWindow_ClampsToMinimum()
    {
        // Width narrower than two cells: clamps to 2 cols.
        var (cols, rows) = SDLDriver.CalculateGridSize(1, 1, 14, 26);
        Assert.Equal(2, cols);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void CalculateGridSize_ExactMultiple_NoRemainder()
    {
        var (cols, rows) = SDLDriver.CalculateGridSize(280, 520, 14, 26);
        Assert.Equal(20, cols);
        Assert.Equal(20, rows);
    }

    [Fact]
    public void CalculateGridSize_WithRemainder_TruncatesDown()
    {
        // 285 / 14 = 20 remainder 5  →  20
        // 530 / 26 = 20 remainder 10 →  20
        var (cols, rows) = SDLDriver.CalculateGridSize(285, 530, 14, 26);
        Assert.Equal(20, cols);
        Assert.Equal(20, rows);
    }

    // ── SDLDriver headless: default grid dimensions ────────────────────────

    [Fact]
    public void SDLDriver_DefaultCols_Is120()
    {
        Assert.Equal(120, new SDLDriver().GetCols());
    }

    [Fact]
    public void SDLDriver_DefaultRows_Is37()
    {
        Assert.Equal(37, new SDLDriver().GetRows());
    }

    // ── SDLDriver headless: lifecycle still works after adding resize fields ──

    [Fact]
    public void SDLDriver_HeadlessInitShutdown_GridStable()
    {
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", "1");
        try
        {
            var d = new SDLDriver();
            d.Initialize();
            // Grid must remain at defaults when SDL never attaches.
            Assert.Equal(120, d.GetCols());
            Assert.Equal(37,  d.GetRows());
            d.Shutdown();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", null);
        }
    }
}
