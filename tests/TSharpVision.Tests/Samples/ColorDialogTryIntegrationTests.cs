using System.Runtime.CompilerServices;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using Xunit;

namespace TSharpVision.Tests.Samples;

// TV12c: TVDemo Options > UI colors drives the real menu, command dispatch and modal TColorDialog with live Try.
// TVTerm is an executable outside this test assembly, so its Options > UI Colors wiring is pinned at source level.
[Collection("NonParallel")]
public sealed class ColorDialogTryIntegrationTests
{
    [Fact]
    public void TVDemoMenuTryRepaintsLiveThenEscapeRestoresPaletteAndScreen()
    {
        using var scope = new DemoTestScope(ColorDialogTryTests.Width, ColorDialogTryTests.Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var screen = MonochromeModeQualificationTests.Snapshot();
        var probes = surface.ProbeAttrs();
        var tables = PaletteGuard.AllTables(app);

        // Alt+O, then the "UI colors" mnemonic, through the real menu loop.
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            Assert.Contains(TVDemoMenuNavigationTests.Items(box.Menu!), i => i.Command == ShowcaseCmd.Colors);
            ComboInput.Send(ComboInput.Typed("c"));
        });
        var altO = ComboInput.Key(Keys.kbAltO); app.HandleEvent(ref altO);
        TEvent ev = default; app.GetEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(ShowcaseCmd.Colors, ev.message.command);

        app.Step(() =>
        {
            var dlg = app.Modal<TColorDialog>();
            Assert.NotSame(app.GetPalette().Data, dlg.Pal!.Data);
            TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            var dlg = app.Modal<TColorDialog>();
            Assert.Equal(dlg.Pal!.Data, app.GetPalette().Data);
            surface.AssertProbes(probes, 0x08, "TVDemo Try");
            MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
            TrySurface.EditAll(dlg, 0x10); TrySurface.ClickButton(dlg, Views.cmTryColors);
        });
        app.Step(() =>
        {
            surface.AssertProbes(probes, 0x18, "TVDemo second Try");
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ev.message.command);

        PaletteGuard.AssertTables(tables, app, "TVDemo Escape");
        MonochromeModeQualificationTests.AssertSameCells(screen, "TVDemo Escape");
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TVDemoColorsOkKeepsTheEditWithOrWithoutTry(bool tryFirst)
    {
        using var scope = new DemoTestScope(ColorDialogTryTests.Width, ColorDialogTryTests.Height); var app = scope.App;
        using var guard = new PaletteGuard(app);
        var surface = TrySurface.Build(app);
        var probes = surface.ProbeAttrs();
        byte[] expected = app.GetPalette().Data.ToArray();
        for (int i = 1; i < expected.Length; i++) expected[i] ^= 0x08;

        if (tryFirst)
            app.Step(() => { var dlg = app.Modal<TColorDialog>(); TrySurface.EditAll(dlg, 0x08); TrySurface.ClickButton(dlg, Views.cmTryColors); });
        app.Step(() =>
        {
            var dlg = app.Modal<TColorDialog>();
            if (!tryFirst) TrySurface.EditAll(dlg, 0x08);
            TrySurface.ClickButton(dlg, Views.cmOK);
        });
        app.RunCommand(ShowcaseCmd.Colors);

        Assert.Equal(expected, app.GetPalette().Data);
        surface.AssertProbes(probes, 0x08, $"TVDemo OK (try first: {tryFirst})");
        MonochromeModeQualificationTests.AssertIncrementalMatchesFullRedraw(app);
    }

    [Fact]
    public void TVTermUiColorsOptsIntoLiveTryAndNoLongerCopiesBackManually()
    {
        string body = MethodBody(Path.Combine(RepoRoot(), "samples", "TSharpVision.Samples.TVTerm", "TVTermApp.cs"), "private void OpenUiColors()");
        Assert.Contains("GetPalette().Clone()", body);
        Assert.Contains("dlg.BindLivePalette(GetPalette(), Redraw);", body);
        Assert.Contains("DeskTop.ExecView(dlg);", body);
        Assert.DoesNotContain("Array.Copy", body);
        // The menu command still routes to it.
        string app = File.ReadAllText(Path.Combine(RepoRoot(), "samples", "TSharpVision.Samples.TVTerm", "TVTermApp.cs"));
        Assert.Contains("case Commands.cmUiColors:         OpenUiColors(); break;", app);
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    private static string MethodBody(string file, string signature)
    {
        string text = File.ReadAllText(file);
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        int open = text.IndexOf('{', start), depth = 0, i = open;
        do { if (text[i] == '{') depth++; else if (text[i] == '}') depth--; i++; } while (depth > 0);
        return text[open..i];
    }
}
