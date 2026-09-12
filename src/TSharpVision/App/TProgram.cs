using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Root view that owns the desktop, menu bar, status line, and application event loop.</summary>
public class TProgram : TGroup
{
    /// <summary>Application palette family selected for rendering standard controls.</summary>
    public enum AP : ushort
    {
        /// <summary>Use the standard color display palette.</summary>
        Color = 0,
        /// <summary>Use the black-and-white display palette.</summary>
        BlackWhite = 1,
        /// <summary>Use the monochrome display palette.</summary>
        Monochrome = 2
    }

    // Entry 62 was \x00 in the earlier port; restored to \x38 (dark-gray-on-cyan)
    // to match the original Borland / RHIDE TV definition.
    /// <summary>Packed display attributes for the standard color application palette.</summary>
    public const string cpColor = "\x71\x70\x78\x74\x20\x28\x24\x17\x1F\x1A\x31\x31\x1E\x71\x00" +
        "\x37\x3F\x3A\x13\x13\x3E\x21\x00\x70\x7F\x7A\x13\x13\x70\x7F\x00" +
        "\x70\x7F\x7A\x13\x13\x70\x70\x7F\x7E\x20\x2B\x2F\x78\x2E\x70\x30" +
        "\x3F\x3E\x1F\x2F\x1A\x20\x72\x31\x31\x30\x2F\x3E\x31\x13\x38\x00";

    /// <summary>Packed display attributes for the black-and-white application palette.</summary>
    public const string cpBlackWhite = "\x70\x70\x78\x7F\x07\x07\x0F\x07\x0F\x07\x70\x70\x07\x70\x00" +
        "\x07\x0F\x07\x70\x70\x07\x70\x00\x70\x7F\x7F\x70\x07\x70\x07\x00" +
        "\x70\x7F\x7F\x70\x07\x70\x70\x7F\x7F\x07\x0F\x0F\x78\x0F\x78\x07" +
        "\x0F\x0F\x0F\x70\x0F\x07\x70\x70\x70\x07\x70\x0F\x07\x07\x07\x00";

    /// <summary>Packed display attributes for the monochrome application palette.</summary>
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

    /// <summary>Application reference initialized to this root program.</summary>
    public TProgram Application { get; set; }
    /// <summary>Status-line view displaying context-sensitive shortcuts.</summary>
    public TStatusLine StatusLine { get; set; }
    /// <summary>Top-level menu view for application commands.</summary>
    public TMenuBar MenuBar { get; set; }
    /// <summary>Desktop group that owns application windows.</summary>
    public TDeskTop DeskTop { get; set; }
    /// <summary>Palette family used to resolve standard application colors.</summary>
    public AP AppPalette { get; set; }

    /// <summary>Single pending event returned before normal queued input.</summary>
    public static TEvent Pending;

    // Idle / CPU-throttle bookkeeping.
    /// <summary>Environment tick count, in milliseconds, at the last idle-time measurement.</summary>
    public static int LastIdleClock;
    /// <summary>Accumulated idle duration in milliseconds.</summary>
    public static int InIdleTime;
    /// <summary>Whether idle processing is currently active.</summary>
    public static bool InIdle;
    /// <summary>Nonzero inhibits yielding the CPU during idle processing.</summary>
    public static byte DoNotReleaseCPU;
    /// <summary>Nonzero disables the standard Alt+number window-selection handler.</summary>
    public static byte DoNotHandleAltNumber;

    // Zeroes the idle accumulator so suspend/resume bridges don't leak
    // measured idle ticks across pauses.
    /// <summary>Clears accumulated idle time and restarts measurement from the current environment tick count.</summary>
    public static void ResetIdleTime()
    {
        InIdleTime = 0;
        LastIdleClock = Environment.TickCount;
    }

    /// <summary>Creates the root at the current screen size, claims the UI thread, and constructs standard application views.</summary>
    public TProgram()
        : base(new TRect(0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight))
    {
        // The thread that builds the program owns the screen and runs the event loop, so it
        // is the thread posted events are delivered on.
        TEventQueue.ClaimUiThread();

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

    /// <summary>Finalizer hook for the root program; deterministic shutdown remains the caller's responsibility.</summary>
    ~TProgram()
    {
    }


    Func<TView, TEvent, bool> HasMouse = (TView p, TEvent ev)
        => ((p.state & Views.sfVisible) != 0) && p.MouseInView(ev.mouse.where);

    /// <inheritdoc />
    public override void GetEvent(ref TEvent @event)
    {
        TScreen.driver.PumpMessages();

        // One posted event per pass. Every loop reaches this method through the owner chain,
        // including the nested one a modal view runs via TGroup.ExecView, so a post always
        // gets delivered straight to its target no matter who currently owns dispatch.
        // Taking only one keeps posts and input from starving each other.
        TEventQueue.DeliverPostedEvent();

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

    /// <inheritdoc />
    public override TPalette GetPalette()
    {
        return palettes[(int)AppPalette];
    }

    /// <inheritdoc />
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

        // Track help window closure: when the non-modal help window is closed it
        // broadcasts cmClosingWindow so we can clear the reference.
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmClosingWindow
            && ReferenceEquals(@event.message.infoPtr, _helpWindow))
        {
            _helpWindow = null;
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

            // application-level cmHelp dispatch. The viewer window is inserted
            // non-modally into the desktop so the user can keep it open while
            // working in other windows. If a help window is already open it is
            // brought to the front instead of opening a second one.
            if (@event.message.command == Views.cmHelp)
            {
                // Close notifications are queued; do not reuse a detached window
                // if another request arrives before its notification is dispatched.
                if (_helpWindow != null && !ReferenceEquals(_helpWindow.owner, DeskTop))
                    _helpWindow = null;
                if (_helpWindow != null)
                {
                    _helpWindow.Select();
                    ClearEvent(ref @event);
                }
                else
                {
                    var hf = GetHelpFile();
                    if (hf != null)
                    {
                        var helpCtxNow = GetHelpCtx();
                        var window = new THelpWindow(hf, helpCtxNow);
                        if (DeskTop != null && ValidView(window) != null)
                        {
                            ExecuteHelp(window);
                            ClearEvent(ref @event);
                        }
                    }
                }
            }
        }
    }

    // application-supplied help-file factory. Default returns null, which suppresses the
    // cmHelp dispatch above. Subclasses override to bind a THelpFile.
    /// <summary>Returns the application help store, or null to disable standard help dispatch; override to supply help topics.</summary>
    public virtual THelpFile GetHelpFile() => null;

    // inserts the help window non-modally into the desktop. Split out so smoke
    // tests can intercept this step without reimplementing the cmHelp dispatch.
    /// <summary>Inserts the help window into the desktop for nonmodal interaction.</summary>
    protected virtual void ExecuteHelp(THelpWindow window)
    {
        if (DeskTop != null)
        {
            DeskTop.Insert(window);
            _helpWindow = window;
        }
    }

    private THelpWindow? _helpWindow;

    /// <summary>Updates context-sensitive status and command notifications between input events.</summary>
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

    /// <summary>Chooses palette and shadow settings for the current logical screen mode.</summary>
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

    /// <summary>Application hook for reporting allocation failure; the default takes no action.</summary>
    public virtual void OutOfMemory() { /* empty by design */ }

    /// <inheritdoc />
    public override void PutEvent(ref TEvent ev) { Pending = ev; }

    /// <summary>Claims the calling thread as the UI thread and runs the modal application event loop until termination.</summary>
    public virtual void Run()
    {
        TEventQueue.ClaimUiThread();
        Execute();
    }

    // Host console resize handler.
    // Called when TProgram.HandleEvent receives cmScreenResized.
    // At this point TScreen.ScreenWidth/Height/ScreenBuffer have already been
    // updated by the driver (in PumpMessages).
    //
    // TProgram::setScreenMode (minus the video-mode flip); applies to all child views via growMode.
    /// <summary>Applies driver-updated screen dimensions and buffer to the root and resizes children using their grow modes.</summary>
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
    /// <summary>Refreshes root geometry, palette, and drawing from the current screen state; the mode argument does not switch the backend mode.</summary>
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

    /// <summary>Returns a view that accepts the validation command; otherwise shuts it down and returns null.</summary>
    public TView ValidView(TView p)
    {
        if (p == null) return null;
        if (!p.Valid(Views.cmValid)) { p.ShutDown(); return null; }
        return p;
    }

    private bool _shutDown;

    /// <summary>The root view has no owner, so it stays a valid post target until shutdown.</summary>
    internal override bool CanReceivePostedEvents => !_shutDown;

    /// <inheritdoc />
    public override void ShutDown()
    {
        _shutDown = true;
        _helpWindow = null;

        StatusLine = null;
        MenuBar = null;
        DeskTop = null;
        base.ShutDown();

        // After base.ShutDown every view of this program is detached, so its pending posts
        // are the undeliverable ones. Dropping them releases the view tree without touching
        // posts that belong to another live application — the post queue is process-wide.
        TEventQueue.DropUndeliverablePosts();
    }

    /// <summary>Hook for temporarily releasing application interaction with the host; the base program takes no action.</summary>
    public virtual void Suspend() { }
    /// <summary>Hook for restoring application interaction after suspension; the base program takes no action.</summary>
    public virtual void Resume() { }

    /// <summary>Creates a one-row status line at the bottom of the supplied cell bounds with the default quit shortcut.</summary>
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
    
    /// <summary>Creates an empty menu bar in the top row of the supplied cell bounds.</summary>
    public virtual TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        return new TMenuBar(r, (TMenu)null);
    }
    
    /// <summary>Creates a desktop within the screen bounds, reserving the menu and status areas.</summary>
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
