using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Samples.MonochromeModeQualificationTests;

namespace TSharpVision.Tests.Samples;

// TV12b: TProgram.SetAppPalette switches the logical palette family in one call and the TVDemo
// Options/Framework palette submenu exposes it. Rendering matrices live in MonochromeModeQualificationTests.
[Collection("NonParallel")]
public sealed class PaletteModeSwitchTests
{
    [Theory]
    [InlineData(TProgram.AP.BlackWhite, 80, 25)] [InlineData(TProgram.AP.Monochrome, 80, 25)]
    [InlineData(TProgram.AP.BlackWhite, 100, 35)] [InlineData(TProgram.AP.Monochrome, 100, 35)]
    public void OneCallRepaintsOpenBufferedWindowsImmediately(TProgram.AP theme, int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        BuildSurface(app); app.Redraw();
        Assert.NotEmpty(ColoredCells());

        app.SetAppPalette(theme); // no caller Redraw

        Assert.Equal(theme, app.AppPalette);
        AssertNeutralScreen($"SetAppPalette {theme}");
        AssertIncrementalMatchesFullRedraw(app);
    }

    [Fact]
    public void RoundTripRestoresColorCellsWithoutMutatingPaletteTablesOrLegacyModeState()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var windows = BuildSurface(app); app.Redraw();
        var color = Snapshot();
        byte[][] tables = Tables(app);
        var shadow = TView.shadowSize; bool markers = TView.showMarkers; var mode = TScreen.ScreenMode;

        for (int round = 0; round < 3; round++)
            foreach (var theme in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome, TProgram.AP.Color })
            {
                app.SetAppPalette(theme);
                if (theme != TProgram.AP.Color) AssertNeutralScreen($"round {round} {theme}");
            }

        AssertSameCells(color, "after three round trips");
        Assert.Equal(tables, Tables(app));
        Assert.Equal(shadow, TView.shadowSize); Assert.Equal(markers, TView.showMarkers); Assert.Equal(mode, TScreen.ScreenMode);
        Assert.Same(windows[^1], app.DeskTop!.current);
    }

    [Fact]
    public void SameModeRepeatIsANoOp()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.SetAppPalette(TProgram.AP.Monochrome);
        // A sentinel cell would be overwritten by any repaint.
        var data = TScreen.ScreenBuffer!.Data;
        var saved = data[0]; data[0] = new TScreenChar('#', new TColorAttr(0x4E));
        try
        {
            app.SetAppPalette(TProgram.AP.Monochrome);
            Assert.Equal('#', TScreen.ScreenBuffer.Data[0].Character);
            Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);
        }
        finally { data[0] = saved; }
    }

    [Theory]
    [InlineData(3)] [InlineData(0xFFFF)]
    public void UndefinedPaletteIsRejectedAndLeavesTheCurrentMode(int value)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.SetAppPalette(TProgram.AP.BlackWhite);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => app.SetAppPalette((TProgram.AP)value));
        Assert.Equal("palette", ex.ParamName);
        Assert.Equal(TProgram.AP.BlackWhite, app.AppPalette);
    }

    [Fact]
    public void ShutDownProgramRejectsSwitching()
    {
        using var driver = new DriverScope(80, 25);
        var app = new TApplication();
        app.ShutDown();
        Assert.Throws<ObjectDisposedException>(() => app.SetAppPalette(TProgram.AP.Monochrome));
    }

    [Fact]
    public void LegacySetScreenModeStillRederivesFromScreenModeAndRepaintsAtUnchangedSize()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var savedMode = TScreen.ScreenMode; var savedShadow = TView.shadowSize; bool savedMarkers = TView.showMarkers;
        try
        {
            BuildSurface(app); app.Redraw(); var color = Snapshot();
            app.SetAppPalette(TProgram.AP.Monochrome);
            Assert.Equal(new TPoint(2, 1), TView.shadowSize); // SetAppPalette keeps legacy shadow/marker state
            Assert.False(TView.showMarkers);

            app.SetScreenMode(0); // ScreenMode is CO80, so the legacy path selects Color again and repaints
            Assert.Equal(TProgram.AP.Color, app.AppPalette);
            AssertSameCells(color, "SetScreenMode after SetAppPalette");

            TScreen.ScreenMode = TDisplay.SM.Mono; app.SetScreenMode(0);
            Assert.Equal(new TPoint(0, 0), TView.shadowSize); Assert.True(TView.showMarkers);
            AssertNeutralScreen("legacy Mono");
            app.SetAppPalette(TProgram.AP.Color); // palette only: legacy shadow/marker state is left alone
            Assert.Equal(new TPoint(0, 0), TView.shadowSize); Assert.True(TView.showMarkers);
            Assert.NotEmpty(ColoredCells()); AssertIncrementalMatchesFullRedraw(app);
        }
        finally { TScreen.ScreenMode = savedMode; TView.shadowSize = savedShadow; TView.showMarkers = savedMarkers; }
    }

    [Theory]
    [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void SwitchingWhileAComboPopupIsOpenRepaintsThePopup(TProgram.AP theme)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var combo = app.BuildComboBoxDemoDialog(out var caseBox, out _);
        app.DeskTop!.Insert(combo); caseBox.Select();
        app.Step(() =>
        {
            Assert.NotNull(TView.ModalTopView);
            app.SetAppPalette(theme);
            AssertNeutralScreen($"{theme} popup");
            AssertFocusedListRowDistinct(Assert.Single(ScriptedDemoApp.Descendants((TGroup)TView.ModalTopView!).OfType<TListViewer>()));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        var open = ComboInput.AltDown; combo.HandleEvent(ref open);
        Assert.Null(TView.ModalTopView);
        AssertNeutralScreen($"{theme} after popup"); AssertIncrementalMatchesFullRedraw(app);
    }

    // ---- TVDemo selector ----

    [Fact]
    public void DemoCommandsSwitchWithoutRestartAndMarkTheCurrentMode()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        BuildSurface(app); app.Redraw(); var color = Snapshot();
        AssertMarked(app, 'C');

        app.RunCommand(ShowcaseCmd.PaletteBlackWhite);
        Assert.Equal(TProgram.AP.BlackWhite, app.AppPalette); AssertNeutralScreen("demo BW"); AssertMarked(app, 'B');
        app.RunCommand(ShowcaseCmd.PaletteMonochrome);
        Assert.Equal(TProgram.AP.Monochrome, app.AppPalette); AssertNeutralScreen("demo Mono"); AssertMarked(app, 'M');
        app.RunCommand(ShowcaseCmd.PaletteColor);
        Assert.Equal(TProgram.AP.Color, app.AppPalette); AssertMarked(app, 'C');
        AssertSameCells(color, "demo back to Color");
    }

    [Fact]
    public void KeyboardPathThroughOptionsMenuSelectsMonochrome()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.Step(() => ComboInput.Send(ComboInput.Typed("f"))); // Options -> Framework palette
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            Assert.Contains(PaletteItems(app), i => i == box.Current);
            ComboInput.Send(ComboInput.Typed("m"));
        });
        var key = ComboInput.Key(Keys.kbAltO); app.HandleEvent(ref key);
        Assert.Null(TView.ModalTopView);
        DispatchMenuResult(app);
        Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);
        AssertNeutralScreen("keyboard Mono"); AssertMarked(app, 'M');
    }

    [Fact]
    public void MousePathThroughOptionsMenuSelectsBlackWhite()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var options = TVDemoMenuNavigationTests.Items(app.MenuBar!.Menu!).Single(i => i.HotChar() == 'O');
        var r = app.MenuBar.GetItemRect(options); var point = app.MenuBar.MakeGlobal(new TPoint(r.a.x + 1, r.a.y));
        app.Step(() => ClickItem(app.Modal<TMenuBox>(), 'F'));
        app.Step(() => ClickItem(app.Modal<TMenuBox>(), 'B'));
        var ev = ComboInput.Mouse(Events.evMouseDown, point); app.HandleEvent(ref ev);
        Assert.Null(TView.ModalTopView);
        DispatchMenuResult(app);
        Assert.Equal(TProgram.AP.BlackWhite, app.AppPalette);
        AssertNeutralScreen("mouse BW"); AssertMarked(app, 'B');

        static void ClickItem(TMenuBox box, char hot)
        {
            var item = TVDemoMenuNavigationTests.Items(box.Menu!).Single(i => i.HotChar() == hot);
            var row = box.GetItemRect(item); ComboInput.Click(box.MakeGlobal(new TPoint(row.a.x + 1, row.a.y)));
        }
    }

    [Fact]
    public void DirectAppPaletteAssignmentIsReflectedByTheIndicatorOnIdle()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.AppPalette = TProgram.AP.Monochrome; app.Idle();
        AssertMarked(app, 'M');
    }

    // The menu bar posts the chosen command; the application loop would read and dispatch it next.
    private static void DispatchMenuResult(ScriptedDemoApp app)
    {
        TEvent command = default; app.GetEvent(ref command);
        Assert.Equal(Events.evCommand, command.What);
        app.HandleEvent(ref command);
    }

    private static TMenuItem[] PaletteItems(ScriptedDemoApp app)
    {
        var options = TVDemoMenuNavigationTests.Items(app.MenuBar!.Menu!).Single(i => i.HotChar() == 'O');
        var palette = TVDemoMenuNavigationTests.Items(options.SubMenu!).Single(i => i.HotChar() == 'F');
        return TVDemoMenuNavigationTests.Items(palette.SubMenu!).ToArray();
    }

    private static void AssertMarked(ScriptedDemoApp app, char hot)
    {
        var items = PaletteItems(app);
        Assert.Equal(3, items.Length);
        foreach (var item in items)
        {
            Assert.Equal(Keys.kbNoKey, item.KeyCode);
            Assert.StartsWith(item.HotChar() == hot ? $"({TSharpVisionGlyphs.RadioChecked})" : "( )", item.Name);
        }
    }

    private static byte[][] Tables(TProgram app)
    {
        var saved = app.AppPalette;
        try
        {
            return Enum.GetValues<TProgram.AP>().Select(ap => { app.AppPalette = ap; return app.GetPalette().Data.ToArray(); }).ToArray();
        }
        finally { app.AppPalette = saved; }
    }
}
