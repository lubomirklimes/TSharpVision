using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

public sealed class EditorUnicodeCharBufferTests
{
    private static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < ed.bufLen; p++)
            sb.Append(ed.BufChar(p));
        return sb.ToString();
    }

    [Fact]
    public void LoadFile_Utf8Bom_StripsBomAndPreservesMetadata()
    {
        using var scope = new EditorClipboardScope();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat(Encoding.UTF8.GetBytes("Příliš žluťoučký\n")).ToArray());

            var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

            Assert.True(ed.isValid);
            Assert.Equal("Příliš žluťoučký\n", ReadAll(ed));
            Assert.Equal(EditorEncodingKind.Utf8Bom, ed.OriginalEncoding);
            Assert.True(ed.HadUtf8Bom);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void LoadFile_Utf8Cyrillic_StoresOneCharPerBmpCharacter()
    {
        using var scope = new EditorClipboardScope();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "Привет\n", new UTF8Encoding(false));

            var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);

            Assert.True(ed.isValid);
            Assert.Equal("Привет\n", ReadAll(ed));
            Assert.Equal(7u, ed.bufLen);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void SaveFile_Utf8Unicode_PreservesBmpCharacters()
    {
        using var scope = new EditorClipboardScope();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "start\n", new UTF8Encoding(false));
            var ed = new TFileEditor(new TRect(0, 0, 40, 10), null, null, null, path);
            ed.SetSelect(0, ed.bufLen, false);
            ed.InsertText("Příliš Привет\n");

            Assert.True(ed.SaveFile());

            Assert.Equal("Příliš Привет\n", File.ReadAllText(path, Encoding.UTF8));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void HandleEvent_UnicodeTextPayload_InsertsChars()
    {
        using var scope = new EditorClipboardScope();
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 1024);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.text = "čЯ";

        ed.HandleEvent(ref ev);

        Assert.Equal("čЯ", ReadAll(ed));
        Assert.Equal(2u, ed.bufLen);
    }
}
