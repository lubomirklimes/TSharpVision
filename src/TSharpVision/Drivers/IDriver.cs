// Hardware abstraction. Upstream uses static function pointers (TScreen::,
// TMouse::, THWMouse::) to swap drivers at runtime; we collect the same
// surface into one IDriver interface so concrete drivers (Win32 console,
// ANSI/VT, SDL3, NullDriver for tests) implement it.
using TSharpVision;
namespace TSharpVision.Drivers;

public interface IDriver
{
    // ---- lifecycle -----------------------------------------------------
    void Initialize();
    void Suspend();
    void Resume();
    void Shutdown();

    // ---- screen geometry / video mode ---------------------------------
    ushort GetCols();
    ushort GetRows();
    TDisplay.SM GetScreenMode();
    void SetScreenMode(TDisplay.SM mode);
    ScreenBuffer AllocateScreenBuffer();
    void ClearScreen(ushort cols, ushort rows);

    // ---- cursor --------------------------------------------------------
    ushort GetCursorType();
    void SetCursorType(ushort cursorType);
    void SetCaretPosition(int x, int y);

    // ---- output --------------------------------------------------------
    void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf);
    void MakeBeep();

    // ---- input ---------------------------------------------------------
    /// <summary>
    /// Drains pending OS messages into <see cref="TEventQueue"/>. Called
    /// once per outer event-loop iteration by <see cref="TScreen.GetEvent"/>.
    /// </summary>
    void PumpMessages();

    /// <summary>
    /// Tries to read one keyboard event without blocking. Mouse events are
    /// fed into <see cref="TEventQueue"/> via <see cref="PumpMessages"/>
    /// instead, mirroring the asynchronous mouse interrupt model upstream.
    /// </summary>
    bool ReadKeyEvent(out TEvent ev);

    // ---- capability flags ---------------------------------------------
    bool SupportsMouse { get; }
    bool SupportsTrueColor { get; }
}
