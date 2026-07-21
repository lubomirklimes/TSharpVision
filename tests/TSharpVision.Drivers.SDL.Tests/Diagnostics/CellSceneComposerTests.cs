using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests.Diagnostics;

public class CellSceneComposerTests
{
    private static CellSceneComposer Make(int w = 11, int h = 20) =>
        new(new Cp437GlyphGenerator(w, h));

    [Fact]
    public void ComposeScene_SizesTheBitmapToTheCellGrid()
    {
        AlphaBitmap scene = Make().ComposeScene(["┌─┐", "│ │", "└─┘"]);

        Assert.Equal(3 * 11, scene.Width);
        Assert.Equal(3 * 20, scene.Height);
    }

    [Fact]
    public void ComposeScene_PadsShortRowsInsteadOfThrowing()
    {
        AlphaBitmap scene = Make().ComposeScene(["████", "█"]);

        Assert.Equal(4 * 11, scene.Width);
        Assert.Equal(2 * 20, scene.Height);

        // Row 1, cell 3 was never written.
        Assert.Equal(0, scene[3 * 11, 20]);
    }

    [Fact]
    public void ComposeScene_ReportsUnsupportedCharactersRatherThanThrowing()
    {
        AlphaBitmap scene = Make().ComposeScene(["█A■"], 0, 0, out IReadOnlyCollection<char> unsupported);

        Assert.Equal(new[] { 'A', '■' }, unsupported.OrderBy(static c => c));
        Assert.Equal(255, scene[0, 0]);          // the generated cell is present
        Assert.Equal(0, scene[11, 0]);           // the unsupported cells are blank
    }

    [Fact]
    public void ComposeFill_ProducesAContinuousFullBlockField()
    {
        AlphaBitmap field = Make().ComposeFill('█', 4, 3);

        Assert.All(field.Alpha, a => Assert.Equal(255, a));
    }

    [Fact]
    public void ComposeScene_AppliesTheCellPhaseSoTheDitherIsContinuous()
    {
        // Cell width 11 is odd, so cells 0 and 1 have different phases.
        AlphaBitmap field = Make(11, 20).ComposeFill('░', 2, 1);

        for (int x = 0; x < field.Width; x++)
            Assert.Equal(Cp437ShadingLattice.HasInk(0xB0, x, 0), field[x, 0] != 0);
    }

    [Fact]
    public void ComposeScene_OriginOffsetShiftsThePhase()
    {
        var composer = Make(11, 20);

        AlphaBitmap atZero = composer.ComposeScene(["░"], 0, 0, out _);
        AlphaBitmap atOne  = composer.ComposeScene(["░"], 1, 0, out _);

        Assert.NotEqual(atZero.Alpha, atOne.Alpha);
    }

    [Fact]
    public void ComposeScene_RejectsAnEmptyScene()
    {
        Assert.Throws<ArgumentException>(() => Make().ComposeScene([]));
        Assert.Throws<ArgumentException>(() => Make().ComposeScene([""]));
    }
}
