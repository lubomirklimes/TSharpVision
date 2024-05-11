// TVWrite rendering regression tests:
//   TVWrite srcOffset fix: child draws propagate to the root ScreenBuffer at the correct column offset.
//   TVWrite L30 fix + WriteBuf stride fix + TStaticText blank lines.
//   Shadow rendering: L40 pass-through, edx attribute, shadow restore.
//   Repeated move/resize regression: sfDragging cleared between drags.
//
// All tests are headless (NullDriver + DriverScope). No golden screen snapshots.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class TVWriteRenderingRegressionTests : IDisposable
{
    private readonly DriverScope _ds;

    public TVWriteRenderingRegressionTests()
    {
        _ds = new DriverScope(80, 25);
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    // Helper: allocate a flat ScreenBuffer for a root group.
    // size = width * height * ScreenBuffer.GetSize() (the multiplier used by TGroup.GetBuffer).
    private static ScreenBuffer MakeBuffer(int w, int h)
        => new ScreenBuffer(w * h * ScreenBuffer.GetSize());

    // =========================================================================
    // TVWrite srcOffset fix
    //
    // Root cause: when L10 added origin.x to X (left-clip clamping may push X
    // further), srcOffset was computed as wOffset instead of Max(0, X-wOffset).
    // For an unclipped child at origin (ox,oy) inside its owner, X==ox and
    // wOffset==ox after L10, so skip_offset should be 0 but was ox — the cell
    // at src[0] was never read, producing blank output in the root buffer.
    //
    // Fix: srcOffset = Math.Max(0, X - wOffset) in L50.
    // =========================================================================

    [Fact]
    public void TVWrite_NestedChild_WritesToCorrectRootBufferOffset()
    {
        // Root group 20×5 — simulates TProgram's root buffer.
        var root = new TestGroup(new TRect(0, 0, 20, 5));
        root.buffer = MakeBuffer(20, 5);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        // Intermediate group (child of root) at origin (3,1), size (10,3).
        // Simulates TDeskTop — no buffer of its own.
        var child = new TestGroup(new TRect(3, 1, 13, 4));
        child.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(child);

        // Leaf writer (child of child) at local (2,1), size (4,1).
        // writer global position = root(3+2, 1+1) = root(5, 2).
        var writer = new TVWriteTestView(new TRect(2, 1, 6, 2), 'Q', 0x1F);
        writer.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        child.Insert(writer);

        // Explicit draw (also fires implicitly during Insert, but repeat for clarity).
        writer.DrawView();

        // Expected destination in root buffer: row=2, col=5 → idx = 2*20+5 = 45.
        // With the L50 fix, srcOffset = Max(0, X-wOffset) = Max(0,5-5) = 0 → reads
        // src[0] = 'Q'.  Without the fix, srcOffset = wOffset = 5 → reads src[5]
        // (out of the 4-cell span) and the buffer guard returns early → stays '\0'.
        int idx = 2 * 20 + 5;
        Assert.Equal('Q', root.buffer.Data[idx].Character);
    }

    [Fact]
    public void TVWrite_NestedChild_SecondCellAlsoPropagatedCorrectly()
    {
        var root = new TestGroup(new TRect(0, 0, 20, 5));
        root.buffer = MakeBuffer(20, 5);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var child = new TestGroup(new TRect(3, 1, 13, 4));
        child.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(child);

        // writer covers local (2,1)-(6,2): 4 cells wide, at global root (5,2).
        var writer = new TVWriteTestView(new TRect(2, 1, 6, 2), 'Q', 0x1F);
        writer.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        child.Insert(writer);

        writer.DrawView();

        // Second cell: root(6, 2) → idx = 2*20+6 = 46.
        Assert.Equal('Q', root.buffer.Data[46].Character);
    }

    [Fact]
    public void TVWrite_NestedChild_ColumnsBeforeWriterOriginAreNotOverwritten()
    {
        var root = new TestGroup(new TRect(0, 0, 20, 5));
        root.buffer = MakeBuffer(20, 5);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var child = new TestGroup(new TRect(3, 1, 13, 4));
        child.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(child);

        // writer starts at child-local x=2 (global root x=5).
        // root col 3 (= child-local x=0) was never written by the writer.
        var writer = new TVWriteTestView(new TRect(2, 1, 6, 2), 'Q', 0x1F);
        writer.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        child.Insert(writer);

        writer.DrawView();

        // root(3, 2) = child col 0, writer does not cover it → must not be 'Q'.
        int idx = 2 * 20 + 3;
        Assert.NotEqual('Q', root.buffer.Data[idx].Character);
    }

    [Fact]
    public void TVWrite_WithBufferlessIntermediateOwner_WritePropagatesUpToRoot()
    {
        // Outer group has a buffer; inner is buffer-less (like TDeskTop).
        var outer = new TestGroup(new TRect(0, 0, 10, 3));
        outer.buffer = MakeBuffer(10, 3);
        outer.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var inner = new TestGroup(new TRect(0, 0, 10, 3));
        inner.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        outer.Insert(inner);

        var leaf = new TVWriteTestView(new TRect(0, 0, 10, 1), 'P', 0x1F);
        leaf.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        inner.Insert(leaf);

        leaf.DrawView();

        // With L40 pass-through: leaf writes bubble through inner (no buffer)
        // → outer.buffer receives 'P' at row 0.
        Assert.Equal('P', outer.buffer.Data[0].Character);
    }

    // =========================================================================
    // TVWrite L30 fix (left-strip rendered when sibling blocks
    // middle) + WriteBuf stride fix + TStaticText blank-line fix.
    // =========================================================================

    [Fact]
    public void TVWrite_L30Fix_LeftStripRenderedWhenSiblingBlocksMiddle()
    {
        // Mimics TWindow layout:
        //   frameLike (last = bottommost) draws full width [0,12).
        //   staticLike (First() = topmost) blocks cols [2,10).
        // When frameLike draws, L20 encounters staticLike blocking cols 2-9;
        // L30 must render [0,2) on the left and L40 renders [10,12) on the right.
        // Before the L30 fix, [0,2) was left as zeros (black bar).
        var sbRoot = new TestGroup(new TRect(0, 0, 12, 1));
        sbRoot.buffer = MakeBuffer(12, 1);
        sbRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        // Insert frameLike first → it becomes last (bottommost in z-order).
        var frameLike = new TVWriteTestView(new TRect(0, 0, 12, 1), 'F', 0x1F);
        frameLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(frameLike); // last = frameLike (bottommost)

        // Insert staticLike second → it becomes First() (topmost, blocks middle).
        var staticLike = new TVWriteTestView(new TRect(2, 0, 10, 1), 'S', 0x17);
        staticLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(staticLike); // First() = staticLike (topmost)

        // Draw in z-order: topmost first, then bottommost.
        staticLike.DrawView(); // writes 'S' to cols 2-9
        frameLike.DrawView();  // routes [0,2) via L30 and [10,12) via L40

        // L30 fix: col 0 and 1 must be 'F' (not '\0' from the old bug).
        Assert.Equal('F', sbRoot.buffer.Data[0].Character);
        Assert.Equal('F', sbRoot.buffer.Data[1].Character);
    }

    [Fact]
    public void TVWrite_L30Fix_MiddleColumnsAreFromTopView()
    {
        var sbRoot = new TestGroup(new TRect(0, 0, 12, 1));
        sbRoot.buffer = MakeBuffer(12, 1);
        sbRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var frameLike = new TVWriteTestView(new TRect(0, 0, 12, 1), 'F', 0x1F);
        frameLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(frameLike);

        var staticLike = new TVWriteTestView(new TRect(2, 0, 10, 1), 'S', 0x17);
        staticLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(staticLike);

        staticLike.DrawView();
        frameLike.DrawView();

        // Cols 2-9 are written by staticLike (topmost) and are not overwritten
        // by frameLike (bottommost, blocked by L20 occlusion).
        Assert.Equal('S', sbRoot.buffer.Data[2].Character);
    }

    [Fact]
    public void TVWrite_L30Fix_RightStripAfterSiblingIsRendered()
    {
        var sbRoot = new TestGroup(new TRect(0, 0, 12, 1));
        sbRoot.buffer = MakeBuffer(12, 1);
        sbRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var frameLike = new TVWriteTestView(new TRect(0, 0, 12, 1), 'F', 0x1F);
        frameLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(frameLike);

        var staticLike = new TVWriteTestView(new TRect(2, 0, 10, 1), 'S', 0x17);
        staticLike.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        sbRoot.Insert(staticLike);

        staticLike.DrawView();
        frameLike.DrawView();

        // Col 10 is right of staticLike [2,10): frameLike renders it via L40/L50.
        Assert.Equal('F', sbRoot.buffer.Data[10].Character);
    }

    [Fact]
    public void WriteBuf_ScreenBuffer_NoExceptionOnLastRow()
    {
        // WriteBuf(ScreenBuffer) stride fix: slice must advance by w each row,
        // not w+1.  A 5×2 view with stride w=5 needs exactly 10 cells, not 11.
        var wbRoot = new TestGroup(new TRect(0, 0, 5, 2));
        wbRoot.buffer = MakeBuffer(5, 2);
        wbRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var wbWriter = new TVWriteTestView(new TRect(0, 0, 5, 2), 'W', 0x1F);
        wbWriter.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        wbRoot.Insert(wbWriter);

        // Should not throw an IndexOutOfRangeException.
        var ex = Record.Exception(() => wbWriter.DrawView());
        Assert.Null(ex);
    }

    [Fact]
    public void WriteBuf_ScreenBuffer_AllRowsWritten()
    {
        var wbRoot = new TestGroup(new TRect(0, 0, 5, 2));
        wbRoot.buffer = MakeBuffer(5, 2);
        wbRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var wbWriter = new TVWriteTestView(new TRect(0, 0, 5, 2), 'W', 0x1F);
        wbWriter.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        wbRoot.Insert(wbWriter);

        wbWriter.DrawView();

        // Row 0 at dst offset 0, row 1 at dst offset 5 (stride = 5).
        Assert.Equal('W', wbRoot.buffer.Data[0].Character);
        Assert.Equal('W', wbRoot.buffer.Data[5].Character);
    }

    [Fact]
    public void TStaticText_DoubleNewline_ProducesBlankRow()
    {
        // TStaticText fix: "\n\n" must produce one blank line between Line1 and Line3.
        // Before the fix, the inner `if (s[p] == (char)10) p++` consumed the second
        // '\n', so "\n\n" left row 1 with "Line3" instead of blank.
        var stRoot = new TestGroup(new TRect(0, 0, 20, 5));
        stRoot.buffer = MakeBuffer(20, 5);
        stRoot.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var st = new TStaticText(new TRect(0, 0, 20, 5), "Line1\n\nLine3");
        st.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        stRoot.Insert(st);
        st.DrawView();

        // Row 0 must start with 'L' (from "Line1").
        Assert.Equal('L', stRoot.buffer.Data[0].Character);
        // Row 1 must be blank (all spaces — the blank line from "\n\n").
        for (int c = 0; c < 20; c++)
            Assert.Equal(' ', stRoot.buffer.Data[20 + c].Character);
        // Row 2 must start with 'L' (from "Line3").
        Assert.Equal('L', stRoot.buffer.Data[40].Character);
    }

    // =========================================================================
    // Shadow rendering
    //
    // Fixes:
    //   A) L40 pass-through: writes propagate through buffer-less intermediate owners.
    //   B/C) edx fix: shadow attribute (0x08) applied in shadow areas.
    //   D) Shadow restore: after Hide(), the shadow area is redrawn by the bg view.
    // =========================================================================

    [Fact]
    public void Shadow_L40Passthrough_LeafInsideBufferlessGroupWritesToOuterBuffer()
    {
        // Outer group has a buffer; inner group has none (simulates TDeskTop).
        var outer = new TestGroup(new TRect(0, 0, 10, 3));
        outer.buffer = MakeBuffer(10, 3);
        outer.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var inner = new TestGroup(new TRect(0, 0, 10, 3));
        inner.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        outer.Insert(inner);

        var leaf = new TVWriteTestView(new TRect(0, 0, 10, 1), 'P', 0x1F);
        leaf.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        inner.Insert(leaf);

        leaf.DrawView();

        // L40 pass-through: inner has no buffer → write propagates to outer.buffer.
        Assert.Equal('P', outer.buffer.Data[0].Character);
    }

    [Fact]
    public void Shadow_BottomShadowRow_HasShadowAttribute()
    {
        // root 15×3: bg (last=bottommost, draws 'B' with attr 0x1A),
        //            caster (First()=topmost, at (3,0) size (6,1), sfShadow).
        // caster.shadowSize default = (2,1) — bottom shadow row is row 1,
        // cols [3+2, 3+2+6) = [5, 11).
        var root = new TestGroup(new TRect(0, 0, 15, 3));
        root.buffer = MakeBuffer(15, 3);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        // Insert bg first → bg = last (bottommost).
        var bg = new TVWriteTestView(new TRect(0, 0, 15, 3), 'B', 0x1A);
        bg.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(bg);

        // Insert caster second → caster = First() (topmost), casts shadow.
        var caster = new TVWriteTestView(new TRect(3, 0, 9, 1), 'C', 0x1F);
        caster.state |= (ushort)(Views.sfVisible | Views.sfExposed | Views.sfShadow);
        // shadowSize is already (2,1) by default; set explicitly for clarity.
        caster.shadowSize = new TPoint(2, 1);
        root.Insert(caster);

        // Draw caster first, then bg (which triggers L20 shadow logic).
        caster.DrawView();
        bg.DrawView();

        // Row 1 (offset 1*15=15), col 5 is in the bottom shadow [5,11).
        // The edx fix: bg's 'B' char is written there but with shadow attr 0x08.
        TColorAttr shadowAttr = (TColorAttr)0x08;
        Assert.Equal(shadowAttr, root.buffer.Data[15 + 5].Attr);
    }

    [Fact]
    public void Shadow_ColumnsLeftOfBottomShadow_HaveNormalAttribute()
    {
        var root = new TestGroup(new TRect(0, 0, 15, 3));
        root.buffer = MakeBuffer(15, 3);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var bg = new TVWriteTestView(new TRect(0, 0, 15, 3), 'B', 0x1A);
        bg.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(bg);

        var caster = new TVWriteTestView(new TRect(3, 0, 9, 1), 'C', 0x1F);
        caster.state |= (ushort)(Views.sfVisible | Views.sfExposed | Views.sfShadow);
        caster.shadowSize = new TPoint(2, 1);
        root.Insert(caster);

        caster.DrawView();
        bg.DrawView();

        // Row 1, col 0 is left of the shadow zone [5,11) → normal attr 0x1A.
        TColorAttr normalAttr = (TColorAttr)0x1A;
        Assert.Equal(normalAttr, root.buffer.Data[15 + 0].Attr);
    }

    [Fact]
    public void Shadow_ColumnsRightOfBottomShadow_HaveNormalAttribute()
    {
        var root = new TestGroup(new TRect(0, 0, 15, 3));
        root.buffer = MakeBuffer(15, 3);
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var bg = new TVWriteTestView(new TRect(0, 0, 15, 3), 'B', 0x1A);
        bg.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(bg);

        var caster = new TVWriteTestView(new TRect(3, 0, 9, 1), 'C', 0x1F);
        caster.state |= (ushort)(Views.sfVisible | Views.sfExposed | Views.sfShadow);
        caster.shadowSize = new TPoint(2, 1);
        root.Insert(caster);

        caster.DrawView();
        bg.DrawView();

        // Row 1, col 11 is right of shadow zone [5,11) → normal attr 0x1A.
        TColorAttr normalAttr = (TColorAttr)0x1A;
        Assert.Equal(normalAttr, root.buffer.Data[15 + 11].Attr);
    }

    [Fact]
    public void Shadow_AfterHide_ShadowAreaRestoredByBgView()
    {
        // root 12×2; bg2 (last=bottommost, 'Z' attr 0x1B);
        // shadowed (First()=topmost, at (2,0) size (6,1), sfShadow, shadowSize=(2,1)).
        // Bottom shadow row 1, cols [2+2, 2+2+6) = [4, 10).
        var root2 = new TestGroup(new TRect(0, 0, 12, 2));
        root2.buffer = MakeBuffer(12, 2);
        root2.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var bg2 = new TVWriteTestView(new TRect(0, 0, 12, 2), 'Z', 0x1B);
        bg2.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root2.Insert(bg2); // bg2 = last (bottommost)

        var shadowed = new TVWriteTestView(new TRect(2, 0, 8, 1), 'X', 0x1C);
        shadowed.state |= (ushort)(Views.sfVisible | Views.sfExposed | Views.sfShadow);
        shadowed.shadowSize = new TPoint(2, 1);
        root2.Insert(shadowed); // shadowed = First() (topmost)

        shadowed.DrawView();
        bg2.DrawView();

        // Hide() triggers DrawUnderView → DrawSubViews(bg2, null) → bg2 draws
        // row 1 freely (shadowed is now invisible) → 'Z' appears at the
        // previously-shadowed col 4.
        shadowed.Hide();

        Assert.Equal('Z', root2.buffer.Data[1 * 12 + 4].Character);
    }

    // =========================================================================
    // Repeated move/resize regression
    //
    // Root cause: TEventQueue._lastMouse.buttons was left non-zero after the
    // first drag's evMouseUp, preventing the second evMouseDown from being
    // recognised and starting a new drag.  The fix clears _lastMouse.buttons
    // when processing evMouseUp.
    // =========================================================================

    private static TEvent MakeMouseDown(int x, int y, byte buttons = 0x01)
    {
        var e = new TEvent { What = Events.evMouseDown };
        e.mouse.where = new TPoint(x, y);
        e.mouse.buttons = buttons;
        return e;
    }

    private static TEvent MakeMouseUp(int x, int y, byte buttons = 0)
    {
        var e = new TEvent { What = Events.evMouseUp };
        e.mouse.where = new TPoint(x, y);
        e.mouse.buttons = buttons;
        return e;
    }

    private static TEvent MakeMouseMove(int x, int y)
    {
        var e = new TEvent { What = Events.evMouseMove };
        e.mouse.where = new TPoint(x, y);
        return e;
    }

    [Fact]
    public void RepeatedMove_FirstDragChangesOrigin()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(5, 3, 25, 12), "Test", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        var origBounds = win.GetBounds();

        // Enqueue a mouse-move then mouse-up so DragView loop exits cleanly.
        TEventQueue.Enqueue(MakeMouseMove(8, 5));
        TEventQueue.Enqueue(MakeMouseUp(8, 5));

        var ev = MakeMouseDown(5, 3);
        win.DragView(ev, Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.NotEqual(origBounds.a, win.GetBounds().a);
    }

    [Fact]
    public void RepeatedMove_SfDraggingClearedAfterFirstDrag()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(5, 3, 25, 12), "Test", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        TEventQueue.Enqueue(MakeMouseMove(8, 5));
        TEventQueue.Enqueue(MakeMouseUp(8, 5));

        var ev = MakeMouseDown(5, 3);
        win.DragView(ev, Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.Equal(0, win.state & Views.sfDragging);
    }

    [Fact]
    public void RepeatedMove_SecondDragAlsoChangesOrigin()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(5, 3, 25, 12), "Test", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        // First drag.
        TEventQueue.Enqueue(MakeMouseMove(8, 5));
        TEventQueue.Enqueue(MakeMouseUp(8, 5));
        var ev1 = MakeMouseDown(5, 3);
        win.DragView(ev1, Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);
        var afterFirst = win.GetBounds();

        // Second drag from the new position.
        var o2 = win.origin;
        TEventQueue.Enqueue(MakeMouseMove(o2.x + 4, o2.y + 1));
        TEventQueue.Enqueue(MakeMouseUp(o2.x + 4, o2.y + 1));
        var ev2 = MakeMouseDown(o2.x, o2.y);
        win.DragView(ev2, Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        // Second drag must change origin again (not get stuck on first position).
        Assert.NotEqual(afterFirst, win.GetBounds());
    }

    [Fact]
    public void RepeatedMove_SfDraggingClearedAfterSecondDrag()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(5, 3, 25, 12), "Test", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        TEventQueue.Enqueue(MakeMouseMove(8, 5));
        TEventQueue.Enqueue(MakeMouseUp(8, 5));
        win.DragView(MakeMouseDown(5, 3), Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        var o2 = win.origin;
        TEventQueue.Enqueue(MakeMouseMove(o2.x + 4, o2.y + 1));
        TEventQueue.Enqueue(MakeMouseUp(o2.x + 4, o2.y + 1));
        win.DragView(MakeMouseDown(o2.x, o2.y), Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.Equal(0, win.state & Views.sfDragging);
    }

    [Fact]
    public void RepeatedResize_FirstDragChangesSize()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(2, 2, 20, 12), "Resize", 2);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        var origSize = win.size;
        var corner = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(corner.x + 3, corner.y + 2));
        TEventQueue.Enqueue(MakeMouseUp(corner.x + 3, corner.y + 2));
        win.DragView(MakeMouseDown(corner.x, corner.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.NotEqual(origSize, win.size);
    }

    [Fact]
    public void RepeatedResize_SfDraggingClearedAfterFirstResize()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(2, 2, 20, 12), "Resize", 2);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        var corner = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(corner.x + 3, corner.y + 2));
        TEventQueue.Enqueue(MakeMouseUp(corner.x + 3, corner.y + 2));
        win.DragView(MakeMouseDown(corner.x, corner.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.Equal(0, win.state & Views.sfDragging);
    }

    [Fact]
    public void RepeatedResize_SecondDragAlsoChangesSize()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(2, 2, 20, 12), "Resize", 2);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        // First resize.
        var corner1 = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(corner1.x + 3, corner1.y + 2));
        TEventQueue.Enqueue(MakeMouseUp(corner1.x + 3, corner1.y + 2));
        win.DragView(MakeMouseDown(corner1.x, corner1.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);
        var sizeAfterFirst = win.size;

        // Second resize — _lastMouse.buttons must have been cleared after first up.
        var corner2 = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(corner2.x + 2, corner2.y + 1));
        TEventQueue.Enqueue(MakeMouseUp(corner2.x + 2, corner2.y + 1));
        win.DragView(MakeMouseDown(corner2.x, corner2.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.NotEqual(sizeAfterFirst, win.size);
    }

    [Fact]
    public void RepeatedResize_SfDraggingClearedAfterSecondResize()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(2, 2, 20, 12), "Resize", 2);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        var c1 = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(c1.x + 3, c1.y + 2));
        TEventQueue.Enqueue(MakeMouseUp(c1.x + 3, c1.y + 2));
        win.DragView(MakeMouseDown(c1.x, c1.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        var c2 = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(c2.x + 2, c2.y + 1));
        TEventQueue.Enqueue(MakeMouseUp(c2.x + 2, c2.y + 1));
        win.DragView(MakeMouseDown(c2.x, c2.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        Assert.Equal(0, win.state & Views.sfDragging);
    }

    [Fact]
    public void RepeatedResize_TFrameSizeTracksWindowAfterChangeBounds()
    {
        // TFrame has gfGrowHiX|gfGrowHiY, so its size must match TWindow's after resize.
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(1, 1, 21, 11), "Frame", 3);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        var corner = win.origin + win.size;
        TEventQueue.Enqueue(MakeMouseMove(corner.x + 5, corner.y + 3));
        TEventQueue.Enqueue(MakeMouseUp(corner.x + 5, corner.y + 3));
        win.DragView(MakeMouseDown(corner.x, corner.y), Views.dmDragGrow | Views.dmLimitLoY, ref limits, minSz, maxSz);

        var frame = win.frame;
        Assert.NotNull(frame);
        Assert.Equal(win.size.x, frame.size.x);
        Assert.Equal(win.size.y, frame.size.y);
    }

    [Fact]
    public void Move_WindowBoundsAreCorrectAfterSingleMove()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(10, 5, 30, 15), "W", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        // Move to (12, 7) by dragging from (10,5) to (12,7).
        TEventQueue.Enqueue(MakeMouseMove(12, 7));
        TEventQueue.Enqueue(MakeMouseUp(12, 7));
        win.DragView(MakeMouseDown(10, 5), Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);

        // Origin should have shifted by (+2,+2).
        Assert.Equal(12, win.origin.x);
        Assert.Equal(7, win.origin.y);
        // Size must remain unchanged.
        Assert.Equal(20, win.size.x);
        Assert.Equal(10, win.size.y);
    }

    [Fact]
    public void Move_OwnerRelationshipValidAfterMultipleMoves()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(5, 3, 25, 12), "Test", 1);
        host.Insert(win);

        var limits = host.GetExtent();
        TPoint minSz = default, maxSz = default;
        win.SizeLimits(ref minSz, ref maxSz);

        for (int i = 0; i < 3; i++)
        {
            var o = win.origin;
            TEventQueue.Enqueue(MakeMouseMove(o.x + 1, o.y + 1));
            TEventQueue.Enqueue(MakeMouseUp(o.x + 1, o.y + 1));
            win.DragView(MakeMouseDown(o.x, o.y), Views.dmDragMove | Views.dmLimitLoY, ref limits, minSz, maxSz);
        }

        // After repeated moves the window is still owned by host.
        Assert.Same(host, win.owner);
        // sfDragging must not be set after all moves complete.
        Assert.Equal(0, win.state & Views.sfDragging);
    }
}
