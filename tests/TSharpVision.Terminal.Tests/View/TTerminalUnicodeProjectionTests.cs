using System.Reflection;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Terminal.Tests.View;

/// <summary>
/// TV13p: <see cref="TTerminal"/> projects the emulator's lead/continuation/cluster cells into core cluster cells
/// (<see cref="TScreenChar.CreateCluster"/> leads plus trails), so supplementary and combining text survives drawing.
/// </summary>
[Collection("TerminalView")]
public sealed class TTerminalUnicodeProjectionTests : IDisposable
{
    private const int HostWidth = 40;
    private readonly DriverScope _driver = new(HostWidth, 12);
    private readonly TestGroup _host;
    private readonly TTerminal _terminal;
    private readonly InMemoryTerminalSession _session = new();

    public TTerminalUnicodeProjectionTests()
    {
        TEventQueue.ClearPosted();
        _host = new TestGroup(new TRect(0, 0, HostWidth, 12)) { buffer = new ScreenBuffer(HostWidth * 12) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        _terminal = new TTerminal(new TRect(0, 0, 30, 5), 100) { InputMode = TerminalInputMode.RawSession };
        _host.Insert(_terminal);
        TEventQueue.ClaimUiThread();
        _session.StartAsync();
        _terminal.AttachSession(_session);
    }

    public void Dispose()
    {
        _terminal.DetachSession();
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    private TScreenChar Cell(int x, int y) => _host.buffer!.Data[y * HostWidth + x];

    // The row's text as the core cells carry it: each lead's whole cluster, trails skipped.
    private string RowText(int y, int width = 30)
    {
        var text = new StringBuilder();
        for (int x = 0; x < width; x++)
        {
            TScreenChar cell = Cell(x, y);
            if (cell.Kind != TScreenCellKind.Trail) text.Append(cell.GetText());
        }

        return text.ToString().TrimEnd();
    }

    private (TScreenCellKind Kind, int Width, string Text) Shape(int x, int y = 0)
        => (Cell(x, y).Kind, Cell(x, y).CellWidth, Cell(x, y).GetText());

    private static void WithProfile(TextWidthProfile profile, Action action)
    {
        PropertyInfo seam = typeof(TextLayout).GetProperty("ProfileOverride", BindingFlags.NonPublic | BindingFlags.Static)
                            ?? throw new InvalidOperationException("TextLayout.ProfileOverride test seam not found");
        seam.SetValue(null, profile);
        try { action(); }
        finally { seam.SetValue(null, TextWidthProfile.Default); }
    }

    [Fact]
    public void MixedEmojiSampleKeepsEveryClusterAndTheEmulatorsColumns()
    {
        const string sample = "A\U0001F600B\U0001F44D\U0001F3FDC\U0001F1E8\U0001F1FFD\U0001F468\u200D\U0001F469\u200D\U0001F467E";
        _session.Emit(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(sample)));

        Assert.Equal(sample, RowText(0));
        Assert.Equal((TScreenCellKind.Single, 1, "A"), Shape(0));
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F600"), Shape(1));
        Assert.Equal(TScreenCellKind.Trail, Cell(2, 0).Kind);
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F44D"), Shape(4));     // per-code-point: the modifier is its own cell
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F3FD"), Shape(6));
        Assert.Equal((TScreenCellKind.Lead, 1, "\U0001F1E8"), Shape(9));     // regional indicators: one column each
        Assert.Equal((TScreenCellKind.Lead, 1, "\U0001F1FF"), Shape(10));
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F468\u200D"), Shape(12));   // the ZWJ joins the preceding cell
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F469\u200D"), Shape(14));
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F467"), Shape(16));
        Assert.Equal((TScreenCellKind.Single, 1, "E"), Shape(18));
        lock (_terminal.SyncRoot) Assert.Equal(19, _terminal.Emulator.CursorColumn);
        Assert.Equal(sample, _terminal.GetText());
    }

    [Fact]
    public void CombiningSequenceIsOneCellLeadWithTheBaseAsFallback()
    {
        _session.Emit("e\u0301x");
        Assert.Equal((TScreenCellKind.Lead, 1, "e\u0301"), Shape(0));
        Assert.Equal('e', Cell(0, 0).Character);
        Assert.Equal('x', Cell(1, 0).Character);
    }

    [Fact]
    public void CjkLeadsAndTrails()
    {
        _session.Emit("\u6F22\u5B57!");
        Assert.Equal((TScreenCellKind.Lead, 2, "\u6F22"), Shape(0));
        Assert.Equal(TScreenCellKind.Trail, Cell(1, 0).Kind);
        Assert.Equal((TScreenCellKind.Lead, 2, "\u5B57"), Shape(2));
        Assert.Equal(TScreenCellKind.Trail, Cell(3, 0).Kind);
        Assert.Equal('!', Cell(4, 0).Character);
        Assert.Equal("\u6F22\u5B57!", RowText(0));
    }

    [Fact]
    public void ColoursAndTheCursorStayPerCell()
    {
        _session.Emit("\x1b[31m\u6F22\x1b[0m");
        Assert.Equal((byte)0x04, (byte)Cell(0, 0).Attr);
        Assert.Equal((byte)0x04, (byte)Cell(1, 0).Attr);   // the trail carries the cell's style too
        Assert.Equal((byte)0x70, (byte)Cell(2, 0).Attr);   // the cursor, after the wide character
    }

    [Fact]
    public void SelectionOverWideCellsHighlightsLeadAndTrailAndCopiesTheCluster()
    {
        _session.Emit("a\U0001F600\u6F22b\x1b[?25l");
        _terminal.SetSelection(new TerminalTextPosition(0, 1), new TerminalTextPosition(0, 5));
        for (int x = 1; x < 5; x++) Assert.Equal((byte)0x30, (byte)Cell(x, 0).Attr);
        Assert.Equal((byte)0x07, (byte)Cell(0, 0).Attr);
        Assert.Equal((byte)0x07, (byte)Cell(5, 0).Attr);
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F600"), Shape(1));   // highlighting keeps the cluster
        Assert.Equal("\U0001F600\u6F22", _terminal.GetSelectedText());
    }

    [Fact]
    public void ScrollbackRowsKeepTheirClusters()
    {
        _session.Emit("\U0001F600 e\u0301 \u6F22\r\n");
        for (int i = 0; i < 10; i++) _session.Emit($"line {i}\r\n");
        _terminal.ScrollToTop();
        Assert.Equal("\U0001F600 e\u0301 \u6F22", RowText(0));
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F600"), Shape(0));
    }

    [Fact]
    public void AWideCellCutByTheViewEdgeDrawsBlank()
    {
        _terminal.ChangeBounds(new TRect(0, 0, 6, 3));
        _session.Emit("abcd\u6F22\r\n");                       // fits: the lead at column 4, its half at 5
        Assert.Equal((TScreenCellKind.Lead, 2, "\u6F22"), Shape(4));
        for (int i = 0; i < 5; i++) _session.Emit($"{i}\r\n");   // into the scrollback, still 6 cells long

        _terminal.ChangeBounds(new TRect(0, 0, 5, 3));          // narrower: the scrollback row is not reflowed
        _terminal.ScrollToTop();
        Assert.Equal("abcd", RowText(0, 5));
        Assert.Equal((TScreenCellKind.Single, 1, " "), Shape(4));
        for (int x = 0; x < 5; x++) Assert.NotEqual(TScreenCellKind.Trail, Cell(x, 0).Kind);
    }

    [Fact]
    public void ResizeKeepsTheProjectionWellFormed()
    {
        _session.Emit("\u6F22\u5B57\U0001F600xyz");
        _terminal.ChangeBounds(new TRect(0, 0, 5, 3));   // cuts the emoji's right half off the active row
        _terminal.ChangeBounds(new TRect(0, 0, 12, 3));
        for (int x = 0; x < 12; x++)
        {
            TScreenChar cell = Cell(x, 0);
            if (cell.Kind == TScreenCellKind.Lead && cell.CellWidth == 2) Assert.Equal(TScreenCellKind.Trail, Cell(x + 1, 0).Kind);
            if (cell.Kind == TScreenCellKind.Trail)
                Assert.True(x > 0 && Cell(x - 1, 0).Kind == TScreenCellKind.Lead, $"orphan trail at {x}");
        }

        Assert.StartsWith("\u6F22\u5B57", RowText(0, 12));
    }

    [Fact]
    public void LegacyProfileKeepsTheOneUnitPerCellProjection()
    {
        WithProfile(TextWidthProfile.Legacy, () =>
        {
            _session.Emit("\u6F22x\U0001F600y");
            Assert.Equal((TScreenCellKind.Single, '\u6F22'), (Cell(0, 0).Kind, Cell(0, 0).Character));
            Assert.Equal((TScreenCellKind.Single, ' '), (Cell(1, 0).Kind, Cell(1, 0).Character));
            Assert.Equal('x', Cell(2, 0).Character);
            Assert.Equal('\uFFFD', Cell(3, 0).Character);
            Assert.Equal(' ', Cell(4, 0).Character);
            Assert.Equal('y', Cell(5, 0).Character);
        });
    }

    [Fact]
    public void CommandLineEditsAndCaretFollowGraphemes()
    {
        _terminal.DetachSession();
        _terminal.InputMode = TerminalInputMode.Command;
        _terminal.Prompt = "> ";
        foreach (char c in "a\U0001F600e\u0301") _terminal.InputInsertChar(c);

        Press(Keys.kbLeft);                      // before e + acute, not between them
        Assert.Equal(3, _terminal.InputCursor);
        Press(Keys.kbLeft);                      // before the emoji, not between its surrogates
        Assert.Equal(1, _terminal.InputCursor);

        int prompt = 4;                          // the prompt row of a 5-row view
        Assert.Equal((TScreenCellKind.Lead, 2, "\U0001F600"), Shape(3, prompt));
        Assert.NotEqual(Cell(2, prompt).Attr, Cell(3, prompt).Attr);   // the caret covers the emoji's cells
        Assert.Equal(Cell(3, prompt).Attr, Cell(4, prompt).Attr);

        _terminal.InputDelete();                 // removes the whole emoji
        Assert.Equal("ae\u0301", _terminal.InputBuffer);
        Press(Keys.kbEnd);
        _terminal.InputBackspace();              // removes e + acute together
        Assert.Equal("a", _terminal.InputBuffer);
    }

    private void Press(ushort code)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = code;
        _terminal.HandleEvent(ref ev);
    }
}
