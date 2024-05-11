// Migrated from SharpVision.Demo/Program.cs lines 569-841.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class DragViewWindowIntegrationTests : IDisposable
{
    private readonly DriverScope _ds;
    private readonly NullDriver  _nd;

    public DragViewWindowIntegrationTests()
    {
        _ds = new DriverScope(80, 25);
        _nd = _ds.Driver;
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    private static TEvent MakeKey(ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        return ev;
    }

    // ── DragView ───────────────────────────────────────────────

    [Fact]
    public void DragView_MouseMove_RepositionsOrigin()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(10, 10, 30, 15));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);

        var down = new TEvent { What = Events.evMouseDown };
        down.mouse.where = new TPoint(10, 10);
        var move = new TEvent { What = Events.evMouseMove };
        move.mouse.where = new TPoint(15, 12);
        var up = new TEvent { What = Events.evMouseUp };
        up.mouse.where = new TPoint(15, 12);
        TEventQueue.Enqueue(move);
        TEventQueue.Enqueue(up);

        v.DragView(down, Views.dmDragMove, ref limits, minSize, maxSize);
        Assert.Equal(new TRect(15, 12, 35, 17), v.GetBounds());
        Assert.True((v.state & Views.sfDragging) == 0);
    }

    [Fact]
    public void DragView_MouseMove_WithDmLimitAll_ClampsInsideLimits()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(0, 0, 20, 5));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);

        var down = new TEvent { What = Events.evMouseDown };
        down.mouse.where = new TPoint(0, 0);
        var move = new TEvent { What = Events.evMouseMove };
        move.mouse.where = new TPoint(200, 200);
        var up = new TEvent { What = Events.evMouseUp };
        up.mouse.where = new TPoint(200, 200);
        TEventQueue.Enqueue(move);
        TEventQueue.Enqueue(up);

        v.DragView(down, (byte)(Views.dmDragMove | Views.dmLimitAll), ref limits, minSize, maxSize);
        Assert.True(v.origin.x + v.size.x <= limits.b.x);
        Assert.True(v.origin.y + v.size.y <= limits.b.y);
        Assert.True(v.origin.x >= limits.a.x);
        Assert.True(v.origin.y >= limits.a.y);
    }

    [Fact]
    public void DragView_MouseGrow_EnlargesSize()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(0, 0, 20, 5));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);
        var origSize = v.size;

        var grow = new TEvent { What = Events.evMouseDown };
        grow.mouse.where = origSize;
        var growMove = new TEvent { What = Events.evMouseMove };
        growMove.mouse.where = new TPoint(origSize.x + 5, origSize.y + 2);
        var growUp = new TEvent { What = Events.evMouseUp };
        growUp.mouse.where = growMove.mouse.where;
        TEventQueue.Enqueue(growMove);
        TEventQueue.Enqueue(growUp);

        v.DragView(grow, Views.dmDragGrow, ref limits, minSize, maxSize);
        Assert.Equal(new TPoint(origSize.x + 5, origSize.y + 2), v.size);
    }

    [Fact]
    public void DragView_Keyboard_ArrowsMoveOrigin()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(20, 10, 40, 15));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);
        var before = v.origin;

        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbDown));
        _nd.EnqueueKey(MakeKey(Keys.kbEnter));
        var seed = new TEvent { What = Events.evKeyDown };
        seed.keyDown.keyCode = Keys.kbNoKey;
        v.DragView(seed, Views.dmDragMove, ref limits, minSize, maxSize);
        Assert.Equal(new TPoint(before.x + 1, before.y + 1), v.origin);
    }

    [Fact]
    public void DragView_Keyboard_CtrlLeft_Jumps8()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(40, 5, 60, 10));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);

        _nd.EnqueueKey(MakeKey(Keys.kbCtrlLeft));
        _nd.EnqueueKey(MakeKey(Keys.kbEnter));
        var seed = new TEvent { What = Events.evKeyDown };
        seed.keyDown.keyCode = Keys.kbNoKey;
        v.DragView(seed, Views.dmDragMove, ref limits, minSize, maxSize);
        Assert.Equal(32, v.origin.x);
    }

    [Fact]
    public void DragView_Keyboard_KbEsc_RestoresBounds()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive,  true);
        host.SetState(Views.sfExposed, true);
        var v = new ProbeView(new TRect(20, 10, 40, 15));
        host.Insert(v);
        var limits  = host.GetExtent();
        var minSize = new TPoint(5, 2);
        var maxSize = new TPoint(40, 15);
        var saveOrigin = v.origin;
        var saveSize   = v.size;

        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbDown));
        _nd.EnqueueKey(MakeKey(Keys.kbEsc));
        var seed = new TEvent { What = Events.evKeyDown };
        seed.keyDown.keyCode = Keys.kbNoKey;
        v.DragView(seed, Views.dmDragMove, ref limits, minSize, maxSize);
        Assert.Equal(saveOrigin, v.origin);
        Assert.Equal(saveSize,   v.size);
        Assert.True((v.state & Views.sfDragging) == 0);
    }

    // ── TWindow integration ────────────────────────────────────

    [Fact]
    public void Window_Constructor_DefaultFlags()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        var window  = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);
        Assert.True((window.flags & (Views.wfMove | Views.wfGrow | Views.wfClose | Views.wfZoom))
                    == (Views.wfMove | Views.wfGrow | Views.wfClose | Views.wfZoom));
        Assert.NotNull(window.frame);
    }

    [Fact]
    public void Window_CmResize_Keyboard_MovesOrigin()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        desktop.SetState(Views.sfActive,  true);
        desktop.SetState(Views.sfExposed, true);
        var window = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);
        window.SetState(Views.sfSelected, true);
        var before = window.origin;

        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbDown));
        _nd.EnqueueKey(MakeKey(Keys.kbEnter));
        var resize = new TEvent { What = Events.evCommand };
        resize.message.command = Views.cmResize;
        window.HandleEvent(ref resize);
        Assert.Equal(new TPoint(before.x + 1, before.y + 1), window.origin);
        Assert.Equal(Events.evNothing, resize.What);
    }

    [Fact]
    public void Window_CmResize_Esc_RevertsBounds()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        desktop.SetState(Views.sfActive,  true);
        desktop.SetState(Views.sfExposed, true);
        var window = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);
        window.SetState(Views.sfSelected, true);
        var priorBounds = window.GetBounds();

        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbRight));
        _nd.EnqueueKey(MakeKey(Keys.kbEsc));
        var resize = new TEvent { What = Events.evCommand };
        resize.message.command = Views.cmResize;
        window.HandleEvent(ref resize);
        Assert.Equal(priorBounds, window.GetBounds());
    }

    [Fact]
    public void Window_CmZoom_EnlargesToMax()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        desktop.SetState(Views.sfActive,  true);
        desktop.SetState(Views.sfExposed, true);
        var window = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);
        window.SetState(Views.sfSelected, true);
        var beforeZoom = window.GetBounds();

        var zoom = new TEvent { What = Events.evCommand };
        zoom.message.command = Views.cmZoom;
        window.HandleEvent(ref zoom);
        Assert.Equal(desktop.size, window.size);
        Assert.Equal(new TPoint(0, 0), window.origin);
        Assert.Equal(beforeZoom, window.zoomRect);
        Assert.Equal(Events.evNothing, zoom.What);
    }

    [Fact]
    public void Window_CmZoom_Twice_RestoresPrior()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        desktop.SetState(Views.sfActive,  true);
        desktop.SetState(Views.sfExposed, true);
        var window = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);
        window.SetState(Views.sfSelected, true);
        var beforeZoom = window.GetBounds();

        var zoom1 = new TEvent { What = Events.evCommand };
        zoom1.message.command = Views.cmZoom;
        window.HandleEvent(ref zoom1);

        var zoom2 = new TEvent { What = Events.evCommand };
        zoom2.message.command = Views.cmZoom;
        window.HandleEvent(ref zoom2);
        Assert.Equal(beforeZoom, window.GetBounds());
    }

    [Fact]
    public void Window_CmClose_DetachesFrame()
    {
        var desktop = new TestGroup(new TRect(0, 0, 80, 25));
        var window  = new TWindow(new TRect(10, 5, 40, 15), "Test", 1);
        desktop.Insert(window);

        var closeCmd = new TEvent { What = Events.evCommand };
        closeCmd.message.command = Views.cmClose;
        window.HandleEvent(ref closeCmd);
        Assert.Null(window.frame);
        Assert.Equal(Events.evNothing, closeCmd.What);
    }

    [Fact]
    public void Group_ShutDown_RemovesChildren()
    {
        var host = new TestGroup(new TRect(0, 0, 40, 10));
        var child = new ProbeView(new TRect(1, 1, 5, 5));
        host.Insert(child);
        Assert.Same(host, child.owner);

        host.ShutDown();
        Assert.Null(host.current);
        Assert.Null(host.last);
    }
}
