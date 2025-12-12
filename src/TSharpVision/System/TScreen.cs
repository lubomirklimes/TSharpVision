namespace TSharpVision;

public class TScreen : TDisplay
{
    public static SM StartupMode { get; set; }
    public static ushort StartupCursor { get; set; }
    public static SM ScreenMode { get; set; }
    public static ushort ScreenWidth { get; set; }
    public static ushort ScreenHeight { get; set; }
    public static bool HiResScreen { get; set; }
    public static bool CheckSnow { get; set; }
    public static /*byte[]*/ ScreenBuffer ScreenBuffer { get; set; }
    public static ushort CursorLines { get; set; }

    public TScreen() 
        : base() 
    {        
        StartupMode = GetCrtMode();
        StartupCursor = GetCursorType();
        SetCrtData();
        ScreenBuffer = driver.AllocateScreenBuffer();
    }

    ~TScreen() 
    {
        Dispose(disposing: false);
    }

    public static void SetVideoMode(ushort mode)
    {
        SetCrtMode(FixCrtMode(mode));
        SetCrtData();
    }

    public static void ClearScreen()
    {
        TDisplay.ClearScreen(ScreenWidth, ScreenHeight);
    }

    public static void SetCrtData()
    {
        ScreenMode = GetCrtMode();
        ScreenWidth = GetCols();
        ScreenHeight = GetRows();
        HiResScreen = ScreenHeight > 25;

        CursorLines = GetCursorType();
        SetCursorType(0);
    }

    public static SM FixCrtMode(ushort mode)
    {
        throw new NotImplementedException("TScreen.FixCrtMode(ushort) není implementováno.");
    }

    public static void Suspend()
    {
        if (TDisplay.driver == null) return;
        if (StartupMode != ScreenMode)
            SetCrtMode(StartupMode);
        ClearScreen();
        SetCursorType(StartupCursor);
    }

    public static void Resume()
    {
        StartupMode = GetCrtMode();
        StartupCursor = GetCursorType();
        if (ScreenMode != StartupMode)
            SetCrtMode(ScreenMode);
        SetCrtData();
    }

    private bool _suspendedOnDispose;

    protected override void Dispose(bool disposing)
    {
        if (!_suspendedOnDispose)
        {
            _suspendedOnDispose = true;
            Suspend();
        }
        base.Dispose(disposing);
    }

    public static void ScreenWrite(int x, int y, Span<TScreenChar> span, int len)
    {
        driver.WriteBuf(x, y, len, 1, span);
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
