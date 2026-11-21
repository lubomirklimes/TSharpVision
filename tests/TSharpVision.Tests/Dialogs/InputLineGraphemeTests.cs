using System.Globalization;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

/// <summary>
/// TV13o: TInputLine editing keys never split grapheme clusters; the caret, scrolling, selection, mouse and the
/// password mask use visual columns; the legacy profile keeps one column per UTF-16 code unit.
/// </summary>
[Collection("NonParallel")]
public sealed class InputLineGraphemeTests : IDisposable
{
    private const string Emoji = "\U0001F600";
    private const string Combining = "e\u0301";
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string Flag = "\U0001F1E8\U0001F1FF";
    private const string Cjk = "\u6F22";

    private readonly DriverScope _driver = new(80, 25);
    private TestGroup? _host;

    public void Dispose()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Default;
        _driver.Dispose();
    }

    private sealed class ProbeLine : TInputLine
    {
        public ProbeLine(TRect bounds, int maxLen) : base(bounds, maxLen) { }

        public int PositionAt(int x)
        {
            var ev = new TEvent { What = Events.evMouseDown };
            ev.mouse.where = new TPoint(x, 0);
            return MousePos(ev);
        }
    }

    // A selected line holding text with the cursor at its end and nothing selected, on a drawable host.
    private ProbeLine Line(string text, int width = 12, int maxLen = 64)
    {
        _host = new TestGroup(new TRect(0, 0, width, 1)) { buffer = new ScreenBuffer(width) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        var il = new ProbeLine(new TRect(0, 0, width, 1), maxLen + 1);
        _host.Insert(il);
        il.SetState(Views.sfSelected, true);
        il.SetData(text);
        il.SelStart = il.SelEnd = 0;
        return il;
    }

    private static void Key(TInputLine il, ushort keyCode, string? text = null)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        if (text != null) ev.keyDown.text = text;
        il.HandleEvent(ref ev);
    }

    private static List<int> Boundaries(string text)
    {
        var result = new List<int> { 0 };
        for (int i = 0; i < text.Length;)
        {
            i += StringInfo.GetNextTextElementLength(text.AsSpan(i));
            result.Add(i);
        }
        return result;
    }

    private TScreenChar Cell(int x) => _host!.buffer!.Data[x];

    private const string Mixed = "a" + Emoji + Combining + Family + Flag + Cjk + "z";

    [Fact]
    public void ArrowsStepOverWholeClusters()
    {
        TInputLine il = Line(Mixed, width: 40);
        List<int> boundaries = Boundaries(Mixed);
        for (int k = boundaries.Count - 2; k >= 0; k--)
        {
            Key(il, Keys.kbLeft);
            Assert.Equal(boundaries[k], il.CurPos);
        }
        for (int k = 1; k < boundaries.Count; k++)
        {
            Key(il, Keys.kbRight);
            Assert.Equal(boundaries[k], il.CurPos);
        }
    }

    [Fact]
    public void BackspaceAndDeleteRemoveWholeClusters()
    {
        List<int> boundaries = Boundaries(Mixed);
        for (int k = 1; k < boundaries.Count; k++)
        {
            string removed = Mixed.Remove(boundaries[k - 1], boundaries[k] - boundaries[k - 1]);
            TInputLine il = Line(Mixed, width: 40);
            il.CurPos = boundaries[k];
            Key(il, Keys.kbBack);
            Assert.Equal((removed, boundaries[k - 1]), (il.Data, il.CurPos));

            il = Line(Mixed, width: 40);
            il.CurPos = boundaries[k - 1];
            Key(il, Keys.kbDel);
            Assert.Equal((removed, boundaries[k - 1]), (il.Data, il.CurPos));
        }
    }

    [Fact]
    public void OverwriteReplacesOneWholeCluster()
    {
        TInputLine il = Line("a" + Emoji + Combining + "z", width: 40);
        il.SetState(Views.sfCursorIns, true);                           // overwrite mode
        il.CurPos = 1;
        Key(il, 'x', "x");
        Assert.Equal("ax" + Combining + "z", il.Data);
        Key(il, 0, Family);                                             // a typed sequence overwrites one cluster
        Assert.Equal("ax" + Family + "z", il.Data);
        Assert.Equal(2 + Family.Length, il.CurPos);
    }

    [Fact]
    public void ATypedClusterThatDoesNotFitIsNotHalfInserted()
    {
        TInputLine il = Line("ab", maxLen: 3);
        Key(il, 0, Emoji);
        Assert.Equal("ab", il.Data);
        Key(il, 'c', "c");
        Assert.Equal("abc", il.Data);
        il = Line("ab", maxLen: 4);
        Key(il, 0, Emoji);
        Assert.Equal("ab" + Emoji, il.Data);
    }

    [Fact]
    public void WideTextScrollsByVisualColumns()
    {
        TInputLine il = Line("", width: 10);                            // 8 text columns
        for (int i = 0; i < 10; i++) Key(il, 0, Cjk);
        Assert.Equal(10, il.CurPos);
        Assert.Equal(6, il.FirstPos);                                   // four wide characters fill the 8 columns
        il.Draw();
        Assert.Equal(9, il.cursor.x);
        Assert.Equal('<', Cell(0).Character);
        for (int x = 1; x < 9; x += 2)
        {
            Assert.Equal((TScreenCellKind.Lead, Cjk), (Cell(x).Kind, Cell(x).GetText()));
            Assert.Equal(TScreenCellKind.Trail, Cell(x + 1).Kind);
        }
        Assert.False(il.CanScroll(1));
        Key(il, Keys.kbHome);
        Assert.Equal(0, il.FirstPos);
        Assert.True(il.CanScroll(1));
        il.Draw();
        Assert.Equal(1, il.cursor.x);
        Assert.Equal('>', Cell(9).Character);
    }

    [Fact]
    public void AWideCharacterThatDoesNotFitAtTheRightEdgeIsBlank()
    {
        TInputLine il = Line("abc" + Cjk, width: 6);                    // 4 text columns
        Key(il, Keys.kbHome);
        il.Draw();
        Assert.Equal("abc", string.Concat(Cell(1).Character, Cell(2).Character, Cell(3).Character));
        Assert.Equal((' ', TScreenCellKind.Single), (Cell(4).Character, Cell(4).Kind));
        Assert.Equal('>', Cell(5).Character);
        Key(il, Keys.kbEnd);
        Assert.Equal(1, il.FirstPos);                                   // "bc" + the wide character: 4 columns
        il.Draw();
        Assert.Equal(5, il.cursor.x);
    }

    [Fact]
    public void TheMouseOnTheSecondCellOfAWideCharacterGivesItsStart()
    {
        ProbeLine il = Line("a" + Cjk + "b" + Emoji + "c", width: 20);
        Key(il, Keys.kbHome);
        int[] expected = { 0, 0, 1, 1, 2, 3, 3, 5, 6, 6 };              // cells 0..9 (cell 0 is the left arrow)
        for (int x = 0; x < expected.Length; x++) Assert.Equal(expected[x], il.PositionAt(x));
    }

    [Fact]
    public void ThePasswordMaskShowsOneCharacterPerCluster()
    {
        TInputLine il = Line("a" + Emoji + Combining + Family, width: 20);
        il.PasswordChar = '*';
        il.Draw();
        for (int x = 1; x <= 4; x++) Assert.Equal('*', Cell(x).Character);
        Assert.Equal(' ', Cell(5).Character);
        Assert.Equal(5, il.cursor.x);
        Key(il, Keys.kbLeft);
        il.Draw();
        Assert.Equal(4, il.cursor.x);
        Assert.Equal("a" + Emoji + Combining + Family, il.Data);       // the value is untouched

        il.PasswordChar = Cjk[0];                                       // a wide mask: two columns a cluster
        il.Draw();
        Assert.Equal((TScreenCellKind.Lead, TScreenCellKind.Trail), (Cell(1).Kind, Cell(2).Kind));
        Assert.Equal(7, il.cursor.x);
    }

    [Fact]
    public void TheSelectionCoversVisualColumns()
    {
        TInputLine il = Line(Cjk + "b" + Emoji, width: 20);
        Key(il, Keys.kbHome);
        il.SelStart = 0;
        il.SelEnd = 1;
        il.Draw();
        byte selected = (byte)il.GetColor(3);
        Assert.Equal(selected, (byte)Cell(1).Attr);
        Assert.Equal(selected, (byte)Cell(2).Attr);
        Assert.NotEqual(selected, (byte)Cell(3).Attr);
    }

    [Fact]
    public void TheLegacyProfileKeepsOneCellPerCodeUnit()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        TInputLine il = Line("a" + Emoji + "b", width: 20);
        Key(il, Keys.kbHome);
        il.Draw();
        Assert.Equal(('a', '\uD83D', '\uDE00', 'b'), (Cell(1).Character, Cell(2).Character, Cell(3).Character, Cell(4).Character));
        Key(il, Keys.kbRight);
        Key(il, Keys.kbRight);                                          // still one step over the pair
        Assert.Equal(3, il.CurPos);
        il.Draw();
        Assert.Equal(4, il.cursor.x);
        il.PasswordChar = '*';
        il.Draw();
        for (int x = 1; x <= 4; x++) Assert.Equal('*', Cell(x).Character);
        Assert.Equal(' ', Cell(5).Character);
    }

    [Fact]
    public void DataMaxLenAndValidationAreUnchanged()
    {
        TInputLine il = Line("", maxLen: 5);
        Assert.Equal(6, il.DataSize());
        il.SetData("ab" + Cjk + Emoji + "cd");                          // cut at MaxLen code units, as before
        Assert.Equal(("ab" + Cjk + Emoji).Length, il.Data.Length);
        object rec = string.Empty;
        il.GetData(ref rec);
        Assert.Equal(il.Data, rec);
        Assert.True(il.Valid(Views.cmOK));
    }
}
