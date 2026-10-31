using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

// TV-1.1-FIX-3: Size/Move (cmResize) from the keyboard. Arrows move, Shift+arrows resize from the
// bottom-right edge with the origin fixed, Enter accepts and Esc restores the original bounds.
[Collection("NonParallel")]
public sealed class KeyboardWindowResizeTests : IDisposable
{
    private readonly DriverScope _ds;
    private readonly NullDriver  _nd;

    public KeyboardWindowResizeTests()
    {
        _ds = new DriverScope(80, 25);
        _nd = _ds.Driver;
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    // An 80x23 desktop, as between the menu bar and the status line of an 80x25 screen.
    private static TestGroup MakeDesktop()
    {
        var desktop = new TestGroup(new TRect(0, 1, 80, 24));
        desktop.SetState(Views.sfActive,  true);
        desktop.SetState(Views.sfExposed, true);
        return desktop;
    }

    private static TEvent Key(ushort keyCode, uint controlKeyState = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.controlKeyState = controlKeyState;
        return ev;
    }

    private static TEvent Shifted(ushort keyCode) => Key(keyCode, Keys.kbLeftShift);

    private void Keys_(params TEvent[] keys)
    {
        foreach (var key in keys) _nd.EnqueueKey(key);
    }

    private static void Resize(TWindow window)
    {
        var resize = new TEvent { What = Events.evCommand };
        resize.message.command = Views.cmResize;
        window.HandleEvent(ref resize);
        Assert.Equal(Events.evNothing, resize.What);
        Assert.True((window.state & Views.sfDragging) == 0);
    }

    private static TWindow InsertWindow(TGroup desktop, TRect bounds)
    {
        var window = new TWindow(bounds, "Resize", 1);
        desktop.Insert(window);
        window.SetState(Views.sfSelected, true);
        return window;
    }

    private sealed class LimitedWindow : TWindow
    {
        public LimitedWindow(TRect bounds) : base(bounds, "Limited", 1) { }

        public override void SizeLimits(ref TPoint min, ref TPoint max)
        {
            base.SizeLimits(ref min, ref max);
            max = new TPoint(32, 12);
        }
    }

    [Fact]
    public void Arrow_MovesWindow_SizeUnchanged()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Key(Keys.kbRight), Key(Keys.kbDown), Key(Keys.kbLeft), Key(Keys.kbLeft), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(9, 6, 39, 16), window.GetBounds());
    }

    [Theory]
    [InlineData(Keys.kbRight,  1,  0)]
    [InlineData(Keys.kbLeft,  -1,  0)]
    [InlineData(Keys.kbDown,   0,  1)]
    [InlineData(Keys.kbUp,     0, -1)]
    public void ShiftArrow_ResizesOneCell_OriginFixed(ushort key, int dx, int dy)
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Shifted(key), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TPoint(10, 5), window.origin);
        Assert.Equal(new TPoint(30 + dx, 10 + dy), window.size);
    }

    [Fact]
    public void RightShift_AlsoResizes()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Key(Keys.kbRight, Keys.kbRightShift), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(10, 5, 41, 15), window.GetBounds());
    }

    [Fact]
    public void ShiftArrows_EachKeyIsOneCell()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Shifted(Keys.kbRight), Shifted(Keys.kbRight), Shifted(Keys.kbRight),
              Shifted(Keys.kbDown), Shifted(Keys.kbDown),
              Shifted(Keys.kbLeft), Shifted(Keys.kbUp), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(10, 5, 42, 16), window.GetBounds());
    }

    [Fact]
    public void MoveThenResize_InOneSession()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Key(Keys.kbRight), Shifted(Keys.kbRight), Key(Keys.kbDown), Shifted(Keys.kbUp), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(11, 6, 42, 15), window.GetBounds());
    }

    [Fact]
    public void ShrinkStopsAtMinimumWindowSize()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 28, 13));   // 18x8, minimum 16x6
        var keys = new List<TEvent>();
        for (int i = 0; i < 10; i++) { keys.Add(Shifted(Keys.kbLeft)); keys.Add(Shifted(Keys.kbUp)); }
        keys.Add(Key(Keys.kbEnter));
        Keys_(keys.ToArray());
        Resize(window);
        Assert.Equal(new TPoint(10, 5), window.origin);
        Assert.Equal(TWindow.MinWinSize, window.size);
    }

    [Fact]
    public void GrowStopsAtSizeLimitsMaximum()
    {
        var desktop = MakeDesktop();
        var window = new LimitedWindow(new TRect(2, 2, 32, 12));              // 30x10, maximum 32x12
        desktop.Insert(window);
        var keys = new List<TEvent>();
        for (int i = 0; i < 5; i++) { keys.Add(Shifted(Keys.kbRight)); keys.Add(Shifted(Keys.kbDown)); }
        keys.Add(Key(Keys.kbEnter));
        Keys_(keys.ToArray());
        Resize(window);
        Assert.Equal(new TRect(2, 2, 34, 14), window.GetBounds());
    }

    [Fact]
    public void GrowStopsAtDesktopEdge_OriginFixed()
    {
        var desktop = MakeDesktop();
        var window = InsertWindow(desktop, new TRect(60, 15, 78, 21));        // 2 columns and 2 rows left
        var keys = new List<TEvent>();
        for (int i = 0; i < 6; i++) { keys.Add(Shifted(Keys.kbRight)); keys.Add(Shifted(Keys.kbDown)); }
        keys.Add(Key(Keys.kbEnter));
        Keys_(keys.ToArray());
        Resize(window);
        Assert.Equal(new TRect(60, 15, 80, 23), window.GetBounds());
        Assert.True(window.origin.x + window.size.x <= desktop.size.x);
        Assert.True(window.origin.y + window.size.y <= desktop.size.y);
    }

    [Fact]
    public void WindowPastTheDesktopEdge_DoesNotGrowButCanShrink()
    {
        var desktop = MakeDesktop();
        var window = InsertWindow(desktop, new TRect(70, 5, 90, 15));         // already 10 columns past the edge
        Keys_(Shifted(Keys.kbRight), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(70, 5, 90, 15), window.GetBounds());

        Keys_(Shifted(Keys.kbLeft), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(70, 5, 89, 15), window.GetBounds());
    }

    [Fact]
    public void Enter_AcceptsResizedGeometry()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Shifted(Keys.kbRight), Shifted(Keys.kbDown), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(10, 5, 41, 16), window.GetBounds());

        // A later session starts from the accepted geometry.
        Keys_(Key(Keys.kbRight), Key(Keys.kbEsc));
        Resize(window);
        Assert.Equal(new TRect(10, 5, 41, 16), window.GetBounds());
    }

    [Fact]
    public void Esc_RestoresOriginalBounds_AfterMoveAndResize()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        var original = window.GetBounds();
        Keys_(Shifted(Keys.kbRight), Shifted(Keys.kbRight), Key(Keys.kbDown), Shifted(Keys.kbUp), Key(Keys.kbEsc));
        Resize(window);
        Assert.Equal(original, window.GetBounds());
    }

    [Fact]
    public void NonResizableWindow_ShiftArrowNeitherMovesNorResizes()
    {
        var desktop = MakeDesktop();
        var window = InsertWindow(desktop, new TRect(10, 5, 40, 15));
        window.flags = (byte)(window.flags & ~Views.wfGrow);
        Keys_(Shifted(Keys.kbRight), Shifted(Keys.kbDown), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(10, 5, 40, 15), window.GetBounds());

        Keys_(Key(Keys.kbRight), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(11, 5, 41, 15), window.GetBounds());
    }

    [Fact]
    public void Dialog_ArrowsMove_ShiftArrowsDoNotResize()
    {
        var desktop = MakeDesktop();
        var dialog = new TDialog(new TRect(10, 5, 50, 15), "Dialog");
        desktop.Insert(dialog);
        Keys_(Shifted(Keys.kbRight), Shifted(Keys.kbDown), Key(Keys.kbLeft), Key(Keys.kbEnter));
        var resize = new TEvent { What = Events.evCommand };
        resize.message.command = Views.cmResize;
        dialog.HandleEvent(ref resize);
        Assert.Equal(new TRect(9, 5, 49, 15), dialog.GetBounds());
    }

    [Fact]
    public void MoveOnlyMode_IgnoresShift_GrowOnlyMode_IgnoresPlainArrows()
    {
        var host = MakeDesktop();
        var v = new ProbeView(new TRect(20, 10, 40, 15));
        host.Insert(v);
        var limits = host.GetExtent();
        var seed = Key(Keys.kbNoKey);

        Keys_(Shifted(Keys.kbRight), Key(Keys.kbEnter));
        v.DragView(seed, Views.dmDragMove, ref limits, new TPoint(5, 2), new TPoint(40, 15));
        Assert.Equal(new TRect(20, 10, 40, 15), v.GetBounds());

        Keys_(Key(Keys.kbRight), Shifted(Keys.kbRight), Key(Keys.kbEnter));
        v.DragView(seed, Views.dmDragGrow, ref limits, new TPoint(5, 2), new TPoint(40, 15));
        Assert.Equal(new TRect(20, 10, 41, 15), v.GetBounds());
    }

    [Fact]
    public void CtrlShiftArrow_ResizesByEight_CtrlArrow_StillMovesByEight()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(20, 5, 50, 15));
        Keys_(Key(Keys.kbCtrlRight, Keys.kbCtrlShift | Keys.kbLeftShift), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(20, 5, 58, 15), window.GetBounds());

        Keys_(Key(Keys.kbCtrlLeft, Keys.kbCtrlShift), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(12, 5, 50, 15), window.GetBounds());
    }

    [Fact]
    public void LockStatesAreNotShift()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        Keys_(Key(Keys.kbRight, Keys.kbCapsState | Keys.kbNumState), Key(Keys.kbEnter));
        Resize(window);
        Assert.Equal(new TRect(11, 5, 41, 15), window.GetBounds());
    }

    [Fact]
    public void MouseResize_Unchanged_WithShiftHeld()
    {
        var window = InsertWindow(MakeDesktop(), new TRect(10, 5, 40, 15));
        var limits = window.owner!.GetExtent();
        TPoint min = default, max = default;
        window.SizeLimits(ref min, ref max);

        var down = new TEvent { What = Events.evMouseDown };
        down.mouse.where = window.size;
        down.mouse.controlKeyState = Keys.kbLeftShift;
        var move = new TEvent { What = Events.evMouseMove };
        move.mouse.where = new TPoint(window.size.x + 4, window.size.y + 2);
        move.mouse.controlKeyState = Keys.kbLeftShift;
        var up = new TEvent { What = Events.evMouseUp };
        up.mouse.where = move.mouse.where;
        TEventQueue.Enqueue(move);
        TEventQueue.Enqueue(up);

        window.DragView(down, (byte)(window.dragMode | Views.dmDragGrow), ref limits, min, max);
        Assert.Equal(new TRect(10, 5, 44, 17), window.GetBounds());
    }
}
