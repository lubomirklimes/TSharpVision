using System;
using System.IO;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

[Collection("NonParallel")]
public sealed class TEditorEncodingDialogTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public void Dispose() => _driver.Dispose();

    private static string ReadAll(TEditor editor)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++)
            sb.Append(editor.BufChar(p));
        return sb.ToString();
    }

    [Fact]
    public void EncodingSelector_DefaultIsAuto()
    {
        var dlg = CreateEncodingDialog();
        try
        {
            Assert.NotNull(dlg.encodingSelector);
            Assert.Equal(EditorTextEncodingMode.Auto, dlg.SelectedEncoding.Mode);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void EncodingChoices_IncludeBuiltIns()
    {
        var dlg = CreateEncodingDialog();
        try
        {
            var selector = Assert.IsType<TRadioButtons>(dlg.encodingSelector);
            Assert.Contains("Auto", selector.Strings);
            Assert.Contains("UTF-8", selector.Strings);
            Assert.Contains("Latin-1", selector.Strings);
            Assert.Contains("CP437", selector.Strings);
            Assert.Contains("CP852", selector.Strings);
            Assert.Contains("Windows-1250", selector.Strings);
            Assert.Contains("ISO-8859-2", selector.Strings);
            Assert.Contains("Kamenicky / KEYBCS2", selector.Strings);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void SelectingCp852_ProducesCp852EditorEncoding()
    {
        var dlg = CreateEncodingDialog();
        try
        {
            var selector = Assert.IsType<TRadioButtons>(dlg.encodingSelector);
            selector.SetData((ushort)4);

            Assert.Equal(EditorTextEncodingMode.Legacy, dlg.SelectedEncoding.Mode);
            Assert.Same(LegacyTextEncodings.Cp852, dlg.SelectedEncoding.LegacyEncoding);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void SelectingKamenicky_ProducesKamenickyEditorEncoding()
    {
        var dlg = CreateEncodingDialog();
        try
        {
            var selector = Assert.IsType<TRadioButtons>(dlg.encodingSelector);
            selector.SetData((ushort)7);

            Assert.Equal(EditorTextEncodingMode.Legacy, dlg.SelectedEncoding.Mode);
            Assert.Same(LegacyTextEncodings.Kamenicky, dlg.SelectedEncoding.LegacyEncoding);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void NormalFileDialog_HasNoEncodingSelector()
    {
        var dlg = new TFileDialog(
            "*.*",
            "Open",
            "Name",
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdNoLoadDir),
            0);
        try
        {
            Assert.Null(dlg.encodingSelector);
            Assert.Equal(EditorTextEncodingMode.Auto, dlg.SelectedEncoding.Mode);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void FileOpen_SelectingCp852_PassesCp852ToEditorOpen()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "cp852.txt");
        File.WriteAllBytes(path, LegacyTextEncodings.Cp852.Encode("Příliš žluťoučký kůň\n"));
        var app = new CapturingEditorApp(path, 4);
        try
        {
            app.FileOpen();

            Assert.Equal(path, app.OpenedFileName);
            var openedOptions = Assert.IsType<TFileEditorOpenOptions>(app.OpenedOptions);
            Assert.Equal(EditorTextEncodingMode.Legacy, openedOptions.Encoding.Mode);
            Assert.Same(LegacyTextEncodings.Cp852, app.OpenedOptions.Encoding.LegacyEncoding);
            Assert.Equal("Příliš žluťoučký kůň\n", app.OpenedText);
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void FileOpen_DefaultAuto_OpensUtf8Correctly()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "utf8.txt");
        File.WriteAllText(path, "Příliš žluťoučký kůň\n", new UTF8Encoding(false));
        var app = new CapturingEditorApp(path, 0);
        try
        {
            app.FileOpen();

            var openedOptions = Assert.IsType<TFileEditorOpenOptions>(app.OpenedOptions);
            Assert.Equal(EditorTextEncodingMode.Auto, openedOptions.Encoding.Mode);
            Assert.Equal("Příliš žluťoučký kůň\n", app.OpenedText);
        }
        finally
        {
            app.ShutDown();
        }
    }

    [Fact]
    public void LocalizationKeys_Exist()
    {
        var saved = TSharpVisionIntl.Current;
        int misses = 0;
        EventHandler<MissingLocalizationKeyEventArgs> handler = (_, _) => misses++;
        try
        {
            TSharpVisionIntl.Current = new DefaultEnglishStringProvider();
            TSharpVisionIntl.MissingKey += handler;

            foreach (var choice in EditorEncodingChoices.BuiltIn)
                Assert.Equal(choice.Fallback, TSharpVisionIntl.Get(choice.Key, "fallback"));
            Assert.Equal("~E~ncoding", TSharpVisionIntl.Get("File_Label_Encoding", "fallback"));
            Assert.Equal(
                "The file could not be decoded using the selected encoding.",
                TSharpVisionIntl.Get("File_Err_EncodingDecode", "fallback"));
            Assert.Equal(
                "The file contains characters that cannot be saved using the selected encoding.",
                TSharpVisionIntl.Get("File_Err_EncodingEncode", "fallback"));
            Assert.Equal(0, misses);
        }
        finally
        {
            TSharpVisionIntl.MissingKey -= handler;
            TSharpVisionIntl.Current = saved;
        }
    }

    private static TFileDialog CreateEncodingDialog()
        => new TFileDialog(
            "*.*",
            "Open",
            "Name",
            (ushort)(FileDialogOptions.fdOpenButton
                | FileDialogOptions.fdNoLoadDir
                | FileDialogOptions.fdEncodingSelector),
            0);

    private sealed class CapturingEditorApp : TEditorApp
    {
        private readonly string _path;
        private readonly ushort _encodingIndex;

        public CapturingEditorApp(string path, ushort encodingIndex)
        {
            _path = path;
            _encodingIndex = encodingIndex;
            DeskTop = new StubDeskTop(_path, _encodingIndex);
        }

        public string? OpenedFileName { get; private set; }
        public TFileEditorOpenOptions? OpenedOptions { get; private set; }
        public string? OpenedText { get; private set; }

        public override TEditWindow? OpenEditor(
            string? fileName,
            bool visible,
            TFileEditorOpenOptions? openOptions)
        {
            OpenedFileName = fileName ?? string.Empty;
            OpenedOptions = openOptions ?? new TFileEditorOpenOptions();
            var editor = new TFileEditor(
                new TRect(0, 0, 80, 25),
                null,
                null,
                null,
                fileName,
                Assert.IsType<TFileEditorOpenOptions>(OpenedOptions));
            OpenedText = ReadAll(editor);
            return null;
        }
    }

    private sealed class StubDeskTop : TDeskTop
    {
        private readonly string _path;
        private readonly ushort _encodingIndex;

        public StubDeskTop(string path, ushort encodingIndex)
            : base(new TRect(0, 0, 100, 30))
        {
            _path = path;
            _encodingIndex = encodingIndex;
        }

        public override ushort ExecView(TView view)
        {
            if (view is TFileDialog dialog)
            {
                dialog.SetData(_path);
                dialog.encodingSelector?.SetData(_encodingIndex);
                return Views.cmOK;
            }

            return Views.cmCancel;
        }
    }
}
