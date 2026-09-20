namespace TSharpVision;

/// <summary>Shared character-cell screen state and buffer layered over the active display driver.</summary>
public class TScreen : TDisplay
{
    /// <summary>Screen mode captured before application screen use, for restoration during suspension.</summary>
    public static SM StartupMode { get; set; }
    /// <summary>Cursor-shape value captured at startup or resume for later restoration.</summary>
    public static ushort StartupCursor { get; set; }
    /// <summary>Cached mode used by the application screen.</summary>
    public static SM ScreenMode { get; set; }
    /// <summary>Cached display width in character cells.</summary>
    public static ushort ScreenWidth { get; set; }
    /// <summary>Cached display height in character cells.</summary>
    public static ushort ScreenHeight { get; set; }
    /// <summary>Whether the cached screen height exceeds 25 text rows.</summary>
    public static bool HiResScreen { get; set; }
    /// <summary>Legacy display-snow compatibility flag; not used by the current screen implementation.</summary>
    public static bool CheckSnow { get; set; }
    /// <summary>Shared screen-cell buffer allocated by the active driver.</summary>
    public static /*byte[]*/ ScreenBuffer? ScreenBuffer { get; set; }
    /// <summary>Cursor-shape value captured when refreshing CRT data, before hiding the cursor.</summary>
    public static ushort CursorLines { get; set; }

    internal static TPoint ShadowSize { get; set; } = new TPoint(2, 1);

    /// <summary>Initializes or reuses the shared driver, captures startup display state, and allocates its screen buffer.</summary>
    public TScreen() 
        : base() 
    {        
        StartupMode = GetCrtMode();
        StartupCursor = GetCursorType();
        SetCrtData();
        ScreenBuffer = ActiveDriver.AllocateScreenBuffer();
    }

    // No finalizer: TScreen owns no unmanaged resource, and Suspend() drives the shared
    // driver, which a finalizer must not do — see Dispose below.

    /// <summary>Unsupported legacy mode-change path; currently throws NotImplementedException while normalizing the mode.</summary>
    public static void SetVideoMode(ushort mode)
    {
        SetCrtMode(FixCrtMode(mode));
        SetCrtData();
    }

    /// <summary>Clears the driver display using the cached character-cell dimensions.</summary>
    public static void ClearScreen()
    {
        TDisplay.ClearScreen(ScreenWidth, ScreenHeight);
    }

    /// <summary>Refreshes cached mode, dimensions, and cursor shape from the driver, then requests cursor type zero.</summary>
    public static void SetCrtData()
    {
        ScreenMode = GetCrtMode();
        ScreenWidth = GetCols();
        ScreenHeight = GetRows();
        HiResScreen = ScreenHeight > 25;

        CursorLines = GetCursorType();
        SetCursorType(0);
    }

    /// <summary>Unsupported legacy mode normalization; always throws NotImplementedException.</summary>
    public static SM FixCrtMode(ushort mode)
    {
        throw new NotImplementedException("TScreen.FixCrtMode(ushort) není implementováno.");
    }

    /// <summary>Clears the screen and restores startup cursor state; restoring a different mode reaches the unsupported mode-setting path.</summary>
    public static void Suspend()
    {
        if (TDisplay.driver == null) return;
        if (StartupMode != ScreenMode)
            SetCrtMode(StartupMode);
        ClearScreen();
        SetCursorType(StartupCursor);
    }

    /// <summary>Captures current startup state and refreshes screen data; switching to a different application mode reaches the unsupported mode-setting path.</summary>
    public static void Resume()
    {
        StartupMode = GetCrtMode();
        StartupCursor = GetCursorType();
        if (ScreenMode != StartupMode)
            SetCrtMode(ScreenMode);
        SetCrtData();
    }

    private bool _suspendedOnDispose;

    /// <summary>
    /// Restores the startup screen mode and cursor. That drives <see cref="TDisplay.driver"/>,
    /// which is process-wide and may belong to another live application, so it only happens on
    /// a deterministic <see cref="TDisplay.Dispose()"/> call — never from a finalizer.
    /// Idempotent: suspending twice would fight whoever resumed in between.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_suspendedOnDispose)
        {
            _suspendedOnDispose = true;
            Suspend();
        }

        base.Dispose(disposing);
    }

    /// <summary>Writes len cells from the supplied span as one row at the specified screen character-cell coordinates.</summary>
    public static void ScreenWrite(int x, int y, Span<TScreenChar> span, int len)
    {
        ActiveDriver.WriteBuf(x, y, len, 1, span);
    }

    /// <summary>
    /// Pumps driver messages then drains one event into <paramref name="ev"/>:
    /// queued events first (mouse, broadcast, command), then a non-blocking
    /// keyboard probe, otherwise <c>evNothing</c>. Mirrors upstream
    /// <c>TScreen::getEvent</c>.
    /// </summary>
    public static void GetEvent(ref TEvent ev)
    {
        driver?.PumpMessages();

        // 1) queued events (commands, broadcasts, mouse) take priority
        TEventQueue.GetMouseEvent(ref ev);
        if (ev.What != Constants.Events.evNothing)
        {
            if (InputTrace.Enabled)
                InputTrace.LogEvent("Stage3-TScreen.GetEvent(queue)", ev);
            return;
        }

        // 2) probe keyboard
        if (driver != null && driver.ReadKeyEvent(out var keyEv))
        {
            ev = keyEv;
            if (InputTrace.Enabled)
                InputTrace.LogEvent("Stage3-TScreen.GetEvent(key)", ev);
            return;
        }

        ev.What = Constants.Events.evNothing;
    }

    /// <summary>
    /// Quick beep through the driver. Mirrors upstream <c>TScreen::makeBeep</c>.
    /// </summary>
    public static void MakeBeep() => driver?.MakeBeep();
}
