using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

// TV12: Color / BlackWhite / Monochrome selection qualified on composed screen cells of a real TVDemo surface.
// BlackWhite and Monochrome contain only black, light gray, dark gray and white, so every framework-drawn cell in
// those modes must use only those four indices; a colored cell means a hard-coded or stale attribute.
[Collection("NonParallel")]
public sealed class MonochromeModeQualificationTests
{
    public static IEnumerable<object[]> NeutralModesAndSizes()
    {
        foreach (var theme in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome })
            foreach (var (w, h) in new[] { (80, 25), (100, 35) })
                yield return new object[] { theme, w, h };
    }

    [Theory]
    [MemberData(nameof(NeutralModesAndSizes))]
    public void FrameworkSurfaceUsesOnlyNeutralReadableCellsAcrossFocusMoveAndClose(TProgram.AP theme, int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        app.AppPalette = theme; app.Redraw();
        var windows = BuildSurface(app);
        AssertNeutralScreen($"{theme} initial");
        AssertIncrementalMatchesFullRedraw(app);

        foreach (var window in windows) { window.Select(); AssertNeutralScreen($"{theme} select {window.title}"); }
        AssertIncrementalMatchesFullRedraw(app);

        // Focus every selectable control of the control showcase, including nested clusters and lists.
        foreach (var control in ScriptedDemoApp.Descendants(windows[0]).Where(v => (v.options & Views.ofSelectable) != 0 && !v.GetState(Views.sfDisabled)))
        {
            windows[0].Select(); control.Select();
            AssertNeutralScreen($"{theme} focus {control.GetType().Name}");
        }
        AssertIncrementalMatchesFullRedraw(app);

        // Move the overlapping window away and close the top one: exposed cells must not keep stale buffers.
        var top = windows[^1]; top.Select(); top.MoveTo(top.origin.x + 7, top.origin.y + 2);
        AssertNeutralScreen($"{theme} moved"); AssertIncrementalMatchesFullRedraw(app);
        top.Close();
        AssertNeutralScreen($"{theme} closed"); AssertIncrementalMatchesFullRedraw(app);
    }

    [Fact]
    public void ColorSurfaceIsNotNeutralSoTheNeutralAuditDiscriminates()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        BuildSurface(app); app.Redraw();
        Assert.True(ColoredCells().Count > 100);
    }

    [Theory]
    [MemberData(nameof(NeutralModesAndSizes))]
    public void MenuCascadeComboAndHistoryPopupsStayNeutralAndDistinguishSelection(TProgram.AP theme, int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        app.AppPalette = theme; app.Redraw();

        // Menu bar -> Demo -> Controls cascading submenu, through the real modal menu loops.
        TMenuBox? parent = null;
        app.Step(() => { parent = app.Modal<TMenuBox>(); ComboInput.Send(ComboInput.Key(Keys.kbDown)); });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEnter)));
        // A menu paints its highlight after its first input (as upstream), so move once inside the submenu.
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbDown)));
        app.Step(() =>
        {
            var child = app.Modal<TMenuBox>(); Assert.Same(parent, child.ParentMenu);
            AssertNeutralScreen($"{theme} menu cascade");
            AssertSelectedRowDistinct(parent!); AssertSelectedRowDistinct(child);
            var arrow = parent!.MakeGlobal(new TPoint(parent.size.x - 4, parent.GetItemRect(parent.Current!).a.y));
            Assert.Equal(TSharpVisionGlyphs.MenuSubmenuArrow, RootScreenCopy.CopyRoot()[arrow.x, arrow.y].Character);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        var key = ComboInput.Key(Keys.kbAltD); app.HandleEvent(ref key);
        Assert.Null(TView.ModalTopView);
        AssertNeutralScreen($"{theme} after menu");

        // Combo dropdown opened from a non-modal dialog.
        var combo = app.BuildComboBoxDemoDialog(out var caseBox, out _);
        app.DeskTop!.Insert(combo); caseBox.Select();
        app.Step(() =>
        {
            Assert.NotNull(TView.ModalTopView);
            AssertNeutralScreen($"{theme} combo dropdown");
            AssertFocusedListRowDistinct(Assert.Single(ScriptedDemoApp.Descendants((TGroup)TView.ModalTopView!).OfType<TListViewer>()));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        var open = ComboInput.AltDown; combo.HandleEvent(ref open);
        Assert.Null(TView.ModalTopView);
        combo.Close();

        // History dropdown.
        THistoryList.Add(1, "alpha"); THistoryList.Add(1, "beta");
        var history = app.BuildHistoryDemoDialog();
        app.DeskTop.Insert(history);
        var input = Assert.Single(ScriptedDemoApp.Descendants(history).OfType<TInputLine>()); input.Select();
        app.Step(() =>
        {
            Assert.IsType<THistoryWindow>(TView.ModalTopView);
            AssertNeutralScreen($"{theme} history dropdown");
            AssertFocusedListRowDistinct(Assert.Single(ScriptedDemoApp.Descendants((TGroup)TView.ModalTopView!).OfType<TListViewer>()));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        var down = ComboInput.Key(Keys.kbDown); history.HandleEvent(ref down);
        Assert.Null(TView.ModalTopView);
        AssertNeutralScreen($"{theme} after history"); AssertIncrementalMatchesFullRedraw(app);
    }

    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void RuntimeSwitchColorBlackWhiteMonochromeColorRepaintsOpenWindowsAndRestoresExactCells(int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        var windows = BuildSurface(app); app.Redraw();
        var color = Snapshot();

        foreach (var theme in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome })
        {
            app.AppPalette = theme; app.Redraw();
            var current = Snapshot();
            Assert.Equal(color.Select(c => c.Character), current.Select(c => c.Character));
            AssertNeutralScreen($"switched to {theme}");
            Assert.Same(windows[^1], app.DeskTop!.current);
            // Interaction after the switch must not resurrect cached attributes of the previous mode.
            windows[0].Select(); AssertNeutralScreen($"{theme} reselect"); AssertIncrementalMatchesFullRedraw(app);
            // Restore the original z-order so the final Color frame is comparable cell for cell.
            foreach (var window in windows) window.Select();
            AssertIncrementalMatchesFullRedraw(app);
        }

        app.AppPalette = TProgram.AP.Color; app.Redraw();
        AssertSameCells(color, "back to Color");
    }

    [Fact]
    public void PaletteChangeNeedsRedrawBecauseBufferedWindowsKeepTheirComposedCells()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        BuildSurface(app); app.Redraw();
        app.AppPalette = TProgram.AP.Monochrome;
        app.DrawView();
        // Documented contract: changing AppPalette is not an invalidation; Redraw() recomposes every buffer.
        Assert.NotEmpty(ColoredCells());
        app.Redraw();
        Assert.Empty(ColoredCells());
    }

    [Fact]
    public void ScreenModeDrivesInitScreenAndSetScreenModeWhileResizeKeepsTheSelectedPalette()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var savedMode = TScreen.ScreenMode; var savedShadow = TView.shadowSize; bool savedMarkers = TView.showMarkers;
        try
        {
            BuildSurface(app); app.Redraw();
            var color = Snapshot();
            Assert.Equal(TDisplay.SM.CO80, TScreen.ScreenMode); // every shipped driver reports CO80

            // A palette chosen directly survives a host resize notification...
            app.AppPalette = TProgram.AP.Monochrome; app.Redraw();
            var resized = ScriptedDemoApp.CommandEvent(Views.cmScreenResized); app.HandleEvent(ref resized);
            Assert.Equal(TProgram.AP.Monochrome, app.AppPalette); AssertNeutralScreen("after resize");
            // ...but SetScreenMode re-derives it from TScreen.ScreenMode, which a nonzero argument selects (TV12j).
            app.SetScreenMode(0);
            Assert.Equal(TProgram.AP.Color, app.AppPalette);
            AssertSameCells(color, "SetScreenMode(0) re-derives from ScreenMode");
            app.SetScreenMode((ushort)TDisplay.SM.Mono);
            Assert.Equal(TDisplay.SM.Mono, TScreen.ScreenMode);
            Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);

            // The legacy logical-mode path: ScreenMode Mono selects Monochrome, removes shadows, sets markers.
            TScreen.ScreenMode = TDisplay.SM.Mono; app.SetScreenMode(0);
            Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);
            Assert.Equal(new TPoint(0, 0), TView.shadowSize); Assert.True(TView.showMarkers);
            AssertNeutralScreen("ScreenMode Mono"); AssertIncrementalMatchesFullRedraw(app);

            TScreen.ScreenMode = TDisplay.SM.BW80; app.SetScreenMode(0);
            Assert.Equal(TProgram.AP.BlackWhite, app.AppPalette);
            Assert.Equal(new TPoint(2, 1), TView.shadowSize);
            AssertNeutralScreen("ScreenMode BW80");

            TScreen.ScreenMode = TDisplay.SM.CO80; app.SetScreenMode(0);
            Assert.Equal(TProgram.AP.Color, app.AppPalette);
            AssertSameCells(color, "ScreenMode CO80");
        }
        finally { TScreen.ScreenMode = savedMode; TView.shadowSize = savedShadow; TView.showMarkers = savedMarkers; }
    }

    // Regression: SetScreenMode redrew while the root was unexposed, so with an unchanged size (every shipped driver)
    // nothing was painted and the previous palette stayed on screen.
    [Theory]
    [InlineData(TDisplay.SM.Mono)] [InlineData(TDisplay.SM.BW80)]
    public void SetScreenModeWithUnchangedSizeRepaintsInTheNewPalette(TDisplay.SM mode)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var savedMode = TScreen.ScreenMode; var savedShadow = TView.shadowSize; bool savedMarkers = TView.showMarkers;
        try
        {
            app.Redraw();
            Assert.NotEmpty(ColoredCells());
            TScreen.ScreenMode = mode; app.SetScreenMode(0);
            AssertNeutralScreen($"SetScreenMode {mode}");
            AssertIncrementalMatchesFullRedraw(app);
        }
        finally { TScreen.ScreenMode = savedMode; TView.shadowSize = savedShadow; TView.showMarkers = savedMarkers; }
    }

    [Theory]
    [InlineData(TProgram.AP.Color)] [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void StartupSelectionThroughInitScreenOverrideRendersTheFirstFrameInThatMode(TProgram.AP theme)
    {
        using var driver = new DriverScope(80, 25);
        StartupApp.Requested = theme;
        var app = new StartupApp();
        try
        {
            Assert.Equal(theme, app.AppPalette);
            app.DeskTop!.Insert(new TDialog(new TRect(5, 3, 45, 12), "Startup") { });
            app.Redraw();
            if (theme == TProgram.AP.Color) Assert.NotEmpty(ColoredCells()); else AssertNeutralScreen($"startup {theme}");
        }
        finally { app.ShutDown(); }
    }

    private sealed class StartupApp : TApplication
    {
        // InitScreen runs inside the TProgram constructor, before any instance field of this class is assigned.
        internal static TProgram.AP Requested;
        public override void InitScreen() { base.InitScreen(); AppPalette = Requested; }
    }

    // Representative framework controls: labels, input/history, combo, clusters, list, buttons (default, normal,
    // disabled, shadows), group box, progress track/fill/percentage, file dialog metadata, scrollbars, a nested
    // buffered group and overlapping windows. The TVDemo welcome window stays underneath.
    internal static List<TWindow> BuildSurface(ScriptedDemoApp app)
    {
        var desk = app.DeskTop!;
        var showcase = app.BuildControlsShowcaseDialog(out _, out _, out _, out _);
        var file = new DemoFileDialog(false);
        var group = TVDemoApp.CreateGroupBoxDemoDialog(new TRect(0, 0, 64, 19));
        var progress = new ProgressDemoDialog(new TRect(0, 0, 58, 11));
        var combo = app.BuildComboBoxDemoDialog(out _, out _);
        var scroller = new TWindow(new TRect(0, 0, 30, 10), "Scroller", 2);
        scroller.Insert(scroller.StandardScrollBar(Views.sbVertical | Views.sbHandleKeyboard));
        scroller.Insert(scroller.StandardScrollBar(Views.sbHorizontal | Views.sbHandleKeyboard));
        var nested = new TDialog(new TRect(0, 0, 36, 10), "Nested");
        var inner = new TGroup(new TRect(2, 2, 30, 7)) { };
        inner.options |= Views.ofBuffered;
        inner.Insert(new TStaticText(new TRect(1, 1, 24, 2), "Nested buffered text"));
        inner.Insert(new TButton(new TRect(1, 2, 13, 4), "~C~lose", Views.cmCancel, ButtonConstants.bfNormal));
        nested.Insert(inner);
        var disabled = new TButton(new TRect(15, 7, 27, 9), "~O~ff", Views.cmOK, ButtonConstants.bfNormal);
        nested.Insert(disabled); disabled.SetState(Views.sfDisabled, true);

        var windows = new List<TWindow> { showcase, file, group, progress, combo, scroller, nested };
        int i = 0;
        foreach (var w in windows)
        {
            desk.Insert(w);
            w.MoveTo(Math.Max(0, Math.Min(desk.size.x - w.size.x, i * 5)), Math.Max(0, Math.Min(desk.size.y - w.size.y, i * 2)));
            i++;
        }
        return windows;
    }

    internal static TScreenChar[] Snapshot() => TScreen.ScreenBuffer!.Data.ToArray();

    private static bool Neutral(int index) => index is 0 or 7 or 8 or 15;

    internal static List<string> ColoredCells()
    {
        var screen = RootScreenCopy.CopyRoot(); var result = new List<string>();
        for (int y = 0; y < TScreen.ScreenHeight; y++)
            for (int x = 0; x < TScreen.ScreenWidth; x++)
            {
                byte attr = (byte)screen[x, y].Attr;
                if (!Neutral(attr & 15) || (attr >> 4) is not (0 or 7))
                    result.Add($"{x},{y} '{screen[x, y].Character}' {attr:X2}");
            }
        return result;
    }

    // Every cell uses a neutral attribute and every visible glyph contrasts with its background.
    internal static void AssertNeutralScreen(string context)
    {
        var colored = ColoredCells();
        Assert.True(colored.Count == 0, $"{context}: colored cells {string.Join("; ", colored.Take(12))}");
        var screen = RootScreenCopy.CopyRoot();
        for (int y = 0; y < TScreen.ScreenHeight; y++)
            for (int x = 0; x < TScreen.ScreenWidth; x++)
            {
                var cell = screen[x, y];
                // Shadow half-blocks on buttons are decoration and may vanish on a black surface.
                if (cell.Character is ' ' or '\0' or '▀' or '▄') continue;
                byte attr = (byte)cell.Attr;
                Assert.True((attr & 15) != (attr >> 4), $"{context}: '{cell.Character}' at {x},{y} unreadable {attr:X2}");
            }
    }

    internal static void AssertSameCells(TScreenChar[] expected, string context)
    {
        var actual = Snapshot();
        for (int i = 0; i < expected.Length; i++)
            Assert.True(expected[i].Equals(actual[i]),
                $"{context}: cell {i % TScreen.ScreenWidth},{i / TScreen.ScreenWidth} expected '{expected[i].Character}' {(byte)expected[i].Attr:X2}, got '{actual[i].Character}' {(byte)actual[i].Attr:X2}");
    }

    // Stale-buffer detector: what incremental drawing produced must equal a full recomposition.
    internal static void AssertIncrementalMatchesFullRedraw(TProgram app)
    {
        var incremental = Snapshot();
        app.Redraw();
        var full = Snapshot();
        for (int i = 0; i < full.Length; i++)
            Assert.True(incremental[i].Equals(full[i]),
                $"stale cell {i % TScreen.ScreenWidth},{i / TScreen.ScreenWidth}: '{incremental[i].Character}' {(byte)incremental[i].Attr:X2} vs '{full[i].Character}' {(byte)full[i].Attr:X2}");
    }

    internal static void AssertSelectedRowDistinct(TMenuBox box)
    {
        var screen = RootScreenCopy.CopyRoot();
        var selected = box.Current!;
        var other = TVDemoMenuNavigationTests.Items(box.Menu!).First(i => i != selected && !string.IsNullOrEmpty(i.Name) && !i.Disabled);
        // Column 2 is the blank before the name: the normal or selected text role, never the frame or shortcut role.
        byte Attr(TMenuItem item) { var p = box.MakeGlobal(new TPoint(2, box.GetItemRect(item).a.y)); return (byte)screen[p.x, p.y].Attr; }
        Assert.NotEqual(Attr(other), Attr(selected));
    }

    internal static void AssertFocusedListRowDistinct(TListViewer list)
    {
        Assert.True(list.range > 1);
        var screen = RootScreenCopy.CopyRoot();
        var origin = list.MakeGlobal(new TPoint(0, 0));
        int focusedRow = list.focused - list.topItem;
        int otherRow = focusedRow == 0 ? 1 : 0;
        Assert.NotEqual((byte)screen[origin.x + 1, origin.y + otherRow].Attr, (byte)screen[origin.x + 1, origin.y + focusedRow].Attr);
    }
}
