using System.Text;
using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

public sealed class EditorLineEndingTests
{
    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < ed.bufLen; p++) sb.Append((char)ed.BufChar(p));
        return sb.ToString();
    }

    private static string CreateBinaryFile(TempDirectory temp, string name, string content)
    {
        string path = System.IO.Path.Combine(temp.Path, name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(content));
        return path;
    }

    [Fact]
    public void LoadFile_CrLf_NormalizesBufferToLf()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "crlf.txt", "a\r\nb\r\n");

        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal("a\nb\n", ReadAll(ed));
        Assert.Equal(LineEndingKind.CrLf, ed.OriginalLineEnding);
        Assert.Equal(LineEndingKind.CrLf, ed.SaveLineEnding);
        Assert.False(ed.HadMixedLineEndings);
    }

    [Fact]
    public void LoadFile_MissingFile_UsesPlatformDefaultForNewFile()
    {
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "new.txt");

        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal("", ReadAll(ed));
        Assert.Equal(LineEndingKind.Unknown, ed.OriginalLineEnding);
        Assert.Equal(
            OperatingSystem.IsWindows() ? LineEndingKind.CrLf : LineEndingKind.Lf,
            ed.SaveLineEnding);
        Assert.False(ed.HadMixedLineEndings);
    }

    [Fact]
    public void SaveFile_CrLf_PreservesCrLfOnDisk()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "crlf.txt", "a\r\nb\r\n");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.True(ed.SaveFile());

        Assert.Equal("a\r\nb\r\n", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LoadFile_Lf_PreservesLfSaveStyle()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "lf.txt", "a\nb\n");

        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal("a\nb\n", ReadAll(ed));
        Assert.Equal(LineEndingKind.Lf, ed.OriginalLineEnding);
        Assert.Equal(LineEndingKind.Lf, ed.SaveLineEnding);
    }

    [Fact]
    public void SaveFile_Lf_PreservesLfOnDisk()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "lf.txt", "a\nb\n");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.True(ed.SaveFile());

        Assert.Equal("a\nb\n", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LoadFile_Cr_NormalizesBufferToLfAndPreservesCrSaveStyle()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "cr.txt", "a\rb\r");

        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal("a\nb\n", ReadAll(ed));
        Assert.Equal(LineEndingKind.Cr, ed.OriginalLineEnding);
        Assert.Equal(LineEndingKind.Cr, ed.SaveLineEnding);
    }

    [Fact]
    public void SaveFile_Cr_PreservesCrOnDisk()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "cr.txt", "a\rb\r");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.True(ed.SaveFile());

        Assert.Equal("a\rb\r", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LoadFile_Mixed_NormalizesAndChoosesDominantSaveStyle()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "mixed.txt", "a\r\nb\nc\r\nd");

        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal("a\nb\nc\nd", ReadAll(ed));
        Assert.Equal(LineEndingKind.Mixed, ed.OriginalLineEnding);
        Assert.Equal(LineEndingKind.CrLf, ed.SaveLineEnding);
        Assert.True(ed.HadMixedLineEndings);
    }

    [Fact]
    public void SaveFile_Mixed_WritesDominantStyle()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "mixed.txt", "a\r\nb\nc\r\nd");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.True(ed.SaveFile());

        Assert.Equal("a\r\nb\r\nc\r\nd", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LoadFile_CrLf_LineMoveTreatsCrLfAsOneLineEnding()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "crlf.txt", "a\r\nb\r\n");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal(2u, ed.LineMove(0, 1));
    }

    [Fact]
    public void LoadFile_CrLf_GetMousePtrUsesNormalizedRows()
    {
        using var temp = new TempDirectory();
        string path = CreateBinaryFile(temp, "crlf.txt", "abc\r\ndef\r\n");
        var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

        Assert.Equal(5u, ed.GetMousePtr(new TPoint(1, 1)));
    }
}
