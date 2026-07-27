using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class AccessoryAppearanceTests
{
    private static TGroup Render(TWindow window)
    {
        var root = new TGroup(new TRect(0, 0, 80, 25)) { buffer = new ScreenBuffer(80 * 25) };
        root.state |= Views.sfVisible | Views.sfExposed;
        window.MoveTo(0, 0);
        root.Insert(window);
        window.state |= Views.sfExposed | Views.sfVisible | Views.sfActive;
        window.DrawView();
        return root;
    }

    private static string Row(TGroup root, int y, int width) =>
        new(root.buffer.Data.Slice(y * 80, width).ToArray().Select(c => c.Character).ToArray());

    [Fact]
    public void ChartRendersDosGlyphsAndUpdatesSelectionReadout()
    {
        using var driver = new DriverScope();
        var chart = new AsciiTableDialog();
        var root = Render(chart);
        try
        {
            Assert.Contains("ASCII Chart", Row(root, 0, chart.size.x));
            Assert.Equal('☺', root.buffer.Data[80 + 2].Character);
            Assert.Equal('█', root.buffer.Data[(1 + 219 / 32) * 80 + 1 + 219 % 32].Character);
            AsciiTableBody? body = null;
            chart.ForEachView(v => { if (v is AsciiTableBody b) body = b; });
            Assert.NotNull(body);
            var ev = new TEvent { What = Events.evKeyboard };
            ev.keyDown.keyCode = Keys.kbEnd;
            body.HandleEvent(ref ev);
            Assert.Equal(255, body.SelectedCode);
            Assert.Contains("Decimal: 255", Row(root, 11, chart.size.x));
            Assert.Contains("Hex: FF", Row(root, 11, chart.size.x));
        }
        finally { root.ShutDown(); }
    }

    [Fact]
    public void CalendarAndCalculatorHaveCompleteTitlesAndReferenceColors()
    {
        using var driver = new DriverScope();
        var calendar = new CalendarDialog();
        var root = Render(calendar);
        try
        {
            Assert.Contains("Calendar", Row(root, 0, calendar.size.x));
            Assert.Equal('▲', root.buffer.Data[80 + 2 + 17].Character);
            Assert.Equal('▼', root.buffer.Data[80 + 2 + 20].Character);
            Assert.Equal((byte)0x3E, (byte)root.buffer.Data[80 + 2].Attr);
        }
        finally { root.ShutDown(); }
        var calculator = new CalculatorDialog();
        root = Render(calculator);
        try
        {
            Assert.Contains("Calculator", Row(root, 0, calculator.size.x));
            Assert.Equal((byte)0x1F, (byte)root.buffer.Data[2 * 80 + 3].Attr);
            Assert.Equal((byte)0x20, (byte)root.buffer.Data[4 * 80 + 5].Attr);
            Assert.Equal((byte)0x70, (byte)root.buffer.Data[80 + 1].Attr);
        }
        finally { root.ShutDown(); }
    }

    [Fact]
    public void PuzzleCheckerboardColorsTravelWithTilesAndCounterUpdates()
    {
        using var driver = new DriverScope();
        var puzzle = new PuzzleDialog();
        puzzle.Model.Reset();
        var root = Render(puzzle);
        try
        {
            Assert.Equal((byte)0x1E, (byte)root.buffer.Data[80 + 2].Attr); // A
            Assert.Equal((byte)0x71, (byte)root.buffer.Data[80 + 5].Attr); // B
            Assert.Equal((byte)0x71, (byte)root.buffer.Data[2 * 80 + 2].Attr); // E
            Assert.True(puzzle.Model.TryMove(3, 2)); // O slides into the blank.
            puzzle.View.DrawView();
            Assert.Equal('O', root.buffer.Data[4 * 80 + 11].Character);
            Assert.Equal((byte)0x71, (byte)root.buffer.Data[4 * 80 + 11].Attr);
            Assert.Equal('1', root.buffer.Data[3 * 80 + 16].Character);
            var close = new TEvent { What = Events.evCommand };
            close.message.command = Views.cmClose;
            puzzle.HandleEvent(ref close);
            Assert.Null(puzzle.owner);
            Assert.Equal(Events.evNothing, close.What);
        }
        finally { root.ShutDown(); }
    }
}
