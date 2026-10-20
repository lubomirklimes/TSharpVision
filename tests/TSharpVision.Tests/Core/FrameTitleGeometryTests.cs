using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

// TW-F2: TFrame.Draw title layout at every width. The title budget is width - 10, minus 6 for close/zoom and 4 for a
// window number; at or below zero no title is drawn, and a truncated title (" .." + tail) needs a budget of two.
// Every check reads the composed logical screen, so the frame is drawn through the real view tree.
[Collection("NonParallel")]
public sealed class FrameTitleGeometryTests
{
    private const string Ascii = "ABCDEFGH";

    /// <summary>A window whose minimum size is one cell, to reach widths below the standard 16-column limit.</summary>
    private sealed class NarrowWindow(TRect bounds, string title, ushort number) : TWindow(bounds, title, number)
    {
        public override void SizeLimits(ref TPoint min, ref TPoint max)
        {
            base.SizeLimits(ref min, ref max);
            min = new TPoint(1, 1);
        }
    }

    private static TWindow Show(TApplication app, TWindow window)
    {
        app.DeskTop!.Insert(window);
        window.SetState(Views.sfActive, true);
        app.Redraw();
        return window;
    }

    private static string Row(TView view, int localY)
    {
        TPoint at = view.MakeGlobal(new TPoint(0, localY));
        var cells = TScreen.ScreenBuffer!;
        var chars = new char[view.size.x];
        for (int x = 0; x < chars.Length; x++) chars[x] = cells.Data[at.y * TScreen.ScreenWidth + at.x + x].Character;
        return new string(chars);
    }

    private static int[] LetterColumns(string row) =>
        Enumerable.Range(0, row.Length).Where(i => char.IsLetter(row[i]) || char.IsSurrogate(row[i])).ToArray();

    private static void AssertFrameEdges(string top, int width)
    {
        Assert.Equal(width, top.Length);
        Assert.Equal('╔', top[0]);
        if (width >= 8) Assert.Equal('╗', top[^1]);
    }

    [Fact]
    public void NormalWidthCentresTheFullTitleBetweenTheButtons()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        var window = Show(app, new TWindow(new TRect(0, 0, 40, 8), "Hello", Views.wnNoNumber));
        string top = Row(window, 0);
        AssertFrameEdges(top, 40);
        Assert.Equal("[■]", top.Substring(2, 3));
        Assert.Equal("[↑]", top.Substring(35, 3));
        Assert.Equal(" Hello ", top.Substring(16, 7)); // i = (40 - 5) >> 1
        Assert.Equal(new string('═', 11), top.Substring(5, 11));
    }

    [Fact]
    public void JustSufficientWidthShowsTheFullTitleAndOneLessTruncatesIt()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        // 24 columns with close and zoom: budget 8 == title length.
        var full = Show(app, new TWindow(new TRect(0, 0, 24, 6), Ascii, Views.wnNoNumber));
        string top = Row(full, 0);
        AssertFrameEdges(top, 24);
        Assert.Equal(" ABCDEFGH ", top.Substring(7, 10));

        // 23 columns: budget 7 -> " .." + the last 5 characters, then the two historical trailing spaces.
        full.Locate(new TRect(0, 0, 23, 6));
        app.Redraw();
        top = Row(full, 0);
        AssertFrameEdges(top, 23);
        Assert.Equal(" ..DEFGH  ", top.Substring(7, 10));
        Assert.Equal("[↑]", top.Substring(18, 3));
    }

    [Fact]
    public void WindowNumberReducesTheBudgetAndStaysVisible()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        // 30 columns, number 3: budget 30 - 16 - 4 = 10.
        var window = Show(app, new TWindow(new TRect(0, 0, 30, 6), "Long window title", 3));
        string top = Row(window, 0);
        AssertFrameEdges(top, 30);
        Assert.Equal('3', top[30 - 7]);
        Assert.Equal(" ..ow title  ", top.Substring(9, 13));
    }

    [Theory]
    [InlineData(16)] // budget 0
    [InlineData(17)] // budget 1: threw ArgumentOutOfRangeException before TW-F2
    public void NoTitleIsDrawnWhenTheBudgetCannotHoldIt(int width)
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        var window = Show(app, new TWindow(new TRect(0, 0, width, 6), Ascii, Views.wnNoNumber));
        string top = Row(window, 0);
        AssertFrameEdges(top, width);
        Assert.Empty(LetterColumns(top));
        Assert.DoesNotContain("..", top);
    }

    [Fact]
    public void BudgetOfTwoShowsOnlyTheTruncationMarker()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        var window = Show(app, new TWindow(new TRect(0, 0, 18, 6), Ascii, Views.wnNoNumber));
        string top = Row(window, 0);
        AssertFrameEdges(top, 18);
        Assert.Equal(" ..  ", top.Substring(7, 5));
        Assert.Empty(LetterColumns(top));
    }

    [Fact]
    public void EmptyTitleDrawsAnUnbrokenTopEdge()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        var window = Show(app, new TWindow(new TRect(0, 0, 30, 6), "", Views.wnNoNumber));
        string top = Row(window, 0);
        AssertFrameEdges(top, 30);
        Assert.Equal(new string('═', 20), top.Substring(5, 20));
    }

    [Fact]
    public void NonAsciiTitleIsCentredAndTruncatedByUtf16UnitsWithoutSplittingASurrogatePair()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        const string czech = "Příliš žluťoučký";
        var window = Show(app, new TWindow(new TRect(0, 0, 40, 6), czech, Views.wnNoNumber));
        string top = Row(window, 0);
        Assert.Equal(" " + czech + " ", top.Substring(11, czech.Length + 2)); // i = (40 - 16) >> 1

        window.Locate(new TRect(0, 0, 30, 6)); // budget 14: tail of 12
        app.Redraw();
        top = Row(window, 0);
        Assert.Equal(" .." + czech[^12..] + "  ", top.Substring(7, 17));

        // "ab😀cd😀ef" is 10 UTF-16 units; budget 5 puts the tail start (index 7) on a low surrogate, which is skipped.
        const string emoji = "ab\U0001F600cd\U0001F600ef";
        window.title = emoji;
        window.Locate(new TRect(0, 0, 21, 6));
        app.Redraw();
        top = Row(window, 0);
        AssertFrameEdges(top, 21);
        Assert.Equal(" ..ef   ", top.Substring(7, 8)); // the skipped unit leaves a cleared title cell, not frame
        for (int x = 0; x < top.Length; x++)
            if (char.IsLowSurrogate(top[x])) Assert.True(x > 0 && char.IsHighSurrogate(top[x - 1]), $"lone low surrogate at {x}");
    }

    [Fact]
    public void RepeatedShrinkingAndExpandingDownToOneColumnNeverThrowsAndRestoresTheLayout()
    {
        using var scope = new DriverScope(80, 25);
        using var app = new TApplication();
        var window = Show(app, new NarrowWindow(new TRect(0, 0, 40, 6), "Relatively long title", 7));
        string original = Row(window, 0);
        foreach (int height in new[] { 6, 2, 1 })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var widths = Enumerable.Range(1, 40).Reverse().Concat(Enumerable.Range(1, 40));
                foreach (int width in widths)
                {
                    window.Locate(new TRect(0, 0, width, height));
                    app.Redraw();
                    Assert.Equal(new TPoint(width, height), window.size);
                    string top = Row(window, 0);
                    Assert.Equal(width, top.Length);
                    int budget = width - 10 - 6 - 4;
                    int[] letters = LetterColumns(top);
                    if (budget <= 0) Assert.Empty(letters);
                    // Title text stays between the close button (2..4) and the number/zoom area.
                    else Assert.All(letters, x => Assert.InRange(x, 7, width - 9));
                    // Below four columns the three-cell resize icon covers the bottom-left corner as well.
                    if (height >= 2 && width >= 4) Assert.Equal('╚', Row(window, height - 1)[0]);
                }
            }
        }
        window.Locate(new TRect(0, 0, 40, 6));
        app.Redraw();
        Assert.Equal(original, Row(window, 0));
    }

    [Fact]
    public void ScreenResizeShrinksARelativelyGrowingWindowBelowItsTitleBudgetWithoutThrowing()
    {
        using var scope = new DriverScope(140, 40);
        using var app = new TApplication();
        var window = new TWindow(new TRect(2, 1, 44, 11), "Relatively growing window", 1);
        Assert.Equal(Views.gfGrowAll | Views.gfGrowRel, window.growMode);
        Show(app, window);

        void ResizeTo(ushort cols, ushort rows)
        {
            scope.Driver.SimulateResize(cols, rows);
            for (int i = 0; i < 50; i++)
            {
                var ev = new TEvent();
                app.GetEvent(ref ev);
                if (ev.What == Events.evNothing) break;
                app.HandleEvent(ref ev);
            }
            Assert.Equal((cols, rows), ((ushort)app.size.x, (ushort)app.size.y));
        }

        const string title = "Relatively growing window"; // 25 UTF-16 units
        string top = Row(window, 0); // 42 columns, budget 22: tail of 20
        AssertFrameEdges(top, 42);
        string top42 = top;
        Assert.Contains(" .." + title[5..] + "  ", top);

        ResizeTo(100, 30);
        Assert.Equal(30, window.size.x);   // 42 * 100 / 140
        top = Row(window, 0);
        AssertFrameEdges(top, 30);
        Assert.Contains(" .." + title[17..] + "  ", top); // budget 10: tail of 8

        ResizeTo(60, 20);
        Assert.Equal(18, window.size.x);   // 42 * 60 / 140; budget -2 threw before TW-F2
        top = Row(window, 0);
        AssertFrameEdges(top, 18);
        Assert.Empty(LetterColumns(top));
        Assert.Equal('1', top[18 - 7]);
        Assert.Equal("╚" + new string('═', 14) + "──┘", Row(window, window.size.y - 1)); // resize icon in the corner

        ResizeTo(140, 40);
        Assert.Equal(42, window.size.x);
        Assert.Equal(top42, Row(window, 0));
    }
}
