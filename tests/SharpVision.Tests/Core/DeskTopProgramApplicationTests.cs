// Migrated from SharpVision.Demo/Program.cs lines 842-1108.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class DeskTopProgramApplicationTests : IDisposable
{
    private readonly DriverScope _ds;
    public DeskTopProgramApplicationTests() => _ds = new DriverScope();
    public void Dispose() => _ds.Dispose();

    // ── TDeskTop ───────────────────────────────────────────────

    [Fact]
    public void DeskTop_Constructor_GrowMode()
    {
        var dt = new TDeskTop(new TRect(0, 1, 80, 24));
        Assert.True((dt.growMode & (Views.gfGrowHiX | Views.gfGrowHiY))
                    == (Views.gfGrowHiX | Views.gfGrowHiY));
    }

    [Fact]
    public void DeskTop_Constructor_CreatesBackground()
    {
        var dt = new TDeskTop(new TRect(0, 1, 80, 24));
        Assert.NotNull(dt.background);
        Assert.Same(dt, dt.background.owner);
        Assert.Equal(TDeskTop.defaultBkgrnd, dt.background.pattern);
        Assert.Equal(dt.GetExtent(), dt.background.GetBounds());
        Assert.Equal(1, dt.background.GetPalette().Size);
    }

    [Fact]
    public void Background_ChangePattern_UpdatesField()
    {
        var dt = new TDeskTop(new TRect(0, 1, 80, 24));
        dt.SetState(Views.sfActive,  true);
        dt.SetState(Views.sfExposed, true);
        dt.background.ChangePattern('▓');
        Assert.Equal('▓', dt.background.pattern);
    }

    [Fact]
    public void DeskTop_GetOptions_SetOptions_RoundTrip()
    {
        var dt = new TDeskTop(new TRect(0, 1, 80, 24));
        Assert.Equal(0u, dt.GetOptions());
        dt.SetOptions(Views.dsktTileVertical);
        Assert.Equal((uint)Views.dsktTileVertical, dt.GetOptions());
        dt.SetOptions(0);
        Assert.Equal(0u, dt.GetOptions());
    }

    [Fact]
    public void DeskTop_CanShowCursor_TrueWhenUnlocked()
    {
        var dt = new TDeskTop(new TRect(0, 1, 80, 24));
        Assert.True(dt.CanShowCursor());
    }

    [Fact]
    public void Tile_FourWindows_Covers2x2Grid()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        dt.SetState(Views.sfActive,  true);
        dt.SetState(Views.sfExposed, true);
        var windows = new TWindow[4];
        for (int i = 0; i < 4; i++)
        {
            windows[i] = new TWindow(new TRect(0, 0, 16, 6), "", (ushort)(i + 1));
            windows[i].options |= Views.ofTileable;
            dt.Insert(windows[i]);
        }
        var r = dt.GetExtent();
        dt.Tile(r);
        Assert.Equal(new TPoint(20, 10), windows[0].size);
        Assert.Equal(new TPoint(20, 10), windows[3].size);
        int xMax = windows.Max(w => w.GetBounds().b.x);
        int yMax = windows.Max(w => w.GetBounds().b.y);
        Assert.Equal(r.b.x, xMax);
        Assert.Equal(r.b.y, yMax);
    }

    [Fact]
    public void Cascade_ThreeWindows_StaggersBy1()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        dt.SetState(Views.sfActive,  true);
        dt.SetState(Views.sfExposed, true);
        var ws = new TWindow[3];
        for (int i = 0; i < 3; i++)
        {
            ws[i] = new TWindow(new TRect(0, 0, 16, 6), "", (ushort)(i + 1));
            ws[i].options |= Views.ofTileable;
            dt.Insert(ws[i]);
        }
        dt.Cascade(dt.GetExtent());
        var origins = ws.Select(w => w.origin).OrderBy(o => o.x).ToArray();
        Assert.Equal(1, origins[1].x - origins[0].x);
        Assert.Equal(1, origins[2].x - origins[1].x);
        Assert.Equal(1, origins[1].y - origins[0].y);
        Assert.Equal(1, origins[2].y - origins[1].y);
    }

    [Fact]
    public void Tile_ImpossibleLayout_InvokesTileError()
    {
        bool errorFired = false;
        var tiny = new TileErrorDeskTop(new TRect(0, 0, 1, 10), () => errorFired = true);
        tiny.SetOptions(Views.dsktTileVertical);
        tiny.SetState(Views.sfActive,  true);
        tiny.SetState(Views.sfExposed, true);
        for (int i = 0; i < 2; i++)
        {
            var w = new TWindow(new TRect(0, 0, 16, 6), "", (ushort)(i + 1));
            w.options |= Views.ofTileable;
            tiny.Insert(w);
        }
        tiny.Tile(tiny.GetExtent());
        Assert.True(errorFired);
    }

    [Fact]
    public void DeskTop_CmNext_RotatesFocus()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        dt.SetState(Views.sfActive,  true);
        dt.SetState(Views.sfExposed, true);
        var n1 = new TWindow(new TRect(2, 2, 18, 10), "", 1);
        var n2 = new TWindow(new TRect(4, 4, 20, 12), "", 2);
        dt.Insert(n1); dt.Insert(n2);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmNext;
        dt.HandleEvent(ref ev);
        Assert.NotNull(dt.current);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void DeskTop_CmPrev_ChangesCurrentAndClearsEvent()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        dt.SetState(Views.sfActive,  true);
        dt.SetState(Views.sfExposed, true);
        var n1 = new TWindow(new TRect(2, 2, 18, 10), "", 1);
        var n2 = new TWindow(new TRect(4, 4, 20, 12), "", 2);
        dt.Insert(n1); dt.Insert(n2);
        var saved = dt.current;
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmPrev;
        dt.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.NotNull(dt.current);
        Assert.NotSame(saved, dt.current);
    }

    [Fact]
    public void DeskTop_UnhandledCommand_PassesThrough()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmCancel;
        dt.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
    }

    [Fact]
    public void DeskTop_ShutDown_NullsBackground()
    {
        var dt = new TDeskTop(new TRect(0, 0, 40, 20));
        Assert.NotNull(dt.background);
        dt.ShutDown();
        Assert.Null(dt.background);
        Assert.Null(dt.last);
    }

    // ── TProgram ───────────────────────────────────────────────

    [Fact]
    public void TProgram_Constructor_WiresApplication()
    {
        var app = new TestProgram();
        try
        {
            Assert.Same(app, app.Application);
            Assert.NotNull(app.StatusLine);
            Assert.NotNull(app.MenuBar);
            Assert.NotNull(app.DeskTop);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_Constructor_State()
    {
        var app = new TestProgram();
        try
        {
            Assert.True((app.state & (Views.sfVisible | Views.sfSelected
                                    | Views.sfFocused | Views.sfModal | Views.sfExposed))
                        == (Views.sfVisible | Views.sfSelected
                          | Views.sfFocused | Views.sfModal | Views.sfExposed));
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_Constructor_SizeMatchesScreen()
    {
        var app = new TestProgram();
        try
        {
            Assert.Equal(new TPoint(TScreen.ScreenWidth, TScreen.ScreenHeight), app.size);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_Constructor_LayoutRows()
    {
        var app = new TestProgram();
        try
        {
            Assert.Equal(1, app.MenuBar.size.y);
            Assert.Equal(app.size.y, app.StatusLine.GetBounds().b.y);
            Assert.Equal(app.MenuBar.size.y,               app.DeskTop.GetBounds().a.y);
            Assert.Equal(app.size.y - app.StatusLine.size.y, app.DeskTop.GetBounds().b.y);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_PutEvent_GetEvent_ConsumePending()
    {
        var app = new TestProgram();
        try
        {
            var stash = new TEvent { What = Events.evCommand };
            stash.message.command = Views.cmHelp;
            app.PutEvent(ref stash);
            Assert.Equal(Events.evCommand, TProgram.Pending.What);
            Assert.Equal(Views.cmHelp, TProgram.Pending.message.command);

            TEvent consumed = default;
            app.GetEvent(ref consumed);
            Assert.Equal(Events.evCommand, consumed.What);
            Assert.Equal(Views.cmHelp, consumed.message.command);
            Assert.Equal(Events.evNothing, TProgram.Pending.What);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_HandleEvent_CmQuit_SetsEndState()
    {
        var app = new TestProgram();
        try
        {
            var ev = new TEvent { What = Events.evCommand };
            ev.message.command = Views.cmQuit;
            app.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(Views.cmQuit, app.endState);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_Run_ExitsOnCmQuit()
    {
        var app2 = new TestProgram();
        var quit = new TEvent { What = Events.evCommand };
        quit.message.command = Views.cmQuit;
        app2.PutEvent(ref quit);
        app2.Run();
        Assert.Equal(Views.cmQuit, app2.endState);
    }

    [Fact]
    public void TProgram_ValidView_ReturnsSameOrNull()
    {
        var app = new TestProgram();
        try
        {
            var v = new ProbeView(new TRect(0, 0, 5, 5));
            Assert.Same(v, app.ValidView(v));
            Assert.Null(app.ValidView(null));
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TProgram_ShutDown_NilsPointers()
    {
        var app3 = new TestProgram();
        app3.ShutDown();
        Assert.Null(app3.StatusLine);
        Assert.Null(app3.MenuBar);
        Assert.Null(app3.DeskTop);
    }

    [Fact]
    public void TProgram_Constructor_ZListOrder()
    {
        var app = new TestProgram();
        try
        {
            Assert.Same(app.DeskTop, app.current);
            Assert.Same(app.StatusLine, app.last);
        }
        finally { app.ShutDown(); }
    }

    // ── TApplication ───────────────────────────────────────────

    [Fact]
    public void TApplication_Constructor_WiresApplication()
    {
        var app = new TApplication();
        try
        {
            Assert.Same(app, app.Application);
            Assert.NotNull(app.StatusLine);
            Assert.NotNull(app.MenuBar);
            Assert.NotNull(app.DeskTop);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TApplication_Suspend_Resume_ResetsIdleTime()
    {
        var app = new TApplication();
        try
        {
            TProgram.InIdleTime = 12345;
            app.Suspend();
            app.Resume();
            Assert.Equal(0, TProgram.InIdleTime);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TApplication_ResetIdleTime_ZerosCounter()
    {
        TProgram.InIdleTime = 99;
        TProgram.ResetIdleTime();
        Assert.Equal(0, TProgram.InIdleTime);
    }

    [Fact]
    public void TApplication_Defaults_DoNotReleaseCPU()
    {
        var app = new TApplication();
        try
        {
            Assert.Equal(0, TProgram.DoNotReleaseCPU);
            Assert.Equal(0, TProgram.DoNotHandleAltNumber);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TApplication_Run_ExitsOnCmQuit()
    {
        var app = new TApplication();
        var qev = new TEvent { What = Events.evCommand };
        qev.message.command = Views.cmQuit;
        app.PutEvent(ref qev);
        app.Run();
        Assert.Equal(Views.cmQuit, app.endState);
    }

    [Fact]
    public void TApplication_ShutDown_NilsDeskTop()
    {
        var app = new TApplication();
        app.ShutDown();
        Assert.Null(app.DeskTop);
    }
}
