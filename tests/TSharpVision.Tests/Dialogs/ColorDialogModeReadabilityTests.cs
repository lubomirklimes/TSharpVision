using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;
using static TSharpVision.Tests.Samples.MonochromeModeQualificationTests;

namespace TSharpVision.Tests.Dialogs;

// TV12j: TColorDialog chrome stays readable in Color, BlackWhite and Monochrome, with every control focused in turn,
// whether the family comes from SetAppPalette or the legacy SetScreenMode. The foreground/background selectors and the
// preview draw the colors being edited by design, so they are left out of the audit; the dormant TMonoSelector stays
// hidden in every family (no underline/MDA selector is offered).
[Collection("NonParallel")]
public sealed class ColorDialogModeReadabilityTests
{
    public static IEnumerable<object[]> Families() => new[]
    {
        new object[] { TProgram.AP.Color, false }, new object[] { TProgram.AP.BlackWhite, false },
        new object[] { TProgram.AP.Monochrome, false }, new object[] { TProgram.AP.BlackWhite, true },
        new object[] { TProgram.AP.Monochrome, true },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void ChromeIsReadableAndFocusIsVisibleForEveryControl(TProgram.AP family, bool throughLegacyMode)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var saved = (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers);
        TColorDialog? dlg = null;
        try
        {
            if (throughLegacyMode)
                app.SetScreenMode((ushort)(family == TProgram.AP.Monochrome ? TDisplay.SM.Mono : TDisplay.SM.BW80));
            else
                app.SetAppPalette(family);
            Assert.Equal(family, app.AppPalette);

            dlg = NewDialog(app);
            app.DeskTop!.Insert(dlg);
            app.Redraw();

            var controls = ScriptedDemoApp.Descendants(dlg)
                .Where(v => (v.options & Views.ofSelectable) != 0 && !v.GetState(Views.sfDisabled) && v.GetState(Views.sfVisible))
                .ToList();
            Assert.True(controls.Count >= 6, $"selectable controls: {controls.Count}");
            Assert.DoesNotContain(controls, v => v is TMonoSelector);

            foreach (var control in controls)
            {
                control.Select();
                AssertChromeReadable(dlg, family, $"{family} legacy={throughLegacyMode} focus {control.GetType().Name}");
                if (control is TListViewer list && list.range > 1)
                    AssertFocusedListRowDistinct(list);
            }
            AssertIncrementalMatchesFullRedraw(app);
        }
        finally
        {
            dlg?.Close();
            (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers) = saved;
        }
    }

    private static TColorDialog NewDialog(TProgram app)
    {
        var groups =
            new TColorGroup("Desktop", new TColorItem("Background", 1)) +
            new TColorGroup("Menu", new TColorItem("Normal", 2) + new TColorItem("Selected", 4)) +
            new TColorGroup("Window", new TColorItem("Frame", 8) + new TColorItem("Title", 9)) +
            new TColorGroup("Dialog", new TColorItem("Frame", 32) + new TColorItem("Title", 34));
        return new TColorDialog(app.GetPalette().Clone(), groups);
    }

    // Every glyph inside the dialog, outside the color swatches and the preview, contrasts with its background; in
    // BlackWhite and Monochrome it also uses only the four neutral indices.
    private static void AssertChromeReadable(TColorDialog dlg, TProgram.AP family, string context)
    {
        var excluded = ScriptedDemoApp.Descendants(dlg)
            .Where(v => v is TColorSelector or TColorDisplay && v.GetState(Views.sfVisible))
            .Select(v => { var a = v.MakeGlobal(new TPoint(0, 0)); return new TRect(a.x, a.y, a.x + v.size.x, a.y + v.size.y); })
            .ToList();
        var origin = dlg.MakeGlobal(new TPoint(0, 0));
        var screen = RootScreenCopy.CopyRoot();
        int glyphs = 0;
        for (int y = origin.y; y < origin.y + dlg.size.y; y++)
            for (int x = origin.x; x < origin.x + dlg.size.x; x++)
            {
                if (excluded.Any(r => r.Contains(new TPoint(x, y)))) continue;
                var cell = screen[x, y];
                byte attr = (byte)cell.Attr;
                if (family != TProgram.AP.Color)
                    Assert.True((attr & 15) is 0 or 7 or 8 or 15 && (attr >> 4) is 0 or 7,
                        $"{context}: colored cell '{cell.Character}' at {x},{y} {attr:X2}");
                if (cell.Character is ' ' or '\0' or '▀' or '▄') continue;
                glyphs++;
                Assert.True((attr & 15) != (attr >> 4), $"{context}: '{cell.Character}' at {x},{y} unreadable {attr:X2}");
            }
        Assert.True(glyphs > 100, $"{context}: only {glyphs} glyphs audited");
    }
}
