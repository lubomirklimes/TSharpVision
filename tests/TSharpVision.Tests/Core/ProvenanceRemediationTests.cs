using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ProvenanceRemediationTests : IDisposable
{
    readonly DriverScope driver = new();
    readonly TCommandSet commands = new();
    readonly uint tabs = TEditor.tabSize, sorting = TFileCollection.SortOptions;
    public ProvenanceRemediationTests() { TView.GetCommands(commands); TView.EnableCommand(Views.cmClose); TView.EnableCommand(Views.cmZoom); }
    public void Dispose() { TView.SetCommands(commands); TEditor.tabSize = tabs; TFileCollection.SortOptions = sorting; driver.Dispose(); }

    sealed class QueueGroup : TGroup
    {
        public readonly List<TEvent> EventsReceived = new();
        public Action? OnQueue;
        public QueueGroup() : base(new TRect(0, 0, 80, 25)) { }
        public override void PutEvent(ref TEvent ev) { EventsReceived.Add(ev); OnQueue?.Invoke(); }
    }
    sealed class FrameProbe : TFrame
    {
        public string? Glyph; public int X;
        public FrameProbe() : base(new TRect(0, 0, 30, 10)) { }
        public override void WriteLine(int x, int y, int w, int h, Span<TScreenChar> cells)
        { X = x; Glyph = new string(cells[..w].ToArray().Select(c => c.Character).ToArray()); }
    }
    static TEvent Mouse(ushort type, int x, int y, bool twice = false)
    { TEvent ev = default; ev.What = type; ev.mouse.where = new TPoint(x, y); ev.mouse.doubleClick = twice; return ev; }

    [Theory]
    [InlineData(3, 0, false, 4)]
    [InlineData(26, 0, false, 5)]
    [InlineData(15, 0, true, 5)]
    public void Frame_QueuesTargetedCommandOnlyAtActivation(int x, int y, bool twice, int expected)
    {
        var queue = new QueueGroup(); var window = new TWindow(new TRect(0, 0, 30, 10), "", 0);
        queue.InsertView(window, null); var frame = Assert.IsType<TFrame>(window.frame); frame.state |= Views.sfActive;
        var down = Mouse(Events.evMouseDown, x, y, twice); frame.HandleEvent(ref down);
        Assert.Equal(Events.evNothing, down.What);
        if (!twice) { Assert.Empty(queue.EventsReceived); var up = Mouse(Events.evMouseUp, x, y); frame.HandleEvent(ref up); Assert.Equal(Events.evNothing, up.What); }
        var command = Assert.Single(queue.EventsReceived);
        Assert.Equal((ushort)expected, command.message.command); Assert.Same(window, command.message.infoPtr);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(30, 0)] [InlineData(3, 1)] [InlineData(3, 10)]
    public void Frame_ReleaseOutsideControlsDoesNothing(int x, int y)
    {
        var queue = new QueueGroup(); var window = new TWindow(new TRect(0, 0, 30, 10), "", 0);
        queue.InsertView(window, null); var frame = Assert.IsType<TFrame>(window.frame); frame.state |= Views.sfActive;
        var ev = Mouse(Events.evMouseUp, x, y); frame.HandleEvent(ref ev); Assert.Empty(queue.EventsReceived);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Frame_DisabledOrUnavailableZoomDoesNotQueue(int reason)
    {
        var queue = new QueueGroup(); var window = new TWindow(new TRect(0, 0, 30, 10), "", 0);
        queue.InsertView(window, null); var frame = Assert.IsType<TFrame>(window.frame); frame.state |= Views.sfActive;
        if (reason == 0) window.flags = 0;
        if (reason == 1) window.state |= Views.sfDisabled;
        if (reason == 2) frame.state |= Views.sfDisabled;
        if (reason == 3) TView.DisableCommand(Views.cmZoom);
        var ev = Mouse(Events.evMouseDown, 15, 0, true); frame.HandleEvent(ref ev); Assert.Empty(queue.EventsReceived);
    }

    [Theory]
    [InlineData(0, 1, 2, "[■]")] [InlineData(0, 0, 2, "[+]")]
    [InlineData(1, 1, 25, "[↑]")] [InlineData(1, 0, 25, "[+]")]
    public void Frame_IconCellsAndPlacement(int kind, int normal, int x, string glyph)
    {
        var window = new TWindow(new TRect(0, 0, 30, 10), "", 0); var frame = new FrameProbe();
        window.InsertView(frame, null); frame.DrawIcon(normal, kind); Assert.Equal(x, frame.X); Assert.Equal(glyph, frame.Glyph);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(2, 1)]
    [InlineData(5, 0)] [InlineData(5, 2)] [InlineData(5, 4)]
    public void RemoveView_PreservesRingOrderAndLowLevelLifecycle(int count, int removed)
    {
        var group = new QueueGroup(); var children = Enumerable.Range(0, count).Select(_ => new TView(new TRect(0, 0, 2, 2))).ToArray();
        foreach (var child in children) group.InsertView(child, null);
        var oldNext = children[removed].Next; group.current = children[removed]; children[removed].state |= Views.sfFocused;
        group.RemoveView(children[removed]);
        Assert.True((children[removed].state & Views.sfFocused) != 0); Assert.Same(group, children[removed].owner); Assert.Same(oldNext, children[removed].Next); Assert.Same(children[removed], group.current);
        var expected = children.Where((_, i) => i != removed).ToArray();
        if (expected.Length == 0) { Assert.Null(group.last); return; }
        var last = Assert.IsType<TView>(group.last);
        var cursor = Assert.IsType<TView>(last.Next);
        foreach (var child in expected) { Assert.Same(child, cursor); cursor = Assert.IsType<TView>(cursor.Next); }
        Assert.Same(expected[0], cursor); Assert.Same(expected[^1], group.last);
        group.RemoveView(null); group.RemoveView(new TView(new TRect(0, 0, 1, 1))); Assert.Same(expected[^1], group.last);
    }

    static TEditor Editor(string text, int gap)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 256);
        editor.bufLen = (uint)text.Length; editor.curPtr = (uint)gap; editor.gapLen = editor.bufSize - editor.bufLen;
        text.AsSpan(0, gap).CopyTo(editor.buffer);
        text.AsSpan(gap).CopyTo(editor.buffer.AsSpan(gap + (int)editor.gapLen));
        return editor;
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void Editor_TabColumnsAcrossEveryGap(uint width)
    {
        TEditor.tabSize = width; const string text = "a\t\tbč😀\nend";
        int stop = (int)Math.Max(1u, width);
        var columns = new List<int> { 0 };
        foreach (char c in text[..text.IndexOf('\n')]) columns.Add(c == '\t' ? columns[^1] + stop - columns[^1] % stop : columns[^1] + 1);
        for (int gap = 0; gap <= text.Length; gap++)
        {
            var editor = Editor(text, gap);
            for (int offset = 0; offset < columns.Count; offset++) Assert.Equal(columns[offset], editor.CharPos(0, (uint)offset));
            for (int column = -1; column <= columns[^1] + 3; column++)
            {
                int expected = columns.FindLastIndex(c => c <= Math.Max(0, column));
                Assert.Equal((uint)expected, editor.CharPtr(0, column));
            }
        }
    }
    [Theory]
    [InlineData("")] [InlineData("a\nb\n")] [InlineData("a\rb")]
    [InlineData("a\r\nb")] [InlineData("😀č\tlast")] [InlineData("\t\r\n\t\n\t")]
    public void Editor_LineAndUnitNavigationAcrossEveryGap(string text)
    {
        for (int gap = 0; gap <= text.Length; gap++)
        {
            var editor = Editor(text, gap);
            for (int position = 0; position <= text.Length; position++)
            {
                int end = position; while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
                int start = position; while (start > 0 && text[start - 1] != '\r' && text[start - 1] != '\n') start--;
                Assert.Equal((uint)end, editor.LineEnd((uint)position)); Assert.Equal((uint)start, editor.LineStart((uint)position));
                Assert.Equal((uint)Math.Min(position + 1, text.Length), editor.NextChar((uint)position));
                Assert.Equal((uint)Math.Max(position - 1, 0), editor.PrevChar((uint)position));
            }
        }
    }

    [Fact]
    public void RemoveView_EmptyAndRepeatedReinsertionsPreserveOwnership()
    {
        var group = new QueueGroup(); var child = new TView(new TRect(0, 0, 2, 2));
        group.RemoveView(null); group.RemoveView(child); Assert.Null(group.last);
        for (int iteration = 0; iteration < 20; iteration++)
        {
            group.InsertView(child, null); Assert.Same(child, child.Next); Assert.Same(group, child.owner);
            group.Remove(child); Assert.Null(child.Next); Assert.Null(child.owner); Assert.Null(group.last);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FrameClose_CommandThenNotificationOrModalCancel(bool modal)
    {
        var queue = new QueueGroup(); var window = new TWindow(new TRect(0, 0, 30, 10), "", 0);
        queue.InsertView(window, null); var frame = Assert.IsType<TFrame>(window.frame); frame.state |= Views.sfActive;
        if (modal) window.state |= Views.sfModal;
        var down = Mouse(Events.evMouseDown, 3, 0); frame.HandleEvent(ref down);
        Assert.Empty(queue.EventsReceived);
        var up = Mouse(Events.evMouseUp, 3, 0); window.frame.HandleEvent(ref up);
        var close = Assert.Single(queue.EventsReceived); Assert.Equal(Views.cmClose, close.message.command);
        Assert.Same(queue, window.owner);
        window.HandleEvent(ref close); Assert.Equal(Events.evNothing, close.What);
        Assert.Equal(2, queue.EventsReceived.Count);
        Assert.Equal(modal ? Views.cmCancel : Views.cmClosingWindow, queue.EventsReceived[1].message.command);
        Assert.Equal(modal ? Events.evCommand : Events.evBroadcast, queue.EventsReceived[1].What);
        if (modal) { Assert.Same(queue, window.owner); Assert.NotNull(window.frame); }
        else Assert.Null(window.owner);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Scrollbar_DisabledOrCollapsedHidesFocusedHardwareCursor(bool disabled)
    {
        var bar = new TScrollBar(new TRect(0, 0, 1, 10));
        bar.state |= Views.sfFocused | Views.sfCursorVis;
        bar.maxVal = disabled ? 10 : 0;
        if (disabled) bar.state |= Views.sfDisabled;
        var screenDriver = Assert.IsAssignableFrom<TSharpVision.Drivers.IDriver>(TScreen.driver);
        screenDriver.SetCursorType(100); bar.DrawPos(3);
        Assert.Equal(0, screenDriver.GetCursorType());
    }

    sealed class SmallDesktop : TDeskTop
    {
        public int Errors;
        public SmallDesktop() : base(new TRect(0, 0, 80, 24)) { }
        public override void TileError() => Errors++;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Desktop_TooSmallReportsErrorWithoutMovingChildren(bool vertical)
    {
        var desktop = new SmallDesktop(); desktop.SetOptions(vertical ? (uint)Views.dsktTileVertical : 0u);
        var windows = Enumerable.Range(0, 2).Select(_ => new TWindow(new TRect(2, 2, 32, 12), "", 0)).ToArray();
        foreach (var window in windows) { window.options |= Views.ofTileable; desktop.InsertView(window, null); }
        desktop.Tile(new TRect(0, 0, 1, 1)); Assert.Equal(1, desktop.Errors);
        foreach (var window in windows) Assert.Equal(new TPoint(2, 2), window.origin);
    }

    [Fact]
    public void Exposed_RetainedVisibilityClipAndSiblingBehavior()
    {
        var group = new QueueGroup(); var front = new TView(new TRect(0, 0, 10, 10));
        var back = new TView(new TRect(0, 0, 10, 10));
        group.InsertView(front, null); group.InsertView(back, null); back.state |= Views.sfExposed;
        Assert.False(back.Exposed()); front.state &= unchecked((ushort)~Views.sfVisible); Assert.True(back.Exposed());
        group.clip = new TRect(20, 20, 30, 30); Assert.False(back.Exposed());
        back.state &= unchecked((ushort)~Views.sfExposed); Assert.False(back.Exposed());
    }

    sealed class ClosingWindow : TWindow
    {
        public bool Allow = true; public int Validations;
        public ClosingWindow() : base(new TRect(0, 0, 30, 10), "", 0) { }
        public override bool Valid(ushort command) { Validations++; return Allow; }
    }
    [Fact]
    public void Close_VetoThenReentrantAndRepeatedCallQueuesOnceBeforeDetachment()
    {
        var queue = new QueueGroup(); var window = new ClosingWindow { Allow = false }; queue.InsertView(window, null);
        window.Close(); Assert.Empty(queue.EventsReceived); Assert.Same(queue, window.owner);
        window.Allow = true; queue.OnQueue = () => { Assert.Same(queue, window.owner); Assert.NotNull(window.frame); window.Close(); };
        window.Close(); window.Close(); var ev = Assert.Single(queue.EventsReceived);
        Assert.Equal(Views.cmClosingWindow, ev.message.command); Assert.Equal(Events.evBroadcast, ev.What); Assert.Same(window, ev.message.infoPtr);
        Assert.Null(window.owner); Assert.Null(window.frame); Assert.Equal(2, window.Validations);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Scrollbar_FocusedThumbUsesItsOwnAxis(bool vertical)
    {
        var bar = new TScrollBar(vertical ? new TRect(0, 0, 1, 10) : new TRect(0, 0, 10, 1));
        bar.minVal = 0; bar.maxVal = 10; bar.state |= Views.sfFocused; bar.DrawPos(4);
        Assert.Equal(vertical ? new TPoint(0, 4) : new TPoint(4, 0), bar.cursor);
        bar.state &= unchecked((ushort)~Views.sfFocused); bar.DrawPos(6);
        Assert.Equal(vertical ? new TPoint(0, 4) : new TPoint(4, 0), bar.cursor);
    }
    sealed class FileProbe : TFileList
    {
        public FileProbe() : base(new TRect(0, 0, 20, 10), null) { }
        public static bool Excludes(string name) => ExcludeSpecial(name);
    }
    [Theory]
    [InlineData("")] [InlineData(".")] [InlineData("..")] [InlineData(".hidden")]
    [InlineData("name~")] [InlineData("x.BkP")] [InlineData(".bkp")] [InlineData("normal")]
    public void FileFilters_IndependentFlagsAndCombinations(string name)
    {
        uint[] flags = { FileCollectionOptions.fcolHideStartDot, FileCollectionOptions.fcolHideEndTilde, FileCollectionOptions.fcolHideEndBkp };
        for (int mask = 0; mask < 8; mask++)
        {
            TFileCollection.SortOptions = 0; for (int bit = 0; bit < 3; bit++) if ((mask & (1 << bit)) != 0) TFileCollection.SortOptions |= flags[bit];
            bool expected = (mask & 1) != 0 && name.StartsWith('.') || (mask & 2) != 0 && name.EndsWith('~') || (mask & 4) != 0 && name.Length > 4 && name.EndsWith(".bkp", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expected, FileProbe.Excludes(name));
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Desktop_OrientationForTwoWindows(bool vertical)
    {
        var desktop = new TDeskTop(new TRect(0, 0, 80, 24)); desktop.SetOptions(vertical ? (uint)Views.dsktTileVertical : 0u);
        var windows = new[] { new TWindow(new TRect(0, 0, 30, 10), "A", 0), new TWindow(new TRect(0, 0, 30, 10), "B", 0) };
        foreach (var window in windows) { window.options |= Views.ofTileable; desktop.InsertView(window, null); }
        desktop.Tile(desktop.GetExtent());
        foreach (var window in windows) Assert.Equal(vertical ? new TPoint(40, 24) : new TPoint(80, 12), window.size);
        Assert.NotEqual(windows[0].origin, windows[1].origin);
    }
}
