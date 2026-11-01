using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TFileEditor.SaveAs / SaveFile(EditorTextEncoding) write the selected output encoding, strictly and safely.
[Collection("NonParallel")]
public sealed class TFileEditorSaveAsEncodingTests : IDisposable
{
    private const string Czech = "Příliš žluťoučký kůň úpěl ďábelské ódy";
    private readonly TempDirectory _temp = new();
    private readonly TEditor.TEditorDialog _dialog = TEditor.editorDialog;
    private readonly ushort _flags = TEditor.editorFlags;
    private readonly List<int> _prompts = new();

    public TFileEditorSaveAsEncodingTests() => TEditor.editorFlags &= unchecked((ushort)~Views.efBackupFiles);

    public void Dispose()
    {
        TEditor.editorDialog = _dialog;
        TEditor.editorFlags = _flags;
        _temp.Dispose();
    }

    private string PathOf(string name) => Path.Combine(_temp.Path, name);

    // Answers edSaveAs like an accepting chooser: sets the name and, when given, the selected encoding.
    private void AnswerSaveAs(string? path, EditorTextEncoding? encoding)
    {
        TEditor.editorDialog = (dialog, info) =>
        {
            _prompts.Add(dialog);
            if (dialog != Views.edSaveAs) return Views.cmOK;
            if (path == null) return Views.cmCancel;
            var fe = Assert.IsAssignableFrom<TFileEditor>(info);
            Assert.Null(fe.SaveAsEncoding); // SaveAs clears any stale request before asking.
            fe.fileName = path;
            fe.SaveAsEncoding = encoding;
            return Views.cmOK;
        };
    }

    private static TFileEditor NewEditor(string? path = null, EditorTextEncoding? open = null) =>
        new(new TRect(0, 0, 60, 10), null, null, null, path,
            open == null ? null : new TFileEditorOpenOptions { Encoding = open });

    private static string Text(TEditor editor)
        => new(Enumerable.Range(0, (int)editor.bufLen).Select(i => editor.BufChar((uint)i)).ToArray());

    private static byte[] Expected(TFileEditor editor, Func<string, byte[]> encode)
        => encode(Text(editor).Replace("\n", editor.SaveLineEnding == LineEndingKind.Lf ? "\n" : "\r\n"));

    public static IEnumerable<object[]> Encodings() => new[]
    {
        new object[] { "UTF-8" }, new object[] { "Latin-1" }, new object[] { "CP437" }, new object[] { "CP852" },
        new object[] { "Windows-1250" }, new object[] { "ISO-8859-2" }, new object[] { "Kamenicky / KEYBCS2" },
    };

    private static EditorTextEncoding Choice(string label) =>
        EditorEncodingChoices.BuiltIn.Single(c => c.Fallback == label).Encoding;

    private static Func<string, byte[]> Encoder(EditorTextEncoding encoding) =>
        encoding.LegacyEncoding is { } legacy ? legacy.Encode : Encoding.UTF8.GetBytes;

    [Theory]
    [MemberData(nameof(Encodings))]
    public void SaveAsWritesExactBytesAndTheNextSaveKeepsTheEncoding(string label)
    {
        var encoding = Choice(label);
        string text = label is "Latin-1" or "CP437" ? "café naïve" : Czech;
        var editor = NewEditor();
        editor.InsertText(text + "\n");
        AnswerSaveAs(PathOf("out.txt"), encoding);
        Assert.True(editor.SaveAs());
        Assert.Equal(Expected(editor, Encoder(encoding)), File.ReadAllBytes(PathOf("out.txt")));
        Assert.False(editor.modified);
        Assert.Null(editor.SaveAsEncoding);
        Assert.Same(encoding.LegacyEncoding, editor.OriginalLegacyEncoding);

        editor.InsertText("x");
        Assert.True(editor.Save());
        Assert.Equal(Expected(editor, Encoder(encoding)), File.ReadAllBytes(PathOf("out.txt")));

        // Reopening with the same encoding round-trips the text.
        var reopened = NewEditor(PathOf("out.txt"), encoding);
        Assert.Equal(Text(editor), Text(reopened));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Auto")]
    public void AutoOrNoChoiceKeepsTheLoadedEncoding(string? label)
    {
        File.WriteAllBytes(PathOf("in.txt"), LegacyTextEncodings.Cp852.Encode(Czech + "\r\n"));
        var editor = NewEditor(PathOf("in.txt"), EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852));
        AnswerSaveAs(PathOf("copy.txt"), label == null ? null : Choice(label));
        Assert.True(editor.SaveAs());
        Assert.Equal(File.ReadAllBytes(PathOf("in.txt")), File.ReadAllBytes(PathOf("copy.txt")));
        Assert.Same(LegacyTextEncodings.Cp852, editor.OriginalLegacyEncoding);
    }

    [Fact]
    public void ConvertingBetweenEncodingsChangesTheBytesOfLaterSaves()
    {
        File.WriteAllBytes(PathOf("doc.txt"), LegacyTextEncodings.Windows1250.Encode(Czech + "\r\n"));
        var editor = NewEditor(PathOf("doc.txt"), EditorTextEncoding.Legacy(LegacyTextEncodings.Windows1250));
        AnswerSaveAs(PathOf("doc.txt"), Choice("CP852"));
        Assert.True(editor.SaveAs());
        Assert.Equal(LegacyTextEncodings.Cp852.Encode(Czech + "\r\n"), File.ReadAllBytes(PathOf("doc.txt")));
        AnswerSaveAs(PathOf("doc.txt"), EditorTextEncoding.Utf8);
        Assert.True(editor.SaveAs());
        Assert.Equal(Encoding.UTF8.GetBytes(Czech + "\r\n"), File.ReadAllBytes(PathOf("doc.txt")));
        Assert.Equal(EditorEncodingKind.Utf8, editor.OriginalEncoding);
        Assert.Null(editor.OriginalLegacyEncoding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrepresentableTextWritesNothingAndKeepsTheDocument(bool targetExists)
    {
        byte[] original = Encoding.UTF8.GetBytes("previous target");
        if (targetExists) File.WriteAllBytes(PathOf("t.txt"), original);
        File.WriteAllBytes(PathOf("src.txt"), Encoding.UTF8.GetBytes(Czech + " 日本\r\n"));
        byte[] source = File.ReadAllBytes(PathOf("src.txt"));
        var editor = NewEditor(PathOf("src.txt"));
        editor.InsertText("!");
        string text = Text(editor);
        AnswerSaveAs(PathOf("t.txt"), Choice("CP852"));

        Assert.False(editor.SaveAs());

        Assert.Equal(new[] { Views.edSaveAs, Views.edEncodingWriteError }, _prompts);
        Assert.Equal(PathOf("src.txt"), editor.fileName);
        Assert.True(editor.modified);
        Assert.Equal(text, Text(editor));
        Assert.Equal(EditorEncodingKind.Utf8, editor.OriginalEncoding);
        Assert.Null(editor.SaveAsEncoding);
        Assert.Equal(source, File.ReadAllBytes(PathOf("src.txt")));
        if (targetExists) Assert.Equal(original, File.ReadAllBytes(PathOf("t.txt")));
        else Assert.False(File.Exists(PathOf("t.txt")));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }

    // An auto-detected Latin-1 document used to save 'č' as '?', and UTF-8 a lone surrogate as U+FFFD. The surrogate is
    // built here: xUnit would replace it if it were passed as test data.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDocumentsOwnEncodingIsStrictToo(bool latin)
    {
        string character = latin ? "\u010d" : new string((char)0xD800, 1);
        File.WriteAllBytes(PathOf("doc.txt"), latin ? new byte[] { 0x63, 0xE9, 0x0D, 0x0A } : Encoding.UTF8.GetBytes("ok\r\n"));
        byte[] before = File.ReadAllBytes(PathOf("doc.txt"));
        var editor = NewEditor(PathOf("doc.txt")); // Auto: invalid UTF-8 falls back to Latin-1
        Assert.Equal(latin ? EditorEncodingKind.Latin1 : EditorEncodingKind.Utf8, editor.OriginalEncoding);
        editor.InsertText(character);
        AnswerSaveAs(null, null);
        Assert.False(editor.Save());
        Assert.Equal(new[] { Views.edEncodingWriteError }, _prompts);
        Assert.Equal(before, File.ReadAllBytes(PathOf("doc.txt")));
        Assert.True(editor.modified);
    }
    [Fact]
    public void CancelLeavesNameEncodingAndDiskUnchanged()
    {
        var editor = NewEditor();
        editor.InsertText(Czech);
        AnswerSaveAs(null, Choice("CP852"));
        Assert.False(editor.SaveAs());
        Assert.Empty(editor.fileName);
        Assert.True(editor.modified);
        Assert.Equal(EditorEncodingKind.Utf8, editor.OriginalEncoding);
        Assert.Null(editor.SaveAsEncoding);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void FailedWriteRestoresTheNameAndEncoding()
    {
        var editor = NewEditor();
        editor.InsertText(Czech);
        string unreachable = Path.Combine(_temp.Path, "missing-dir", "x.txt");
        AnswerSaveAs(unreachable, Choice("CP852"));
        Assert.False(editor.SaveAs());
        Assert.Equal(new[] { Views.edSaveAs, Views.edCreateError }, _prompts);
        Assert.Empty(editor.fileName);
        Assert.True(editor.modified);
        Assert.Null(editor.OriginalLegacyEncoding);
        Assert.False(File.Exists(unreachable));
    }

    [Fact]
    public void Utf8ByteOrderMarkIsKeptForUtf8AndDroppedForLegacy()
    {
        File.WriteAllBytes(PathOf("bom.txt"), new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Czech + "\r\n")).ToArray());
        var editor = NewEditor(PathOf("bom.txt"));
        Assert.Equal(EditorEncodingKind.Utf8Bom, editor.OriginalEncoding);
        byte[] withBom = File.ReadAllBytes(PathOf("bom.txt"));

        AnswerSaveAs(PathOf("utf8.txt"), EditorTextEncoding.Utf8);
        Assert.True(editor.SaveAs());
        Assert.Equal(withBom, File.ReadAllBytes(PathOf("utf8.txt")));

        AnswerSaveAs(PathOf("cp852.txt"), Choice("CP852"));
        Assert.True(editor.SaveAs());
        Assert.Equal(LegacyTextEncodings.Cp852.Encode(Czech + "\r\n"), File.ReadAllBytes(PathOf("cp852.txt")));
        Assert.False(editor.HadUtf8Bom);

        // From a legacy document, UTF-8 is written without a byte-order mark.
        AnswerSaveAs(PathOf("plain.txt"), EditorTextEncoding.Utf8);
        Assert.True(editor.SaveAs());
        Assert.Equal(Encoding.UTF8.GetBytes(Czech + "\r\n"), File.ReadAllBytes(PathOf("plain.txt")));
    }

    [Fact]
    public void SaveFileOverloadConvertsWithoutTheDialog()
    {
        File.WriteAllBytes(PathOf("d.txt"), Encoding.UTF8.GetBytes(Czech + "\n"));
        var editor = NewEditor(PathOf("d.txt"));
        AnswerSaveAs(null, null);
        Assert.True(editor.SaveFile(EditorTextEncoding.Legacy(LegacyTextEncodings.Iso8859_2)));
        Assert.Equal(LegacyTextEncodings.Iso8859_2.Encode(Czech + "\n"), File.ReadAllBytes(PathOf("d.txt")));
        Assert.Empty(_prompts);
    }
}
