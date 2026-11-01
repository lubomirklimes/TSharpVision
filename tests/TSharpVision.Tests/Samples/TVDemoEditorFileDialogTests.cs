using System.Text;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Text;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Samples;

// File/Open in code editor, Save and Save as run the framework TFileDialog with Help and Encoding together.
[Collection("NonParallel")]
public sealed class TVDemoEditorFileDialogTests : IDisposable
{
    private const string Czech = "Příliš žluťoučký kůň úpěl ďábelské ódy";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tvdemo-editor-" + Guid.NewGuid().ToString("N"));
    private readonly Func<TDeskTop?, string, ushort> _overwrite = TEditorDialogHelper.OverwriteConfirm;
    private readonly ITestOutputHelper _output;

    public TVDemoEditorFileDialogTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TEditorDialogHelper.OverwriteConfirm = _overwrite;
        Directory.Delete(_dir, recursive: true);
    }

    private static int ChoiceIndex(string label) =>
        EditorEncodingChoices.BuiltIn.Select((c, i) => (c, i)).Single(x => x.c.Fallback == label).i;

    private static string LabelText(TLabel label)
    {
        label.GetText(out string text);
        return text;
    }

    private static string ReadText(TEditor editor)
        => new(Enumerable.Range(0, (int)editor.bufLen).Select(i => editor.BufChar((uint)i)).ToArray());

    [Fact]
    public void FileMenuOffersOpenSaveAndSaveAsAndSaveFollowsTheActiveEditor()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var file = TVDemoMenuNavigationTests.Items(app.MenuBar!.Menu!).Single(i => i.Name == "~F~ile").SubMenu;
        var items = TVDemoMenuNavigationTests.Items(file).Where(i => !string.IsNullOrEmpty(i.Name)).ToArray();
        Assert.Equal(new ushort[] { ShowcaseCmd.NewWindow, TVDemoCmd.cmFileViewer, TVDemoCmd.cmEditorOpen,
            Views.cmSave, Views.cmSaveAs, Views.cmQuit }, items.Select(i => i.Command));
        var save = items.Single(i => i.Command == Views.cmSave);
        Assert.Equal(Keys.kbF2, save.KeyCode);
        Assert.Equal("F2", save.Param);

        Assert.False(TView.CommandEnabled(Views.cmSave));
        Assert.False(TView.CommandEnabled(Views.cmSaveAs));
        app.RunCommand(ShowcaseCmd.CodeEditor);
        Assert.True(TView.CommandEnabled(Views.cmSave));
        Assert.True(TView.CommandEnabled(Views.cmSaveAs));
        app.RunCommand(ShowcaseCmd.NewWindow);
        Assert.False(TView.CommandEnabled(Views.cmSave));
    }

    [Fact]
    public void OpenAndSaveAsConstructTheFrameworkDialogWithHelpAndEncoding()
    {
        foreach (bool save in new[] { false, true })
        {
            using var scope = new DemoTestScope();
            var app = scope.App;
            if (save) app.RunCommand(ShowcaseCmd.CodeEditor);
            app.Step(() =>
            {
                var dialog = app.Modal<TFileDialog>();
                // Open is the sample's subclass; Save As is the framework editor chooser installed with Help + Encoding.
                Assert.Equal(save ? typeof(TFileDialog) : typeof(DemoEditorFileDialog), dialog.GetType());
                Assert.Equal(DemoHelpCtx.EditorFiles, dialog.helpCtx);
                Assert.NotNull(dialog.encodingSelector);
                ScriptedDemoApp.Button(dialog, Views.cmHelp);
                ScriptedDemoApp.Button(dialog, Views.cmFileOpen);
                // Not reflowed: the framework's own size for Help + encoding (one row taller with Help).
                Assert.Equal(new TPoint(65, 21), dialog.size);
                // Save As starts on the encoding a new document is written in.
                Assert.Equal(save ? (uint)ChoiceIndex("UTF-8") : 0u, dialog.encodingSelector!.value);
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            app.RunCommand(save ? Views.cmSaveAs : TVDemoCmd.cmEditorOpen);
            if (save) Assert.Empty(Assert.IsType<DemoCodeWindow>(app.DeskTop!.current).CodeEditor.fileName);
        }
    }

    // Records the framework geometry (dialog-local half-open TRect [a, b), plus screen origin) and the headless screen
    // text for the reports. Layout assertions live in FileDialogLayoutAndModalHelpTests.
    [Theory]
    [InlineData(false, 80, 25)]
    [InlineData(true, 80, 25)]
    [InlineData(false, 120, 36)]
    [InlineData(true, 120, 36)]
    public void RecordsCombinedHelpAndEncodingGeometry(bool save, int width, int height)
    {
        using var scope = new DemoTestScope(width, height);
        var app = scope.App;
        if (save) app.RunCommand(ShowcaseCmd.CodeEditor);
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            app.Redraw();
            TPoint origin = dialog.MakeGlobal(new TPoint(0, 0));
            _output.WriteLine($"{(save ? "Save As" : "Open")} {width}x{height}: title='{dialog.GetTitle(80)}' " +
                $"origin=({origin.x},{origin.y}) size=({dialog.size.x},{dialog.size.y})");
            foreach (var view in ScriptedDemoApp.Descendants(dialog).Where(v => v.owner == dialog))
            {
                var r = view.GetBounds();
                string name = view switch
                {
                    TButton b => $"TButton cmd={b.Command} '{b.Title}'",
                    TLabel l => $"TLabel '{LabelText(l)}'",
                    _ => view.GetType().Name,
                };
                _output.WriteLine($"  {name}: [{r.a.x},{r.a.y})-({r.b.x},{r.b.y}) screen ({origin.x + r.a.x},{origin.y + r.a.y})");
            }
            var screen = TScreen.ScreenBuffer!;
            for (int y = origin.y; y < origin.y + dialog.size.y; y++)
                _output.WriteLine("  |" + new string(Enumerable.Range(origin.x, dialog.size.x)
                    .Select(x => screen.Data[y * width + x].Character).Select(c => c < ' ' ? ' ' : c).ToArray()) + "|");
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(save ? Views.cmSaveAs : TVDemoCmd.cmEditorOpen);
    }

    // Help only / Encoding only / both on the plain framework dialog, run modally from TVDemo; also records whether the
    // plain dialog's Help button opens help without TVDemo's handler. Output only, for the report.
    [Theory]
    [InlineData(FileDialogOptions.fdHelpButton)]
    [InlineData(FileDialogOptions.fdEncodingSelector)]
    [InlineData(FileDialogOptions.fdHelpButton | FileDialogOptions.fdEncodingSelector)]
    public void RecordsPlainFrameworkDialogVariants(ushort extra)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(FileDialogOptions.fdOpenButton | extra), 100);
        bool helpOpened = false;
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<TFileDialog>());
            _output.WriteLine($"options=0x{FileDialogOptions.fdOpenButton | extra:X4} size=({dialog.size.x},{dialog.size.y})");
            var views = ScriptedDemoApp.Descendants(dialog).Where(v => v.owner == dialog && v is not TFrame).ToArray();
            foreach (var a in views)
                foreach (var b in views.SkipWhile(v => v != a).Skip(1))
                {
                    TRect i = a.GetBounds();
                    i.Intersect(b.GetBounds());
                    if (!i.IsEmpty()) _output.WriteLine($"  overlap {a.GetType().Name} x {b.GetType().Name}: [{i.a.x},{i.a.y})-({i.b.x},{i.b.y})");
                }
            if ((extra & FileDialogOptions.fdHelpButton) != 0) ScriptedDemoApp.Button(dialog, Views.cmHelp).Press();
        });
        app.Step(() =>
        {
            helpOpened = TView.ModalTopView is THelpWindow
                || ScriptedDemoApp.Descendants(app.DeskTop!).OfType<THelpWindow>().Any();
            _output.WriteLine($"  help window after Help press: {helpOpened}; modal={TView.ModalTopView?.GetType().Name}");
            // The framework opens help modally: the first Esc closes it, the second cancels the dialog.
            if (helpOpened) ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        try { app.DeskTop!.ExecView(dialog); }
        finally { dialog.ShutDown(); }
        foreach (var w in ScriptedDemoApp.Descendants(app.DeskTop!).OfType<THelpWindow>().ToArray()) w.Close();
    }

    [Fact]
    public void HelpButtonShowsTheEditorFilesTopicAndReturnsToTheDialog()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        TFileDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<TFileDialog>();
            ScriptedDemoApp.Button(dialog, Views.cmHelp).Press();
        });
        app.Step(() =>
        {
            var help = app.Modal<THelpWindow>();
            Assert.Contains("File/Open in code editor", THelpCollection.PlainText(help.Viewer!.topic));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<TFileDialog>());
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(TVDemoCmd.cmEditorOpen);
        Assert.Empty(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<DemoCodeWindow>());
    }

    [Theory]
    [InlineData("CP852")]
    [InlineData("Windows-1250")]
    [InlineData("UTF-8")]
    public void OpenDecodesWithTheSelectedEncodingAndSaveKeepsItsBytes(string label)
    {
        var choice = EditorEncodingChoices.BuiltIn[ChoiceIndex(label)].Encoding;
        byte[] original = choice.LegacyEncoding?.Encode(Czech + "\r\n") ?? Encoding.UTF8.GetBytes(Czech + "\r\n");
        string path = Path.Combine(_dir, "czech.txt");
        File.WriteAllBytes(path, original);
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(path);
            dialog.encodingSelector!.SetData((ushort)ChoiceIndex(label));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.RunCommand(TVDemoCmd.cmEditorOpen);
        var window = Assert.IsType<DemoCodeWindow>(app.DeskTop!.current);
        var editor = window.CodeEditor;
        Assert.Equal(path, editor.fileName);
        Assert.Equal(Czech + "\n", ReadText(editor));
        Assert.Equal($"czech.txt [{label}]", window.GetTitle(80));

        editor.InsertText("!");
        app.RunCommand(Views.cmSave);
        Assert.False(editor.modified);
        byte[] expected = choice.LegacyEncoding?.Encode("!" + Czech + "\r\n") ?? Encoding.UTF8.GetBytes("!" + Czech + "\r\n");
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [Fact]
    public void OpenRejectsMissingFilesAndReportsUndecodableBytes()
    {
        string missing = Path.Combine(_dir, "missing.txt");
        string latin = Path.Combine(_dir, "latin.txt");
        File.WriteAllBytes(latin, Encoding.Latin1.GetBytes("café"));
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(missing);
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult("Open requires an existing file.");
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(latin);
            dialog.encodingSelector!.SetData((ushort)ChoiceIndex("UTF-8"));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult("could not be decoded");
        app.RunCommand(TVDemoCmd.cmEditorOpen);
        Assert.Empty(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<DemoCodeWindow>());
    }

    [Fact]
    public void SaveAsWritesUtf8AndKeepsTheDocumentOnCancel()
    {
        string path = Path.Combine(_dir, "saved.cs");
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.RunCommand(ShowcaseCmd.CodeEditor);
        var window = Assert.IsType<DemoCodeWindow>(app.DeskTop!.current);
        var editor = window.CodeEditor;
        editor.InsertText(Czech + "\n");
        string content = ReadText(editor);

        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        app.RunCommand(Views.cmSaveAs);
        Assert.Empty(editor.fileName);
        Assert.True(editor.modified);
        Assert.False(File.Exists(path));

        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(path);
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.RunCommand(Views.cmSaveAs);
        Assert.Equal(path, editor.fileName);
        Assert.False(editor.modified);
        Assert.Equal(content, ReadText(editor));
        Assert.Equal(Encoding.UTF8.GetBytes(content.Replace("\n", Environment.NewLine)), File.ReadAllBytes(path));
        Assert.Equal("saved.cs [UTF-8]", window.GetTitle(80));
    }

    [Theory]
    [InlineData("CP852")]
    [InlineData("Windows-1250")]
    [InlineData("Kamenicky / KEYBCS2")]
    public void SaveAsWritesTheSelectedEncodingAndLaterSavesKeepIt(string label)
    {
        var legacy = EditorEncodingChoices.BuiltIn[ChoiceIndex(label)].Encoding.LegacyEncoding!;
        string path = Path.Combine(_dir, "converted.txt");
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.RunCommand(ShowcaseCmd.CodeEditor);
        var window = Assert.IsType<DemoCodeWindow>(app.DeskTop!.current);
        var editor = window.CodeEditor;
        editor.InsertText(Czech + "\n");
        string Expected() => ReadText(editor).Replace("\n", Environment.NewLine);
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(path);
            dialog.encodingSelector!.SetData((ushort)ChoiceIndex(label));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.RunCommand(Views.cmSaveAs);
        Assert.Equal(path, editor.fileName);
        Assert.False(editor.modified);
        Assert.Equal(legacy.Encode(Expected()), File.ReadAllBytes(path));
        Assert.Equal($"converted.txt [{label}]", window.GetTitle(80));

        editor.InsertText("č");
        app.RunCommand(Views.cmSave);
        Assert.Equal(legacy.Encode(Expected()), File.ReadAllBytes(path));

        // Back to UTF-8 through Save As; the chooser starts on the current legacy encoding.
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            Assert.Equal((uint)ChoiceIndex(label), dialog.encodingSelector!.value);
            dialog.encodingSelector.SetData((ushort)ChoiceIndex("UTF-8"));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;
        app.RunCommand(Views.cmSaveAs);
        Assert.Equal(Encoding.UTF8.GetBytes(Expected()), File.ReadAllBytes(path));
        Assert.Equal("converted.txt [UTF-8]", window.GetTitle(80));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveAsWithUnrepresentableTextChangesNothing(bool targetExists)
    {
        string path = Path.Combine(_dir, "target.txt");
        byte[] original = Encoding.UTF8.GetBytes("keep me");
        if (targetExists) File.WriteAllBytes(path, original);
        TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.RunCommand(ShowcaseCmd.CodeEditor);
        var window = Assert.IsType<DemoCodeWindow>(app.DeskTop!.current);
        var editor = window.CodeEditor;
        editor.InsertText("日本 " + Czech);
        string content = ReadText(editor);
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(path);
            dialog.encodingSelector!.SetData((ushort)ChoiceIndex("CP852"));
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.DismissResult("cannot be saved using the selected encoding");
        app.RunCommand(Views.cmSaveAs);
        Assert.Empty(editor.fileName);
        Assert.True(editor.modified);
        Assert.Equal(content, ReadText(editor));
        Assert.Equal(EditorEncodingKind.Utf8, editor.OriginalEncoding);
        Assert.Null(editor.OriginalLegacyEncoding);
        Assert.Equal("Code Editor (C#)", window.GetTitle(80));
        if (targetExists) Assert.Equal(original, File.ReadAllBytes(path));
        else Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveAsHelpButtonOpensTheEditorFilesTopic()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.RunCommand(ShowcaseCmd.CodeEditor);
        TFileDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<TFileDialog>();
            dialog.SetData("draft.txt");
            ScriptedDemoApp.Button(dialog, Views.cmHelp).Press();
        });
        app.Step(() =>
        {
            Assert.Contains("File/Open in code editor", THelpCollection.PlainText(app.Modal<THelpWindow>().Viewer!.topic));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<TFileDialog>());
            Assert.Equal("draft.txt", dialog!.fileName!.Data);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(Views.cmSaveAs);
    }

    [Theory]
    [InlineData(Views.cmNo)]
    [InlineData(Views.cmYes)]
    public void SaveAsOverExistingFileUsesTheFrameworkConfirmation(ushort answer)
    {
        string path = Path.Combine(_dir, "existing.txt");
        File.WriteAllText(path, "keep");
        string? asked = null;
        TEditorDialogHelper.OverwriteConfirm = (_, p) => { asked = p; return answer; };
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.RunCommand(ShowcaseCmd.CodeEditor);
        var editor = Assert.IsType<DemoCodeWindow>(app.DeskTop!.current).CodeEditor;
        app.Step(() =>
        {
            var dialog = app.Modal<TFileDialog>();
            dialog.SetData(path);
            ScriptedDemoApp.Button(dialog, Views.cmFileOpen).Press();
        });
        app.RunCommand(Views.cmSaveAs);
        Assert.Equal(path, asked);
        if (answer == Views.cmYes)
        {
            Assert.Equal(path, editor.fileName);
            Assert.Contains("public static class Sample", File.ReadAllText(path));
        }
        else
        {
            Assert.Empty(editor.fileName);
            Assert.Equal("keep", File.ReadAllText(path));
        }
    }
}
