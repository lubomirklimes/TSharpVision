using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

// TV12c: TColorDialog Try/OK/Cancel against a live application palette (TColorDialog.BindLivePalette).
// Every assertion is on the composed root screen buffer: probes sit in overlapping windows and in a nested
// ofBuffered group, outside the centered 77x18 editor, so a stale cached buffer would show as a wrong attribute.
// The application palette tables are process-wide statics, so every test restores all three in a guard.
[Collection("NonParallel")]
public sealed class ColorDialogTryTests
{
    internal const int Width = 160, Height = 50;

    [Fact]
    public void TryRepaintsEveryWindowWhileTheDialogStaysOpenAndRepeatedTryUpdatesAgain()
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var before = surface.ProbeAttrs();
        var live = app.GetPalette(); byte[] original = live.Data.ToArray();
        var dlg = TrySurface.NewBoundDialog(app);

        app.Step(() => { TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors); });
        app.Step(() =>
        {
            Assert.Same(dlg, TView.ModalTopView);
            Assert.Equal(dlg.Pal!.Data, live.Data);
            surface.AssertProbes(before, 0x08, "first Try");
            MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
            // The dialog stays usable: edit again and Try again.
            TrySurface.EditAll(dlg, 0x10); TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            Assert.Same(dlg, TView.ModalTopView);
            Assert.Equal(dlg.Pal!.Data, live.Data);
            surface.AssertProbes(before, 0x18, "second Try");
            MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dlg));
        Assert.Equal(original, live.Data);
    }

    [Fact]
    public void TryThroughTheRealColorSelectorBroadcastChangesTheDesktopBackgroundCell()
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var bg = surface.DesktopProbe;
        byte before = TrySurface.Attr(bg);
        var dlg = TrySurface.NewBoundDialog(app); // first item: Desktop / Background (palette index 1)
        byte fg = (byte)((before & 0x0F) ^ 0x0F);

        app.Step(() =>
        {
            // Exactly what TColorSelector sends to its owner when the user picks a foreground color.
            var ev = new TEvent { What = Events.evBroadcast };
            ev.message.command = Views.cmColorForegroundChanged; ev.message.infoLong = fg;
            dlg.HandleEvent(ref ev);
            Assert.Equal(before, TrySurface.Attr(bg)); // edits are invisible until Try
            TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            Assert.Equal((byte)((before & 0xF0) | fg), TrySurface.Attr(bg));
            TrySurface.ClickButton(dlg, Views.cmCancel);
        });
        Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dlg));
        Assert.Equal(before, TrySurface.Attr(bg));
    }

    public static IEnumerable<object[]> CancelPaths()
    {
        foreach (var how in new[] { "button", "escape", "closebox" })
            foreach (int tries in new[] { 0, 1, 3 })
                yield return new object[] { how, tries };
    }

    [Theory]
    [MemberData(nameof(CancelPaths))]
    public void CancelEscapeAndCloseBoxRestoreTheExactSnapshotAndComposedCells(string how, int tries)
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var screen = MonochromeModeQualificationTests.Snapshot();
        var probes = surface.ProbeAttrs();
        var tables = PaletteGuard.AllTables(app);
        var dlg = TrySurface.NewBoundDialog(app);

        byte mask = 0;
        for (int i = 0; i < tries; i++)
        {
            byte step = (byte)(i % 2 == 0 ? 0x08 : 0x10);
            app.Step(() => { TrySurface.EditAll(dlg, step); TrySurface.ClickButton(dlg, Views.cmTryColors); });
            mask ^= step;
            byte expected = mask;
            app.Step(() => surface.AssertProbes(probes, expected, $"Try {expected:X2}"));
        }
        // An untried edit pending at Cancel must not leak either.
        app.Step(() =>
        {
            TrySurface.EditAll(dlg, 0x40);
            switch (how)
            {
                case "button": TrySurface.ClickButton(dlg, Views.cmCancel); break;
                case "escape": ComboInput.Send(ComboInput.Key(Keys.kbEsc)); break;
                default: ComboInput.Click(ComboInput.At(dlg, 3, 0)); break;
            }
        });

        Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dlg));
        PaletteGuard.AssertTables(tables, app, $"{how} after {tries} Try");
        MonochromeModeQualificationTests.AssertSameCells(screen, $"{how} after {tries} Try");
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OkKeepsTheFinalEditWithOrWithoutTry(int tries)
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var probes = surface.ProbeAttrs();
        var live = app.GetPalette();
        byte[] expected = live.Data.ToArray();
        for (int i = 1; i < expected.Length; i++) expected[i] ^= 0x08;
        var dlg = TrySurface.NewBoundDialog(app);

        for (int i = 0; i < tries; i++)
            app.Step(() => { TrySurface.EditAll(dlg, 0x10); TrySurface.ClickButton(dlg, Views.cmTryColors); });
        // The final edit (net XOR 0x08 from the original) is made after the last Try, so OK alone must commit it.
        app.Step(() =>
        {
            for (int i = 1; i <= dlg.Pal!.Size; i++) dlg.Pal.Data[i] = expected[i];
            TrySurface.ClickButton(dlg, Views.cmOK);
        });

        Assert.Equal(Views.cmOK, app.DeskTop!.ExecView(dlg));
        Assert.Equal(expected, live.Data);
        surface.AssertProbes(probes, 0x08, $"OK after {tries} Try");
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
        // Finished: a later shutdown of the dialog must not restore anything.
        dlg.ShutDown();
        Assert.Equal(expected, live.Data);
    }

    [Fact]
    public void DirectCallerWithoutOptInIsUnchanged()
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        TrySurface.Build(app);
        var tables = PaletteGuard.AllTables(app);
        var probe = new BroadcastProbe();
        app.DeskTop!.Insert(probe);
        var pal = app.GetPalette().Clone();
        var dlg = new TColorDialog(pal, TrySurface.Groups());
        TScreenChar[]? beforeTry = null;

        app.Step(() =>
        {
            TrySurface.EditAll(dlg, 0x08);
            beforeTry = MonochromeModeQualificationTests.Snapshot();
            TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            // Try only broadcasts cmUpdateColorsChanged to the owner, as before; nothing is applied or repainted.
            Assert.Equal(1, probe.UpdateColorsChanged);
            PaletteGuard.AssertTables(tables, app, "unbound Try");
            MonochromeModeQualificationTests.AssertSameCells(beforeTry!, "unbound Try");
            TrySurface.ClickButton(dlg, Views.cmCancel);
        });

        Assert.Equal(Views.cmCancel, app.DeskTop.ExecView(dlg));
        PaletteGuard.AssertTables(tables, app, "unbound Cancel");
        // The edited copy is left as the editor made it; restoring it is the caller's business, as before.
        Assert.Same(pal, dlg.Pal);
        Assert.NotEqual(app.GetPalette().Data, pal.Data);

        app.Step(() => TrySurface.ClickButton(dlg, Views.cmOK));
        Assert.Equal(Views.cmOK, app.DeskTop.ExecView(dlg));
        PaletteGuard.AssertTables(tables, app, "unbound OK");
        dlg.ShutDown();
        PaletteGuard.AssertTables(tables, app, "unbound shutdown");
    }

    [Theory]
    [InlineData(TProgram.AP.Color, TProgram.AP.BlackWhite, true)]
    [InlineData(TProgram.AP.Color, TProgram.AP.Monochrome, false)]
    [InlineData(TProgram.AP.BlackWhite, TProgram.AP.Color, true)]
    [InlineData(TProgram.AP.Monochrome, TProgram.AP.Color, false)]
    public void ModeChangeWhileOpenNeverWritesAnotherModesTable(TProgram.AP openMode, TProgram.AP switchTo, bool accept)
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        app.SetAppPalette(openMode);
        TrySurface.Build(app);
        var tables = PaletteGuard.AllTables(app);
        var bound = app.GetPalette();
        byte[] edited = bound.Data.ToArray();
        for (int i = 1; i < edited.Length; i++) edited[i] ^= 0x08;
        var dlg = TrySurface.NewBoundDialog(app);

        app.Step(() =>
        {
            app.SetAppPalette(switchTo);
            Array.Copy(edited, dlg.Pal!.Data, edited.Length);
            TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            // Try wrote the table captured at open; the family on screen and the other tables are untouched.
            Assert.Equal(edited, bound.Data);
            Assert.Equal(switchTo, app.AppPalette);
            foreach (var (mode, bytes) in tables)
                if (mode != openMode) Assert.Equal(bytes, PaletteGuard.Table(app, mode).Data);
            // The editor's own swatches are fixed colors by design; everything outside it follows the switched family.
            if (switchTo != TProgram.AP.Color) TrySurface.AssertNeutralOutside(dlg, "switched while open");
            MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
            TrySurface.ClickButton(dlg, accept ? Views.cmOK : Views.cmCancel);
        });

        Assert.Equal(accept ? Views.cmOK : Views.cmCancel, app.DeskTop!.ExecView(dlg));
        Assert.Equal(switchTo, app.AppPalette);
        foreach (var (mode, bytes) in tables)
            Assert.Equal(mode == openMode && accept ? edited : bytes, PaletteGuard.Table(app, mode).Data);
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
    }

    [Fact]
    public void BindingThePaletteBeingEditedItselfStillRestoresOnCancel()
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var screen = MonochromeModeQualificationTests.Snapshot();
        var probes = surface.ProbeAttrs();
        var live = app.GetPalette(); byte[] original = live.Data.ToArray();
        var dlg = new TColorDialog(live, TrySurface.Groups());
        dlg.BindLivePalette(live, app.Redraw);

        app.Step(() => { TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors); });
        app.Step(() => { surface.AssertProbes(probes, 0x08, "in-place Try"); TrySurface.ClickButton(dlg, Views.cmCancel); });
        Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dlg));
        Assert.Equal(original, live.Data);
        MonochromeModeQualificationTests.AssertSameCells(screen, "in-place Cancel");
    }

    [Fact]
    public void ModelessBoundDialogRestoresAndRepaintsWhenClosed()
    {
        using var scope = new DemoTestScope(Width, Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var live = app.GetPalette(); byte[] original = live.Data.ToArray();
        var dlg = TrySurface.NewBoundDialog(app);
        app.DeskTop!.Insert(dlg);
        var probes = surface.ProbeAttrs(); // after the insert, which deactivates the front window's frame
        TrySurface.EditAll(dlg, 0x08);
        var tryEv = ScriptedDemoApp.CommandEvent(Views.cmTryColors); dlg.HandleEvent(ref tryEv);
        surface.AssertProbes(probes, 0x08, "modeless Try");

        dlg.Close();
        Assert.Null(dlg.owner);
        Assert.Equal(original, live.Data);
        // Window activation may differ after the modeless dialog leaves, so compare the content probes and staleness.
        surface.AssertProbes(probes, 0, "modeless Close", textOnly: true);
        Assert.Equal(probes[^1], TrySurface.Attr(surface.DesktopProbe));
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
    }

    [Fact]
    public void BindLivePaletteValidatesItsArguments()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var dlg = new TColorDialog(app.GetPalette().Clone(), TrySurface.Groups());
        Assert.Throws<ArgumentNullException>(() => dlg.BindLivePalette(null!, app.Redraw));
        Assert.Throws<ArgumentNullException>(() => dlg.BindLivePalette(app.GetPalette(), null!));
        dlg.BindLivePalette(app.GetPalette(), app.Redraw);
        Assert.Throws<InvalidOperationException>(() => dlg.BindLivePalette(app.GetPalette(), app.Redraw));
        dlg.ShutDown();
    }

    private sealed class BroadcastProbe : TView
    {
        internal int UpdateColorsChanged;
        internal BroadcastProbe() : base(new TRect(0, 0, 0, 0)) { eventMask |= Events.evBroadcast; }
        public override void HandleEvent(ref TEvent @event)
        {
            base.HandleEvent(ref @event);
            if (@event.What == Events.evBroadcast && @event.message.command == Views.cmUpdateColorsChanged) UpdateColorsChanged++;
        }
    }
}

// Saves and restores every application palette table (they are process-wide statics) and the selected family.
internal sealed class PaletteGuard : IDisposable
{
    private readonly TProgram _app;
    private readonly TProgram.AP _mode;
    private readonly List<(TProgram.AP Mode, byte[] Bytes)> _tables;

    internal PaletteGuard(TProgram app) { _app = app; _mode = app.AppPalette; _tables = AllTables(app); }

    internal static TPalette Table(TProgram app, TProgram.AP mode)
    {
        var saved = app.AppPalette; app.AppPalette = mode;
        try { return app.GetPalette(); } finally { app.AppPalette = saved; }
    }

    internal static List<(TProgram.AP Mode, byte[] Bytes)> AllTables(TProgram app) =>
        Enum.GetValues<TProgram.AP>().Select(m => (m, Table(app, m).Data.ToArray())).ToList();

    internal static void AssertTables(List<(TProgram.AP Mode, byte[] Bytes)> expected, TProgram app, string context)
    {
        foreach (var (mode, bytes) in expected)
            Assert.True(bytes.AsSpan().SequenceEqual(Table(app, mode).Data), $"{context}: {mode} table changed");
    }

    public void Dispose()
    {
        foreach (var (mode, bytes) in _tables) Array.Copy(bytes, Table(_app, mode).Data, bytes.Length);
        _app.AppPalette = _mode;
    }
}

// Overlapping windows plus a nested ofBuffered group, all kept clear of the centered editor on a 160x50 screen.
internal sealed class TrySurface
{
    private readonly List<TView> _probes = new();
    internal TPoint DesktopProbe { get; private set; }

    internal static TColorGroup Groups() =>
        new TColorGroup("Desktop", new TColorItem("Background", 1)) +
        new TColorGroup("Window", new TColorItem("Frame", 8) + new TColorItem("Title", 9));

    internal static TColorDialog NewBoundDialog(TProgram app)
    {
        var dlg = new TColorDialog(app.GetPalette().Clone(), Groups());
        dlg.BindLivePalette(app.GetPalette(), app.Redraw);
        return dlg;
    }

    internal static TrySurface Build(TProgram app)
    {
        var desk = app.DeskTop!;
        var s = new TrySurface();
        // Two overlapping windows on the left (the dialog is centered at x 41..117).
        var back = new TWindow(new TRect(0, 0, 34, 12), "Back", 1);
        var backText = new TStaticText(new TRect(2, 2, 20, 3), "Back text");
        back.Insert(backText);
        var front = new TDialog(new TRect(6, 5, 38, 16), "Front");
        var frontText = new TStaticText(new TRect(2, 2, 20, 3), "Front text");
        front.Insert(frontText);
        // A dialog on the right whose content lives in a nested buffered group.
        var nested = new TDialog(new TRect(120, 2, 158, 14), "Nested");
        var inner = new TGroup(new TRect(2, 2, 34, 8));
        inner.options |= Views.ofBuffered;
        var innerText = new TStaticText(new TRect(1, 1, 24, 2), "Nested buffered text");
        inner.Insert(innerText); nested.Insert(inner);
        desk.Insert(back); desk.Insert(nested); desk.Insert(front);
        app.Redraw();
        Assert.NotNull(inner.buffer);
        s._probes.AddRange(new TView[] { backText, frontText, innerText });
        s._frames.AddRange(new TWindow[] { back, front, nested });
        s.DesktopProbe = new TPoint(desk.size.x - 2, desk.size.y - 2 + desk.origin.y);
        return s;
    }

    private readonly List<TWindow> _frames = new();

    private IEnumerable<TPoint> Points()
    {
        foreach (var v in _probes) yield return v.MakeGlobal(new TPoint(0, 0));
        // Top-left frame corner of each window: the back one is overlapped elsewhere but this corner is visible.
        foreach (var w in _frames) yield return w.MakeGlobal(new TPoint(0, 0));
        yield return DesktopProbe;
    }

    internal byte[] ProbeAttrs() => Points().Select(Attr).ToArray();

    internal void AssertProbes(byte[] before, byte xorMask, string context, bool textOnly = false)
    {
        var points = Points().ToArray();
        for (int i = 0; i < (textOnly ? _probes.Count : points.Length); i++)
            Assert.True((byte)(before[i] ^ xorMask) == Attr(points[i]),
                $"{context}: probe {i} at {points[i].x},{points[i].y} expected {(byte)(before[i] ^ xorMask):X2} got {Attr(points[i]):X2}");
    }

    internal static void AssertNeutralOutside(TView excluded, string context)
    {
        var screen = RootScreenCopy.CopyRoot();
        var a = excluded.MakeGlobal(new TPoint(0, 0));
        // Include the shadow area (2 columns right, 1 row below) of the excluded view.
        var b = new TPoint(a.x + excluded.size.x + 2, a.y + excluded.size.y + 1);
        for (int y = 0; y < TScreen.ScreenHeight; y++)
            for (int x = 0; x < TScreen.ScreenWidth; x++)
            {
                if (x >= a.x && x < b.x && y >= a.y && y < b.y) continue;
                byte attr = (byte)screen[x, y].Attr;
                Assert.True((attr & 15) is 0 or 7 or 8 or 15 && (attr >> 4) is 0 or 7,
                    $"{context}: colored cell {x},{y} '{screen[x, y].Character}' {attr:X2}");
            }
    }

    internal static byte Attr(TPoint p) => (byte)RootScreenCopy.CopyRoot()[p.x, p.y].Attr;

    internal static void EditAll(TColorDialog dlg, byte xorMask)
    {
        for (int i = 1; i <= dlg.Pal!.Size; i++) dlg.Pal.Data[i] ^= xorMask;
    }

    internal static void ClickButton(TGroup dialog, ushort command)
    {
        var button = ScriptedDemoApp.Button(dialog, command);
        ComboInput.Click(ComboInput.At(button, 2, 0));
    }
}
