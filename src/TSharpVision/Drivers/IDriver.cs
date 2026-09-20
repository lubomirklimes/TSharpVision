// Hardware abstraction. Upstream uses static function pointers (TScreen::,
// TMouse::, THWMouse::) to swap drivers at runtime; we collect the same
// surface into one IDriver interface so concrete drivers (Win32 console,
// ANSI/VT, SDL3, NullDriver for tests) implement it.
using TSharpVision;
namespace TSharpVision.Drivers;

/// <summary>Backend contract for character-cell display, input, cursor control, and lifecycle management.</summary>
public interface IDriver
{
    // ---- lifecycle -----------------------------------------------------
    /// <summary>Initializes the backend resources needed for display and input.</summary>
    void Initialize();
    /// <summary>Temporarily relinquishes backend interaction with the host.</summary>
    void Suspend();
    /// <summary>Restores backend operation after suspension.</summary>
    void Resume();
    /// <summary>Releases host resources acquired by the backend.</summary>
    void Shutdown();

    // ---- screen geometry / video mode ---------------------------------
    /// <summary>Returns the current display width in character cells.</summary>
    ushort GetCols();
    /// <summary>Returns the current display height in character cells.</summary>
    ushort GetRows();
    /// <summary>Returns the backend's current logical display mode.</summary>
    TDisplay.SM GetScreenMode();
    /// <summary>Requests a logical display mode; support depends on the backend.</summary>
    void SetScreenMode(TDisplay.SM mode);
    /// <summary>Allocates a cell buffer sized for the backend's current display dimensions.</summary>
    ScreenBuffer AllocateScreenBuffer();
    /// <summary>Clears the requested character-cell area of the display.</summary>
    void ClearScreen(ushort cols, ushort rows);

    // ---- cursor --------------------------------------------------------
    /// <summary>Returns the backend's encoded caret shape and visibility value.</summary>
    ushort GetCursorType();
    /// <summary>Requests an encoded caret shape; zero hides the caret.</summary>
    void SetCursorType(ushort cursorType);
    /// <summary>Moves the caret to a zero-based screen column and row in character cells.</summary>
    void SetCaretPosition(int x, int y);

    // ---- output --------------------------------------------------------
    /// <summary>Writes a width-by-height cell rectangle at a zero-based screen position from a row-ordered source span.</summary>
    void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf);
    /// <summary>Requests an audible notification when supported by the host.</summary>
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
    /// <summary>Whether the backend can deliver mouse input.</summary>
    bool SupportsMouse { get; }
    /// <summary>Whether the backend supports RGB display colors.</summary>
    bool SupportsTrueColor { get; }

    /// <summary>
    /// True for graphical (windowed) drivers such as SDL.
    /// False for text-mode drivers (console, terminal).
    /// </summary>
    bool SupportsGraphics { get; }

    /// <summary>Gets the optional keyboard information this backend reports reliably.</summary>
    /// <remarks>Defaults to <see cref="TSharpVision.Drivers.KeyboardCapabilities.None"/> so existing third-party drivers remain compatible.</remarks>
    KeyboardCapabilities KeyboardCapabilities => KeyboardCapabilities.None;
}
