using System.Collections.Generic;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TV13e: an unmodified document saves back byte for byte, without any prompt, in every supported encoding and
// line-ending style.
[Collection("NonParallel")]
public sealed class TV13eEncodingRoundTripTests : IDisposable
{
    private readonly TEditor.TEditorDialog _savedDialog = TEditor.editorDialog;
    private readonly ushort _savedFlags = TEditor.editorFlags;
    private readonly List<int> _dialogs = new();

    public TV13eEncodingRoundTripTests()
    {
        TEditor.editorDialog = (d, _) =>
        {
            _dialogs.Add(d);
            return Views.cmCancel;
        };
    }

    public void Dispose()
    {
        TEditor.editorDialog = _savedDialog;
        TEditor.editorFlags = _savedFlags;
    }

    private void AssertRoundTrip(byte[] original, TFileEditorOpenOptions? options = null, bool backup = false)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "round.txt");
        File.WriteAllBytes(path, original);
        TEditor.editorFlags = (ushort)(backup ? _savedFlags | Views.efBackupFiles : _savedFlags & ~Views.efBackupFiles);

        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path, options);
        Assert.True(ed.isValid);
        Assert.True(ed.SaveFile());

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(_dialogs);
        Assert.False(ed.modified);
        Assert.Equal(backup ? 2 : 1, Directory.GetFiles(temp.Path).Length);
    }

    [Theory]
    [InlineData("a\nb\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\rb\r")]
    [InlineData("no line ending")]
    [InlineData("Příliš žluťoučký kůň 🙂\r\nsecond line")]
    public void Utf8_RoundTripsByteIdentically(string text)
        => AssertRoundTrip(new UTF8Encoding(false).GetBytes(text));

    [Theory]
    [InlineData("a\nb\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\rb\r")]
    public void Utf8Bom_RoundTripsByteIdentically(string text)
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Příliš " + text)).ToArray();
        AssertRoundTrip(bytes);
    }

    [Fact]
    public void Utf8_WithBackup_RoundTripsAndKeepsOriginalAsBackup()
        => AssertRoundTrip(Encoding.UTF8.GetBytes("backup\r\n"), backup: true);

    [Fact]
    public void EmptyFile_RoundTripsAsEmpty()
        => AssertRoundTrip(Array.Empty<byte>());

    [Fact]
    public void BomOnlyFile_RoundTrips()
        => AssertRoundTrip(new byte[] { 0xEF, 0xBB, 0xBF });

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Latin1Fallback_AllNonNewlineBytes_RoundTrip(string newline)
    {
        // 0xFF first makes the content invalid UTF-8, so automatic detection falls back to Latin-1.
        var bytes = new List<byte> { 0xFF };
        for (int b = 0; b < 256; b++)
        {
            if (b == 0x0A || b == 0x0D) continue;
            bytes.Add((byte)b);
            if (b % 32 == 31) bytes.AddRange(Encoding.ASCII.GetBytes(newline));
        }

        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "latin1.txt");
        File.WriteAllBytes(path, bytes.ToArray());
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Latin1, ed.OriginalEncoding);
        Assert.Equal(EditorEncodingKind.Latin1, ed.SaveEncoding);
        Assert.Same(LegacyTextEncodings.Latin1, ed.SaveLegacyEncoding);

        AssertRoundTrip(bytes.ToArray());
    }

    public static IEnumerable<object[]> BuiltInLegacyCodecs()
    {
        yield return new object[] { "latin1" };
        yield return new object[] { "cp437" };
        yield return new object[] { "cp852" };
        yield return new object[] { "windows-1250" };
        yield return new object[] { "iso-8859-2" };
        yield return new object[] { "kamenicky" };
    }

    [Theory]
    [MemberData(nameof(BuiltInLegacyCodecs))]
    public void ExplicitLegacyCodec_EveryMappedByte_RoundTrips(string name)
    {
        Assert.True(LegacyTextEncodings.TryGet(name, out var codec));
        var bytes = new List<byte>();
        for (int b = 0; b < 256; b++)
        {
            if (b == 0x0A || b == 0x0D) continue;
            char ch;
            try { ch = codec.DecodeByte((byte)b); }
            catch (Exception ex) when (ex is DecoderFallbackException or InvalidOperationException) { continue; }
            if (ch == '\r' || ch == '\n') continue;
            if (!codec.TryEncodeChar(ch, out byte back) || back != b) continue;
            bytes.Add((byte)b);
            if (b % 16 == 15) bytes.AddRange(new byte[] { 0x0D, 0x0A });
        }
        Assert.True(bytes.Count > 200, $"{name} mapped only {bytes.Count} bytes");

        AssertRoundTrip(bytes.ToArray(), new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Legacy(codec) });
    }

    [Theory]
    [InlineData(false, "a\r\nb\r\n")]
    [InlineData(false, "Příliš 🙂\nlast")]
    [InlineData(true, "a\r\nb\r\n")]
    [InlineData(true, "Příliš 🙂\rlast")]
    public void Utf16WithBom_IsDetectedAndRoundTrips(bool bigEndian, string text)
    {
        var codec = new UnicodeEncoding(bigEndian, byteOrderMark: false);
        byte[] bom = bigEndian ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE };
        byte[] bytes = bom.Concat(codec.GetBytes(text)).ToArray();

        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "utf16.txt");
        File.WriteAllBytes(path, bytes);
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        var expected = bigEndian ? EditorEncodingKind.Utf16BeBom : EditorEncodingKind.Utf16LeBom;
        Assert.Equal(expected, ed.OriginalEncoding);
        Assert.Equal(expected, ed.SaveEncoding);
        Assert.Null(ed.SaveLegacyEncoding);
        Assert.False(ed.HadUtf8Bom);
        Assert.Equal(text.Replace("\r\n", "\n").Replace('\r', '\n'), ReadAll(ed));

        AssertRoundTrip(bytes);
    }

    [Fact]
    public void Utf16BomOnly_RoundTripsAsUtf16()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "utf16-empty.txt");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE });
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Utf16LeBom, ed.OriginalEncoding);
        Assert.Equal(0u, ed.bufLen);

        AssertRoundTrip(new byte[] { 0xFF, 0xFE });
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]               // odd length
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0xD8, 0x41, 0x00 })] // unpaired high surrogate
    public void InvalidUtf16AfterBom_FallsBackToLatin1AndRoundTrips(byte[] bytes)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "bad-utf16.txt");
        File.WriteAllBytes(path, bytes);
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Latin1, ed.OriginalEncoding);

        AssertRoundTrip(bytes);
    }

    [Fact]
    public void ExplicitUtf8_DoesNotDetectUtf16Bom()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "utf16.txt");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x41, 0x00 });

        Assert.Throws<DecoderFallbackException>(() => new TFileEditor(
            new TRect(0, 0, 80, 25), null, null, null, path,
            new TFileEditorOpenOptions { Encoding = EditorTextEncoding.Utf8 }));
    }

    [Fact]
    public void LargeUtf8File_RoundTripsByteIdentically()
    {
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1_100_000; i++)
            sb.Append("line ").Append(i).Append(" Příliš žluťoučký kůň úpěl ďábelské ódy 🙂\r\n");
        byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        Assert.True(bytes.Length > 1_000_000);

        AssertRoundTrip(bytes);
    }

    [Fact]
    public void NewDocument_DefaultsToUtf8WithoutBom()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "new.txt");
        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        Assert.Equal(EditorEncodingKind.Utf8, ed.SaveEncoding);
        Assert.Null(ed.SaveLegacyEncoding);

        ed.InsertText("Příliš");
        Assert.True(ed.SaveFile());
        Assert.Equal(new UTF8Encoding(false).GetBytes("Příliš"), File.ReadAllBytes(path));

        var unnamed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);
        Assert.Equal(EditorEncodingKind.Utf8, unnamed.SaveEncoding);
    }

    // Lead request (TV13b): delCount no longer belongs to the gap tail in SetBufSize.
    [Theory]
    [InlineData(0u)]
    [InlineData(100u)]
    [InlineData(19_000u)]
    public void DeleteLargeSelectionThenGrowBuffer_SaveAndReloadKeepText(uint caretAfterDelete)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "grow.txt");
        var original = new StringBuilder();
        for (int i = 0; original.Length < 40_000; i++)
            original.Append("row ").Append(i).Append('\n');
        File.WriteAllText(path, original.ToString(), new UTF8Encoding(false));

        var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
        string expected = original.ToString();

        ed.SetSelect(50, 20_050, false);
        ed.InsertText(string.Empty);
        expected = expected.Remove(50, 20_000);
        Assert.Equal(expected, ReadAll(ed));

        uint caret = Math.Min(caretAfterDelete, ed.bufLen);
        ed.SetSelect(caret, caret, false);
        string big = new string('x', 70_000) + "\nend";
        ed.InsertText(big);
        expected = expected.Insert((int)caret, big);
        Assert.Equal(expected, ReadAll(ed));

        ed.InsertText("typed");
        expected = expected.Insert((int)caret + big.Length, "typed");
        Assert.Equal(expected, ReadAll(ed));

        Assert.True(ed.SaveFile());
        Assert.Equal(expected, File.ReadAllText(path));
        Assert.True(ed.LoadFile());
        Assert.Equal(expected, ReadAll(ed));
        Assert.True(ed.SaveFile());
        Assert.Equal(expected, File.ReadAllText(path));
    }

    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder((int)ed.bufLen);
        for (uint p = 0; p < ed.bufLen; p++)
            sb.Append(ed.BufChar(p));
        return sb.ToString();
    }
}
