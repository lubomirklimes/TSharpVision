using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoReadabilityTests
{
    public static IEnumerable<object[]> Scenes()
    {
        foreach (TProgram.AP theme in Enum.GetValues<TProgram.AP>())
            foreach (string scene in new[] { "CharacterMap", "FileOpen", "FileSave", "Directory", "GroupBox", "Memo", "Progress", "Validated", "GrayGallery", "BlueGallery", "CustomGallery", "BlueModal", "Message", "Input", "Result" })
                yield return new object[] { theme, scene };
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void RecentDemoScenesRenderReadableTextAcrossStandardThemes(TProgram.AP theme, string scene)
    {
        using var scope = new DemoTestScope();
        var app = scope.App; app.AppPalette = theme;
        TView.DisableCommand(PaletteGalleryCmd.Unavailable);
        TWindow window = scene switch
        {
            "CharacterMap" => new CharacterMapWindow(new TRect(0, 0, 78, 23)),
            "FileOpen" => new DemoFileDialog(false), "FileSave" => new DemoFileDialog(true),
            "Directory" => new DemoDirectoryDialog(),
            "GroupBox" => TVDemoApp.CreateGroupBoxDemoDialog(new TRect(0, 0, 64, 20)),
            "Memo" => new MemoDemoDialog(new TRect(0, 0, 70, 21), TVDemoApp.CreateMemoDemoData()),
            "Progress" => new ProgressDemoDialog(new TRect(0, 0, 58, 11)),
            "Validated" => new ValidatedInputDemoDialog(new TRect(0, 0, 70, 21)),
            "GrayGallery" => new GrayGalleryDialog(new TRect(0, 0, 47, 16)),
            "BlueGallery" => new BlueGalleryWindow(new TRect(0, 0, 47, 16)),
            "CustomGallery" => new CustomPaletteGalleryWindow(new TRect(0, 0, 47, 16)),
            "BlueModal" => new BlueDocumentDialog(),
            "Message" => new MessageDemoDialog(new TRect(0, 0, 64, 14)),
            "Input" => MsgBox.BuildInputBox(new TRect(0, 0, 60, 8), "Input", "~N~ame:", "World", 40),
            _ => MsgBox.BuildMessageBox(new TRect(0, 0, 60, 12), "Operation result\nNo file was written.", MsgBox.mfInformation | MsgBox.mfOKButton)
        };
        try
        {
            app.DeskTop!.Insert(window); window.Select(); Check();
            // Exercise focus/selection mappings, including nested groups and lists.
            foreach (var control in ScriptedDemoApp.Descendants(window).Where(v => (v.options & Views.ofSelectable) != 0 && !v.GetState(Views.sfDisabled)))
            {
                control.Select(); Check();
            }
            var button = ScriptedDemoApp.Descendants(window).OfType<TButton>().FirstOrDefault();
            if (button != null) { button.SetState(Views.sfDisabled, true); Check(); }
        }
        finally { window.ShutDown(); }

        void Check()
        {
            app.Redraw(); AssertReadable(window);
        }
    }

    internal static void AssertReadable(TWindow window)
    {
        var screen = RootScreenCopy.CopyRoot();
        var origin = window.MakeGlobal(new TPoint(0, 0));
        int textCells = 0;
        for (int y = 0; y < window.size.y; y++)
            for (int x = 0; x < window.size.x; x++)
            {
                var cell = screen[origin.x + x, origin.y + y];
                // Button half-blocks are shadow decoration, intentionally invisible
                // on a black monochrome surface; text and frame glyphs must contrast.
                if (cell.Character is ' ' or '\0' or '\u2580' or '\u2584') continue;
                textCells++;
                byte attr = (byte)cell.Attr;
                // Foreground intensity is meaningful: standard white-on-gray is readable.
                // Only the background blink bit is excluded.
                Assert.True((attr & 15) != ((attr >> 4) & 7), $"{window.title}: '{cell.Character}' at {x},{y} has unreadable attribute {attr:X2}");
            }
        Assert.True(textCells > 20);
    }

    [Theory]
    [InlineData(78, 23)] [InlineData(60, 16)]
    public void CharacterMapHasOneHintLineAndAFullWidthSeparator(int width, int height)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var window = new CharacterMapWindow(new TRect(0, 0, width, height));
        app.DeskTop!.Insert(window); app.Redraw();
        var view = window.View;
        var screen = RootScreenCopy.CopyRoot(); var origin = view.MakeGlobal(new TPoint(0, 0));
        string[] lines = Enumerable.Range(0, view.size.y).Select(y => new string(Enumerable.Range(0, view.size.x).Select(x => screen[origin.x + x, origin.y + y].Character).ToArray())).ToArray();
        Assert.Single(lines, line => line.Contains("G:Go to"));
        Assert.Equal(new string('\u2500', view.size.x), lines[view.GridRows + 1]);
        Assert.Contains("U+2500", lines[view.GridRows + 2]);
        Assert.Contains(lines, line => line.Contains("Home/End:full range"));
        Assert.Contains(lines, line => line.Contains("S:Copy C#"));
        Assert.DoesNotContain(lines, line => line.Contains("Range labels"));
        view.Feedback = "Selected U+251C"; view.Model.Select(0x251C); app.Redraw();
        AssertReadable(window);
    }

    [Theory]
    [InlineData(TProgram.AP.Color)] [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void CharacterMapHelpersUseDesktopPaletteAndCleanUpAfterCancel(TProgram.AP theme)
    {
        using var scope = new DemoTestScope(); var app = scope.App; app.AppPalette = theme;
        app.RunCommand(ShowcaseCmd.CharacterMap);
        foreach (string action in new[] { "g", "r" })
        {
            TDialog? captured = null;
            app.Step(() =>
            {
                var dialog = captured = app.Modal<TDialog>();
                Assert.Same(app.DeskTop, dialog.owner);
                app.Redraw(); AssertReadable(dialog);
                if (action == "r")
                {
                    Assert.Contains("Group names, not individual character names.", ScriptedDemoApp.Text(dialog));
                    // Open the production range popup and test its list text too.
                    ComboInput.Send(ComboInput.Key(Keys.kbF4));
                }
                else ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            if (action == "r")
            {
                app.Step(() => { app.Redraw(); AssertReadable(captured!); ComboInput.Send(ComboInput.Key(Keys.kbEsc)); });
                app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
            }
            var key = ComboInput.Typed(action); app.HandleEvent(ref key);
            Assert.NotNull(captured); Assert.Null(captured.owner); Assert.Null(captured.last);
            Assert.Null(TView.ModalTopView);
        }
    }
}
