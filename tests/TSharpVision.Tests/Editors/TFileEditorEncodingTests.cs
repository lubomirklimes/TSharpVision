using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

[Collection("NonParallel")]
public sealed class TFileEditorEncodingTests
{
    [Fact]
    public void FailedBackupReplacementPreservesDestinationAndRemovesTemporaryFile()
    {
        using var temp = new TempDirectory();
        string path = WriteBytes(temp, "safe.txt", Encoding.UTF8.GetBytes("original"));
        Directory.CreateDirectory(Path.ChangeExtension(path, ".bak"));
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);
        var flags = TEditor.editorFlags;
        var dialog = TEditor.editorDialog;
        try
        {
            TEditor.editorFlags |= Views.efBackupFiles;
            TEditor.editorDialog = (_, _) => Views.cmOK;
            ed.SetSelect(0, ed.bufLen, false);
            ed.InsertText("new content");
            Assert.False(ed.SaveFile());
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(temp.Path));
            Directory.Delete(Path.ChangeExtension(path, ".bak"));
            Assert.True(ed.SaveFile());
            Assert.Equal("new content", File.ReadAllText(path));
            Assert.Equal("original", File.ReadAllText(Path.ChangeExtension(path, ".bak")));
        }
        finally
        {
            TEditor.editorFlags = flags;
            TEditor.editorDialog = dialog;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSavePreservesDestinationAndBackupAndCanRetry(bool backup)
    {
        using var temp = new TempDirectory();
        string path = WriteBytes(temp, "safe.txt", LegacyTextEncodings.Cp852.Encode("original"));
        string backupPath = Path.ChangeExtension(path, ".bak");
        File.WriteAllText(backupPath, "previous backup");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852) });
        var flags = TEditor.editorFlags;
        var dialog = TEditor.editorDialog;
        try
        {
            TEditor.editorFlags = (ushort)(backup ? flags | Views.efBackupFiles : flags & ~Views.efBackupFiles);
            TEditor.editorDialog = (_, _) => Views.cmOK;
            ed.SetSelect(0, ed.bufLen, false);
            ed.InsertText("漢");
            Assert.False(ed.SaveFile());
            Assert.Equal(LegacyTextEncodings.Cp852.Encode("original"), File.ReadAllBytes(path));
            Assert.Equal("previous backup", File.ReadAllText(backupPath));
            Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
            ed.SetSelect(0, ed.bufLen, false);
            ed.InsertText("retry");
            Assert.True(ed.SaveFile());
            Assert.Equal("retry", File.ReadAllText(path));
            Assert.Equal(backup ? "original" : "previous backup", File.ReadAllText(backupPath));
        }
        finally
        {
            TEditor.editorFlags = flags;
            TEditor.editorDialog = dialog;
        }
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    public void SavePreservesSurrogatesAcrossGap(uint gap)
    {
        using var temp = new TempDirectory();
        byte[] expected = Encoding.UTF8.GetBytes("A🙂B");
        string path = WriteBytes(temp, "unicode.txt", expected);
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);
        ed.SetSelect(gap, gap, false);
        Assert.Equal(gap, ed.curPtr);
        Assert.True(ed.SaveFile());
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < ed.bufLen; p++)
            sb.Append(ed.BufChar(p));
        return sb.ToString();
    }

    private static string WriteBytes(TempDirectory temp, string name, byte[] bytes)
    {
        string path = Path.Combine(temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExplicitCp852Open_DecodesCzechText()
    {
        using var temp = new TempDirectory();
        string expected = "Příliš žluťoučký kůň\n";
        string path = WriteBytes(temp, "cp852.txt", LegacyTextEncodings.Cp852.Encode(expected));

        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions
            {
                Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852),
            });

        Assert.True(ed.isValid);
        Assert.Equal(expected, ReadAll(ed));
        Assert.Equal(EditorEncodingKind.Legacy, ed.OriginalEncoding);
        Assert.Same(LegacyTextEncodings.Cp852, ed.OriginalLegacyEncoding);
    }

    [Fact]
    public void ExplicitKamenickyOpen_DecodesCzechTextAndBoxDrawing()
    {
        using var temp = new TempDirectory();
        string expected = "Příliš žluťoučký kůň ─\n";
        string path = WriteBytes(temp, "keybcs2.txt", LegacyTextEncodings.Kamenicky.Encode(expected));

        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions
            {
                Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Kamenicky),
            });

        Assert.True(ed.isValid);
        Assert.Equal(expected, ReadAll(ed));
        Assert.Equal(EditorEncodingKind.Legacy, ed.OriginalEncoding);
        Assert.Same(LegacyTextEncodings.Kamenicky, ed.OriginalLegacyEncoding);
    }

    [Fact]
    public void ExplicitLatin1_DoesNotGuessCp852()
    {
        using var temp = new TempDirectory();
        string expected = "Příliš žluťoučký kůň\n";
        string path = WriteBytes(temp, "wrong.txt", LegacyTextEncodings.Cp852.Encode(expected));

        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Latin1 });

        Assert.True(ed.isValid);
        Assert.NotEqual(expected, ReadAll(ed));
        Assert.Equal(EditorEncodingKind.Latin1, ed.OriginalEncoding);
        Assert.Same(LegacyTextEncodings.Latin1, ed.OriginalLegacyEncoding);
    }

    [Fact]
    public void ExplicitUtf8_InvalidBytesThrow()
    {
        using var temp = new TempDirectory();
        string path = WriteBytes(temp, "invalid-utf8.txt", new byte[] { 0xC3, 0x28 });

        Assert.Throws<DecoderFallbackException>(() =>
            new TFileEditor(
                new TRect(0, 0, 40, 10),
                null,
                null,
                null,
                path,
                new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Utf8 }));
    }

    [Fact]
    public void SaveFile_ExplicitCp852_PreservesCp852Encoding()
    {
        using var temp = new TempDirectory();
        string text = "Příliš žluťoučký kůň\n";
        string path = WriteBytes(temp, "save-cp852.txt", LegacyTextEncodings.Cp852.Encode(text));
        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions
            {
                Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852),
            });

        Assert.True(ed.SaveFile());

        Assert.Equal(LegacyTextEncodings.Cp852.Encode(text), File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveFile_ExplicitLegacyUnsupportedUnicode_ReturnsFalse()
    {
        using var temp = new TempDirectory();
        string path = WriteBytes(temp, "unsupported-cp852.txt", LegacyTextEncodings.Cp852.Encode("start\n"));
        var savedDialog = TEditor.editorDialog;
        int dialog = -1;
        object? info = null;
        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions
            {
                Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852),
            });

        try
        {
            TEditor.editorDialog = (d, i) =>
            {
                dialog = d;
                info = i;
                return Views.cmOK;
            };
            ed.SetSelect(0, ed.bufLen, false);
            ed.InsertText("Unsupported 漢\n");

            Assert.False(ed.SaveFile());
            Assert.Equal(Views.edEncodingWriteError, dialog);
            Assert.Equal(path, info);
        }
        finally
        {
            TEditor.editorDialog = savedDialog;
        }
    }

    [Fact]
    public void CustomRegisteredEncoding_CanOpenFile()
    {
        using var temp = new TempDirectory();
        var table = new char[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = (char)i;
        table[0x80] = 'č';
        string name = "test-editor-custom-" + Guid.NewGuid().ToString("N");
        LegacyTextEncodings.RegisterSingleByte(name, table);
        Assert.True(LegacyTextEncodings.TryGet(name, out var encoding));
        string path = WriteBytes(temp, "custom.txt", new byte[] { (byte)'A', 0x80, (byte)'\n' });

        var ed = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Legacy(encoding) });

        Assert.Equal("Ač\n", ReadAll(ed));
    }

    [Fact]
    public void ExplicitLegacyOpen_DoesNotChangeGlyphConstants()
    {
        using var temp = new TempDirectory();
        char before = TSharpVisionGlyphs.FrameHorizontal;
        string path = WriteBytes(temp, "glyphs.txt", LegacyTextEncodings.Kamenicky.Encode("text\n"));

        _ = new TFileEditor(
            new TRect(0, 0, 40, 10),
            null,
            null,
            null,
            path,
            new TFileEditorOpenOptions
            {
                Encoding = EditorTextEncoding.Legacy(LegacyTextEncodings.Kamenicky),
            });

        Assert.Equal(TSharpVisionGlyphs.FrameHorizontal, before);
    }
}
