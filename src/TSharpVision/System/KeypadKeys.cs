using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>
/// Semantic normalization of numeric-keypad key identities.
/// </summary>
/// <remarks>
/// <para>
/// Drivers report a keypad key under its own identity when the transport can tell it from the main keyboard. Views
/// handle keys by meaning, so <see cref="TProgram.GetEvent"/> gives each such event the key code its main-keyboard
/// equivalent has before the event is dispatched, and keeps the reported identity in
/// <see cref="KeyDownEvent.keypadKey"/>.
/// </para>
/// <para>
/// What a keypad key means is decided from the event alone, in this order:
/// </para>
/// <list type="number">
///   <item><description><b>Enter</b> is <see cref="Keys.kbEnter"/>, or <see cref="Keys.kbCtrlEnter"/> with Ctrl.</description></item>
///   <item><description><b>Text.</b> A key whose event carries text types that text. Its key code becomes the
///   character when the text is one ASCII character and zero otherwise, as for any typed key; the text is never
///   derived from the identity.</description></item>
///   <item><description><b>Navigation.</b> A digit or decimal key without text, with NumLock off in
///   <see cref="KeyDownEvent.controlKeyState"/>, is the cursor key printed on it, with the modifier variants the
///   main cursor keys have. Keypad 5 has none.</description></item>
///   <item><description><b>Otherwise</b> the key code stays the keypad identity, which no view handles: a key
///   without text while NumLock is on (its text, if any, arrives as an event of its own), keypad 5, and an operator
///   key without text.</description></item>
/// </list>
/// <para>
/// <see cref="Keys.kbGrayMinus"/> and <see cref="Keys.kbGrayPlus"/> keep their historical key codes. Like every
/// keypad key they type only the text their event carries, so the character in the legacy scan pair is cleared when
/// there is none.
/// </para>
/// </remarks>
public static class KeypadKeys
{
    /// <summary>Whether <paramref name="keyCode"/> is a numeric-keypad identity a driver reports: <see cref="Keys.kbKeypad0"/> through <see cref="Keys.kbKeypadEnter"/>, <see cref="Keys.kbGrayMinus"/> or <see cref="Keys.kbGrayPlus"/>.</summary>
    /// <remarks><see cref="Keys.kbNumLock"/> is the lock key, not a keypad key, and is never normalized.</remarks>
    public static bool IsKeypadIdentity(ushort keyCode) =>
        keyCode is >= Keys.kbKeypad0 and <= Keys.kbKeypadEnter && (keyCode & 0xFF) == 0
        || keyCode is Keys.kbGrayMinus or Keys.kbGrayPlus;

    /// <summary>
    /// Gives a keypad key press or release its semantic key code and stores the identity the driver reported in
    /// <see cref="KeyDownEvent.keypadKey"/>. Returns whether the event came from the keypad.
    /// </summary>
    /// <remarks>
    /// Modifiers, lock state and text are left as reported. A release carries no text, so the release of a key that
    /// typed text keeps the keypad identity as its key code; <see cref="KeyDownEvent.keypadKey"/> pairs it with the
    /// press. If normalization replaces the key code with a non-keypad identity, subsequent calls leave it alone.
    /// Keys that retain their keypad identity may be processed again with the same result.
    /// </remarks>
    public static bool Normalize(ref TEvent ev)
    {
        if (ev.What != Events.evKeyDown && ev.What != Events.evKeyUp) return false;

        ushort identity = ev.keyDown.keyCode;
        if (!IsKeypadIdentity(identity)) return false;

        ev.keyDown.keypadKey = identity;
        uint state = ev.keyDown.controlKeyState;
        string text = ev.keyDown.text;

        if (identity == Keys.kbKeypadEnter)
            SetKeyCode(ref ev, (state & Keys.kbCtrlShift) != 0 ? Keys.kbCtrlEnter : Keys.kbEnter);
        else if (identity is Keys.kbGrayMinus or Keys.kbGrayPlus)
        {
            if (text.Length == 0) ev.keyDown.charScan.charCode = 0;
        }
        else if (text.Length != 0)
            SetKeyCode(ref ev, text.Length == 1 && text[0] <= 0x7F ? text[0] : (ushort)0);
        else if ((state & Keys.kbNumState) == 0 && NavigationKey(identity, state) is var navigation && navigation != 0)
            SetKeyCode(ref ev, navigation);

        return true;
    }

    /// <summary>
    /// The cursor key a digit or decimal keypad key is with NumLock off, in the modifier variant the main key reports
    /// for <paramref name="controlKeyState"/>; zero for keypad 5 and for any other key.
    /// </summary>
    public static ushort NavigationKey(ushort keypadKey, uint controlKeyState)
    {
        bool control = (controlKeyState & Keys.kbCtrlShift) != 0;
        bool shift = (controlKeyState & Keys.kbShift) != 0;
        return keypadKey switch
        {
            Keys.kbKeypad7 => control ? Keys.kbCtrlHome : Keys.kbHome,
            Keys.kbKeypad8 => Keys.kbUp,
            Keys.kbKeypad9 => control ? Keys.kbCtrlPgUp : Keys.kbPgUp,
            Keys.kbKeypad4 => control ? Keys.kbCtrlLeft : Keys.kbLeft,
            Keys.kbKeypad6 => control ? Keys.kbCtrlRight : Keys.kbRight,
            Keys.kbKeypad1 => control ? Keys.kbCtrlEnd : Keys.kbEnd,
            Keys.kbKeypad2 => Keys.kbDown,
            Keys.kbKeypad3 => control ? Keys.kbCtrlPgDn : Keys.kbPgDn,
            Keys.kbKeypad0 => control && shift ? Keys.kbCtrlShiftIns
                : control ? Keys.kbCtrlIns : shift ? Keys.kbShiftIns : Keys.kbIns,
            Keys.kbKeypadDecimal => control && shift ? Keys.kbCtrlShiftDel
                : control ? Keys.kbCtrlDel : shift ? Keys.kbShiftDel : Keys.kbDel,
            _ => 0,
        };
    }

    private static void SetKeyCode(ref TEvent ev, ushort keyCode)
    {
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
    }
}
