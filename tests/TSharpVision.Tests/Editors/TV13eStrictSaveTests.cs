using System.Collections.Generic;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TV13e: saving never replaces or best-fits characters. Unrepresentable text reports edEncodingWriteError and leaves
// the file and its backup untouched; choosing another encoding (API or Save As) then saves.
[Collection("NonParallel")]
public sealed class TV13eStrictSaveTests : IDisposable
{
    private readonly TEditor.TEditorDialog _savedDialog = TEditor.editorDialog;
    private readonly ushort _savedFlags = TEditor.editorFlags;
    private readonly Func<TDeskTop?, string, ushort> _savedOverwrite = TEditorDialogHelper.OverwriteConfirm;
    private readonly DriverScope _driver = new();
    private readonly List<(int Dialog, object? Info)> _calls = new();

    public TV13eStrictSaveTests()
    {
        TEditor.editorDialog = (d, i) =>
        {
            _calls.Add((d, i));
            return Views.cmOK;
        };
    }

    public void Dispose()
    {
        TEditor.editorDialog = _savedDialog;
        TEditor.editorFlags = _savedFlags;
        TEditorDialogHelper.OverwriteConfirm = _savedOverwrite;
        _driver.Dispose();
    }

    private static readonly byte[] Latin1Original = { (byte)'c', (byte)'a', (byte)'f', 0xE9, 0x0D, 0x0A };

    private static TFileEditor OpenAutoLatin1(TempDirectory temp, out string path, out string backupPath)
    {
        path = Path.Combine(temp.Path, "latin.txt");
        backupPath = Path.Combine(temp.Path, "latin.bak");
        File.WriteAllBytes(path, Latin1Original);
        File.WriteAllText(backupPath, "previous backup");
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Latin1, ed.OriginalEncoding);
        Assert.Null(ed.OriginalLegacyEncoding);
        return ed;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoLatin1_UnrepresentableCharacter_FailsAndLeavesFileAndBackup(bool backup)
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out string backupPath);
        TEditor.editorFlags = (ushort)(backup ? _savedFlags | Views.efBackupFiles : _savedFlags & ~Views.efBackupFiles);
        ed.SetSelect(0, 0, false);
        ed.InsertText("Č");

        Assert.False(ed.SaveFile());

        var call = Assert.Single(_calls);
        Assert.Equal(Views.edEncodingWriteError, call.Dialog);
        Assert.Equal(path, call.Info);
        Assert.Equal(Latin1Original, File.ReadAllBytes(path));
        Assert.Equal("previous backup", File.ReadAllText(backupPath));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
        Assert.True(ed.modified);
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoLatin1_AfterChoosingUtf8_SaveSucceeds(bool backup)
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out string backupPath);
        TEditor.editorFlags = (ushort)(backup ? _savedFlags | Views.efBackupFiles : _savedFlags & ~Views.efBackupFiles);
        ed.SetSelect(0, 0, false);
        ed.InsertText("Č");
        Assert.False(ed.SaveFile());

        ed.SetSaveEncoding(EditorEncodingKind.Utf8);
        Assert.True(ed.SaveFile());

        Assert.Equal(new UTF8Encoding(false).GetBytes("Čcafé\r\n"), File.ReadAllBytes(path));
        if (backup)
            Assert.Equal(Latin1Original, File.ReadAllBytes(backupPath));
        else
            Assert.Equal("previous backup", File.ReadAllText(backupPath));
        Assert.False(ed.modified);
        Assert.Equal(EditorEncodingKind.Utf8, ed.SaveEncoding);
        Assert.Equal(EditorEncodingKind.Latin1, ed.OriginalEncoding);
    }

    [Fact]
    public void AutoLatin1_RepresentableEdit_SavesAsLatin1()
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out _);
        ed.SetSelect(0, 0, false);
        ed.InsertText("é");
        Assert.True(ed.SaveFile());
        Assert.Equal(new byte[] { 0xE9 }.Concat(Latin1Original).ToArray(), File.ReadAllBytes(path));
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData(0xD800, false)]
    [InlineData(0xDFFF, false)]
    [InlineData(0xD83D, true)]
    public void Utf8_LoneSurrogate_FailsAndLeavesFile(int surrogate, bool surrounded)
    {
        // Built here: xUnit theory serialization would turn a lone surrogate in InlineData into U+FFFD.
        string inserted = surrounded ? "a" + (char)surrogate + "b" : ((char)surrogate).ToString();
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "utf8.txt");
        byte[] original = Encoding.UTF8.GetBytes("Příliš\n");
        File.WriteAllBytes(path, original);
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        ed.SetSelect(2, 2, false);
        ed.InsertText(inserted);
        Assert.Contains((char)surrogate, ReadAll(ed));

        Assert.False(ed.SaveFile());

        var call = Assert.Single(_calls);
        Assert.Equal(Views.edEncodingWriteError, call.Dialog);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Theory]
    [InlineData(EditorEncodingKind.Utf16LeBom)]
    [InlineData(EditorEncodingKind.Utf16BeBom)]
    [InlineData(EditorEncodingKind.Utf8Bom)]
    public void LoneSurrogate_FailsInEveryUnicodeSaveEncoding(EditorEncodingKind kind)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "u.txt");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        ed.SetSaveEncoding(kind);
        ed.SetSelect(1, 1, false);
        ed.InsertText("\uDC00");

        Assert.False(ed.SaveFile());
        Assert.Equal(Views.edEncodingWriteError, Assert.Single(_calls).Dialog);
        Assert.Equal(Encoding.ASCII.GetBytes("abc"), File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(EditorEncodingKind.Utf8, new byte[0])]
    [InlineData(EditorEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(EditorEncodingKind.Utf16LeBom, new byte[] { 0xFF, 0xFE })]
    [InlineData(EditorEncodingKind.Utf16BeBom, new byte[] { 0xFE, 0xFF })]
    public void SetSaveEncoding_WritesEncodingAndByteOrderMark(EditorEncodingKind kind, byte[] preamble)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "convert.txt");
        File.WriteAllBytes(path, LegacyTextEncodings.Cp852.Encode("Příliš\r\n"));
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852) });

        ed.SetSaveEncoding(kind);
        Assert.Equal(kind, ed.SaveEncoding);
        Assert.Null(ed.SaveLegacyEncoding);
        Assert.True(ed.SaveFile());

        Encoding body = kind switch
        {
            EditorEncodingKind.Utf16LeBom => new UnicodeEncoding(false, false),
            EditorEncodingKind.Utf16BeBom => new UnicodeEncoding(true, false),
            _ => new UTF8Encoding(false),
        };
        Assert.Equal(preamble.Concat(body.GetBytes("Příliš\r\n")).ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void SetSaveEncoding_Overloads_MapKindsAndCodecs()
    {
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);

        ed.SetSaveEncoding(EditorEncodingKind.Latin1);
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
        Assert.Same(LegacyTextEncodings.Latin1, ed.SaveLegacyEncoding);

        ed.SetSaveEncoding(LegacyTextEncodings.Cp852);
        Assert.Equal(EditorEncodingKind.Legacy, ed.SaveEncoding);
        Assert.Same(LegacyTextEncodings.Cp852, ed.SaveLegacyEncoding);

        ed.SetSaveEncoding(EditorTextEncoding.Auto);
        Assert.Equal(EditorEncodingKind.Legacy, ed.SaveEncoding);
        Assert.Same(LegacyTextEncodings.Cp852, ed.SaveLegacyEncoding);

        ed.SetSaveEncoding(LegacyTextEncodings.Latin1);
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);

        ed.SetSaveEncoding(EditorTextEncoding.Utf8);
        Assert.Equal(EditorEncodingKind.Utf8, ed.SaveEncoding);
        Assert.Null(ed.SaveLegacyEncoding);

        ed.SetSaveEncoding(EditorEncodingKind.Utf8Bom);
        ed.SetSaveEncoding(EditorTextEncoding.Utf8);
        Assert.Equal(EditorEncodingKind.Utf8Bom, ed.SaveEncoding);

        ed.SetSaveEncoding(EditorTextEncoding.Legacy(LegacyTextEncodings.Kamenicky));
        Assert.Equal(EditorEncodingKind.Legacy, ed.SaveEncoding);
        Assert.Same(LegacyTextEncodings.Kamenicky, ed.SaveLegacyEncoding);

        Assert.Throws<ArgumentException>(() => ed.SetSaveEncoding(EditorEncodingKind.Legacy));
        Assert.Throws<ArgumentException>(() => ed.SetSaveEncoding((EditorEncodingKind)99));
        Assert.Throws<ArgumentNullException>(() => ed.SetSaveEncoding((ILegacyTextEncoding)null!));
        Assert.Throws<ArgumentNullException>(() => ed.SetSaveEncoding((EditorTextEncoding)null!));
        Assert.Equal(EditorEncodingKind.Legacy, ed.SaveEncoding);
    }

    [Fact]
    public void LoadFile_ResetsSaveEncodingToLoadedEncoding()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "reload.txt");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' });
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Utf8Bom, ed.SaveEncoding);
        ed.SetSaveEncoding(EditorEncodingKind.Utf16LeBom);

        Assert.True(ed.LoadFile());
        Assert.Equal(EditorEncodingKind.Utf8Bom, ed.SaveEncoding);
    }

    // ── Save As through the installed dialog helper ─────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveAs_ChoosingUtf8_RecoversFromEncodingError(bool sameFile)
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out _);
        ed.SetSelect(0, 0, false);
        ed.InsertText("Č");
        Assert.False(ed.SaveFile());

        string target = sameFile ? path : Path.Combine(temp.Path, "copy.txt");
        var desk = new SaveAsDeskTop(target, encodingIndex: 1);
        TEditorDialogHelper.Install(desk);
        TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;

        Assert.True(ed.SaveAs());

        Assert.Equal(1, desk.FileDialogs);
        Assert.Equal(target, ed.fileName);
        Assert.Equal(EditorEncodingKind.Utf8, ed.SaveEncoding);
        Assert.Equal(new UTF8Encoding(false).GetBytes("Čcafé\r\n"), File.ReadAllBytes(target));
        if (!sameFile)
            Assert.Equal(Latin1Original, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveAs_KeepCurrent_KeepsEncodingAndStillFailsStrictly()
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out _);
        ed.SetSelect(0, 0, false);
        ed.InsertText("Č");
        string target = Path.Combine(temp.Path, "other.txt");
        var desk = new SaveAsDeskTop(target, encodingIndex: 0);
        TEditorDialogHelper.Install(desk);

        Assert.False(ed.SaveAs());

        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
        Assert.False(File.Exists(target));
        Assert.Equal(Latin1Original, File.ReadAllBytes(path));
        Assert.Equal(1, desk.MessageBoxes); // the edEncodingWriteError message
    }

    [Fact]
    public void SaveAs_ChoosingCp852_SavesInCp852()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "src.txt");
        File.WriteAllText(path, "Příliš\n", new UTF8Encoding(false));
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        string target = Path.Combine(temp.Path, "cp852.txt");
        TEditorDialogHelper.Install(new SaveAsDeskTop(target, encodingIndex: 4));

        Assert.True(ed.SaveAs());

        Assert.Same(LegacyTextEncodings.Cp852, ed.SaveLegacyEncoding);
        Assert.Equal(LegacyTextEncodings.Cp852.Encode("Příliš\n"), File.ReadAllBytes(target));
    }

    [Fact]
    public void SaveAs_DeclinedOverwrite_DoesNotChangeEncodingOrName()
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out string path, out _);
        string target = Path.Combine(temp.Path, "exists.txt");
        File.WriteAllText(target, "keep");
        TEditorDialogHelper.Install(new SaveAsDeskTop(target, encodingIndex: 1));
        TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmNo;

        Assert.False(ed.SaveAs());

        Assert.Equal(path, ed.fileName);
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    public void SaveAs_CancelledDialog_DoesNotChangeEncoding()
    {
        using var temp = new TempDirectory();
        var ed = OpenAutoLatin1(temp, out _, out _);
        TEditorDialogHelper.Install(new SaveAsDeskTop(null, encodingIndex: 1));

        Assert.False(ed.SaveAs());
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
    }

    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder((int)ed.bufLen);
        for (uint p = 0; p < ed.bufLen; p++)
            sb.Append(ed.BufChar(p));
        return sb.ToString();
    }

    private sealed class SaveAsDeskTop : TDeskTop
    {
        private readonly string? _target;
        private readonly ushort _encodingIndex;

        public SaveAsDeskTop(string? target, ushort encodingIndex) : base(new TRect(0, 0, 100, 30))
        {
            _target = target;
            _encodingIndex = encodingIndex;
        }

        public int FileDialogs { get; private set; }
        public int MessageBoxes { get; private set; }

        public override ushort ExecView(TView view)
        {
            if (view is TFileDialog dialog)
            {
                FileDialogs++;
                Assert.NotNull(dialog.encodingSelector);
                Assert.Equal("Keep current", dialog.encodingSelector!.Strings[0]);
                if (_target == null) return Views.cmCancel;
                dialog.SetData(_target);
                dialog.encodingSelector.SetData(_encodingIndex);
                return Views.cmOK;
            }
            MessageBoxes++;
            return Views.cmOK;
        }
    }
}
