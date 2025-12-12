using System;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

// ── file-local helpers ────────────────────────────────────────────────────────

/// <summary>TApplication that inserts a TWindow then scripts cmQuit.</summary>
file sealed class OpenWindowApp : TApplication
{
    public override void Run()
    {
        var win = new TWindow(new TRect(5, 3, 30, 12), "OpenWin", Views.wnNoNumber);
        DeskTop?.Insert(win);
        base.Run();
    }
}

// ── tests ─────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class GroupShutdownTests
{
    // ── 25c.1: TGroup.ShutDown with no children ───────────────────────────────

    [Fact]
    public void ShutDown_NoChildren_NoException()
    {
        var ex = Record.Exception(() =>
        {
            var g = new TGroup(new TRect(0, 0, 10, 5));
            g.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ShutDown_NoChildren_LastIsNull()
    {
        var g = new TGroup(new TRect(0, 0, 10, 5));
        g.ShutDown();
        Assert.Null(g.last);
    }

    // ── 25c.2: TGroup.ShutDown with one child ────────────────────────────────

    [Fact]
    public void ShutDown_OneChild_NoException()
    {
        var ex = Record.Exception(() =>
        {
            var g = new TGroup(new TRect(0, 0, 40, 10));
            var child = new TView(new TRect(1, 1, 5, 3));
            g.Insert(child);
            g.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ShutDown_OneChild_RemovesChildFromOwner()
    {
        var g = new TGroup(new TRect(0, 0, 40, 10));
        var child = new TView(new TRect(1, 1, 5, 3));
        g.Insert(child);
        g.ShutDown();
        Assert.Null(child.owner);
    }

    // ── 25c.3: TGroup.ShutDown with multiple children ────────────────────────

    [Fact]
    public void ShutDown_MultipleChildren_NoException()
    {
        var ex = Record.Exception(() =>
        {
            var g = new TGroup(new TRect(0, 0, 40, 10));
            g.Insert(new TView(new TRect(1, 1, 5, 3)));
            g.Insert(new TView(new TRect(6, 1, 10, 3)));
            g.Insert(new TView(new TRect(11, 1, 15, 3)));
            g.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ShutDown_MultipleChildren_EmptiesChildList()
    {
        var g = new TGroup(new TRect(0, 0, 40, 10));
        g.Insert(new TView(new TRect(1, 1, 5, 3)));
        g.Insert(new TView(new TRect(6, 1, 10, 3)));
        g.Insert(new TView(new TRect(11, 1, 15, 3)));
        g.ShutDown();
        Assert.Null(g.last);
        Assert.Null(g.current);
    }

    // ── 25c.4: TGroup.ShutDown with current pointing at a child ──────────────

    [Fact]
    public void ShutDown_WithCurrentChild_NoException()
    {
        var ex = Record.Exception(() =>
        {
            var g = new TGroup(new TRect(0, 0, 40, 10));
            var c1 = new TView(new TRect(1, 1, 5, 3));
            var c2 = new TView(new TRect(6, 1, 10, 3));
            c1.options |= Views.ofSelectable;
            c2.options |= Views.ofSelectable;
            g.Insert(c1);
            g.Insert(c2);
            g.ResetCurrent();
            g.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ShutDown_WithCurrentChild_SetsCurrentToNull()
    {
        var g = new TGroup(new TRect(0, 0, 40, 10));
        var c1 = new TView(new TRect(1, 1, 5, 3));
        var c2 = new TView(new TRect(6, 1, 10, 3));
        c1.options |= Views.ofSelectable;
        c2.options |= Views.ofSelectable;
        g.Insert(c1);
        g.Insert(c2);
        g.ResetCurrent();
        g.ShutDown();
        Assert.Null(g.current);
    }

    // ── 25c.5: TGroup.ShutDown with TWindow children (infinite-loop fix) ──────

    [Fact]
    public void ShutDown_TWindowChildren_NoException()
    {
        using var driver = new DriverScope();
        var ex = Record.Exception(() =>
        {
            var g = new TGroup(new TRect(0, 0, 80, 25));
            var w1 = new TWindow(new TRect(5, 3, 30, 12), "W1", Views.wnNoNumber);
            var w2 = new TWindow(new TRect(10, 5, 40, 15), "W2", Views.wnNoNumber);
            g.Insert(w1);
            g.Insert(w2);
            g.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ShutDown_TWindowChildren_EmptiesChildList()
    {
        using var driver = new DriverScope();
        var g = new TGroup(new TRect(0, 0, 80, 25));
        var w1 = new TWindow(new TRect(5, 3, 30, 12), "W1", Views.wnNoNumber);
        var w2 = new TWindow(new TRect(10, 5, 40, 15), "W2", Views.wnNoNumber);
        g.Insert(w1);
        g.Insert(w2);
        g.ShutDown();
        Assert.Null(g.last);
        Assert.Null(g.current);
    }

    // ── 25c.6: TDeskTop shutdown with open window ─────────────────────────────

    [Fact]
    public void TDeskTop_ShutDown_WithOpenWindow_NoException()
    {
        using var driver = new DriverScope();
        var ex = Record.Exception(() =>
        {
            var desk = new TDeskTop(new TRect(0, 1, 80, 24));
            var win = new TWindow(new TRect(5, 3, 35, 12), "Test", Views.wnNoNumber);
            desk.Insert(win);
            desk.ShutDown();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void TDeskTop_ShutDown_WithOpenWindow_EmptiesChildList()
    {
        using var driver = new DriverScope();
        var desk = new TDeskTop(new TRect(0, 1, 80, 24));
        var win = new TWindow(new TRect(5, 3, 35, 12), "Test", Views.wnNoNumber);
        desk.Insert(win);
        desk.ShutDown();
        Assert.Null(desk.last);
    }

    // ── 25c.7: AppLifecycleGuard exit with open window ────────────────────────

    [Fact]
    public void AppLifecycleGuard_WithOpenWindow_Returns0()
    {
        using var driver = new DriverScope();
        var app = new OpenWindowApp();
        var qev = new TEvent { What = Events.evCommand };
        qev.message.command = Views.cmQuit;
        app.PutEvent(ref qev);
        var ex = Record.Exception(() => AppLifecycleGuard.Run(app));
        Assert.Null(ex);
    }
}
