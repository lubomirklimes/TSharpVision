namespace TSharpVision;

/// <summary>Shared character-cell screen state and buffer layered over the active display driver.</summary>
public class TScreen : TDisplay
{
    /// <summary>Screen mode captured before application screen use, for restoration during suspension.</summary>
    public static SM StartupMode { get; set; }
    /// <summary>Cursor-shape value captured at startup or resume for later restoration.</summary>
    public static ushort StartupCursor { get; set; }
    /// <summary>
    /// Logical screen mode of the application: <see cref="TDisplay.SM.CO80"/>, <see cref="TDisplay.SM.BW80"/> or <see cref="TDisplay.SM.Mono"/>
    /// in the low byte, optionally with <see cref="TDisplay.SM.Font8x8"/>. <c>TProgram.InitScreen</c> derives the palette
    /// family, shadows and markers from it.
    /// </summary>
    /// <remarks>
    /// Startup reads it from the driver. <see cref="SetVideoMode"/> sets it to the requested mode even when the driver
    /// keeps its physical display mode, and <see cref="Resume"/> keeps it.
    /// </remarks>
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

    /// <summary>
    /// Selects a logical screen mode: normalizes <paramref name="mode"/> with <see cref="FixCrtMode"/>, passes it to
    /// the driver, refreshes the cached screen data and records the normalized mode in <see cref="ScreenMode"/>.
    /// </summary>
    /// <remarks>
    /// Shipped drivers keep their physical display, so this changes no resolution, font or row count; the cached
    /// dimensions stay whatever the driver reports. Nothing is repainted here: <c>TProgram.SetScreenMode</c> calls
    /// this and then re-derives the palette and redraws the program.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The display driver is not initialized.</exception>
    public static void SetVideoMode(ushort mode)
    {
        SM requested = FixCrtMode(mode);
        SetCrtMode(requested);
        SetCrtData();
        ScreenMode = requested;
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

    /// <summary>
    /// Normalizes a requested mode as Turbo Vision does: a low byte other than <see cref="TDisplay.SM.CO80"/>,
    /// <see cref="TDisplay.SM.BW80"/> or <see cref="TDisplay.SM.Mono"/> becomes <see cref="TDisplay.SM.CO80"/>, and the high byte, which carries
    /// <see cref="TDisplay.SM.Font8x8"/>, is kept.
    /// </summary>
    public static SM FixCrtMode(ushort mode)
    {
        var low = (SM)(mode & 0x00FF);
        if (low != SM.Mono && low != SM.CO80 && low != SM.BW80)
            mode = (ushort)((mode & 0xFF00) | (ushort)SM.CO80);
        return (SM)mode;
    }

    /// <summary>Clears the screen and restores the startup cursor, asking the driver for the startup mode when the logical mode differs.</summary>
    public static void Suspend()
    {
        if (TDisplay.driver == null) return;
        if (StartupMode != ScreenMode)
            SetCrtMode(StartupMode);
        ClearScreen();
        SetCursorType(StartupCursor);
    }

    /// <summary>
    /// Captures the current startup state, asks the driver for the application's mode again when it differs, and
    /// refreshes screen data while keeping the logical <see cref="ScreenMode"/>.
    /// </summary>
    public static void Resume()
    {
        StartupMode = GetCrtMode();
        StartupCursor = GetCursorType();
        SM logical = ScreenMode;
        if (logical != StartupMode)
            SetCrtMode(logical);
        SetCrtData();
        // A driver that keeps its physical mode reports it again here; the application's mode outlives the suspension.
        ScreenMode = logical;
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
        TSharpVision.Drivers.PixelGraphicsSynchronization.BeforePresent();
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
