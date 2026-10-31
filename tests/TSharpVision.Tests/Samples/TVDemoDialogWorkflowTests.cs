using System.Security.Cryptography;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoDialogWorkflowTests
{
    [Theory]
    [InlineData("button")]
    [InlineData("enter")]
    [InlineData("mnemonic")]
    [InlineData("double-click")]
    public void ViewerAcceptsRealOpenActionsAndLoadsUnicodeContent(string action)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "příliš.txt");
        File.WriteAllText(path, "First\nŽluťoučký kůň");
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<DemoFileDialog>();
            Assert.Equal((ushort)112, FindHistory(dialog).HistoryId);
            dialog.SetData(Path.Combine(temp.Path, "*.*"));
            if (action == "double-click")
            {
                var list = dialog.fileList!;
                list.Select();
                int index = Enumerable.Range(0, list.range).Single(i => list.GetText(i, 260) == "příliš.txt");
                list.FocusItem(index);
                list.SelectItem(index); // production cmFileDoubleClicked -> cmOK
            }
            else
            {
                dialog.SetData(path);
                if (action == "button") ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
                else ComboInput.Send(ComboInput.Key(action == "enter" ? Keys.kbEnter : Keys.kbAltO));
            }
        });
        // Exercise the F3 binding through the real application preprocessing.
        var key = ComboInput.Key(Keys.kbF3);
        app.HandleEvent(ref key);
        TEvent pending = default;
        app.GetEvent(ref pending);
        Assert.Equal(TVDemoCmd.cmFileViewer, pending.message.command);
        app.HandleEvent(ref pending);
        var viewer = Assert.IsType<FileViewerWindow>(app.DeskTop!.current);
        Assert.Equal(new[] { "First", "Žluťoučký kůň" }, viewer.Model.Lines);
        Assert.Equal("příliš.txt", viewer.GetTitle(100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewerCancelOrMissingFileDoesNotCreateWindow(bool missing)
    {
        using var temp = new TempDirectory();
        using var scope = new DemoTestScope();
        var app = scope.App;
        TDialog? chooser = null;
        int before = ScriptedDemoApp.Descendants(app.DeskTop!).OfType<FileViewerWindow>().Count();
        app.Step(() =>
        {
            chooser = app.Modal<DemoFileDialog>();
            if (missing)
            {
                ((TFileDialog)chooser).SetData(Path.Combine(temp.Path, "missing.txt"));
                ScriptedDemoApp.Button(chooser, Views.cmFileOpen).Press();
            }
            else ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        if (missing)
        {
            app.DismissResult("Open requires an existing file");
            app.Step(() =>
            {
                Assert.Same(chooser, app.Modal<DemoFileDialog>());
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
        }
        app.RunCommand(TVDemoCmd.cmFileViewer);
        Assert.Equal(before, ScriptedDemoApp.Descendants(app.DeskTop!).OfType<FileViewerWindow>().Count());
        Assert.Null(chooser!.owner);
        Assert.Null(((TFileDialog)chooser).fileList);
    }

    [Fact]
    public void ViewerReportsUnreadableFile()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "locked.txt");
        File.WriteAllText(path, "locked");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<DemoFileDialog>();
            dialog.SetData(path);
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult("Cannot read file:");
        app.RunCommand(TVDemoCmd.cmFileViewer);
        Assert.DoesNotContain(ScriptedDemoApp.Descendants(app.DeskTop!), v => v is FileViewerWindow);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FilenameDemosReportPathOrCancellationAndNeverWrite(bool save, bool cancel)
    {
        using var temp = new TempDirectory();
        string existing = Path.Combine(temp.Path, "existing.txt");
        string path = save ? Path.Combine(temp.Path, "new-name.txt") : existing;
        File.WriteAllText(existing, "must remain unchanged");
        byte[] before = SHA256.HashData(File.ReadAllBytes(existing));
        using var scope = new DemoTestScope();
        string cwd = Directory.GetCurrentDirectory();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<DemoFileDialog>();
            Assert.Equal((ushort)(save ? 111 : 110), FindHistory(dialog).HistoryId);
            dialog.SetData(path);
            if (cancel) ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            else ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.Step(() =>
        {
            string text = ScriptedDemoApp.Text(app.Modal<TDialog>());
            Assert.Contains(cancel ? "cancelled" : path, text);
            Assert.Contains("No file was written.", text);
            if (!cancel) Assert.Contains("cmFileOpen", text);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(save ? ShowcaseCmd.FileSaveDialog : ShowcaseCmd.FileOpenDialog);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(existing)));
        Assert.False(File.Exists(Path.Combine(temp.Path, "new-name.txt")));
        Assert.Equal(cwd, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void OpenTutorialFiltersAndNavigatesBeforeSelecting()
    {
        using var temp = new TempDirectory();
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "yes.txt"), "yes");
        File.WriteAllText(Path.Combine(child, "no.cs"), "no");
        using var scope = new DemoTestScope();
        var app = scope.App;
        DemoFileDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<DemoFileDialog>();
            dialog.SetData(Path.Combine(child, "*.txt"));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<DemoFileDialog>());
            var entries = Enumerable.Range(0, dialog!.fileList!.range).Select(i => dialog.fileList.GetText(i, 260)).ToArray();
            Assert.Contains("yes.txt", entries);
            Assert.DoesNotContain("no.cs", entries);
            Assert.Contains(".." + Path.DirectorySeparatorChar, entries);
            dialog.SetData(Path.Combine(child, "yes.txt"));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult(Path.Combine(child, "yes.txt"));
        app.RunCommand(ShowcaseCmd.FileOpenDialog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryBrowseRevertAndAcceptOrCancelHaveDistinctCwdEffects(bool cancel)
    {
        using var temp = new TempDirectory();
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        using var scope = new DemoTestScope(); // captures CWD before the change, restores before temp disposal
        Directory.SetCurrentDirectory(temp.Path);
        var app = scope.App;
        DemoDirectoryDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<DemoDirectoryDialog>();
            var list = dialog.dirList!;
            var entries = list.List()!;
            int index = Enumerable.Range(0, entries.Count).Single(i =>
                Path.GetFullPath(Path.TrimEndingDirectorySeparator(entries.At(i).Dir() ?? "")) == child);
            list.FocusItem(index);
            ScriptedDemoApp.Button(dialog, Views.cmChangeDir).Press();
        });
        app.Step(() =>
        {
            Assert.Equal(temp.Path, Directory.GetCurrentDirectory());
            Assert.Equal(child, Path.GetFullPath(dialog!.dirInput!.Data));
            ScriptedDemoApp.Button(dialog, Views.cmRevert).Press();
        });
        app.Step(() =>
        {
            Assert.Equal(temp.Path, dialog!.dirInput!.Data);
            dialog.dirInput.SetData(child);
            if (cancel) ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            else ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.Step(() =>
        {
            string text = ScriptedDemoApp.Text(app.Modal<TDialog>());
            Assert.Contains(cancel ? "cancelled" : "accepted", text);
            Assert.Contains("Previous: " + temp.Path, text);
            Assert.Contains("Current: " + (cancel ? temp.Path : child), text);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.ChangeDirectory);
        Assert.Equal(cancel ? temp.Path : child, Directory.GetCurrentDirectory());
        Assert.Null(dialog!.dirList);
    }

    [Fact]
    public void InvalidDirectoryBlocksOkAndFrameCloseCancels()
    {
        using var temp = new TempDirectory();
        using var scope = new DemoTestScope();
        var app = scope.App;
        string cwd = Directory.GetCurrentDirectory();
        DemoDirectoryDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<DemoDirectoryDialog>();
            dialog.dirInput!.SetData(Path.Combine(temp.Path, "missing"));
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.DismissResult("Invalid drive or directory");
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<DemoDirectoryDialog>());
            Assert.NotEmpty(dialog!.LastError);
            Assert.Equal(cwd, Directory.GetCurrentDirectory());
            ScriptedDemoApp.SendCommand(Views.cmClose);
        });
        app.DismissResult("Directory cancelled");
        app.RunCommand(ShowcaseCmd.ChangeDirectory);
        Assert.Equal(cwd, Directory.GetCurrentDirectory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealInputBoxAcceptsTextAndCancelReturnsOriginal(bool cancel)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<TDialog>();
            var line = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>());
            line.SetData("Changed");
            if (cancel) ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            else ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.DismissResult("Returned: " + (cancel ? "World" : "Changed"));
        app.RunCommand(ShowcaseCmd.InputBox);
    }

    [Theory]
    [InlineData(0, 0, Views.cmOK)]
    [InlineData(1, 1, Views.cmCancel)]
    [InlineData(2, 2, Views.cmYes)]
    [InlineData(3, 2, Views.cmNo)]
    [InlineData(3, 2, Views.cmCancel)]
    public void MessageFactoryShowsChosenCategoryButtonsAndReportsResponse(int category, int buttons, ushort response)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        MessageDemoDialog? selector = null;
        app.Step(() =>
        {
            selector = app.Modal<MessageDemoDialog>();
            selector.Category.value = (uint)category;
            selector.Buttons.value = (uint)buttons;
            ScriptedDemoApp.Button(selector, MessageDemoDialog.ShowMessage).Press();
        });
        app.Step(() =>
        {
            var box = app.Modal<TDialog>();
            Assert.NotSame(selector, box);
            Assert.Equal(new[] { "Information", "Warning", "Error", "Confirm" }[category], box.GetTitle(100));
            ScriptedDemoApp.Button(box, response).Press();
        });
        app.Step(() =>
        {
            Assert.Same(selector, app.Modal<MessageDemoDialog>());
            Assert.Contains(TVDemoApp.DemoResultName(response), ScriptedDemoApp.Text(selector!));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.MessageBoxes);
    }

    [Fact]
    public void ValidatorsRejectCharacterAndFocusOrOkAndCancelStillWorks()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        ValidatedInputDemoDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<ValidatedInputDemoDialog>();
            Assert.False(dialog.Letters.InsertChar('7'));
            Assert.Contains("Rejected", ScriptedDemoApp.Text(dialog));
            Assert.True(dialog.Range.Focus());
            dialog.Range.SetData("101");
            ComboInput.Send(ComboInput.Key(Keys.kbTab));
        });
        app.Step(() =>
        {
            Assert.True(dialog!.Range.GetState(Views.sfFocused));
            Assert.Contains("Range: enter", ScriptedDemoApp.Text(dialog));
            dialog.Range.SetData("42");
            dialog.Picture.SetData("12-34");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<ValidatedInputDemoDialog>());
            Assert.Contains("Picture:", ScriptedDemoApp.Text(dialog!));
            dialog!.Picture.SetData("123-45");
            dialog.Lookup.SetData("Purple");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<ValidatedInputDemoDialog>());
            Assert.Contains("Lookup:", ScriptedDemoApp.Text(dialog!));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.DismissResult("cancelled");
        app.RunCommand(ShowcaseCmd.ValidatedInput);
    }

    [Fact]
    public void ValidValuesRoundTripThroughSampleHandler()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<ValidatedInputDemoDialog>();
            dialog.Letters.SetData("Grace");
            dialog.Range.SetData("99");
            dialog.Picture.SetData("456-78");
            dialog.Lookup.SetData("Blue");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.Step(() =>
        {
            string text = ScriptedDemoApp.Text(app.Modal<TDialog>());
            foreach (string value in new[] { "Grace", "99", "456-78", "Blue" }) Assert.Contains(value, text);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.ValidatedInput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemoRealEditingTransferAndCancellationPreserveCallerData(bool cancel)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var source = TVDemoApp.CreateMemoDemoData();
        string original = new(source.buffer, 0, (int)source.length);
        var dialog = new MemoDemoDialog(new TRect(5, 1, 75, 22), source);
        try
        {
            app.Step(() =>
            {
                Assert.True(dialog.Memo.InsertText("Changed\nSecond\n"));
                ComboInput.Send(ComboInput.Key(Keys.kbTab));
            });
            app.Step(() =>
            {
                Assert.False(dialog.Memo.GetState(Views.sfFocused));
                if (cancel) ComboInput.Send(ComboInput.Key(Keys.kbEsc));
                else ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
            });
            ushort code = app.DeskTop!.ExecView(dialog);
            dialog.ReadAccepted(code, source);
            string actual = new(source.buffer, 0, (int)source.length);
            Assert.Equal(cancel ? original : "Changed\nSecond\n" + original, actual);
            Assert.False(dialog.Memo.InsertText(new string('x', (int)MemoDemoDialog.Capacity + 1)));
            Assert.True(dialog.Memo.bufLen <= MemoDemoDialog.Capacity);
        }
        finally { dialog.ShutDown(); }
    }

    [Fact]
    public void MemoMenuHandlerReportsTransferAndDoesNotWrite()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() => ScriptedDemoApp.Button(app.Modal<MemoDemoDialog>(), Views.cmOK).Press());
        app.DismissResult("Accepted TMemoData");
        app.RunCommand(ShowcaseCmd.Memo);
    }

    [Fact]
    public void SaveCanSelectAnExistingFileWithoutChangingItsBytes()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "existing.txt");
        File.WriteAllText(path, "Never replace this text.");
        byte[] before = File.ReadAllBytes(path);
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<DemoFileDialog>();
            dialog.SetData(path);
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult("No file was written.");
        app.RunCommand(ShowcaseCmd.FileSaveDialog);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void UnconfiguredFileActionIsNotTreatedAsSuccessfulSelection()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "not-written.txt");
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            app.Modal<DemoFileDialog>().SetData(path);
            ScriptedDemoApp.SendCommand(Views.cmFileClear);
        });
        app.DismissResult("Save filename: cancelled.");
        app.RunCommand(ShowcaseCmd.FileSaveDialog);
        Assert.False(File.Exists(path));
    }

    private static THistory FindHistory(TGroup dialog) =>
        Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<THistory>());
}
