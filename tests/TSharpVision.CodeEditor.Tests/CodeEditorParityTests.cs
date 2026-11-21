using System.Text;
using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// TV13f: <see cref="TCodeEditor"/> (with and without a classifier) draws exactly the characters a plain
/// <see cref="TEditor"/> draws for the same text, tab size, selection and horizontal scroll; attributes differ
/// only where a syntax colour applies.
/// </summary>
public sealed class CodeEditorParityTests
{
    private const int Width = 40;
    private const int Height = 6;

    public static IEnumerable<object[]> Cases()
    {
        foreach (string kind in new[] { "plain", "tabs", "cr" })
        foreach (uint tabSize in new uint[] { 0, 1, 3, 8 })
        foreach (int column in new[] { 0, 300, 100000 })
        foreach (bool classify in new[] { false, true })
            yield return new object[] { kind, tabSize, column, classify };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCodeEditorDrawsTheSameCharactersAsAPlainEditor(string kind, uint tabSize, int column, bool classify)
    {
        uint savedTabSize = TEditor.tabSize;
        TEditor.tabSize = tabSize;
        try
        {
            string unit = kind switch
            {
                "plain" => "word if 7 ",
                "tabs" => "a\tif 7\tbc\t",
                _ => "x\rif 7\r\t",
            };

            string text = Document(unit, column + (Width * 3));
            using var pair = new ParityPair(text, classify);

            // A selection that crosses the visible window of the first line and ends on the second.
            uint firstLineEnd = pair.Plain.LineEnd(0);
            uint selectionStart = pair.Plain.CharPtr(0, column + 5);
            uint selectionEnd = Math.Min(pair.Plain.NextLine(0) + 3, pair.Plain.bufLen);
            Assert.True(selectionStart < firstLineEnd);
            pair.Select(selectionStart, selectionEnd);
            pair.ScrollTo(column);
            pair.Draw();

            pair.AssertParity(classify);
        }
        finally
        {
            TEditor.tabSize = savedTabSize;
        }
    }

    // TV13o: wide clusters (CJK, emoji, flags, ZWJ sequences) as lead and trail cells, combining sequences as one
    // cell, tabs after them, a selection and every horizontal scroll position, including ones that cut a wide
    // cluster at the left or right window edge, draw identically in both editors with and without highlighting.
    public static IEnumerable<object[]> WideCases()
    {
        string[] units =
        {
            "\u6F22\u5B57 if 7 ",
            "\U0001F600x\U0001F1E8\U0001F1FF if ",
            "e\u0301\u0302 \U0001F468\u200D\U0001F469\u200D\U0001F467\t7 ",
            "\uFF21\t\u4E00\u0301 if\r",
        };
        foreach (string unit in units)
        foreach (int column in new[] { 0, 1, 2, 3, 37, 38, 39, 41, 1000 })
        foreach (bool classify in new[] { false, true })
            yield return new object[] { unit, column, classify };
    }

    [Theory]
    [MemberData(nameof(WideCases))]
    public void WideClustersDrawIdenticallyInBothEditorsAtEveryScroll(string unit, int column, bool classify)
    {
        string text = Document(unit, 1500);
        using var pair = new ParityPair(text, classify);
        uint selectionStart = pair.Plain.CharPtr(0, column + 3);
        uint selectionEnd = Math.Min(pair.Plain.NextLine(0) + 5, pair.Plain.bufLen);
        pair.Select(selectionStart, selectionEnd);
        pair.ScrollTo(column);
        pair.Draw();
        pair.AssertParity(classify);

        // No half glyph: every lead's trails follow it and no trail is without its lead.
        Span<TScreenChar> cells = pair.PlainHost.buffer!.Data;
        for (int y = 0; y < Height; y++)
        {
            int expectTrails = 0;
            for (int x = 0; x < Width; x++)
            {
                TScreenChar cell = cells[(y * Width) + x];
                if (expectTrails > 0) { Assert.Equal(TScreenCellKind.Trail, cell.Kind); expectTrails--; continue; }
                Assert.NotEqual(TScreenCellKind.Trail, cell.Kind);
                if (cell.Kind == TScreenCellKind.Lead) expectTrails = cell.CellWidth - 1;
            }
            Assert.Equal(0, expectTrails);
        }
    }

    // Supplementary characters, combining sequences and ZWJ emoji draw as clusters (TV13o: lead and trail cells, no
    // longer one cell per UTF-16 code unit) in both editors, and the spans after them stay on their characters.

    [Theory]
    [InlineData("\U0001F600 if 7")]
    [InlineData("e\u0301 if 7")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 if 7")]
    [InlineData("\U0001F600\t\u0301\r if 7\n\U0001F468\u200D\U0001F469 7")]
    public void NonBmpCombiningAndZwjTextDrawsLikeAPlainEditorWithAlignedSpans(string text)
    {
        using var pair = new ParityPair(text, classify: true);
        pair.Draw();
        pair.AssertParity(classify: true);

        string first = text.Split('\n')[0];
        int keyword = first.IndexOf("if", StringComparison.Ordinal);
        int x = pair.Code.Editor.CharPos(0, (uint)keyword);
        ushort normal = ParityPair.Normal(pair.Code.Editor);
        Assert.Equal('i', pair.Code.Host.buffer!.Data[x].Character);
        Assert.Equal(SyntaxColorScheme.Default.Apply(SyntaxClass.Keyword, normal), (ushort)(byte)pair.Code.Host.buffer.Data[x].Attr);
        Assert.Equal(SyntaxColorScheme.Default.Apply(SyntaxClass.Number, normal), (ushort)(byte)pair.Code.Host.buffer.Data[x + 3].Attr);
        Assert.Equal(normal, (ushort)(byte)pair.Code.Host.buffer.Data[0].Attr);
    }

    /// <summary>Three lines of <paramref name="unit"/> repeated to at least <paramref name="length"/> code units, a short line and an empty one.</summary>
    internal static string Document(string unit, int length)
    {
        var line = new StringBuilder(length + unit.Length);
        while (line.Length < length) line.Append(unit);
        string first = line.ToString();
        return string.Join("\n", first, "if 1" + first, "short 2\tif", string.Empty, first[..(length / 2)] + " 9");
    }

    /// <summary>A code editor and a plain <see cref="TEditor"/> over the same text, each on its own screen buffer.</summary>
    internal sealed class ParityPair : IDisposable
    {
        public ParityPair(string text, bool classify)
        {
            Code = new EditorScreen(Width, Height);
            Code.Load(text);
            if (classify) Code.Syntax.SetClassifier(new ToyClassifier());
            else Code.Syntax.SetClassifier(null);
            EditorScreen.Pump();

            PlainHost = new TestGroup(new TRect(0, 0, Width, Height)) { buffer = new ScreenBuffer(Width * Height) };
            PlainHost.state |= (ushort)(Views.sfVisible | Views.sfExposed);
            Plain = new TEditor(new TRect(0, 0, Width, Height), null, null, null, (uint)text.Length + 64);
            PlainHost.Insert(Plain);
            Plain.InsertText(text, false);
            Plain.SetCurPtr(0, 0);
        }

        public EditorScreen Code { get; }

        public TestGroup PlainHost { get; }

        public TEditor Plain { get; }

        public static ushort Normal(TEditor editor) => (byte)editor.GetColor(0x0201);

        public static ushort Selected(TEditor editor) => (byte)(editor.GetColor(0x0201) >> 8);

        public void Select(uint start, uint end)
        {
            Code.Editor.SetSelect(start, end, false);
            Plain.SetSelect(start, end, false);
        }

        /// <summary>Scrolls both editors to <paramref name="column"/>, past TEditor's own extent cap if need be.</summary>
        public void ScrollTo(int column)
        {
            foreach (TEditor editor in new TEditor[] { Code.Editor, Plain })
            {
                editor.limit.x = Math.Max(editor.limit.x, column + Width + 1);
                editor.delta.x = column;
            }
        }

        public void Draw()
        {
            Code.Draw();
            Plain.Draw();
        }

        public void AssertParity(bool classify)
        {
            Span<TScreenChar> code = Code.Host.buffer!.Data;
            Span<TScreenChar> plain = PlainHost.buffer!.Data;
            ushort normal = Normal(Plain);
            ushort selected = Selected(Plain);
            Assert.Equal(normal, Normal(Code.Editor));
            Assert.Equal(selected, Selected(Code.Editor));

            for (int y = 0; y < Height; y++)
            {
                Assert.Equal(Row(plain, y), Row(code, y));
                for (int x = 0; x < Width; x++)
                {
                    // Cell for cell the same cluster structure (lead, trail, width) and text.
                    TScreenChar plainCell = plain[(y * Width) + x], codeCell = code[(y * Width) + x];
                    Assert.True(plainCell.Kind == codeCell.Kind && plainCell.CellWidth == codeCell.CellWidth
                                && plainCell.GetText() == codeCell.GetText(), $"cell at ({x},{y})");
                    ushort plainAttr = (byte)plain[(y * Width) + x].Attr;
                    ushort codeAttr = (byte)code[(y * Width) + x].Attr;
                    if (!classify || plainAttr != normal)
                        Assert.True(plainAttr == codeAttr, $"attribute at ({x},{y}): plain {plainAttr:X2}, code {codeAttr:X2}");
                }
            }
        }

        private static string Row(Span<TScreenChar> cells, int y) => EditorScreen.RowText(cells.Slice(y * Width, Width));

        public void Dispose()
        {
            Code.Dispose();
        }
    }
}
