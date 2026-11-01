using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Samples.MonochromeModeQualificationTests;

namespace TSharpVision.Tests.Samples;

// TV12d: TVDemo Window > Save/Load desktop state and startup restore the framework palette family and the three
// family tables. Every test points TVDemo at a private snapshot file and restores the process-wide palette tables
// and AppPalette (PaletteGuard per program; TVDemoApp.ShutDown puts back the tables it found at start).
[Collection("NonParallel")]
public sealed class DesktopPaletteModePersistenceTests
{
    private const int ProbeX = 79, ProbeY = 23; // desktop background (palette entry 1) on an 80x25 screen
    private static readonly TProgram.AP[] Modes = { TProgram.AP.Color, TProgram.AP.BlackWhite, TProgram.AP.Monochrome };

    // A distinct, recognizable entry-1 (desktop background) edit per family; BW/Mono edits stay neutral.
    private static readonly Dictionary<TProgram.AP, byte> Edit = new()
    {
        [TProgram.AP.Color] = 0x1E, [TProgram.AP.BlackWhite] = 0x0F, [TProgram.AP.Monochrome] = 0x07,
    };

    private static ushort Command(TProgram.AP mode) => mode switch
    {
        TProgram.AP.BlackWhite => ShowcaseCmd.PaletteBlackWhite,
        TProgram.AP.Monochrome => ShowcaseCmd.PaletteMonochrome,
        _ => ShowcaseCmd.PaletteColor,
    };

    private static char Hot(TProgram.AP mode) => mode switch { TProgram.AP.BlackWhite => 'B', TProgram.AP.Monochrome => 'M', _ => 'C' };

    // ---- format 1 (HEAD) compatibility ----

    [Fact]
    public void LegacyWriterStillProducesTheExactHeadBytes()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope();
        string head = file.Path + ".head";
        var windows = SampleWindows();
        try
        {
            byte[] palette = DesktopState.DefaultTable(TProgram.AP.Color); palette[1] = 0x1E;
            HeadFormat.Save(head, windows, palette);
            DesktopState.Save(file.Path, windows, palette);
            Assert.Equal(File.ReadAllBytes(head), File.ReadAllBytes(file.Path));
        }
        finally { foreach (var w in windows) w.ShutDown(); File.Delete(head); }
    }

    [Fact]
    public void HeadSnapshotLoadsAsTheColorTableAndSelectsColorLeavingOtherTablesAlone()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        byte[] legacy = DesktopState.DefaultTable(TProgram.AP.Color); legacy[1] = 0x1E; legacy[9] = 0x4F;
        var windows = SampleWindows();
        try { HeadFormat.Save(file.Path, windows, legacy); } finally { foreach (var w in windows) w.ShutDown(); }
        byte[] fileBytes = File.ReadAllBytes(file.Path);

        // In-session edits of the other families, and BW active: a format 1 file says nothing about them.
        PaletteGuard.Table(app, TProgram.AP.BlackWhite).Data[1] = 0x0F;
        PaletteGuard.Table(app, TProgram.AP.Monochrome).Data[1] = 0x07;
        var bw = PaletteGuard.Table(app, TProgram.AP.BlackWhite).Data.ToArray();
        var mono = PaletteGuard.Table(app, TProgram.AP.Monochrome).Data.ToArray();
        app.RunCommand(ShowcaseCmd.PaletteBlackWhite);

        app.DismissResult("older snapshot"); app.RunCommand(ShowcaseCmd.LoadDesktop);

        Assert.Equal(TProgram.AP.Color, app.AppPalette);
        AssertMarked(app, 'C');
        Assert.Equal(legacy, PaletteGuard.Table(app, TProgram.AP.Color).Data);
        Assert.Equal(bw, PaletteGuard.Table(app, TProgram.AP.BlackWhite).Data);
        Assert.Equal(mono, PaletteGuard.Table(app, TProgram.AP.Monochrome).Data);
        Assert.Equal(0x1E, ProbeAttr());
        AssertRestoredSampleWindows(app);
        Assert.Equal(fileBytes, File.ReadAllBytes(file.Path)); // loading never rewrites the user's file
    }

    [Fact]
    public void HeadSnapshotAtStartupSetsColorTableAndColorFamily()
    {
        using var file = new SnapshotFile();
        byte[] legacy = DesktopState.DefaultTable(TProgram.AP.Color); legacy[1] = 0x1E;
        using (var writer = new DemoTestScope())
        {
            var windows = SampleWindows();
            try { HeadFormat.Save(file.Path, windows, legacy); } finally { foreach (var w in windows) w.ShutDown(); }
        }
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        Assert.Equal(TProgram.AP.Color, app.AppPalette);
        Assert.Equal(legacy, PaletteGuard.Table(app, TProgram.AP.Color).Data);
        Assert.Equal(DesktopState.DefaultTable(TProgram.AP.BlackWhite), PaletteGuard.Table(app, TProgram.AP.BlackWhite).Data);
        Assert.Equal(0x1E, ProbeAttr());
        Assert.DoesNotContain(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TWindow>(), w => w.title == "First");
    }

    // A pre-TV12d TVDemo reads a new snapshot through its own (HEAD) reader: it gets the windows and the Color table.
    [Fact]
    public void HeadReaderLoadsANewSnapshotAsColorOnly()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        EditAllFamilies(app);
        app.RunCommand(ShowcaseCmd.PaletteMonochrome);
        var windows = SampleWindows();
        try { DesktopState.Save(file.Path, windows, app.CapturePalettes()); } finally { foreach (var w in windows) w.ShutDown(); }

        var (restored, palette) = HeadFormat.Load(file.Path, DesktopState.DefaultTable(TProgram.AP.Color).Length);
        try
        {
            Assert.Equal(PaletteGuard.Table(app, TProgram.AP.Color).Data, palette); // never the active Monochrome table
            Assert.Equal(new[] { "First", "Second" }, restored.Select(w => w.title));
        }
        finally { foreach (var w in restored) w.ShutDown(); }
    }

    // ---- three families ----

    [Theory]
    [InlineData(TProgram.AP.Color)] [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void SaveRestartRestoresFamilyAllThreeTablesMarkerAndScreen(TProgram.AP mode)
    {
        using var file = new SnapshotFile();
        List<(TProgram.AP Mode, byte[] Bytes)> pristine, edited;
        using (var first = new DemoTestScope())
        {
            var app = first.App;
            using var guard = new PaletteGuard(app);
            pristine = PaletteGuard.AllTables(app);
            EditAllFamilies(app);
            edited = PaletteGuard.AllTables(app);
            app.RunCommand(Command(mode));
            AddSampleWindows(app);
            app.DismissResult("Saved 3 demonstration windows"); app.RunCommand(ShowcaseCmd.SaveDesktop);
        }

        using (var scope = new DemoTestScope())
        {
            var restarted = scope.App;
            using var guard2 = new PaletteGuard(restarted);
            // ShutDown of the first program put the shared tables back before this one started; startup re-applied them.
            Assert.Equal(mode, restarted.AppPalette);
            AssertMarked(restarted, Hot(mode));
            PaletteGuard.AssertTables(edited, restarted, $"restart in {mode}");
            Assert.Equal(Edit[mode], ProbeAttr());
            if (mode != TProgram.AP.Color) AssertNeutralScreen($"restart in {mode}");
            AssertIncrementalMatchesFullRedraw(restarted);
            // Startup restores palettes only; Load desktop state brings back the windows with their geometry.
            Assert.DoesNotContain(ScriptedDemoApp.Descendants(restarted.DeskTop!).OfType<TWindow>(), w => w.title == "First");
            restarted.DismissResult("Restored 3"); restarted.RunCommand(ShowcaseCmd.LoadDesktop);
            AssertRestoredSampleWindows(restarted);
            Assert.Equal(mode, restarted.AppPalette);
        }

        // After that program shut down, the process-wide tables are back to what the first program found.
        File.Delete(file.Path);
        using var after = new DemoTestScope();
        PaletteGuard.AssertTables(pristine, after.App, "after shutdown");
        Assert.Equal(TProgram.AP.Color, after.App.AppPalette);
    }

    [Theory]
    [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void SavingInANeutralFamilyNeverWritesItsTableIntoColor(TProgram.AP mode)
    {
        using var file = new SnapshotFile();
        using (var first = new DemoTestScope())
        {
            var app = first.App;
            using var guard = new PaletteGuard(app);
            app.RunCommand(Command(mode));
            PaletteGuard.Table(app, mode).Data[1] = Edit[mode]; // only the active family is customized
            app.DismissResult("Saved"); app.RunCommand(ShowcaseCmd.SaveDesktop);
        }

        var record = ReadRecords(file.Path);
        Assert.Equal(Convert.ToHexString(DesktopState.DefaultTable(TProgram.AP.Color)), record.Metadata[1]);
        Assert.Equal("mode=" + mode, record.Palettes![1]);
        foreach (var other in Modes)
        {
            string entry = record.Palettes.Single(e => e.StartsWith(other + "=", StringComparison.Ordinal));
            if (other == mode) Assert.NotEqual($"{other}=default", entry);
            else Assert.Equal($"{other}=default", entry); // uncustomized families keep the built-in table
        }

        using var scope = new DemoTestScope(); var app2 = scope.App;
        using var guard2 = new PaletteGuard(app2);
        Assert.Equal(DesktopState.DefaultTable(TProgram.AP.Color), PaletteGuard.Table(app2, TProgram.AP.Color).Data);
        Assert.Equal(Edit[mode], PaletteGuard.Table(app2, mode).Data[1]);
        app2.RunCommand(ShowcaseCmd.PaletteColor);
        Assert.Equal(DesktopState.DefaultTable(TProgram.AP.Color)[1], ProbeAttr());
    }

    [Fact]
    public void EachFamilyRoundTripsIndependentlyAndDefaultsResetEditedTables()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        foreach (var edited in Modes)
        {
            // Save with exactly one customized family.
            foreach (var m in Modes) Array.Copy(DesktopState.DefaultTable(m), PaletteGuard.Table(app, m).Data, DesktopState.DefaultTable(m).Length);
            PaletteGuard.Table(app, edited).Data[1] = Edit[edited];
            app.RunCommand(Command(edited));
            app.DismissResult("Saved"); app.RunCommand(ShowcaseCmd.SaveDesktop);

            // Scribble over every table and switch family; Load must undo all of it.
            foreach (var m in Modes) PaletteGuard.Table(app, m).Data[1] = 0x4E;
            app.RunCommand(Command(edited == TProgram.AP.Color ? TProgram.AP.Monochrome : TProgram.AP.Color));
            app.DismissResult("Restored"); app.RunCommand(ShowcaseCmd.LoadDesktop);

            Assert.Equal(edited, app.AppPalette);
            AssertMarked(app, Hot(edited));
            foreach (var m in Modes)
            {
                byte[] expected = DesktopState.DefaultTable(m);
                if (m == edited) expected[1] = Edit[m];
                Assert.Equal(expected, PaletteGuard.Table(app, m).Data);
            }
            Assert.Equal(Edit[edited], ProbeAttr());
        }
    }

    [Fact]
    public void RepeatedSaveAndLoadKeepsEveryRoundExact()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        AddSampleWindows(app);
        for (int round = 0; round < 3; round++)
            foreach (var mode in Modes)
            {
                PaletteGuard.Table(app, mode).Data[2] = (byte)(0x10 + round);
                app.RunCommand(Command(mode));
                var saved = PaletteGuard.AllTables(app);
                app.DismissResult("Saved 3"); app.RunCommand(ShowcaseCmd.SaveDesktop);
                app.DismissResult("Restored 3"); app.RunCommand(ShowcaseCmd.LoadDesktop);
                Assert.Equal(mode, app.AppPalette);
                PaletteGuard.AssertTables(saved, app, $"round {round} {mode}");
                AssertRestoredSampleWindows(app);
                Assert.Equal(2, ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TWindow>().Count(w => w.title is "First" or "Second"));
            }
        AssertIncrementalMatchesFullRedraw(app);
    }

    // ---- missing, corrupt, unknown ----

    [Fact]
    public void WithoutASnapshotStartupKeepsDefaultsAndLoadReportsIt()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        Assert.Equal(TProgram.AP.Color, app.AppPalette);
        AssertMarked(app, 'C');
        var tables = PaletteGuard.AllTables(app);
        app.RunCommand(ShowcaseCmd.PaletteMonochrome);
        app.DismissResult("No desktop state has been saved yet"); app.RunCommand(ShowcaseCmd.LoadDesktop);
        Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);
        PaletteGuard.AssertTables(tables, app, "missing file");
        Assert.False(File.Exists(file.Path));
    }

    public static IEnumerable<object[]> Corruptions() => new[]
    {
        "empty", "garbage", "truncated-header", "truncated-middle", "truncated-tail",
        "newer-desktop-version", "newer-palettes-version", "not-a-desktop", "mode-number", "mode-case", "mode-unknown",
        "bad-hex", "short-table", "size-byte", "extra-entry", "missing-entry", "wrong-key", "record-is-window",
        "metadata-extra", "missing-window", "negative-count",
    }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void CorruptOrUnknownSnapshotIsReportedAndNothingIsApplied(string corruption)
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        WriteCorrupt(app, file.Path, corruption);
        byte[] fileBytes = File.ReadAllBytes(file.Path);

        // A live session with its own windows, edits and family.
        PaletteGuard.Table(app, TProgram.AP.Color).Data[1] = 0x2F;
        PaletteGuard.Table(app, TProgram.AP.BlackWhite).Data[1] = 0x0F;
        app.RunCommand(ShowcaseCmd.PaletteBlackWhite);
        var live = new TWindow(new TRect(10, 3, 40, 12), "Live", 9);
        app.DeskTop!.Insert(live);
        var tables = PaletteGuard.AllTables(app);
        var windows = ScriptedDemoApp.Descendants(app.DeskTop).OfType<TWindow>().ToList();
        var cells = Snapshot();

        app.DismissResult("Persistence failed"); app.RunCommand(ShowcaseCmd.LoadDesktop);
        AssertUnchanged("Load desktop state");

        // The startup path (LoadPalette) validates the same way.
        app.DismissResult("Persistence failed");
        typeof(TVDemoApp).GetMethod("LoadPalette", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        AssertUnchanged("startup");

        void AssertUnchanged(string context)
        {
            Assert.Equal(TProgram.AP.BlackWhite, app.AppPalette);
            AssertMarked(app, 'B');
            PaletteGuard.AssertTables(tables, app, $"{corruption} {context}");
            Assert.Equal(windows, ScriptedDemoApp.Descendants(app.DeskTop).OfType<TWindow>().ToList());
            AssertSameCells(cells, $"{corruption} {context}");
            Assert.Equal(fileBytes, File.ReadAllBytes(file.Path)); // the user's file is never deleted or rewritten
        }
    }

    [Theory]
    [InlineData("newer-desktop-version", "newer TVDemo")] [InlineData("newer-palettes-version", "newer TVDemo")]
    [InlineData("not-a-desktop", "Not a TVDemo desktop snapshot")] [InlineData("garbage", "Not a TVDemo desktop snapshot")]
    [InlineData("mode-number", "Unknown framework palette")]
    public void FailureMessagesNameTheProblem(string corruption, string message)
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(); var app = scope.App;
        using var guard = new PaletteGuard(app);
        WriteCorrupt(app, file.Path, corruption);
        var ex = Assert.ThrowsAny<Exception>(() => DesktopState.Load(file.Path));
        Assert.Contains(message, ex.Message);
    }

    // ---- color dialog with a pending Try ----

    [Fact]
    public void SaveCannotCaptureAPendingTryAndLaterSavesTheRestoredPalette()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(ColorDialogTryTests.Width, ColorDialogTryTests.Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        app.RunCommand(ShowcaseCmd.PaletteMonochrome);
        var tables = PaletteGuard.AllTables(app);

        app.Step(() =>
        {
            var dlg = app.Modal<TColorDialog>();
            TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            // The preview is live now.
            Assert.NotEqual(tables.Single(t => t.Mode == TProgram.AP.Monochrome).Bytes, app.GetPalette().Data);
            // Menu route: the modal dialog owns the event loop, so the Window menu and its command never reach TVDemo.
            ScriptedDemoApp.SendCommand(ShowcaseCmd.SaveDesktop);
            var altW = ComboInput.Key(Keys.kbAltW); TEventQueue.Enqueue(altW);
        });
        app.Step(() =>
        {
            Assert.IsType<TColorDialog>(TView.ModalTopView);
            Assert.False(File.Exists(file.Path));
            // Any other route that dispatches the command to the application is refused while the dialog runs.
            app.Step(() =>
            {
                Assert.Contains("Close the open dialog", ScriptedDemoApp.Text(app.Modal<TDialog>()));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc))); // then cancel the color dialog
            var save = ScriptedDemoApp.CommandEvent(ShowcaseCmd.SaveDesktop); app.HandleEvent(ref save);
            Assert.False(File.Exists(file.Path));
        });
        app.RunCommand(ShowcaseCmd.Colors);

        PaletteGuard.AssertTables(tables, app, "after Cancel");
        app.DismissResult("Saved"); app.RunCommand(ShowcaseCmd.SaveDesktop);
        var record = ReadRecords(file.Path);
        Assert.Equal(new[] { "TVDemo palettes 1", "mode=Monochrome", "Color=default", "BlackWhite=default", "Monochrome=default" }, record.Palettes);
    }

    [Fact]
    public void SaveAfterTryAndOkKeepsTheEditInTheActiveFamilyOnly()
    {
        using var file = new SnapshotFile();
        using var scope = new DemoTestScope(ColorDialogTryTests.Width, ColorDialogTryTests.Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        app.RunCommand(ShowcaseCmd.PaletteBlackWhite);
        byte[] expected = app.GetPalette().Data.ToArray();
        for (int i = 1; i < expected.Length; i++) expected[i] ^= 0x08;
        app.Step(() => { var dlg = app.Modal<TColorDialog>(); TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors); });
        app.Step(() => TrySurface.ClickButton(app.Modal<TColorDialog>(), Views.cmOK));
        app.RunCommand(ShowcaseCmd.Colors);
        app.DismissResult("Saved"); app.RunCommand(ShowcaseCmd.SaveDesktop);

        var record = ReadRecords(file.Path);
        Assert.Equal(new[] { "TVDemo palettes 1", "mode=BlackWhite", "Color=default", "BlackWhite=" + Convert.ToHexString(expected), "Monochrome=default" }, record.Palettes);
        Assert.Equal(Convert.ToHexString(DesktopState.DefaultTable(TProgram.AP.Color)), record.Metadata[1]);
    }

    // ---- helpers ----

    private static void EditAllFamilies(TProgram app)
    {
        foreach (var mode in Modes) PaletteGuard.Table(app, mode).Data[1] = Edit[mode];
    }

    private static int ProbeAttr() => (byte)RootScreenCopy.CopyRoot()[ProbeX, ProbeY].Attr;

    private static List<TWindow> SampleWindows()
    {
        var first = new TWindow(new TRect(2, 3, 42, 15), "First", 7);
        first.Insert(new TStaticText(new TRect(2, 2, 30, 4), "Saved text"));
        var second = new TWindow(new TRect(5, 4, 45, 16), "Second", 8);
        return new List<TWindow> { first, second };
    }

    private static void AddSampleWindows(ScriptedDemoApp app)
    {
        var windows = SampleWindows();
        for (int i = windows.Count - 1; i >= 0; i--) app.DeskTop!.Insert(windows[i]);
    }

    private static void AssertRestoredSampleWindows(ScriptedDemoApp app)
    {
        var windows = ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TWindow>().ToList();
        var first = Assert.Single(windows, w => w.title == "First");
        var second = Assert.Single(windows, w => w.title == "Second");
        Assert.Equal(new TRect(2, 3, 42, 15), first.GetBounds());
        Assert.Equal(new TRect(5, 4, 45, 16), second.GetBounds());
        Assert.Equal((ushort)7, first.number);
        Assert.Equal((ushort)8, second.number);
        Assert.Contains("Saved text", ScriptedDemoApp.Text(first));
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
            Assert.StartsWith(item.HotChar() == hot ? $"({TSharpVisionGlyphs.RadioChecked})" : "( )", item.Name);
    }

    private sealed record Records(List<string> Metadata, List<string>? Palettes);

    private static Records ReadRecords(string path)
    {
        StreamableRegistration.RegisterAll();
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var resources = new TResourceFile(new Fpstream(file));
        var meta = Assert.IsType<TStringCollection>(resources.Get("metadata"));
        var palettes = resources.Get("palettes") as TStringCollection;
        return new Records(meta.Items.ToList(), palettes?.Items.ToList());
    }

    private static TStringCollection Strings(IEnumerable<string> items)
    {
        var c = new TStringCollection();
        foreach (var s in items) c.Insert(s);
        return c;
    }

    private static void WriteRecords(string path, IList<string> metadata, IList<string>? palettes, int windows, TStreamable? palettesOverride = null)
    {
        StreamableRegistration.RegisterAll();
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        var stream = new Fpstream(file);
        var resources = new TResourceFile(stream);
        resources.Put(Strings(metadata), "metadata");
        var sample = SampleWindows();
        try
        {
            for (int i = 0; i < windows; i++) resources.Put(sample[i], $"window.{i:D5}");
            if (palettesOverride != null) resources.Put(palettesOverride, "palettes");
            else if (palettes != null) resources.Put(Strings(palettes), "palettes");
            resources.Flush();
        }
        finally { foreach (var w in sample) w.ShutDown(); }
    }

    // Starts from a valid snapshot (Monochrome, all three families edited) and breaks one thing.
    private static void WriteCorrupt(ScriptedDemoApp app, string path, string corruption)
    {
        string color = Convert.ToHexString(Edited(TProgram.AP.Color));
        var metadata = new List<string> { "TVDemo desktop 1", color, "2" };
        var palettes = new List<string>
        {
            "TVDemo palettes 1", "mode=Monochrome", "Color=" + color,
            "BlackWhite=" + Convert.ToHexString(Edited(TProgram.AP.BlackWhite)), "Monochrome=" + Convert.ToHexString(Edited(TProgram.AP.Monochrome)),
        };
        int windows = 2;
        TStreamable? record = null;
        switch (corruption)
        {
            case "empty": File.WriteAllBytes(path, Array.Empty<byte>()); return;
            case "garbage": File.WriteAllBytes(path, Enumerable.Range(0, 300).Select(i => (byte)(i * 37)).ToArray()); return;
            case "newer-desktop-version": metadata[0] = "TVDemo desktop 2"; metadata.Add("future"); break;
            case "newer-palettes-version": palettes[0] = "TVDemo palettes 2"; palettes.Add("future=1"); break;
            case "not-a-desktop": metadata[0] = "Something else"; break;
            case "mode-number": palettes[1] = "mode=1"; break;
            case "mode-case": palettes[1] = "mode=monochrome"; break;
            case "mode-unknown": palettes[1] = "mode=Sepia"; break;
            case "bad-hex": palettes[3] = "BlackWhite=ZZ" + palettes[3][13..]; break;
            case "short-table": palettes[4] = palettes[4][..^2]; break;
            case "size-byte": palettes[3] = "BlackWhite=00" + palettes[3][13..]; break;
            case "extra-entry": palettes.Add("Sepia=default"); break;
            case "missing-entry": palettes.RemoveAt(4); break;
            case "wrong-key": palettes[2] = "Colour=default"; break;
            case "record-is-window": record = new TWindow(new TRect(0, 0, 20, 5), "Not a record", 1); break;
            case "metadata-extra": metadata.Add("mode=Monochrome"); break; // never read trailing data as a mode
            case "missing-window": windows = 1; break;
            case "negative-count": metadata[2] = "-1"; break;
            case "truncated-header" or "truncated-middle" or "truncated-tail": break;
            default: throw new ArgumentException(corruption);
        }
        WriteRecords(path, metadata, palettes, windows, record);
        if (record is TView v) v.ShutDown();
        if (corruption.StartsWith("truncated", StringComparison.Ordinal))
        {
            byte[] bytes = File.ReadAllBytes(path);
            int keep = corruption switch { "truncated-header" => 6, "truncated-middle" => bytes.Length / 2, _ => bytes.Length - 3 };
            File.WriteAllBytes(path, bytes[..keep]);
        }

        static byte[] Edited(TProgram.AP mode) { var t = DesktopState.DefaultTable(mode); t[1] = 0x70; t[5] = 0x07; return t; }
    }

    private sealed class SnapshotFile : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tv12d-" + Guid.NewGuid().ToString("N") + ".tvr");
        private readonly string? _previous = TVDemoApp.DesktopPathOverride;
        internal SnapshotFile() => TVDemoApp.DesktopPathOverride = Path;
        public void Dispose() { TVDemoApp.DesktopPathOverride = _previous; File.Delete(Path); }
    }

    // Verbatim copies of the HEAD (ffb4d5c) DesktopState.Save/Load logic: the format 1 writer and an old TVDemo's reader.
    private static class HeadFormat
    {
        internal static void Save(string path, IReadOnlyList<TWindow> windows, byte[] palette)
        {
            StreamableRegistration.RegisterAll();
            using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            var stream = new Fpstream(file);
            var resources = new TResourceFile(stream);
            var metadata = new TStringCollection();
            metadata.Insert("TVDemo desktop 1");
            metadata.Insert(Convert.ToHexString(palette));
            metadata.Insert(windows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            resources.Put(metadata, "metadata");
            for (int i = 0; i < windows.Count; i++)
                resources.Put(windows[i], $"window.{i:D5}");
            resources.Flush();
            Assert.Equal(0, stream.Out.Fail());
        }

        internal static (List<TWindow> Windows, byte[] Palette) Load(string path, int paletteLength)
        {
            StreamableRegistration.RegisterAll();
            var windows = new List<TWindow>();
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var copy = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                4096, FileOptions.DeleteOnClose);
            file.CopyTo(copy);
            copy.Position = 0;
            var stream = new Fpstream(copy);
            var resources = new TResourceFile(stream);
            if (resources.Get("metadata") is not TStringCollection meta || meta.Count != 3
                || meta[0] != "TVDemo desktop 1") throw new InvalidDataException("Not a TVDemo desktop snapshot.");
            byte[] palette = Convert.FromHexString(meta[1]);
            if (palette.Length != paletteLength) throw new InvalidDataException("Palette size differs from this application.");
            if (!int.TryParse(meta[2], out int count) || count < 0 || count > 1000)
                throw new InvalidDataException("Invalid window count.");
            for (int i = 0; i < count; i++)
            {
                if (resources.Get($"window.{i:D5}") is not TWindow window)
                    throw new InvalidDataException("Missing saved window.");
                windows.Add(window);
            }
            if (stream.In.Fail() != 0) throw new InvalidDataException("Incomplete desktop snapshot.");
            return (windows, palette);
        }
    }
}
