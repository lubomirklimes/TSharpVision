using TSharpVision.Constants;

namespace TSharpVision;

public class TProgram : TGroup
{
    public enum AP : ushort
    {
        Color = 0,
        BlackWhite = 1,
        Monochrome = 2
    }

    // Entry 62 was \x00 in the earlier port; restored to \x38 (dark-gray-on-cyan)
    // to match the original Borland / RHIDE TV definition.
    public const string cpColor = "\x71\x70\x78\x74\x20\x28\x24\x17\x1F\x1A\x31\x31\x1E\x71\x00" +
        "\x37\x3F\x3A\x13\x13\x3E\x21\x00\x70\x7F\x7A\x13\x13\x70\x7F\x00" +
        "\x70\x7F\x7A\x13\x13\x70\x70\x7F\x7E\x20\x2B\x2F\x78\x2E\x70\x30" +
        "\x3F\x3E\x1F\x2F\x1A\x20\x72\x31\x31\x30\x2F\x3E\x31\x13\x38\x00";

    public const string cpBlackWhite = "\x70\x70\x78\x7F\x07\x07\x0F\x07\x0F\x07\x70\x70\x07\x70\x00" +
        "\x07\x0F\x07\x70\x70\x07\x70\x00\x70\x7F\x7F\x70\x07\x70\x07\x00" +
        "\x70\x7F\x7F\x70\x07\x70\x70\x7F\x7F\x07\x0F\x0F\x78\x0F\x78\x07" +
        "\x0F\x0F\x0F\x70\x0F\x07\x70\x70\x70\x07\x70\x0F\x07\x07\x07\x00";

    public const string cpMonochrome = "\x70\x07\x07\x0F\x70\x70\x70\x07\x0F\x07\x70\x70\x07\x70\x00" +
        "\x07\x0F\x07\x70\x70\x07\x70\x00\x70\x70\x70\x07\x07\x70\x07\x00" +
        "\x70\x70\x70\x07\x07\x70\x70\x70\x0F\x07\x07\x0F\x70\x0F\x70\x07" +
        "\x0F\x0F\x07\x70\x07\x07\x70\x07\x07\x07\x70\x0F\x07\x07\x07\x00";

    private static readonly TPalette color = new TPalette(cpColor, cpColor.Length);
    private static readonly TPalette blackwhite = new TPalette(cpBlackWhite, cpBlackWhite.Length);
    private static readonly TPalette monochrome = new TPalette(cpMonochrome, cpMonochrome.Length);

    private static readonly TPalette[] palettes =
    {
        color,
        blackwhite,
        monochrome
    };

    public TProgram Application { get; set; }
    public TStatusLine StatusLine { get; set; }
    public TMenuBar MenuBar { get; set; }
    public TDeskTop DeskTop { get; set; }
    public AP AppPalette { get; set; }

    public static TEvent Pending;

    // Idle / CPU-throttle bookkeeping.
    public static int LastIdleClock;
    public static int InIdleTime;
    public static bool InIdle;
    public static byte DoNotReleaseCPU;
    public static byte DoNotHandleAltNumber;

    // Zeroes the idle accumulator so suspend/resume bridges don't leak
    // measured idle ticks across pauses.
    public static void ResetIdleTime()
    {
        InIdleTime = 0;
        LastIdleClock = Environment.TickCount;
    }

    private static readonly string exitText = "Exit";

    public TProgram()
        : base(new TRect(0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight))
    {
        Application = this;
        InitScreen();
        state = Views.sfVisible | Views.sfSelected | Views.sfFocused | Views.sfModal | Views.sfExposed;
        size.x = TScreen.ScreenWidth;
        size.y = TScreen.ScreenHeight;
        options = 0;
        buffer = TScreen.ScreenBuffer;

        StatusLine = InitStatusLine(GetExtent());
        MenuBar = InitMenuBar(GetExtent());
        DeskTop = InitDesktop(GetExtent());

        // Upstream insert order: statusLine, menuBar, deskTop.
        // Each Insert sends the new child to the front of the z-list
        // and selects it, so deskTop ends up as `current`.
        if (StatusLine != null)
            Insert(StatusLine);

        if (MenuBar != null)
            Insert(MenuBar);

        if (DeskTop != null)
            Insert(DeskTop);
    }

    ~TProgram()
    {
    }


    Func<TView, TEvent, bool> HasMouse = (TView p, TEvent ev)
        => ((p.state & Views.sfVisible) != 0) && p.MouseInView(ev.mouse.where);

    public override void GetEvent(ref TEvent @event)
    {
        TScreen.driver.PumpMessages();

        if (Pending.What != Events.evNothing)
        {
            @event = Pending;
            Pending.What = Events.evNothing;
            if (InputTrace.Enabled)
                InputTrace.LogEvent("Stage4-GetEvent(Pending)", @event);
        }
        else
        {
            @event.GetNextEvent(TScreen.driver);
            if (@event.What == Events.evNothing)
            {
                Idle();
            }
            else if (InputTrace.Enabled)
                InputTrace.LogEvent("Stage4-GetEvent(driver)", @event);
        }

        if (StatusLine != null)
        {
            if (((@event.What & Events.evKeyDown) != 0) ||
                (((@event.What & Events.evMouseDown) != 0) &&
                 (FirstThat<TEvent>(HasMouse, @event) == StatusLine)))
            {
                if (InputTrace.Enabled)
                    InputTrace.LogEvent("Stage4-StatusLinePreFilter(before)", @event);
                StatusLine.HandleEvent(ref @event);
                if (InputTrace.Enabled)
                    InputTrace.LogEvent("Stage4-StatusLinePreFilter(after)", @event);
            }
        }
    }

    public override TPalette GetPalette()
    {
        return palettes[(int)AppPalette];
    }

    public override void HandleEvent(ref TEvent @event)
    {
        if (InputTrace.Enabled && @event.What != Events.evNothing)
            InputTrace.LogEvent("Stage5-TProgram.HandleEvent(entry)", @event);

        // Host console resize.
        // Intercept before base.HandleEvent so the event is not dispatched
        // to children (TProgram owns the relayout; children resize via growMode).
        if (@event.What == Events.evCommand
            && @event.message.command == Views.cmScreenResized)
        {
            HandleScreenResize();
            ClearEvent(ref @event);
            return;
        }

        // Alt+1..9 selects the numbered desktop window by broadcasting cmSelectWindowNum.
        // Must run BEFORE base.HandleEvent so the key is processed here first.
        if (DoNotHandleAltNumber == 0 && @event.What == Events.evKeyDown)
        {
            char ac = TMenuView.GetAltChar(
                @event.keyDown.keyCode,
                @event.keyDown.charScan.charCode,
                @event.keyDown.shiftState);
            if (ac >= '1' && ac <= '9'
                && (current == null || current.Valid(Views.cmReleasedFocus)))
            {
                if (DeskTop != null)
                {
                    TEvent selEv = default;
                    selEv.What = Events.evBroadcast;
                    selEv.message.command = Views.cmSelectWindowNum;
                    selEv.message.infoInt = (short)(ac - '0');
                    DeskTop.HandleEvent(ref selEv);
                    if (selEv.What == Events.evNothing)
                        ClearEvent(ref @event);
                }
            }
        }

        base.HandleEvent(ref @event);

        if (@event.What == Events.evCommand)
        {
            if (@event.message.command == Views.cmQuit)
            {
                EndModal(Views.cmQuit);
                ClearEvent(ref @event);
                return;
            }

            // application-level cmHelp dispatch. The viewer window is built
            // lazily via the virtual GetHelpFile() hook and run modally over
            // the desktop with the current focused help context. The static
            // `helpInUse` guard matches upstream's reentrancy interlock.
            // The actual modal exec is delegated to ExecuteHelp so subclasses
            // can intercept without reimplementing the dispatch above.
            if (@event.message.command == Views.cmHelp && !_helpInUse)
            {
                var hf = GetHelpFile();
                if (hf != null)
                {
                    _helpInUse = true;
                    try
                    {
                        var helpCtxNow = GetHelpCtx();
                        var window = new THelpWindow(hf, helpCtxNow);
                        if (ValidView(window) != null)
                            ExecuteHelp(window);
                    }
                    finally
                    {
                        _helpInUse = false;
                    }
                    ClearEvent(ref @event);
                }
            }
        }
    }

    // application-supplied help-file factory. Default returns null, which suppresses the
    // cmHelp dispatch above. Subclasses override to bind a THelpFile.
    public virtual THelpFile GetHelpFile() => null;

    // runs the help window modally over the desktop. Split out so smoke tests can intercept the
    // modal phase without rebuilding the cmHelp dispatch logic above.
    protected virtual void ExecuteHelp(THelpWindow window)
    {
        if (DeskTop != null)
            DeskTop.ExecView(window);
    }

    private static bool _helpInUse;

    public virtual void Idle()
    {
        if (StatusLine != null)
            StatusLine.Update();

        if (commandSetChanged)
        {
            Message(this, Events.evBroadcast, Views.cmCommandSetChanged, null);
            commandSetChanged = false;
        }

        // cooperative yield so the OS can schedule other threads when the event loop is
        // spinning idle. Mirrors upstream CLY_ReleaseCPU().
        // Skipped when DoNotReleaseCPU is set (e.g. real-time or test harnesses).
        if (DoNotReleaseCPU == 0)
            System.Threading.Thread.Yield();
    }

    public virtual void InitScreen()
    {
        if ((TScreen.ScreenMode & (TScreen.SM)0x00FF) != TDisplay.SM.Mono)
        {
            if ((TScreen.ScreenMode & TDisplay.SM.Font8x8) != 0)
                shadowSize.x = 1;
            else
                shadowSize.x = 2;

            shadowSize.y = 1;
            showMarkers = false;

            if ((TScreen.ScreenMode & (TScreen.SM)0x00FF) == TDisplay.SM.BW80)
                AppPalette = AP.BlackWhite;
            else
                AppPalette = AP.Color;
        }
        else
        {
            shadowSize.x = 0;
            shadowSize.y = 0;
            showMarkers = true;
            AppPalette = AP.Monochrome;
        }
    }

    public virtual void OutOfMemory() { /* empty by design */ }

    public override void PutEvent(ref TEvent ev) { Pending = ev; }

    public virtual void Run() { Execute(); }

    // Host console resize handler.
    // Called when TProgram.HandleEvent receives cmScreenResized.
    // At this point TScreen.ScreenWidth/Height/ScreenBuffer have already been
    // updated by the driver (in PumpMessages).
    //
    // TProgram::setScreenMode (minus the video-mode flip); applies to all child views via growMode.
    protected virtual void HandleScreenResize()
    {
        if (InputTrace.Enabled)
            InputTrace.Log("Resize",
                $"HandleScreenResize start: {TScreen.ScreenWidth}x{TScreen.ScreenHeight} " +
                $"bufSame={ReferenceEquals(buffer, TScreen.ScreenBuffer)}");

        // 1. Sync root buffer BEFORE ChangeBounds.
        buffer = TScreen.ScreenBuffer;

        // 2. Clear stale console content BEFORE drawing the new layout.
        TScreen.ClearScreen();

        // 3. Resize root group; propagates sizes to all children via CalcBounds + ChangeBounds. 
        TRect r = new TRect(0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        ChangeBounds(r);

        if (InputTrace.Enabled)
            InputTrace.Log("Resize",
                $"HandleScreenResize after ChangeBounds: bufSame={ReferenceEquals(buffer, TScreen.ScreenBuffer)}");

        // 4. Reset exposure so child groups receive fresh buffers at the new size
        //    on the next draw cycle.
        SetState(Views.sfExposed, false);
        Redraw();
        SetState(Views.sfExposed, true);
    }

    // Real video-mode change is a driver concern; we just refresh the screen geometry from
    // the active driver and re-expose the desktop.
    public void SetScreenMode(ushort mode)
    {
        TRect r;
        InitScreen();
        // syncScreenBuffer() equivalent: re-grab the driver's buffer.
        buffer = TScreen.ScreenBuffer;
        r = new TRect(0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        ChangeBounds(r);
        SetState(Views.sfExposed, false);
        Redraw();
        SetState(Views.sfExposed, true);
    }

    public TView ValidView(TView p)
    {
        if (p == null) return null;
        if (!p.Valid(Views.cmValid)) { p.ShutDown(); return null; }
        return p;
    }

    public override void ShutDown()
    {
        StatusLine = null;
        MenuBar = null;
        DeskTop = null;
        base.ShutDown();
    }

    public virtual void Suspend() { }
    public virtual void Resume() { }

    public virtual TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;

        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            //new TStatusItem("~F1~ Help", 1, CommandCodes.cmHelp) +
            new TStatusItem(TSharpVisionIntl.Get("Status_AltX_ExitDash", "~Alt-X~ Exit"), Keys.kbAltX, Views.cmQuit) +
            new TStatusItem(null, Keys.kbF10, Views.cmMenu) +
            new TStatusItem(null, Keys.kbAltF3, Views.cmClose) +
            new TStatusItem(null, Keys.kbF5, Views.cmZoom) +
            new TStatusItem(null, Keys.kbCtrlF5, Views.cmResize)
            );
    }
    
    public virtual TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        return new TMenuBar(r, (TMenu)null);
    }
    
    public virtual TDeskTop InitDesktop(TRect r)
    {
        if (MenuBar != null)
            r.a.y += MenuBar.size.y;
        else
            r.a.y++;

        if (StatusLine != null)
            r.b.y -= StatusLine.size.y;
        else
            r.b.y--;

        return new TDeskTop(r);
    }
}
