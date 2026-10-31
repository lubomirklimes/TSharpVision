using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class PaletteRepaintMnemonicTests
{
    [Fact]
    public void ExplicitRedrawRecomposesNestedBuffersWithoutChangingFocusOrOrder()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var palette = app.GetPalette();
        byte[] saved = palette.Data.ToArray();
        var dialog = new TDialog(new TRect(2, 2, 42, 14), "Nested");
        var nested = new TGroup(new TRect(2, 2, 30, 8));
        nested.options |= Views.ofBuffered;
        var text = new TStaticText(new TRect(1, 1, 20, 2), "Palette probe");
        nested.Insert(text); dialog.Insert(nested); app.DeskTop!.Insert(dialog);
        var selected = app.DeskTop.current;
        var order = ScriptedDemoApp.Descendants(app.DeskTop).ToArray();
        var pos = text.MakeGlobal(new TPoint(0, 0));
        byte before = (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr;
        try
        {
            for (int i = 1; i <= palette.Size; i++) palette.Data[i] = 0x1E;
            app.DrawView();
            Assert.Equal(before, (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr);
            app.Redraw();
            Assert.Equal('P', RootScreenCopy.CopyRoot()[pos.x, pos.y].Character);
            Assert.Equal(0x1E, (byte)nested.buffer!.Data[nested.size.x + 1].Attr);
            Assert.Equal(0x1E, (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr);
            Assert.Equal(0x1E, (byte)RootScreenCopy.CopyRoot()[79, 23].Attr);
            Assert.Same(selected, app.DeskTop.current);
            Assert.Equal(order, ScriptedDemoApp.Descendants(app.DeskTop).ToArray());
            Array.Copy(saved, palette.Data, saved.Length); app.Redraw();
            Assert.Equal(before, (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr);
        }
        finally { Array.Copy(saved, palette.Data, saved.Length); }
    }

    [Theory]
    [InlineData(TProgram.AP.Color)] [InlineData(TProgram.AP.BlackWhite)] [InlineData(TProgram.AP.Monochrome)]
    public void StatusLineHasSixRolesAndDrawsSelectedMnemonic(TProgram.AP theme)
    {
        using var scope = new DemoTestScope(); var app = scope.App; app.AppPalette = theme;
        var item = new TStatusItem("~C~lose", Keys.kbAltC, Views.cmCancel);
        var line = new TStatusLine(new TRect(0, 24, 80, 25), new TStatusDef(0, ushort.MaxValue, item));
        app.Insert(line);
        Assert.Equal(6, line.GetPalette().Size);
        for (int role = 1; role <= 6; role++)
        {
            Assert.Equal(role + 1, line.GetPalette().Data[role]);
            Assert.NotEqual(0, line.GetColor((ushort)role));
        }
        typeof(TStatusLine).GetMethod("DrawSelect", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(line, new object[] { item });
        var cells = RootScreenCopy.CopyRoot();
        Assert.Equal('C', cells[1, 24].Character);
        Assert.Equal((byte)(line.GetColor(0x0604) >> 8), (byte)cells[1, 24].Attr);
        Assert.Equal((byte)line.GetColor(4), (byte)cells[2, 24].Attr);
        if (theme == TProgram.AP.Color) Assert.Equal(0x24, (byte)cells[1, 24].Attr);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PaletteEditorAppliesOnlyAcceptedCandidateAndRepaintsExistingWindow(bool accept)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        byte[] saved = app.GetPalette().Data.ToArray();
        var window = new TDialog(new TRect(1, 1, 31, 8), "Retained");
        var text = new TStaticText(new TRect(2, 2, 20, 3), "Retained text");
        window.Insert(text); app.DeskTop!.Insert(window);
        var pos = text.MakeGlobal(new TPoint(0, 0));
        byte before = (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr;
        try
        {
            app.Step(() =>
            {
                var editor = app.Modal<TColorDialog>();
                Assert.NotSame(app.GetPalette().Data, editor.Pal!.Data);
                for (int j = 1; j <= editor.Pal.Size; j++) editor.Pal.Data[j] = 0x1E;
                ScriptedDemoApp.SendCommand(accept ? Views.cmOK : Views.cmCancel);
            });
            app.RunCommand(ShowcaseCmd.Colors);
            Assert.Equal(accept ? (byte)0x1E : before, (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr);
            if (!accept) Assert.Equal(saved, app.GetPalette().Data);
            Assert.Same(window, app.DeskTop.current);
        }
        finally { Array.Copy(saved, app.GetPalette().Data, saved.Length); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PaletteAndDesktopRestoreRepaintRetainedAndRestoredCells(bool desktop)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        byte[] saved = app.GetPalette().Data.ToArray();
        string path = Path.Combine(AppContext.BaseDirectory, "tvdemo-desktop.tvr");
        // Reserve only a new test-output file; never overwrite a pre-existing snapshot.
        using (new FileStream(path, FileMode.CreateNew)) { }
        var retained = new TDialog(new TRect(1, 1, 30, 8), "Retained");
        var text = new TStaticText(new TRect(2, 2, 20, 3), "Retained text");
        retained.Insert(text); app.DeskTop!.Insert(retained);
        var stored = new TWindow(new TRect(35, 2, 65, 10), "Stored", 1);
        stored.Insert(new TStaticText(new TRect(2, 2, 20, 3), "Stored text"));
        try
        {
            byte[] candidate = saved.ToArray(); Array.Fill(candidate, (byte)0x1E, 1, candidate.Length - 1);
            DesktopState.Save(path, new[] { stored }, candidate);
            if (desktop)
            {
                app.DismissResult("Restored 1"); app.RunCommand(ShowcaseCmd.LoadDesktop);
                var restored = Assert.Single(ScriptedDemoApp.Descendants(app.DeskTop).OfType<TWindow>(), w => w.title == "Stored");
                var restoredText = Assert.Single(ScriptedDemoApp.Descendants(restored).OfType<TStaticText>());
                Check(restoredText);
            }
            else typeof(TVDemoApp).GetMethod("LoadPalette", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
            Check(text);
            Assert.Equal(0x1E, (byte)RootScreenCopy.CopyRoot()[79, 23].Attr);
        }
        finally { stored.ShutDown(); File.Delete(path); Array.Copy(saved, app.GetPalette().Data, saved.Length); }
        void Check(TView view)
        {
            var pos = view.MakeGlobal(new TPoint(0, 0));
            Assert.Equal(0x1E, (byte)RootScreenCopy.CopyRoot()[pos.x, pos.y].Attr);
        }
    }

    [Theory]
    [InlineData(ShowcaseCmd.SimpleDialog)] [InlineData(ShowcaseCmd.Selection)]
    public void InlineDialogsDispatchCancelMnemonicInRealModalLoop(ushort code)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<TDialog>();
            Assert.StartsWith("~C~", ScriptedDemoApp.Button(dialog, Views.cmCancel).Title);
            ComboInput.Send(ComboInput.Key(Keys.kbAltC));
        });
        app.RunCommand(code);
    }

    [Theory]
    [InlineData("r")] [InlineData("g")]
    public void CharacterMapNestedDialogsCancelViaMnemonic(string key)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.RunCommand(ShowcaseCmd.CharacterMap);
        app.Step(() =>
        {
            var dialog = app.Modal<TDialog>(); var button = ScriptedDemoApp.Button(dialog, Views.cmCancel);
            Assert.Equal("~C~ancel", button.Title);
            var pos = button.MakeGlobal(new TPoint(0, 0)); var cells = RootScreenCopy.CopyRoot();
            Assert.Contains(Enumerable.Range(0, button.size.x).Select(x => cells[pos.x + x, pos.y]), c => c.Character == 'C' && ((byte)c.Attr & 15) == 14);
            ComboInput.Send(ComboInput.Key(Keys.kbAltC));
        });
        var ev = ComboInput.Typed(key); app.HandleEvent(ref ev);
        Assert.Null(TView.ModalTopView);
    }

    [Fact]
    public void KeyboardDiagnosticIntentionallyCapturesAltCInsteadOfClosing()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var dialog = new KeyboardDiagnosticsDialog(); app.DeskTop!.Insert(dialog);
        Assert.Equal("Close", ScriptedDemoApp.Button(dialog, Views.cmCancel).Title);
        var ev = ComboInput.Key(Keys.kbAltC); dialog.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        TEvent command = default; app.GetEvent(ref command); Assert.Equal(Events.evNothing, command.What);
        ScriptedDemoApp.Button(dialog, Views.cmCancel).Press(); app.GetEvent(ref command);
        Assert.Equal(Views.cmCancel, command.message.command);
    }

    public static IEnumerable<object[]> Scenes()
    {
        foreach (var theme in Enum.GetValues<TProgram.AP>())
            foreach (string scene in new[] { "Sample", "Combo", "History", "Resource", "ResourceLoaded", "Open", "FileClear", "Save", "GroupBox", "Memo", "Progress", "Validated", "GrayGallery", "BlueGallery", "CustomGallery", "BlueModal", "MessageDemo", "MessageBox", "Input", "Colors", "Find", "Replace", "FunctionKeys", "Mouse" })
                yield return new object[] { theme, scene };
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void CancelAndCloseRenderMnemonicAndDispatchOriginalCommand(TProgram.AP theme, string scene)
    {
        using var scope = new DemoTestScope(100, 40); var app = scope.App; app.AppPalette = theme;
        TWindow dialog = scene switch
        {
            "Sample" => app.BuildControlsShowcaseDialog(out _, out _, out _, out _),
            "Combo" => app.BuildComboBoxDemoDialog(out _, out _),
            "History" => app.BuildHistoryDemoDialog(),
            "Resource" => TVDemoApp.BuildResourceSampleDialog(),
            "ResourceLoaded" => LoadResource(),
            "FileClear" => new TFileDialog("*.*", "Clear", "~N~ame", FileDialogOptions.fdClearButton, 0),
            "Open" => new DemoFileDialog(false), "Save" => new DemoFileDialog(true),
            "GroupBox" => TVDemoApp.CreateGroupBoxDemoDialog(new TRect(0, 0, 64, 19)),
            "Memo" => new MemoDemoDialog(new TRect(0, 0, 70, 21), TVDemoApp.CreateMemoDemoData()),
            "Progress" => new ProgressDemoDialog(new TRect(0, 0, 58, 11)),
            "Validated" => new ValidatedInputDemoDialog(new TRect(0, 0, 70, 21)),
            "GrayGallery" => new GrayGalleryDialog(new TRect(0, 0, 47, 16)),
            "BlueGallery" => new BlueGalleryWindow(new TRect(0, 0, 47, 16)),
            "CustomGallery" => new CustomPaletteGalleryWindow(new TRect(0, 0, 47, 16)),
            "BlueModal" => new BlueDocumentDialog(),
            "MessageDemo" => new MessageDemoDialog(new TRect(0, 0, 64, 14)),
            "MessageBox" => MsgBox.BuildMessageBox(new TRect(0, 0, 60, 12), "Question", MsgBox.mfYesNoCancel),
            "Input" => MsgBox.BuildInputBox(new TRect(0, 0, 60, 8), "Input", "~N~ame:", "World", 40),
            "Colors" => new TColorDialog(app.GetPalette().Clone(), new TColorGroup("Desktop", new TColorItem("Background", 1))),
            "Find" => TEditorFindDialog.Build(new TFindDialogRec(new char[80], 0), out _, out _),
            "Replace" => TEditorReplaceDialog.Build(new TReplaceDialogRec(new char[80], new char[80], 0), out _, out _, out _),
            "Keyboard" => new KeyboardDiagnosticsDialog(),
            "FunctionKeys" => new FunctionKeyDiagnosticsDialog(),
            _ => new MouseDialog()
        };
        app.DeskTop!.Insert(dialog); dialog.Select(); app.Redraw();
        var button = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TButton>(), b => b.Title.Replace("~", "") is "Cancel" or "Close");
        Assert.StartsWith("~C~", button.Title);
        CheckCells(); button.Select(); CheckCells();
        ComboInput.ResetQueue();
        var ev = ComboInput.Key(Keys.kbAltC); dialog.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        TEvent command = default; app.GetEvent(ref command);
        Assert.Equal(Events.evCommand, command.What);
        Assert.Equal(button.Command, command.message.command);

        static TDialog LoadResource()
        {
            StreamableRegistration.RegisterAll();
            using var stream = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            var file = new TResourceFile(new Fpstream(stream));
            var original = TVDemoApp.BuildResourceSampleDialog();
            file.Put(original, "dialog"); file.Flush(); original.ShutDown();
            stream.Position = 0;
            var loaded = new TResourceFile(new Fpstream(stream));
            return Assert.IsType<TDialog>(loaded.Get("dialog"));
        }

        void CheckCells()
        {
            button.DrawView(); var screen = RootScreenCopy.CopyRoot(); var origin = button.MakeGlobal(new TPoint(0, 0));
            int y = button.size.y / 2 - 1;
            int x = Enumerable.Range(0, button.size.x).Single(i => screen[origin.x + i, origin.y + y].Character == 'C');
            byte hot = (byte)screen[origin.x + x, origin.y + y].Attr;
            byte normal = (byte)screen[origin.x + x + 1, origin.y + y].Attr;
            Assert.Equal((byte)button.GetColor(5), hot);
            ushort roles = button.GetColor(button.GetState(Views.sfSelected) ? (ushort)0x0703 : button.AmDefault ? (ushort)0x0602 : (ushort)0x0501);
            Assert.Equal((byte)roles, normal);
            if (theme == TProgram.AP.Color) Assert.NotEqual(normal, hot);
            Assert.NotEqual((hot >> 4) & 7, hot & 15);
            if (theme == TProgram.AP.Color) Assert.Equal(14, hot & 15);
        }
    }
}
