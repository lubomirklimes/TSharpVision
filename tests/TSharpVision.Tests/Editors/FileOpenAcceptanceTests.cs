using System.IO;
using System.Text.RegularExpressions;
using TSharpVision.Constants;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TV12m: TFileDialog ends with cmFileOpen from its Open/OK/Add buttons and with cmOK from a double-click (historical
// contract, unchanged). TEditorApp.FileOpen accepted only cmOK, so the Open button opened nothing. These tests run the
// real modal loop with mouse clicks on the real buttons and list, and let the real OpenEditor load the file.
[Collection("NonParallel")]
public sealed class FileOpenAcceptanceTests : IDisposable
{
    private readonly DriverScope _driver = new(100, 30);
    private readonly StreamableRegistryScope _registry = new();
    private readonly TempDirectory _tmp = new();
    private readonly TCommandSet _commands = new();
    private readonly TEditor.TEditorDialog _editorDialog = TEditor.editorDialog;
    private readonly string _directory = Directory.GetCurrentDirectory();
    private readonly ScriptedEditorApp _app;

    public FileOpenAcceptanceTests()
    {
        TView.GetCommands(_commands);
        ComboInput.ResetQueue();
        File.WriteAllText(Path.Combine(_tmp.Path, "alpha.txt"), "alpha text");
        File.WriteAllText(Path.Combine(_tmp.Path, "beta.txt"), "beta text");
        Directory.CreateDirectory(Path.Combine(_tmp.Path, "sub"));
        Directory.SetCurrentDirectory(_tmp.Path);
        _app = new ScriptedEditorApp();
    }

    public void Dispose()
    {
        try { _app.ShutDown(); }
        finally
        {
            Directory.SetCurrentDirectory(_directory);
            TEditor.editorDialog = _editorDialog;
            TView.SetCommands(_commands);
            ComboInput.ResetQueue();
            _tmp.Dispose();
            _registry.Dispose();
            _driver.Dispose();
        }
    }

    private static TButton Button(TFileDialog dlg, ushort command)
        => ScriptedDemoApp.Descendants(dlg).OfType<TButton>().Single(b => b.Command == command);

    private static TPoint Center(TView view) => view.MakeGlobal(new TPoint(view.size.x / 2, 0));

    // The screen cell of a list item, following TListViewer's column layout.
    private static TPoint ItemCell(TListViewer list, int item)
    {
        int colWidth = list.size.x / list.numCols + 1;
        int offset = item - list.topItem;
        return list.MakeGlobal(new TPoint(offset / list.size.y * colWidth + 1, offset % list.size.y));
    }

    private string Path_(string name) => Path.Combine(_tmp.Path, name);

    [Fact]
    public void OpenButtonClickOpensTheTypedFile()
    {
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            dlg.fileName!.SetData("alpha.txt");
            ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
        });
        _app.FileOpen();

        var (file, _) = Assert.Single(_app.Opened);
        PathAssertions.Equal(Path_("alpha.txt"), file);
        var window = Assert.Single(_app.EditWindows());
        PathAssertions.Equal(Path_("alpha.txt"), window.editor!.fileName);
        PathAssertions.Equal(_tmp.Path + Path.DirectorySeparatorChar, _app.FileDialogSessionDirectory); // TV12k still records
    }

    [Fact]
    public void DoubleClickOnAListedFileOpensIt()
    {
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            var list = dlg.fileList!;
            int index = Enumerable.Range(0, list.range).Single(i => list.GetText(i, 260) == "beta.txt");
            var cell = ItemCell(list, index);
            ComboInput.Click(cell);
            ComboInput.Click(cell);
        });
        _app.FileOpen();

        PathAssertions.Equal(Path_("beta.txt"), Assert.Single(_app.Opened).File);
        Assert.Single(_app.EditWindows());
    }

    [Theory]
    [InlineData("cancel")] [InlineData("escape")]
    public void CancelOpensNothingAndRemembersNothing(string how)
    {
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            dlg.fileName!.SetData("alpha.txt");
            if (how == "cancel") ComboInput.Click(Center(Button(dlg, Views.cmCancel)));
            else ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        _app.FileOpen();

        Assert.Empty(_app.Opened);
        Assert.Empty(_app.EditWindows());
        Assert.Null(_app.FileDialogSessionDirectory);
    }

    [Fact]
    public void OpenOnADirectoryOrMaskNavigatesAndOpensNothing()
    {
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            dlg.fileName!.SetData("sub");
            ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
            _app.Step(() =>
            {
                PathAssertions.Equal(Path_("sub") + Path.DirectorySeparatorChar, dlg.Directory); // still open, navigated
                dlg.fileName!.SetData("*.txt");
                ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
                _app.Step(() => ComboInput.Click(Center(Button(dlg, Views.cmCancel))));
            });
        });
        _app.FileOpen();

        Assert.Empty(_app.Opened);
        Assert.Empty(_app.EditWindows());
    }

    [Fact]
    public void InvalidNameShowsAnErrorAndOpensNothing()
    {
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            dlg.fileName!.SetData("bad\0name.txt");
            ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
            _app.Step(() =>
            {
                Assert.Contains("Invalid file name", ScriptedDemoApp.Text(_app.Modal<TDialog>()));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
                _app.Step(() =>
                {
                    Assert.IsAssignableFrom<TFileDialog>(TView.ModalTopView);
                    ComboInput.Send(ComboInput.Key(Keys.kbEsc));
                });
            });
        });
        _app.FileOpen();

        Assert.Empty(_app.Opened);
    }

    [Fact]
    public void NextDialogStartsInTheDirectoryOfTheFileOpenedWithTheOpenButton()
    {
        string other = Path.Combine(_tmp.Path, "sub");
        File.WriteAllText(Path.Combine(other, "gamma.txt"), "gamma");
        _app.Step(() =>
        {
            var dlg = _app.Modal<TFileDialog>();
            dlg.fileName!.SetData(Path.Combine(other, "gamma.txt"));
            ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
        });
        _app.FileOpen();
        PathAssertions.Equal(Path.Combine(other, "gamma.txt"), Assert.Single(_app.Opened).File);

        string? started = null;
        _app.Step(() =>
        {
            started = _app.Modal<TFileDialog>().Directory;
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        _app.FileOpen();
        PathAssertions.Equal(other + Path.DirectorySeparatorChar, started!);
        PathAssertions.Equal(_tmp.Path, Directory.GetCurrentDirectory());
    }

    // The dialog's historical return codes that every consumer must accept: Open and OK buttons -> cmFileOpen,
    // double-click -> cmOK.
    [Theory]
    [InlineData(FileDialogOptions.fdOpenButton, false, Views.cmFileOpen)]
    [InlineData(FileDialogOptions.fdOKButton, false, Views.cmFileOpen)]
    [InlineData(FileDialogOptions.fdOpenButton, true, Views.cmOK)]
    public void DialogReturnCodesAreUnchanged(ushort options, bool doubleClick, ushort expected)
    {
        var dlg = new TFileDialog("*.*", "Open", "~N~ame", options, 0);
        _app.Step(() =>
        {
            if (doubleClick)
            {
                var list = dlg.fileList!;
                var cell = ItemCell(list, Enumerable.Range(0, list.range).Single(i => list.GetText(i, 260) == "alpha.txt"));
                ComboInput.Click(cell); ComboInput.Click(cell);
            }
            else
            {
                dlg.fileName!.SetData("alpha.txt");
                ComboInput.Click(Center(Button(dlg, Views.cmFileOpen)));
            }
        });
        try { Assert.Equal(expected, _app.DeskTop!.ExecView(dlg)); }
        finally { dlg.ShutDown(); }
    }

    // TVIntl and TVTerm are separate executables that this test assembly cannot host (TVTerm needs the terminal
    // extension's hosting and both have their own Main), so their call sites are checked at source level: every method
    // that runs a TFileDialog must accept cmFileOpen as well as cmOK.
    [Theory]
    [InlineData("samples/TSharpVision.Samples.TVIntl/Program.cs", 3)]
    [InlineData("samples/TSharpVision.Samples.TVTerm/TVTermApp.cs", 2)]
    [InlineData("src/TSharpVision/Editors/TEditorApp.cs", 1)]
    public void SampleFileDialogCallSitesAcceptBothSuccessCodes(string relative, int dialogs)
    {
        string root = TSharpVision.Tests.Infrastructure.RepositoryLayout.Root(typeof(FileOpenAcceptanceTests).Assembly);
        string source = File.ReadAllText(Path.Combine(root, relative));
        var methods = Regex.Split(source, @"\n    (?:private|public|internal|protected)[^\n]*\(")
            .Where(m => m.Contains("new TFileDialog(", StringComparison.Ordinal)).ToArray();
        Assert.Equal(dialogs, methods.Length);
        foreach (string body in methods)
        {
            Assert.Contains("Views.cmFileOpen or Views.cmOK", body);
            Assert.DoesNotMatch(@"ExecView\([^)]*\)\s*[!=]=\s*Views\.cmOK", body);
            Assert.DoesNotMatch(@"\bres(ult)?\s*[!=]=\s*Views\.cmOK", body);
        }
    }

    private sealed class ScriptedEditorApp : TEditorApp
    {
        private readonly Queue<Action> _steps = new();
        private int _empty;

        internal List<(string? File, TFileEditorOpenOptions? Options)> Opened { get; } = new();

        internal ScriptedEditorApp() { Pending = default; }

        internal void Step(Action step) => _steps.Enqueue(step);
        internal T Modal<T>() where T : TView => Assert.IsAssignableFrom<T>(TView.ModalTopView);
        internal List<TEditWindow> EditWindows()
        {
            var windows = new List<TEditWindow>();
            DeskTop!.ForEachView(v => { if (v is TEditWindow w) windows.Add(w); });
            return windows;
        }

        protected override int GetIdleWaitTimeout() => 0;

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What != Events.evNothing) { _empty = 0; return; }
            if (_steps.Count > 0) { _empty = 0; _steps.Dequeue()(); return; }
            if (++_empty > 100) throw new InvalidOperationException("Script exhausted with a modal view still open: " + TView.ModalTopView?.GetType().Name);
        }

        public override TEditWindow? OpenEditor(string? fileName, bool visible, TFileEditorOpenOptions? openOptions)
        {
            Opened.Add((fileName, openOptions));
            return base.OpenEditor(fileName, visible, openOptions);
        }
    }
}
