namespace TSharpVision.Constants;

/// <summary>
/// Turbo Vision keyboard scancodes. Layout taken from tkeys.h; values are
/// preserved verbatim so events round-trip with C++ tvision streams.
/// </summary>
public static class Keys
{
    // Normal keys
    /// <summary>Key code for Space, used in the key-down event payload.</summary>
    public const ushort kbSpace = 0x0034;

    // Control keys
    /// <summary>Key code for Control+A, used in the key-down event payload.</summary>
    public const ushort kbCtrlA = 0x0001;
    /// <summary>Key code for Control+B, used in the key-down event payload.</summary>
    public const ushort kbCtrlB = 0x0002;
    /// <summary>Key code for Control+C, used in the key-down event payload.</summary>
    public const ushort kbCtrlC = 0x0003;
    /// <summary>Key code for Control+D, used in the key-down event payload.</summary>
    public const ushort kbCtrlD = 0x0004;
    /// <summary>Key code for Control+E, used in the key-down event payload.</summary>
    public const ushort kbCtrlE = 0x0005;
    /// <summary>Key code for Control+F, used in the key-down event payload.</summary>
    public const ushort kbCtrlF = 0x0006;
    /// <summary>Key code for Control+G, used in the key-down event payload.</summary>
    public const ushort kbCtrlG = 0x0007;
    /// <summary>Key code for Control+H, used in the key-down event payload.</summary>
    public const ushort kbCtrlH = 0x0008;
    /// <summary>Key code for Control+I, used in the key-down event payload.</summary>
    public const ushort kbCtrlI = 0x0009;
    /// <summary>Key code for Control+J, used in the key-down event payload.</summary>
    public const ushort kbCtrlJ = 0x000A;
    /// <summary>Key code for Control+K, used in the key-down event payload.</summary>
    public const ushort kbCtrlK = 0x000B;
    /// <summary>Key code for Control+L, used in the key-down event payload.</summary>
    public const ushort kbCtrlL = 0x000C;
    /// <summary>Key code for Control+M, used in the key-down event payload.</summary>
    public const ushort kbCtrlM = 0x000D;
    /// <summary>Key code for Control+N, used in the key-down event payload.</summary>
    public const ushort kbCtrlN = 0x000E;
    /// <summary>Key code for Control+O, used in the key-down event payload.</summary>
    public const ushort kbCtrlO = 0x000F;
    /// <summary>Key code for Control+P, used in the key-down event payload.</summary>
    public const ushort kbCtrlP = 0x0010;
    /// <summary>Key code for Control+Q, used in the key-down event payload.</summary>
    public const ushort kbCtrlQ = 0x0011;
    /// <summary>Key code for Control+R, used in the key-down event payload.</summary>
    public const ushort kbCtrlR = 0x0012;
    /// <summary>Key code for Control+S, used in the key-down event payload.</summary>
    public const ushort kbCtrlS = 0x0013;
    /// <summary>Key code for Control+T, used in the key-down event payload.</summary>
    public const ushort kbCtrlT = 0x0014;
    /// <summary>Key code for Control+U, used in the key-down event payload.</summary>
    public const ushort kbCtrlU = 0x0015;
    /// <summary>Key code for Control+V, used in the key-down event payload.</summary>
    public const ushort kbCtrlV = 0x0016;
    /// <summary>Key code for Control+W, used in the key-down event payload.</summary>
    public const ushort kbCtrlW = 0x0017;
    /// <summary>Key code for Control+X, used in the key-down event payload.</summary>
    public const ushort kbCtrlX = 0x0018;
    /// <summary>Key code for Control+Y, used in the key-down event payload.</summary>
    public const ushort kbCtrlY = 0x0019;
    /// <summary>Key code for Control+Z, used in the key-down event payload.</summary>
    public const ushort kbCtrlZ = 0x001A;

    // Extended key codes
    /// <summary>Key code for Escape, used in the key-down event payload.</summary>
    public const ushort kbEsc = 0x011B;
    /// <summary>Key code for Alt+Space, used in the key-down event payload.</summary>
    public const ushort kbAltSpace = 0x0200;
    /// <summary>Key code for Control+Insert, used in the key-down event payload.</summary>
    public const ushort kbCtrlIns = 0x0400;
    /// <summary>Key code for Shift+Insert, used in the key-down event payload.</summary>
    public const ushort kbShiftIns = 0x0500;
    /// <summary>Key code for Control+Delete, used in the key-down event payload.</summary>
    public const ushort kbCtrlDel = 0x0600;
    /// <summary>Key code for Shift+Delete, used in the key-down event payload.</summary>
    public const ushort kbShiftDel = 0x0700;
    /// <summary>Key code for Control+Shift+Insert, used in the key-down event payload.</summary>
    public const ushort kbCtrlShiftIns = 0x01cd;
    /// <summary>Key code for Control+Shift+Delete, used in the key-down event payload.</summary>
    public const ushort kbCtrlShiftDel = 0x01ce;
    /// <summary>Key code for Backspace, used in the key-down event payload.</summary>
    public const ushort kbBack = 0x0E08;
    /// <summary>Key code for Control+Backspace, used in the key-down event payload.</summary>
    public const ushort kbCtrlBack = 0x0E7F;
    /// <summary>Key code for Shift+Tab, used in the key-down event payload.</summary>
    public const ushort kbShiftTab = 0x0F00;
    /// <summary>Key code for Tab, used in the key-down event payload.</summary>
    public const ushort kbTab = 0x0F09;

    /// <summary>Key code for Alt+A, used in the key-down event payload.</summary>
    public const ushort kbAltA = 0x1E00;
    /// <summary>Key code for Alt+B, used in the key-down event payload.</summary>
    public const ushort kbAltB = 0x3000;
    /// <summary>Key code for Alt+C, used in the key-down event payload.</summary>
    public const ushort kbAltC = 0x2E00;
    /// <summary>Key code for Alt+D, used in the key-down event payload.</summary>
    public const ushort kbAltD = 0x2000;
    /// <summary>Key code for Alt+E, used in the key-down event payload.</summary>
    public const ushort kbAltE = 0x1200;
    /// <summary>Key code for Alt+F, used in the key-down event payload.</summary>
    public const ushort kbAltF = 0x2100;
    /// <summary>Key code for Alt+G, used in the key-down event payload.</summary>
    public const ushort kbAltG = 0x2200;
    /// <summary>Key code for Alt+H, used in the key-down event payload.</summary>
    public const ushort kbAltH = 0x2300;
    /// <summary>Key code for Alt+I, used in the key-down event payload.</summary>
    public const ushort kbAltI = 0x1700;
    /// <summary>Key code for Alt+J, used in the key-down event payload.</summary>
    public const ushort kbAltJ = 0x2400;
    /// <summary>Key code for Alt+K, used in the key-down event payload.</summary>
    public const ushort kbAltK = 0x2500;
    /// <summary>Key code for Alt+L, used in the key-down event payload.</summary>
    public const ushort kbAltL = 0x2600;
    /// <summary>Key code for Alt+M, used in the key-down event payload.</summary>
    public const ushort kbAltM = 0x3200;
    /// <summary>Key code for Alt+N, used in the key-down event payload.</summary>
    public const ushort kbAltN = 0x3100;
    /// <summary>Key code for Alt+O, used in the key-down event payload.</summary>
    public const ushort kbAltO = 0x1800;
    /// <summary>Key code for Alt+P, used in the key-down event payload.</summary>
    public const ushort kbAltP = 0x1900;
    /// <summary>Key code for Alt+Q, used in the key-down event payload.</summary>
    public const ushort kbAltQ = 0x1000;
    /// <summary>Key code for Alt+R, used in the key-down event payload.</summary>
    public const ushort kbAltR = 0x1300;
    /// <summary>Key code for Alt+S, used in the key-down event payload.</summary>
    public const ushort kbAltS = 0x1F00;
    /// <summary>Key code for Alt+T, used in the key-down event payload.</summary>
    public const ushort kbAltT = 0x1400;
    /// <summary>Key code for Alt+U, used in the key-down event payload.</summary>
    public const ushort kbAltU = 0x1600;
    /// <summary>Key code for Alt+V, used in the key-down event payload.</summary>
    public const ushort kbAltV = 0x2F00;
    /// <summary>Key code for Alt+W, used in the key-down event payload.</summary>
    public const ushort kbAltW = 0x1100;
    /// <summary>Key code for Alt+X, used in the key-down event payload.</summary>
    public const ushort kbAltX = 0x2D00;
    /// <summary>Key code for Alt+Y, used in the key-down event payload.</summary>
    public const ushort kbAltY = 0x1500;
    /// <summary>Key code for Alt+Z, used in the key-down event payload.</summary>
    public const ushort kbAltZ = 0x2C00;

    /// <summary>Key code for Control+Enter, used in the key-down event payload.</summary>
    public const ushort kbCtrlEnter = 0x1C0A;
    /// <summary>Key code for Enter, used in the key-down event payload.</summary>
    public const ushort kbEnter = 0x1C0D;
    /// <summary>Key code for F1, used in the key-down event payload.</summary>
    public const ushort kbF1 = 0x3B00;
    /// <summary>Key code for F2, used in the key-down event payload.</summary>
    public const ushort kbF2 = 0x3C00;
    /// <summary>Key code for F3, used in the key-down event payload.</summary>
    public const ushort kbF3 = 0x3D00;
    /// <summary>Key code for F4, used in the key-down event payload.</summary>
    public const ushort kbF4 = 0x3E00;
    /// <summary>Key code for F5, used in the key-down event payload.</summary>
    public const ushort kbF5 = 0x3F00;
    /// <summary>Key code for F6, used in the key-down event payload.</summary>
    public const ushort kbF6 = 0x4000;
    /// <summary>Key code for F7, used in the key-down event payload.</summary>
    public const ushort kbF7 = 0x4100;
    /// <summary>Key code for F8, used in the key-down event payload.</summary>
    public const ushort kbF8 = 0x4200;
    /// <summary>Key code for F9, used in the key-down event payload.</summary>
    public const ushort kbF9 = 0x4300;
    /// <summary>Key code for F10, used in the key-down event payload.</summary>
    public const ushort kbF10 = 0x4400;
    /// <summary>Key code for F11, used in the key-down event payload.</summary>
    public const ushort kbF11 = 0x5700;
    /// <summary>Key code for F12, used in the key-down event payload.</summary>
    public const ushort kbF12 = 0x5800;

    /// <summary>Key code for Home, used in the key-down event payload.</summary>
    public const ushort kbHome = 0x4700;
    /// <summary>Key code for Up, used in the key-down event payload.</summary>
    public const ushort kbUp = 0x4800;
    /// <summary>Key code for Page Up, used in the key-down event payload.</summary>
    public const ushort kbPgUp = 0x4900;
    /// <summary>Key code for Left, used in the key-down event payload.</summary>
    public const ushort kbLeft = 0x4B00;
    /// <summary>Key code for Right, used in the key-down event payload.</summary>
    public const ushort kbRight = 0x4D00;
    /// <summary>Key code for End, used in the key-down event payload.</summary>
    public const ushort kbEnd = 0x4F00;
    /// <summary>Key code for Down, used in the key-down event payload.</summary>
    public const ushort kbDown = 0x5000;
    /// <summary>Key code for Page Down, used in the key-down event payload.</summary>
    public const ushort kbPgDn = 0x5100;
    /// <summary>Key code for Insert, used in the key-down event payload.</summary>
    public const ushort kbIns = 0x5200;
    /// <summary>Key code for Delete, used in the key-down event payload.</summary>
    public const ushort kbDel = 0x5300;

    /// <summary>Key code for keypad minus, used in the key-down event payload.</summary>
    public const ushort kbGrayMinus = 0x4A2D;
    /// <summary>Key code for keypad plus, used in the key-down event payload.</summary>
    public const ushort kbGrayPlus = 0x4E2B;

    /// <summary>Key code for Shift+F1, used in the key-down event payload.</summary>
    public const ushort kbShiftF1 = 0x5400;
    /// <summary>Key code for Shift+F2, used in the key-down event payload.</summary>
    public const ushort kbShiftF2 = 0x5500;
    /// <summary>Key code for Shift+F3, used in the key-down event payload.</summary>
    public const ushort kbShiftF3 = 0x5600;
    /// <summary>Key code for Shift+F4, used in the key-down event payload.</summary>
    public const ushort kbShiftF4 = 0x5700;
    /// <summary>Key code for Shift+F5, used in the key-down event payload.</summary>
    public const ushort kbShiftF5 = 0x5800;
    /// <summary>Key code for Shift+F6, used in the key-down event payload.</summary>
    public const ushort kbShiftF6 = 0x5900;
    /// <summary>Key code for Shift+F7, used in the key-down event payload.</summary>
    public const ushort kbShiftF7 = 0x5A00;
    /// <summary>Key code for Shift+F8, used in the key-down event payload.</summary>
    public const ushort kbShiftF8 = 0x5B00;
    /// <summary>Key code for Shift+F9, used in the key-down event payload.</summary>
    public const ushort kbShiftF9 = 0x5C00;
    /// <summary>Key code for Shift+F10, used in the key-down event payload.</summary>
    public const ushort kbShiftF10 = 0x5D00;
    /// <summary>Key code for Shift+F11, used in the key-down event payload.</summary>
    public const ushort kbShiftF11 = 0x8700;
    /// <summary>Key code for Shift+F12, used in the key-down event payload.</summary>
    public const ushort kbShiftF12 = 0x8800;

    /// <summary>Key code for Control+F1, used in the key-down event payload.</summary>
    public const ushort kbCtrlF1 = 0x5E00;
    /// <summary>Key code for Control+F2, used in the key-down event payload.</summary>
    public const ushort kbCtrlF2 = 0x5F00;
    /// <summary>Key code for Control+F3, used in the key-down event payload.</summary>
    public const ushort kbCtrlF3 = 0x6000;
    /// <summary>Key code for Control+F4, used in the key-down event payload.</summary>
    public const ushort kbCtrlF4 = 0x6100;
    /// <summary>Key code for Control+F5, used in the key-down event payload.</summary>
    public const ushort kbCtrlF5 = 0x6200;
    /// <summary>Key code for Control+F6, used in the key-down event payload.</summary>
    public const ushort kbCtrlF6 = 0x6300;
    /// <summary>Key code for Control+F7, used in the key-down event payload.</summary>
    public const ushort kbCtrlF7 = 0x6400;
    /// <summary>Key code for Control+F8, used in the key-down event payload.</summary>
    public const ushort kbCtrlF8 = 0x6500;
    /// <summary>Key code for Control+F9, used in the key-down event payload.</summary>
    public const ushort kbCtrlF9 = 0x6600;
    /// <summary>Key code for Control+F10, used in the key-down event payload.</summary>
    public const ushort kbCtrlF10 = 0x6700;
    /// <summary>Key code for Control+F11, used in the key-down event payload.</summary>
    public const ushort kbCtrlF11 = 0x8900;
    /// <summary>Key code for Control+F12, used in the key-down event payload.</summary>
    public const ushort kbCtrlF12 = 0x8A00;

    /// <summary>Key code for Alt+F1, used in the key-down event payload.</summary>
    public const ushort kbAltF1 = 0x6800;
    /// <summary>Key code for Alt+F2, used in the key-down event payload.</summary>
    public const ushort kbAltF2 = 0x6900;
    /// <summary>Key code for Alt+F3, used in the key-down event payload.</summary>
    public const ushort kbAltF3 = 0x6A00;
    /// <summary>Key code for Alt+F4, used in the key-down event payload.</summary>
    public const ushort kbAltF4 = 0x6B00;
    /// <summary>Key code for Alt+F5, used in the key-down event payload.</summary>
    public const ushort kbAltF5 = 0x6C00;
    /// <summary>Key code for Alt+F6, used in the key-down event payload.</summary>
    public const ushort kbAltF6 = 0x6D00;
    /// <summary>Key code for Alt+F7, used in the key-down event payload.</summary>
    public const ushort kbAltF7 = 0x6E00;
    /// <summary>Key code for Alt+F8, used in the key-down event payload.</summary>
    public const ushort kbAltF8 = 0x6F00;
    /// <summary>Key code for Alt+F9, used in the key-down event payload.</summary>
    public const ushort kbAltF9 = 0x7000;
    /// <summary>Key code for Alt+F10, used in the key-down event payload.</summary>
    public const ushort kbAltF10 = 0x7100;
    /// <summary>Key code for Alt+F11, used in the key-down event payload.</summary>
    public const ushort kbAltF11 = 0x8B00;
    /// <summary>Key code for Alt+F12, used in the key-down event payload.</summary>
    public const ushort kbAltF12 = 0x8C00;

    /// <summary>Key code for Control+Print Screen, used in the key-down event payload.</summary>
    public const ushort kbCtrlPrtSc = 0x7200;
    /// <summary>Key code for Control+Left, used in the key-down event payload.</summary>
    public const ushort kbCtrlLeft = 0x7300;
    /// <summary>Key code for Control+Right, used in the key-down event payload.</summary>
    public const ushort kbCtrlRight = 0x7400;
    /// <summary>Key code for Control+End, used in the key-down event payload.</summary>
    public const ushort kbCtrlEnd = 0x7500;
    /// <summary>Key code for Control+Page Down, used in the key-down event payload.</summary>
    public const ushort kbCtrlPgDn = 0x7600;
    /// <summary>Key code for Control+Home, used in the key-down event payload.</summary>
    public const ushort kbCtrlHome = 0x7700;

    /// <summary>Key code for Alt+1, used in the key-down event payload.</summary>
    public const ushort kbAlt1 = 0x7800;
    /// <summary>Key code for Alt+2, used in the key-down event payload.</summary>
    public const ushort kbAlt2 = 0x7900;
    /// <summary>Key code for Alt+3, used in the key-down event payload.</summary>
    public const ushort kbAlt3 = 0x7A00;
    /// <summary>Key code for Alt+4, used in the key-down event payload.</summary>
    public const ushort kbAlt4 = 0x7B00;
    /// <summary>Key code for Alt+5, used in the key-down event payload.</summary>
    public const ushort kbAlt5 = 0x7C00;
    /// <summary>Key code for Alt+6, used in the key-down event payload.</summary>
    public const ushort kbAlt6 = 0x7D00;
    /// <summary>Key code for Alt+7, used in the key-down event payload.</summary>
    public const ushort kbAlt7 = 0x7E00;
    /// <summary>Key code for Alt+8, used in the key-down event payload.</summary>
    public const ushort kbAlt8 = 0x7F00;
    /// <summary>Key code for Alt+9, used in the key-down event payload.</summary>
    public const ushort kbAlt9 = 0x8000;
    /// <summary>Key code for Alt+0, used in the key-down event payload.</summary>
    public const ushort kbAlt0 = 0x8100;
    /// <summary>Key code for Alt+Minus, used in the key-down event payload.</summary>
    public const ushort kbAltMinus = 0x8200;
    /// <summary>Key code for Alt+Equal, used in the key-down event payload.</summary>
    public const ushort kbAltEqual = 0x8300;
    /// <summary>Key code for Control+Page Up, used in the key-down event payload.</summary>
    public const ushort kbCtrlPgUp = 0x8400;

    /// <summary>No key event is represented.</summary>
    public const ushort kbNoKey = 0x0000;
    /// <summary>Key code for Alt+Backspace, used in the key-down event payload.</summary>
    public const ushort kbAltBack = 0x0800;

    // Keyboard state and shift masks
    /// <summary>Keyboard-state mask: Right Shift is pressed.</summary>
    public const uint kbRightShift = 0x0002;
    /// <summary>Keyboard-state mask: Left Shift is pressed.</summary>
    public const uint kbLeftShift  = 0x0001;
    /// <summary>Keyboard-state mask: Either Shift key is pressed.</summary>
    public const uint kbShift      = kbLeftShift | kbRightShift;
    /// <summary>Keyboard-state mask: Control is pressed; this event mask does not distinguish left and right Control.</summary>
    public const uint kbLeftCtrl   = 0x0004;
    /// <summary>Keyboard-state mask: Control is pressed; this event mask does not distinguish left and right Control.</summary>
    public const uint kbRightCtrl  = 0x0004;
    /// <summary>Keyboard-state mask: Either Control key is pressed.</summary>
    public const uint kbCtrlShift  = kbLeftCtrl | kbRightCtrl;
    /// <summary>Keyboard-state mask: Alt is pressed; this event mask does not distinguish left and right Alt.</summary>
    public const uint kbLeftAlt    = 0x0008;
    /// <summary>Keyboard-state mask: Alt is pressed; this event mask does not distinguish left and right Alt.</summary>
    public const uint kbRightAlt   = 0x0008;
    /// <summary>Keyboard-state mask: Either Alt key is pressed.</summary>
    public const uint kbAltShift   = kbLeftAlt | kbRightAlt;
    /// <summary>Keyboard-state mask: Scroll Lock toggle is active.</summary>
    public const uint kbScrollState = 0x0010;
    /// <summary>Keyboard-state mask: Num Lock toggle is active.</summary>
    public const uint kbNumState    = 0x0020;
    /// <summary>Keyboard-state mask: Caps Lock toggle is active.</summary>
    public const uint kbCapsState   = 0x0040;
    /// <summary>Keyboard-state mask: Insert toggle is active.</summary>
    public const uint kbInsState    = 0x0080;
}
