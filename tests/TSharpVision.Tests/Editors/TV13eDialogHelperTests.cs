using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TV13e: the installed editor dialogs for the mixed line-ending prompt, the Save As encoding selector and the
// encoding error, with and without a desktop.
[Collection("NonParallel")]
public sealed class TV13eDialogHelperTests : IDisposable
{
    private readonly TEditor.TEditorDialog _savedDialog = TEditor.editorDialog;
    private readonly ITSharpVisionStringProvider _savedIntl = TSharpVisionIntl.Current;
    private readonly DriverScope _driver = new();

    public TV13eDialogHelperTests() => TSharpVisionIntl.Current = new DefaultEnglishStringProvider();

    public void Dispose()
    {
        TEditor.editorDialog = _savedDialog;
        TSharpVisionIntl.Current = _savedIntl;
        _driver.Dispose();
    }

    private static EditorLineEndingPromptInfo Prompt()
        => new("C:\\x\\mixed.txt", 3, 1, 2, LineEndingKind.CrLf);

    [Fact]
    public void NoDesktop_MixedPromptCancelsWithoutThrowing()
    {
        TEditorDialogHelper.Install(null);
        var info = Prompt();

        Assert.Equal(Views.cmCancel, TEditor.editorDialog(Views.edMixedLineEndings, info));
        Assert.Equal(LineEndingKind.CrLf, info.Choice);
        Assert.Equal(Views.cmCancel, TEditor.editorDialog(Views.edMixedLineEndings, null));
        Assert.Equal(Views.cmCancel, TEditor.editorDialog(Views.edMixedLineEndings, "wrong payload"));
    }

    [Fact]
    public void NoDesktop_EncodingErrorReturnsOkAndSaveAsCancels()
    {
        TEditorDialogHelper.Install(null);
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, null);
        ed.SetSaveEncoding(EditorEncodingKind.Latin1);

        Assert.Equal(Views.cmOK, TEditor.editorDialog(Views.edEncodingWriteError, "file.txt"));
        Assert.Equal(Views.cmCancel, TEditor.editorDialog(Views.edSaveAs, ed));
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
    }

    [Fact]
    public void NoDesktop_MixedSaveThroughEditorIsCancelled()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "m.txt");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("a\r\nb\n"));
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);
        TEditorDialogHelper.Install(null);

        Assert.False(ed.SaveFile());
        Assert.Equal("a\r\nb\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0, LineEndingKind.Lf)]
    [InlineData(1, LineEndingKind.CrLf)]
    [InlineData(2, LineEndingKind.Cr)]
    public void Desktop_MixedPromptReturnsSelectedChoice(int index, LineEndingKind expected)
    {
        var desk = new ScriptedDeskTop(dialog =>
        {
            var radios = Radios(dialog);
            Assert.Equal(1u, radios.value); // the suggested CR LF is preselected
            radios.SetData((ushort)index);
            return Views.cmOK;
        });
        TEditorDialogHelper.Install(desk);
        var info = Prompt();

        Assert.Equal(Views.cmOK, TEditor.editorDialog(Views.edMixedLineEndings, info));
        Assert.Equal(expected, info.Choice);
        Assert.Equal(1, desk.Count);
    }

    [Fact]
    public void Desktop_MixedPromptCancelKeepsChoice()
    {
        TEditorDialogHelper.Install(new ScriptedDeskTop(dialog =>
        {
            Radios(dialog).SetData((ushort)2);
            return Views.cmCancel;
        }));
        var info = Prompt();

        Assert.Equal(Views.cmCancel, TEditor.editorDialog(Views.edMixedLineEndings, info));
        Assert.Equal(LineEndingKind.CrLf, info.Choice);
    }

    [Fact]
    public void Desktop_MixedSaveThroughEditorWritesChosenStyle()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "m.txt");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("a\r\nb\nc"));
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);
        var desk = new ScriptedDeskTop(dialog =>
        {
            Radios(dialog).SetData((ushort)0);
            return Views.cmOK;
        });
        TEditorDialogHelper.Install(desk);

        Assert.True(ed.SaveFile());
        Assert.True(ed.SaveFile());

        Assert.Equal("a\nb\nc", File.ReadAllText(path));
        Assert.Equal(1, desk.Count);
    }

    [Fact]
    public void MixedDialog_ShowsLocalizedTextAndChoices()
    {
        var dlg = TEditorDialogHelper.CreateMixedLineEndingsDialog(Prompt(), out var radios);
        try
        {
            Assert.Equal("Line Endings", dlg.title);
            Assert.Equal(new[] { "LF (Unix)", "CRLF (Windows)", "CR (classic Mac)" }, radios.Strings);
            Assert.Equal(1u, radios.value);
            var text = (TStaticText?)dlg.FirstThat((v, _) => v is TStaticText, null);
            Assert.NotNull(text);
            text!.GetText(out string shown);
            Assert.Contains("mixed.txt", shown);
            Assert.Contains("CRLF: 3, LF: 1, CR: 2", shown);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void SaveAsDialog_ForFileEditorOffersEncodingsWithKeepCurrentFirst()
    {
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, null);
        var dlg = TEditorDialogHelper.CreateSaveAsDialog(ed);
        try
        {
            var selector = Assert.IsType<TRadioButtons>(dlg.encodingSelector);
            Assert.Equal(EditorEncodingChoices.BuiltIn.Count, selector.Strings.Count);
            Assert.Equal("Keep current", selector.Strings[0]);
            Assert.Equal("UTF-8", selector.Strings[1]);
            Assert.Equal(0u, selector.value);
            Assert.Equal(EditorTextEncodingMode.Auto, dlg.SelectedEncoding.Mode);
        }
        finally
        {
            dlg.ShutDown();
        }

        var plain = TEditorDialogHelper.CreateSaveAsDialog(null);
        try
        {
            Assert.Null(plain.encodingSelector);
        }
        finally
        {
            plain.ShutDown();
        }
    }

    [Fact]
    public void SaveAsDialog_UsesTheInstalledOptionsAndHelpContext()
    {
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, null);
        var dlg = TEditorDialogHelper.CreateSaveAsDialog(ed,
            FileDialogOptions.fdOKButton | FileDialogOptions.fdHelpButton, 4242);
        try
        {
            Assert.Equal(4242, dlg.helpCtx);
            Assert.NotNull(dlg.encodingSelector);
            Assert.Contains(Buttons(dlg), b => b.Command == Views.cmHelp);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    // The dialog only records the choice; SaveAs applies it and a failed write leaves the save encoding as it was.
    [Fact]
    public void Desktop_FailedSaveAsKeepsTheSaveEncoding()
    {
        using var temp = new TempDirectory();
        string source = Path.Combine(temp.Path, "doc.txt");
        File.WriteAllBytes(source, TSharpVision.Text.LegacyTextEncodings.Cp852.Encode("ř\r\n"));
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, source,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Legacy(TSharpVision.Text.LegacyTextEncodings.Cp852) });
        int cp437 = Enumerable.Range(0, EditorEncodingChoices.BuiltIn.Count)
            .Single(i => EditorEncodingChoices.BuiltIn[i].Fallback == "CP437");
        TFileDialog? shown = null;
        var desk = new ScriptedDeskTop(dialog =>
        {
            if (dialog is not TFileDialog fd) return Views.cmOK; // the encoding error message
            shown = fd;
            fd.SetData(Path.Combine(temp.Path, "out.txt"));
            fd.encodingSelector!.SetData((ushort)cp437);
            return Views.cmOK;
        });
        TEditorDialogHelper.Install(desk,
            FileDialogOptions.fdOKButton | FileDialogOptions.fdHelpButton | FileDialogOptions.fdEncodingSelector, 4242);

        Assert.False(ed.SaveAs()); // CP437 has no 'ř'

        Assert.Equal(4242, shown!.helpCtx);
        Assert.Equal(source, ed.fileName);
        Assert.Same(TSharpVision.Text.LegacyTextEncodings.Cp852, ed.SaveLegacyEncoding);
        Assert.False(File.Exists(Path.Combine(temp.Path, "out.txt")));
    }

    private static List<TButton> Buttons(TGroup group)
    {
        var result = new List<TButton>();
        group.ForEach<object?>((v, _) => { if (v is TButton b) result.Add(b); }, null);
        return result;
    }

    [Fact]
    public void Desktop_EncodingErrorShowsOneMessage()
    {
        var desk = new ScriptedDeskTop(_ => Views.cmOK);
        TEditorDialogHelper.Install(desk);

        Assert.Equal(Views.cmOK, TEditor.editorDialog(Views.edEncodingWriteError, "file.txt"));
        Assert.Equal(1, desk.Count);
    }

    [Fact]
    public void NewLocalizationKeys_HaveEnglishText()
    {
        int misses = 0;
        EventHandler<MissingLocalizationKeyEventArgs> handler = (_, _) => misses++;
        TSharpVisionIntl.MissingKey += handler;
        try
        {
            Assert.Equal("Keep current", TSharpVisionIntl.Get("Encoding_KeepCurrent", "x"));
            Assert.Equal("Nothing was written. Use Save As to choose another encoding, such as UTF-8.",
                TSharpVisionIntl.Get("File_Err_EncodingSaveAsHint", "x"));
            Assert.Equal("Line Endings", TSharpVisionIntl.Get("Edit_MixedLineEndings_Title", "x"));
            Assert.Equal("'{0}' uses mixed line endings ({1}). Save every line ending as:",
                TSharpVisionIntl.Get("Edit_MixedLineEndings", "x"));
            Assert.Equal("LF (Unix)", TSharpVisionIntl.Get("LineEnding_Lf", "x"));
            Assert.Equal("CRLF (Windows)", TSharpVisionIntl.Get("LineEnding_CrLf", "x"));
            Assert.Equal("CR (classic Mac)", TSharpVisionIntl.Get("LineEnding_Cr", "x"));
            Assert.Equal(0, misses);
        }
        finally
        {
            TSharpVisionIntl.MissingKey -= handler;
        }
    }

    private static TRadioButtons Radios(TDialog dialog)
        => Assert.IsType<TRadioButtons>(dialog.FirstThat((v, _) => v is TRadioButtons, null));

    private sealed class ScriptedDeskTop : TDeskTop
    {
        private readonly Func<TDialog, ushort> _script;

        public ScriptedDeskTop(Func<TDialog, ushort> script) : base(new TRect(0, 0, 100, 30)) => _script = script;

        public int Count { get; private set; }

        public override ushort ExecView(TView view)
        {
            Count++;
            return view is TDialog dialog ? _script(dialog) : Views.cmCancel;
        }
    }
}
