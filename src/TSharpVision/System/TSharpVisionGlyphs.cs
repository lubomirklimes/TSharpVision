namespace TSharpVision;

/// <summary>
/// Central definitions for Turbo Vision / CP437-style UI glyphs, rendered as Unicode.
/// <para>
/// Turbo Vision originally targeted DOS text-mode screens using IBM CP437 (OEM 437).
/// TSharpVision targets Unicode consoles (Win32 WriteConsoleOutputW, Unicode mode).
/// These constants store the Unicode code points that visually match the original
/// CP437 glyphs. The original CP437 byte values are documented in the comments.
/// </para>
/// <para>
/// Win32ConsoleDriver passes each char directly as WCHAR to WriteConsoleOutputW,
/// so the Unicode codepoint is what the console font renders — no CP437 translation
/// is applied at display time.
/// </para>
/// </summary>
public static class TSharpVisionGlyphs
{
    // ── Background fill ───────────────────────────────────────────────────────

    /// <summary>CP437 0xB0 — light shade. Default desktop background fill.</summary>
    public const char BackgroundFillLight  = '░'; // U+2591 LIGHT SHADE
    /// <summary>CP437 0xB1 — medium shade.</summary>
    public const char BackgroundFillMedium = '▒'; // U+2592 MEDIUM SHADE
    /// <summary>CP437 0xB2 — dark shade.</summary>
    public const char BackgroundFillDark   = '▓'; // U+2593 DARK SHADE

    // ── Single-line box-drawing ───────────────────────────────────────────────

    /// <summary>CP437 0xC4 — single horizontal bar. Used in TFrame, TMenuBox, TIndicator.</summary>
    public const char FrameHorizontal  = '─'; // U+2500 BOX DRAWINGS LIGHT HORIZONTAL
    /// <summary>CP437 0xB3 — single vertical bar. Used in TFrame, TMenuBox, TStatusLine hint separator.</summary>
    public const char FrameVertical    = '│'; // U+2502 BOX DRAWINGS LIGHT VERTICAL
    /// <summary>CP437 0xDA — single top-left corner.</summary>
    public const char FrameTopLeft     = '┌'; // U+250C
    /// <summary>CP437 0xBF — single top-right corner.</summary>
    public const char FrameTopRight    = '┐'; // U+2510
    /// <summary>CP437 0xC0 — single bottom-left corner.</summary>
    public const char FrameBottomLeft  = '└'; // U+2514
    /// <summary>CP437 0xD9 — single bottom-right corner.</summary>
    public const char FrameBottomRight = '┘'; // U+2518
    /// <summary>CP437 0xC3 — single left tee.</summary>
    public const char FrameLeftTee     = '├'; // U+251C
    /// <summary>CP437 0xB4 — single right tee.</summary>
    public const char FrameRightTee    = '┤'; // U+2524

    // ── Double-line box-drawing ───────────────────────────────────────────────

    /// <summary>CP437 0xCD — double horizontal bar. TIndicator drag frame, TFrame double borders.</summary>
    public const char FrameDoubleHorizontal  = '═'; // U+2550 BOX DRAWINGS DOUBLE HORIZONTAL
    /// <summary>CP437 0xBA — double vertical bar.</summary>
    public const char FrameDoubleVertical    = '║'; // U+2551
    /// <summary>CP437 0xC9 — double top-left corner.</summary>
    public const char FrameDoubleTopLeft     = '╔'; // U+2554
    /// <summary>CP437 0xBB — double top-right corner.</summary>
    public const char FrameDoubleTopRight    = '╗'; // U+2557
    /// <summary>CP437 0xC8 — double bottom-left corner.</summary>
    public const char FrameDoubleBottomLeft  = '╚'; // U+255A
    /// <summary>CP437 0xBC — double bottom-right corner.</summary>
    public const char FrameDoubleBottomRight = '╝'; // U+255D

    // ── ScrollBar glyphs ──────────────────────────────────────────────────────
    // Array layout: [0]=up/left endpoint, [1]=down/right endpoint,
    //               [2]=empty track, [3]=thumb mark, [4]=full/bright fill.

    /// <summary>CP437 0x1E — up-arrow, vertical scrollbar top endpoint.</summary>
    public const char ScrollArrowUp    = '▲'; // U+25B2 BLACK UP-POINTING TRIANGLE
    /// <summary>CP437 0x1F — down-arrow, vertical scrollbar bottom endpoint.</summary>
    public const char ScrollArrowDown  = '▼'; // U+25BC BLACK DOWN-POINTING TRIANGLE
    /// <summary>CP437 0x11 — left-arrow, horizontal scrollbar left endpoint.</summary>
    public const char ScrollArrowLeft  = '◄'; // U+25C4 BLACK LEFT-POINTING POINTER
    /// <summary>CP437 0x10 — right-arrow, horizontal scrollbar right endpoint.</summary>
    public const char ScrollArrowRight = '►'; // U+25BA BLACK RIGHT-POINTING POINTER
    /// <summary>CP437 0xB1 — medium shade, scrollbar empty track fill.</summary>
    public const char ScrollBarTrack   = '▒'; // U+2592 MEDIUM SHADE
    /// <summary>CP437 0xFE — solid square, scrollbar thumb (position marker).</summary>
    public const char ScrollBarThumb   = '■'; // U+25A0 BLACK SQUARE
    /// <summary>CP437 0xB0 — light shade, scrollbar bright (used when range is full/unfilled).</summary>
    public const char ScrollBarBright  = '░'; // U+2591 LIGHT SHADE

    // ── Checkbox / RadioButton markers ────────────────────────────────────────

    /// <summary>ASCII 0x58 — letter X, checkbox checked marker placed in the [ ] slot.</summary>
    public const char CheckBoxChecked   = 'X';
    /// <summary>ASCII 0x20 — space, checkbox unchecked (empty slot).</summary>
    public const char CheckBoxUnchecked = ' ';

    /// <summary>
    /// CP437 0x07 — radio button checked marker. CP437 byte 0x07 is the bullet glyph '•'
    /// in text-mode video; stored as U+0007 to preserve the current rendering path via
    /// WriteConsoleOutputW.
    /// </summary>
    public const char RadioChecked   = (char)0x07; // CP437 0x07 bullet glyph
    /// <summary>ASCII 0x20 — space, radio button unchecked (empty slot).</summary>
    public const char RadioUnchecked = ' ';

    // ── Editor Indicator glyphs ───────────────────────────────────────────────

    /// <summary>CP437 0xCD '═' — indicator row fill when NOT dragging (advertises drag mode is available).</summary>
    public const char IndicatorDragFrame   = '═'; // U+2550
    /// <summary>CP437 0xC4 '─' — indicator row fill when dragging (advertises normal mode).</summary>
    public const char IndicatorNormalFrame = '─'; // U+2500
    /// <summary>U+25CF '●' — indicator modified-file mark. Replaces upstream CP437 0x0F (☼ sun symbol).</summary>
    public const char IndicatorModified    = '●'; // U+25CF BLACK CIRCLE

    // ── Menu / StatusLine glyphs ──────────────────────────────────────────────

    /// <summary>CP437 0x10 '►' — submenu arrow drawn at the right edge of a popup menu item.</summary>
    public const char MenuSubmenuArrow    = '►'; // U+25BA
    /// <summary>U+2502 '│' — vertical bar separating status items from hint text in TStatusLine.</summary>
    public const char StatusHintSeparator = '│'; // U+2502

    /// <summary>System menu</summary>
    public const char SystemMenu = '☰';
    public const char SystemMenuFallback = '≡'; // ≡
    /*Varianta	Znak	Unicode	Poznámka
    ☰	U+2630 TRIGRAM FOR HEAVEN	Nejčitelnější, často se používá jako menu ikona.
    ≡	U+2261 IDENTICAL TO	Dobře funguje v terminálech, šířka obvykle stabilní.
    ≣	U+2263 STRICTLY EQUIVALENT TO	Vypadá víc jako silnější tři čáry, ale nemusí být všude stejně hezký. Retro / CP437-like volba	
    ≡	ASCII fallback	-	Pokud bude problém s fontem/šířkou.
     */
}
