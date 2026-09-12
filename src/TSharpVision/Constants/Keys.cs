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
    public const ushort kbCtrlA = 0x0101;
    /// <summary>Key code for Control+B, used in the key-down event payload.</summary>
    public const ushort kbCtrlB = 0x0102;
    /// <summary>Key code for Control+C, used in the key-down event payload.</summary>
    public const ushort kbCtrlC = 0x0103;
    /// <summary>Key code for Control+D, used in the key-down event payload.</summary>
    public const ushort kbCtrlD = 0x0104;
    /// <summary>Key code for Control+E, used in the key-down event payload.</summary>
    public const ushort kbCtrlE = 0x0105;
    /// <summary>Key code for Control+F, used in the key-down event payload.</summary>
    public const ushort kbCtrlF = 0x0106;
    /// <summary>Key code for Control+G, used in the key-down event payload.</summary>
    public const ushort kbCtrlG = 0x0107;
    /// <summary>Key code for Control+H, used in the key-down event payload.</summary>
    public const ushort kbCtrlH = 0x0108;
    /// <summary>Key code for Control+I, used in the key-down event payload.</summary>
    public const ushort kbCtrlI = 0x0109;
    /// <summary>Key code for Control+J, used in the key-down event payload.</summary>
    public const ushort kbCtrlJ = 0x010a;
    /// <summary>Key code for Control+K, used in the key-down event payload.</summary>
    public const ushort kbCtrlK = 0x010b;
    /// <summary>Key code for Control+L, used in the key-down event payload.</summary>
    public const ushort kbCtrlL = 0x010c;
    /// <summary>Key code for Control+M, used in the key-down event payload.</summary>
    public const ushort kbCtrlM = 0x010d;
    /// <summary>Key code for Control+N, used in the key-down event payload.</summary>
    public const ushort kbCtrlN = 0x010e;
    /// <summary>Key code for Control+O, used in the key-down event payload.</summary>
    public const ushort kbCtrlO = 0x010f;
    /// <summary>Key code for Control+P, used in the key-down event payload.</summary>
    public const ushort kbCtrlP = 0x0110;
    /// <summary>Key code for Control+Q, used in the key-down event payload.</summary>
    public const ushort kbCtrlQ = 0x0111;
    /// <summary>Key code for Control+R, used in the key-down event payload.</summary>
    public const ushort kbCtrlR = 0x0112;
    /// <summary>Key code for Control+S, used in the key-down event payload.</summary>
    public const ushort kbCtrlS = 0x0113;
    /// <summary>Key code for Control+T, used in the key-down event payload.</summary>
    public const ushort kbCtrlT = 0x0114;
    /// <summary>Key code for Control+U, used in the key-down event payload.</summary>
    public const ushort kbCtrlU = 0x0115;
    /// <summary>Key code for Control+V, used in the key-down event payload.</summary>
    public const ushort kbCtrlV = 0x0116;
    /// <summary>Key code for Control+W, used in the key-down event payload.</summary>
    public const ushort kbCtrlW = 0x0117;
    /// <summary>Key code for Control+X, used in the key-down event payload.</summary>
    public const ushort kbCtrlX = 0x0118;
    /// <summary>Key code for Control+Y, used in the key-down event payload.</summary>
    public const ushort kbCtrlY = 0x0119;
    /// <summary>Key code for Control+Z, used in the key-down event payload.</summary>
    public const ushort kbCtrlZ = 0x011a;

    // Extended key codes
    /// <summary>Key code for Escape, used in the key-down event payload.</summary>
    public const ushort kbEsc = 0x001f;
    /// <summary>Key code for Alt+Space, used in the key-down event payload.</summary>
    public const ushort kbAltSpace = 0x0234;
    /// <summary>Key code for Control+Insert, used in the key-down event payload.</summary>
    public const ushort kbCtrlIns = 0x014d;
    /// <summary>Key code for Shift+Insert, used in the key-down event payload.</summary>
    public const ushort kbShiftIns = 0x00cd;
    /// <summary>Key code for Control+Delete, used in the key-down event payload.</summary>
    public const ushort kbCtrlDel = 0x014e;
    /// <summary>Key code for Shift+Delete, used in the key-down event payload.</summary>
    public const ushort kbShiftDel = 0x00ce;
    /// <summary>Key code for Control+Shift+Insert, used in the key-down event payload.</summary>
    public const ushort kbCtrlShiftIns = 0x01cd;
    /// <summary>Key code for Control+Shift+Delete, used in the key-down event payload.</summary>
    public const ushort kbCtrlShiftDel = 0x01ce;
    /// <summary>Key code for Backspace, used in the key-down event payload.</summary>
    public const ushort kbBack = 0x002a;
    /// <summary>Key code for Control+Backspace, used in the key-down event payload.</summary>
    public const ushort kbCtrlBack = 0x012a;
    /// <summary>Key code for Shift+Tab, used in the key-down event payload.</summary>
    public const ushort kbShiftTab = 0x00ab;
    /// <summary>Key code for Tab, used in the key-down event payload.</summary>
    public const ushort kbTab = 0x002b;

    /// <summary>Key code for Alt+A, used in the key-down event payload.</summary>
    public const ushort kbAltA = 0x0201;
    /// <summary>Key code for Alt+B, used in the key-down event payload.</summary>
    public const ushort kbAltB = 0x0202;
    /// <summary>Key code for Alt+C, used in the key-down event payload.</summary>
    public const ushort kbAltC = 0x0203;
    /// <summary>Key code for Alt+D, used in the key-down event payload.</summary>
    public const ushort kbAltD = 0x0204;
    /// <summary>Key code for Alt+E, used in the key-down event payload.</summary>
    public const ushort kbAltE = 0x0205;
    /// <summary>Key code for Alt+F, used in the key-down event payload.</summary>
    public const ushort kbAltF = 0x0206;
    /// <summary>Key code for Alt+G, used in the key-down event payload.</summary>
    public const ushort kbAltG = 0x0207;
    /// <summary>Key code for Alt+H, used in the key-down event payload.</summary>
    public const ushort kbAltH = 0x0208;
    /// <summary>Key code for Alt+I, used in the key-down event payload.</summary>
    public const ushort kbAltI = 0x0209;
    /// <summary>Key code for Alt+J, used in the key-down event payload.</summary>
    public const ushort kbAltJ = 0x020a;
    /// <summary>Key code for Alt+K, used in the key-down event payload.</summary>
    public const ushort kbAltK = 0x020b;
    /// <summary>Key code for Alt+L, used in the key-down event payload.</summary>
    public const ushort kbAltL = 0x020c;
    /// <summary>Key code for Alt+M, used in the key-down event payload.</summary>
    public const ushort kbAltM = 0x020d;
    /// <summary>Key code for Alt+N, used in the key-down event payload.</summary>
    public const ushort kbAltN = 0x020e;
    /// <summary>Key code for Alt+O, used in the key-down event payload.</summary>
    public const ushort kbAltO = 0x020f;
    /// <summary>Key code for Alt+P, used in the key-down event payload.</summary>
    public const ushort kbAltP = 0x0210;
    /// <summary>Key code for Alt+Q, used in the key-down event payload.</summary>
    public const ushort kbAltQ = 0x0211;
    /// <summary>Key code for Alt+R, used in the key-down event payload.</summary>
    public const ushort kbAltR = 0x0212;
    /// <summary>Key code for Alt+S, used in the key-down event payload.</summary>
    public const ushort kbAltS = 0x0213;
    /// <summary>Key code for Alt+T, used in the key-down event payload.</summary>
    public const ushort kbAltT = 0x0214;
    /// <summary>Key code for Alt+U, used in the key-down event payload.</summary>
    public const ushort kbAltU = 0x0215;
    /// <summary>Key code for Alt+V, used in the key-down event payload.</summary>
    public const ushort kbAltV = 0x0216;
    /// <summary>Key code for Alt+W, used in the key-down event payload.</summary>
    public const ushort kbAltW = 0x0217;
    /// <summary>Key code for Alt+X, used in the key-down event payload.</summary>
    public const ushort kbAltX = 0x0218;
    /// <summary>Key code for Alt+Y, used in the key-down event payload.</summary>
    public const ushort kbAltY = 0x0219;
    /// <summary>Key code for Alt+Z, used in the key-down event payload.</summary>
    public const ushort kbAltZ = 0x021a;

    /// <summary>Key code for Control+Enter, used in the key-down event payload.</summary>
    public const ushort kbCtrlEnter = 0x012c;
    /// <summary>Key code for Enter, used in the key-down event payload.</summary>
    public const ushort kbEnter = 0x002c;
    /// <summary>Key code for F1, used in the key-down event payload.</summary>
    public const ushort kbF1 = 0x0039;
    /// <summary>Key code for F2, used in the key-down event payload.</summary>
    public const ushort kbF2 = 0x003a;
    /// <summary>Key code for F3, used in the key-down event payload.</summary>
    public const ushort kbF3 = 0x003b;
    /// <summary>Key code for F4, used in the key-down event payload.</summary>
    public const ushort kbF4 = 0x003c;
    /// <summary>Key code for F5, used in the key-down event payload.</summary>
    public const ushort kbF5 = 0x003d;
    /// <summary>Key code for F6, used in the key-down event payload.</summary>
    public const ushort kbF6 = 0x003e;
    /// <summary>Key code for F7, used in the key-down event payload.</summary>
    public const ushort kbF7 = 0x003f;
    /// <summary>Key code for F8, used in the key-down event payload.</summary>
    public const ushort kbF8 = 0x0040;
    /// <summary>Key code for F9, used in the key-down event payload.</summary>
    public const ushort kbF9 = 0x0041;
    /// <summary>Key code for F10, used in the key-down event payload.</summary>
    public const ushort kbF10 = 0x0042;
    /// <summary>Key code for F11, used in the key-down event payload.</summary>
    public const ushort kbF11 = 0x0043;
    /// <summary>Key code for F12, used in the key-down event payload.</summary>
    public const ushort kbF12 = 0x0044;

    /// <summary>Key code for Home, used in the key-down event payload.</summary>
    public const ushort kbHome = 0x0045;
    /// <summary>Key code for Up, used in the key-down event payload.</summary>
    public const ushort kbUp = 0x0046;
    /// <summary>Key code for Page Up, used in the key-down event payload.</summary>
    public const ushort kbPgUp = 0x0047;
    /// <summary>Key code for Left, used in the key-down event payload.</summary>
    public const ushort kbLeft = 0x0048;
    /// <summary>Key code for Right, used in the key-down event payload.</summary>
    public const ushort kbRight = 0x0049;
    /// <summary>Key code for End, used in the key-down event payload.</summary>
    public const ushort kbEnd = 0x004a;
    /// <summary>Key code for Down, used in the key-down event payload.</summary>
    public const ushort kbDown = 0x004b;
    /// <summary>Key code for Page Down, used in the key-down event payload.</summary>
    public const ushort kbPgDn = 0x004c;
    /// <summary>Key code for Insert, used in the key-down event payload.</summary>
    public const ushort kbIns = 0x004d;
    /// <summary>Key code for Delete, used in the key-down event payload.</summary>
    public const ushort kbDel = 0x004e;

    /// <summary>Key code for keypad minus, used in the key-down event payload.</summary>
    public const ushort kbGrayMinus = 0x0035;
    /// <summary>Key code for keypad plus, used in the key-down event payload.</summary>
    public const ushort kbGrayPlus = 0x0036;

    /// <summary>Key code for Shift+F1, used in the key-down event payload.</summary>
    public const ushort kbShiftF1 = 0x00b9;
    /// <summary>Key code for Shift+F2, used in the key-down event payload.</summary>
    public const ushort kbShiftF2 = 0x00ba;
    /// <summary>Key code for Shift+F3, used in the key-down event payload.</summary>
    public const ushort kbShiftF3 = 0x00bb;
    /// <summary>Key code for Shift+F4, used in the key-down event payload.</summary>
    public const ushort kbShiftF4 = 0x00bc;
    /// <summary>Key code for Shift+F5, used in the key-down event payload.</summary>
    public const ushort kbShiftF5 = 0x00bd;
    /// <summary>Key code for Shift+F6, used in the key-down event payload.</summary>
    public const ushort kbShiftF6 = 0x00be;
    /// <summary>Key code for Shift+F7, used in the key-down event payload.</summary>
    public const ushort kbShiftF7 = 0x00bf;
    /// <summary>Key code for Shift+F8, used in the key-down event payload.</summary>
    public const ushort kbShiftF8 = 0x00c0;
    /// <summary>Key code for Shift+F9, used in the key-down event payload.</summary>
    public const ushort kbShiftF9 = 0x00c1;
    /// <summary>Key code for Shift+F10, used in the key-down event payload.</summary>
    public const ushort kbShiftF10 = 0x00c2;
    /// <summary>Key code for Shift+F11, used in the key-down event payload.</summary>
    public const ushort kbShiftF11 = 0x00c3;
    /// <summary>Key code for Shift+F12, used in the key-down event payload.</summary>
    public const ushort kbShiftF12 = 0x00c4;

    /// <summary>Key code for Control+F1, used in the key-down event payload.</summary>
    public const ushort kbCtrlF1 = 0x0139;
    /// <summary>Key code for Control+F2, used in the key-down event payload.</summary>
    public const ushort kbCtrlF2 = 0x013a;
    /// <summary>Key code for Control+F3, used in the key-down event payload.</summary>
    public const ushort kbCtrlF3 = 0x013b;
    /// <summary>Key code for Control+F4, used in the key-down event payload.</summary>
    public const ushort kbCtrlF4 = 0x013c;
    /// <summary>Key code for Control+F5, used in the key-down event payload.</summary>
    public const ushort kbCtrlF5 = 0x013d;
    /// <summary>Key code for Control+F6, used in the key-down event payload.</summary>
    public const ushort kbCtrlF6 = 0x013e;
    /// <summary>Key code for Control+F7, used in the key-down event payload.</summary>
    public const ushort kbCtrlF7 = 0x013f;
    /// <summary>Key code for Control+F8, used in the key-down event payload.</summary>
    public const ushort kbCtrlF8 = 0x0140;
    /// <summary>Key code for Control+F9, used in the key-down event payload.</summary>
    public const ushort kbCtrlF9 = 0x0141;
    /// <summary>Key code for Control+F10, used in the key-down event payload.</summary>
    public const ushort kbCtrlF10 = 0x0142;
    /// <summary>Key code for Control+F11, used in the key-down event payload.</summary>
    public const ushort kbCtrlF11 = 0x0143;
    /// <summary>Key code for Control+F12, used in the key-down event payload.</summary>
    public const ushort kbCtrlF12 = 0x0144;

    /// <summary>Key code for Alt+F1, used in the key-down event payload.</summary>
    public const ushort kbAltF1 = 0x0239;
    /// <summary>Key code for Alt+F2, used in the key-down event payload.</summary>
    public const ushort kbAltF2 = 0x023a;
    /// <summary>Key code for Alt+F3, used in the key-down event payload.</summary>
    public const ushort kbAltF3 = 0x023b;
    /// <summary>Key code for Alt+F4, used in the key-down event payload.</summary>
    public const ushort kbAltF4 = 0x023c;
    /// <summary>Key code for Alt+F5, used in the key-down event payload.</summary>
    public const ushort kbAltF5 = 0x023d;
    /// <summary>Key code for Alt+F6, used in the key-down event payload.</summary>
    public const ushort kbAltF6 = 0x023e;
    /// <summary>Key code for Alt+F7, used in the key-down event payload.</summary>
    public const ushort kbAltF7 = 0x023f;
    /// <summary>Key code for Alt+F8, used in the key-down event payload.</summary>
    public const ushort kbAltF8 = 0x0240;
    /// <summary>Key code for Alt+F9, used in the key-down event payload.</summary>
    public const ushort kbAltF9 = 0x0241;
    /// <summary>Key code for Alt+F10, used in the key-down event payload.</summary>
    public const ushort kbAltF10 = 0x0242;
    /// <summary>Key code for Alt+F11, used in the key-down event payload.</summary>
    public const ushort kbAltF11 = 0x0243;
    /// <summary>Key code for Alt+F12, used in the key-down event payload.</summary>
    public const ushort kbAltF12 = 0x0244;

    /// <summary>Key code for Control+Print Screen, used in the key-down event payload.</summary>
    public const ushort kbCtrlPrtSc = 0x0137;
    /// <summary>Key code for Control+Left, used in the key-down event payload.</summary>
    public const ushort kbCtrlLeft = 0x0148;
    /// <summary>Key code for Control+Right, used in the key-down event payload.</summary>
    public const ushort kbCtrlRight = 0x0149;
    /// <summary>Key code for Control+End, used in the key-down event payload.</summary>
    public const ushort kbCtrlEnd = 0x014a;
    /// <summary>Key code for Control+Page Down, used in the key-down event payload.</summary>
    public const ushort kbCtrlPgDn = 0x014c;
    /// <summary>Key code for Control+Home, used in the key-down event payload.</summary>
    public const ushort kbCtrlHome = 0x0145;

    /// <summary>Key code for Alt+1, used in the key-down event payload.</summary>
    public const ushort kbAlt1 = 0x0221;
    /// <summary>Key code for Alt+2, used in the key-down event payload.</summary>
    public const ushort kbAlt2 = 0x0222;
    /// <summary>Key code for Alt+3, used in the key-down event payload.</summary>
    public const ushort kbAlt3 = 0x0223;
    /// <summary>Key code for Alt+4, used in the key-down event payload.</summary>
    public const ushort kbAlt4 = 0x0224;
    /// <summary>Key code for Alt+5, used in the key-down event payload.</summary>
    public const ushort kbAlt5 = 0x0225;
    /// <summary>Key code for Alt+6, used in the key-down event payload.</summary>
    public const ushort kbAlt6 = 0x0226;
    /// <summary>Key code for Alt+7, used in the key-down event payload.</summary>
    public const ushort kbAlt7 = 0x0227;
    /// <summary>Key code for Alt+8, used in the key-down event payload.</summary>
    public const ushort kbAlt8 = 0x0228;
    /// <summary>Key code for Alt+9, used in the key-down event payload.</summary>
    public const ushort kbAlt9 = 0x0229;
    /// <summary>Key code for Alt+0, used in the key-down event payload.</summary>
    public const ushort kbAlt0 = 0x0220;
    /// <summary>Key code for Alt+Minus, used in the key-down event payload.</summary>
    public const ushort kbAltMinus = 0x0235;
    /// <summary>Key code for Alt+Equal, used in the key-down event payload.</summary>
    public const ushort kbAltEqual = 0x0238;
    /// <summary>Key code for Control+Page Up, used in the key-down event payload.</summary>
    public const ushort kbCtrlPgUp = 0x0147;

    /// <summary>No key event is represented.</summary>
    public const ushort kbNoKey = 0x0000;
    /// <summary>Key code for Alt+Backspace, used in the key-down event payload.</summary>
    public const ushort kbAltBack = 0x022a;

    // Keyboard state and shift masks
    /// <summary>Keyboard-state mask: Right Shift is pressed.</summary>
    public const ushort kbRightShift = 0x0001;
    /// <summary>Keyboard-state mask: Left Shift is pressed.</summary>
    public const ushort kbLeftShift  = 0x0002;
    /// <summary>Keyboard-state mask: Either Shift key is pressed.</summary>
    public const ushort kbShift      = kbLeftShift | kbRightShift;
    /// <summary>Keyboard-state mask: Control is pressed; this event mask does not distinguish left and right Control.</summary>
    public const ushort kbLeftCtrl   = 0x0004;
    /// <summary>Keyboard-state mask: Control is pressed; this event mask does not distinguish left and right Control.</summary>
    public const ushort kbRightCtrl  = 0x0004;
    /// <summary>Keyboard-state mask: Either Control key is pressed.</summary>
    public const ushort kbCtrlShift  = kbLeftCtrl | kbRightCtrl;
    /// <summary>Keyboard-state mask: Alt is pressed; this event mask does not distinguish left and right Alt.</summary>
    public const ushort kbLeftAlt    = 0x0008;
    /// <summary>Keyboard-state mask: Alt is pressed; this event mask does not distinguish left and right Alt.</summary>
    public const ushort kbRightAlt   = 0x0008;
    /// <summary>Keyboard-state mask: Either Alt key is pressed.</summary>
    public const ushort kbAltShift   = kbLeftAlt | kbRightAlt;
    /// <summary>Keyboard-state mask: Scroll Lock toggle is active.</summary>
    public const ushort kbScrollState = 0x0010;
    /// <summary>Keyboard-state mask: Num Lock toggle is active.</summary>
    public const ushort kbNumState    = 0x0020;
    /// <summary>Keyboard-state mask: Caps Lock toggle is active.</summary>
    public const ushort kbCapsState   = 0x0040;
    /// <summary>Keyboard-state mask: Insert toggle is active.</summary>
    public const ushort kbInsState    = 0x0080;
}
