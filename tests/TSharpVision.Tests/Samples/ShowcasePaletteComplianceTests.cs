using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Samples.MonochromeModeQualificationTests;

namespace TSharpVision.Tests.Samples;

// TV12c: the TVDemo accessories and games (Puzzle, Calendar, Calculator, Clock, Tetris, ASCII chart) keep their
// classic literal colors in the Color family and resolve neutral palette roles in BlackWhite and Monochrome. Every
// assertion reads the composed root screen of a real TVDemo program, with the views opened through their commands.
[Collection("NonParallel")]
public sealed class ShowcasePaletteComplianceTests
{
    public static readonly string[] Views6 = { "Puzzle", "Calendar", "Calculator", "Clock", "Tetris", "Ascii" };

    public static IEnumerable<object[]> ViewNames() => Views6.Select(v => new object[] { v });

    public static IEnumerable<object[]> ViewsAndNeutralModes()
    {
        foreach (var view in Views6)
            foreach (var theme in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome })
                yield return new object[] { view, theme };
    }

    private static readonly DateTime FixedNow = new(2026, 10, 31, 12, 34, 56);

    // Opens the view through its TVDemo command and pins any random or time-dependent state.
    private static TWindow Open(ScriptedDemoApp app, string name)
    {
        ushort command = name switch
        {
            "Puzzle" => TVDemoCmd.cmPuzzle,
            "Calendar" => TVDemoCmd.cmCalendar,
            "Calculator" => TVDemoCmd.cmCalculator,
            "Clock" => TVDemoCmd.cmClock,
            "Tetris" => ShowcaseCmd.Tetris,
            "Ascii" => TVDemoCmd.cmAsciiTable,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var before = ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TWindow>().ToHashSet();
        app.RunCommand(command);
        var window = Assert.Single(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TWindow>(), w => !before.Contains(w));
        switch (window)
        {
            case PuzzleDialog puzzle: puzzle.Model.Reset(); Assert.True(puzzle.Model.TryMove(3, 2)); break; // blank at (3,2)
            case ClockDialog clock: clock.View.GetNow = () => FixedNow; break;
            case TTetrisWindow tetris: tetris.Game.StopTimer(); LoadTetris(tetris.Game, gameOver: false); break;
        }
        window.Redraw();
        return window;
    }

    // Bottom row holds one cell of each of the seven pieces; an I piece is falling.
    private static void LoadTetris(TTetrisView game, bool gameOver, bool paused = false)
    {
        var board = new byte[20, 10];
        for (int c = 0; c < 7; c++) board[19, c] = (byte)(c + 1);
        game.LoadState(board, 0, 0, 3, 5, 1, paused, gameOver);
    }

    private static TScreenChar Cell(TView view, int x, int y)
    {
        var p = view.MakeGlobal(new TPoint(x, y));
        return RootScreenCopy.CopyRoot()[p.x, p.y];
    }

    private static byte Attr(TView view, int x, int y) => (byte)Cell(view, x, y).Attr;

    private static T Child<T>(TGroup group) where T : TView => Assert.Single(ScriptedDemoApp.Descendants(group).OfType<T>());

    // The calculator's "C" key.
    private static TButton FirstButton(TGroup group) => ScriptedDemoApp.Descendants(group).OfType<TButton>().First();

    // State that color alone used to carry must stay distinguishable (attribute, inverse or glyph).
    private static void AssertStatesDistinct(TWindow window, string context)
    {
        window.Select();
        switch (window)
        {
            case PuzzleDialog puzzle:
            {
                var view = puzzle.View;
                // Solved board with O moved: A (home 0, even) and B (home 1, odd) are on row 0.
                Assert.Equal('A', Cell(view, 1, 0).Character); Assert.Equal('B', Cell(view, 4, 0).Character);
                Assert.NotEqual(Attr(view, 1, 0), Attr(view, 4, 0));
                // The blank is told apart from a tile by its glyph.
                Assert.Equal(' ', Cell(view, 2 * 3 + 1, 3).Character);
                Assert.Equal('O', Cell(view, 3 * 3 + 1, 3).Character);
                break;
            }
            case CalendarDialog calendar:
            {
                var model = calendar.Model; var grid = model.BuildGrid();
                (int r, int c) today = default, other = default; bool found = false, foundOther = false;
                for (int r = 0; r < 6; r++)
                    for (int c = 0; c < 7; c++)
                    {
                        if (grid[r, c] == 0) continue;
                        if (grid[r, c] == model.TodayDay) { today = (r, c); found = true; }
                        else if (!foundOther) { other = (r, c); foundOther = true; }
                    }
                Assert.True(found && foundOther, context);
                byte todayAttr = Attr(calendar.View, today.c * 3 + 1, today.r + 2);
                byte otherAttr = Attr(calendar.View, other.c * 3 + 1, other.r + 2);
                Assert.NotEqual(otherAttr, todayAttr);
                break;
            }
            case CalculatorDialog calculator:
            {
                var display = calculator.Display;
                byte surface = Attr(calculator, 1, 1);
                Assert.NotEqual(surface, Attr(display, display.size.x - 2, 0)); // display stands out
                var button = FirstButton(calculator);
                Assert.NotEqual(surface, Attr(button, 2, 0)); // keys stand out
                break;
            }
            case ClockDialog clock:
                Assert.Equal('(', Cell(clock.View, 0, 1).Character);
                Assert.Equal('1', Cell(clock.View, 0, 0).Character);
                break;
            case TTetrisWindow tetris:
            {
                var game = tetris.Game;
                var cells = Enumerable.Range(0, 7)
                    .Select(c => (Cell(game, c * 2, 19), Cell(game, c * 2 + 1, 19))).ToList();
                var keys = cells.Select(p => $"{p.Item1.Character}{p.Item2.Character}{(byte)p.Item1.Attr:X2}").ToList();
                Assert.Equal(7, keys.Distinct().Count());
                Assert.DoesNotContain(keys, k => k.StartsWith("  ")); // filled cells never look empty
                Assert.Equal(' ', Cell(game, 0, 0).Character);
                break;
            }
            case TDialog ascii when ascii is AsciiTableDialog:
            {
                var body = Child<AsciiTableBody>(ascii);
                int sel = body.SelectedCode, next = sel == 0 ? 1 : sel - 1;
                Assert.NotEqual(Attr(body, next % 32, next / 32), Attr(body, sel % 32, sel / 32));
                break;
            }
            default: throw new InvalidOperationException(window.GetType().Name);
        }
    }

    [Theory]
    [MemberData(nameof(ViewsAndNeutralModes))]
    public void NeutralFamiliesResolveOnlyNeutralReadableCellsAndKeepStatesDistinct(string name, TProgram.AP theme)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var window = Open(app, name);
        app.SetAppPalette(theme);
        AssertNeutralScreen($"{name} {theme}");
        AssertStatesDistinct(window, $"{name} {theme}");
        AssertNeutralScreen($"{name} {theme} focused");
        AssertIncrementalMatchesFullRedraw(app);
        window.Close();
    }

    [Theory]
    [MemberData(nameof(ViewNames))]
    public void ColorFamilyKeepsTheClassicLiteralColors(string name)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var window = Open(app, name);
        AssertStatesDistinct(window, $"{name} Color");
        switch (window)
        {
            case PuzzleDialog puzzle:
                Assert.Equal(0x1E, Attr(puzzle.View, 1, 0)); Assert.Equal(0x71, Attr(puzzle.View, 4, 0));
                Assert.Equal(0x1E, Attr(puzzle.View, 13, 1)); Assert.Equal(0x17, Attr(puzzle, 0, 1));
                break;
            case CalendarDialog calendar:
                Assert.Equal(0x3E, Attr(calendar.View, 0, 0)); Assert.Equal(0x3F, Attr(calendar, 0, 0));
                break;
            case CalculatorDialog calculator:
                Assert.Equal(0x1F, Attr(calculator.Display, 0, 0)); Assert.Equal(0x70, Attr(calculator, 1, 1));
                Assert.Equal(0x20, Attr(FirstButton(calculator), 2, 0));
                break;
            case ClockDialog clock:
                Assert.Equal(0x70, Attr(clock.View, 0, 0)); Assert.Equal(0x70, Attr(clock, 0, 0));
                break;
            case TTetrisWindow tetris:
                byte[] hues = { 0x73, 0x76, 0x75, 0x72, 0x74, 0x71, 0x78 };
                for (int c = 0; c < 7; c++)
                {
                    Assert.Equal('█', Cell(tetris.Game, c * 2, 19).Character);
                    Assert.Equal(hues[c], Attr(tetris.Game, c * 2, 19));
                }
                Assert.Equal(0x70, Attr(tetris.Game, 0, 0)); Assert.Equal(0x70, Attr(tetris.Game, 20, 0));
                break;
            case AsciiTableDialog ascii:
                var body = Child<AsciiTableBody>(ascii);
                Assert.Equal(0x07, Attr(body, 0, 0)); Assert.Equal(0x70, Attr(body, 1, 0));
                break;
        }
    }

    // Color -> BlackWhite -> Monochrome -> Color through the TVDemo Options > Framework palette commands while the
    // view is moved, unfocused and partly covered by a buffered window, across a screen-resize notification.
    [Theory]
    [MemberData(nameof(ViewNames))]
    public void MenuRoundTripWhileUnfocusedAndOverlappedRestoresColorExactly(string name)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var window = Open(app, name);
        window.MoveTo(window.origin.x + 1, window.origin.y + 1);
        var cover = Cover(window);
        app.DeskTop!.Insert(cover); cover.Select();
        app.Redraw();
        var color = Snapshot();
        Assert.NotEmpty(ColoredCells());

        foreach (var (command, theme) in new[] { (ShowcaseCmd.PaletteBlackWhite, TProgram.AP.BlackWhite),
                                                  (ShowcaseCmd.PaletteMonochrome, TProgram.AP.Monochrome) })
        {
            app.RunCommand(command);
            Assert.Equal(theme, app.AppPalette);
            Assert.Same(cover, app.DeskTop.current);
            AssertNeutralScreen($"{name} {theme} unfocused");
            AssertIncrementalMatchesFullRedraw(app);
            var resized = ScriptedDemoApp.CommandEvent(Views.cmScreenResized); app.HandleEvent(ref resized);
            AssertNeutralScreen($"{name} {theme} resized");
            // Focusing and restacking repaints only parts of the screen: nothing of the previous family may survive.
            window.Select(); AssertNeutralScreen($"{name} {theme} focused"); AssertIncrementalMatchesFullRedraw(app);
            cover.Select(); AssertIncrementalMatchesFullRedraw(app);
        }

        app.RunCommand(ShowcasePaletteColorCommand);
        Assert.Equal(TProgram.AP.Color, app.AppPalette);
        AssertSameCells(color, $"{name} back to Color");
    }

    private const ushort ShowcasePaletteColorCommand = ShowcaseCmd.PaletteColor;

    // A buffered dialog with a buffered inner group overlapping the lower right part of the target.
    private static TDialog Cover(TWindow target)
    {
        var cover = new TDialog(new TRect(0, 0, 24, 7), "Cover");
        cover.options |= Views.ofBuffered;
        var inner = new TGroup(new TRect(2, 2, 22, 5));
        inner.options |= Views.ofBuffered;
        inner.Insert(new TStaticText(new TRect(0, 0, 18, 1), "Buffered cover"));
        cover.Insert(inner);
        cover.MoveTo(target.origin.x + target.size.x / 2, target.origin.y + target.size.y / 2);
        return cover;
    }

    [Fact]
    public void AllSixOpenAndStackedSwitchLiveAndRestoreColorExactly()
    {
        using var scope = new DemoTestScope(100, 35); var app = scope.App;
        var windows = Views6.Select(v => Open(app, v)).ToList();
        int i = 0;
        foreach (var w in windows) { w.MoveTo(Math.Min(app.DeskTop!.size.x - w.size.x, i * 9), Math.Min(app.DeskTop.size.y - w.size.y, i * 2)); i++; }
        app.Redraw();
        var color = Snapshot();

        for (int round = 0; round < 2; round++)
            foreach (var theme in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome })
            {
                app.SetAppPalette(theme);
                AssertNeutralScreen($"all six {theme} round {round}");
                foreach (var w in windows) { w.Select(); AssertNeutralScreen($"{w.GetType().Name} {theme} on top"); }
                AssertIncrementalMatchesFullRedraw(app);
            }

        app.SetAppPalette(TProgram.AP.Color);
        AssertSameCells(color, "all six back to Color");
        foreach (var w in windows) w.Close();
    }

    // Input keeps working in the neutral families and its result is drawn with neutral roles.
    [Theory]
    [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void InputStillWorksAndRedrawsNeutrally(TProgram.AP theme)
    {
        using var scope = new DemoTestScope(100, 35); var app = scope.App;
        app.SetAppPalette(theme);

        var puzzle = (PuzzleDialog)Open(app, "Puzzle"); puzzle.View.Select();
        var key = ComboInput.Key(Keys.kbLeft); puzzle.View.HandleEvent(ref key); // O slides back left into the blank
        Assert.Equal(2, puzzle.Model.Moves); Assert.Equal('O', Cell(puzzle.View, 2 * 3 + 1, 3).Character);
        AssertNeutralScreen("puzzle key"); AssertIncrementalMatchesFullRedraw(app);
        puzzle.Close();

        var calendar = (CalendarDialog)Open(app, "Calendar");
        int month = calendar.Model.Month;
        key = ComboInput.Key('+'); calendar.View.HandleEvent(ref key);
        Assert.Equal(month % 12 + 1, calendar.Model.Month);
        Assert.StartsWith(CalendarModel.MonthNames[calendar.Model.Month],
            new string(Enumerable.Range(0, 21).Select(x => Cell(calendar.View, x, 0).Character).ToArray()));
        AssertNeutralScreen("calendar key"); AssertIncrementalMatchesFullRedraw(app);
        calendar.Close();

        var calc = (CalculatorDialog)Open(app, "Calculator");
        foreach (char ch in "1/0=") { key = ComboInput.Key(ch); calc.Display.HandleEvent(ref key); }
        Assert.True(calc.Display.Engine.IsError);
        string shown = new(Enumerable.Range(0, calc.Display.size.x).Select(x => Cell(calc.Display, x, 0).Character).ToArray());
        Assert.Contains("Error", shown);
        AssertNeutralScreen("calculator error"); AssertIncrementalMatchesFullRedraw(app);
        calc.Close();

        var ascii = (AsciiTableDialog)Open(app, "Ascii");
        var body = Child<AsciiTableBody>(ascii); body.Select();
        byte before = Attr(body, 1, 0);
        key = ComboInput.Key(Keys.kbRight); body.HandleEvent(ref key);
        Assert.Equal(1, body.SelectedCode);
        Assert.NotEqual(before, Attr(body, 1, 0)); Assert.Equal(before, Attr(body, 0, 0));
        AssertNeutralScreen("ascii key"); AssertIncrementalMatchesFullRedraw(app);
        ascii.Close();

        var tetris = (TTetrisWindow)Open(app, "Tetris");
        var game = tetris.Game;
        key = ComboInput.Key(Keys.kbLeft); game.HandleEvent(ref key);
        Assert.Equal(2, game.CurrentState.x);
        key = ComboInput.Key('p'); game.HandleEvent(ref key);
        Assert.True(game.CurrentState.paused);
        Assert.Contains("PAUSED", new string(Enumerable.Range(0, 20).Select(x => Cell(game, 3 + x, 10).Character).ToArray()));
        AssertNeutralScreen("tetris paused"); AssertIncrementalMatchesFullRedraw(app);
        LoadTetris(game, gameOver: true); game.DrawView();
        Assert.NotEqual(Attr(game, 0, 0), Attr(game, 5, 8)); // the game-over banner stands out from the board
        Assert.Equal('*', Cell(game, 2, 8).Character);
        AssertNeutralScreen("tetris game over"); AssertIncrementalMatchesFullRedraw(app);
        tetris.Close();
    }

    [Fact]
    public void ClockTickRedrawsInTheActiveFamily()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var clock = (ClockDialog)Open(app, "Clock");
        app.SetAppPalette(TProgram.AP.Monochrome);
        clock.View.GetNow = () => FixedNow.AddSeconds(1);
        clock.Tick();
        Assert.Equal('7', Cell(clock.View, 7, 0).Character);
        AssertNeutralScreen("clock tick"); AssertIncrementalMatchesFullRedraw(app);
        clock.Close();
    }
}
