namespace TSharpVision.Constants;

/// <summary>
/// Standard 16-color BIOS palette indices. A TColorAttr packs foreground in
/// the low nibble and background in the high nibble, exactly as VGA text mode.
/// </summary>
public static class Colors
{
    // Foreground colors (low nibble of color byte)
    public const byte fgBlack        = 0x00;
    public const byte fgBlue         = 0x01;
    public const byte fgGreen        = 0x02;
    public const byte fgCyan         = 0x03;
    public const byte fgRed          = 0x04;
    public const byte fgMagenta      = 0x05;
    public const byte fgBrown        = 0x06;
    public const byte fgLightGray    = 0x07;
    public const byte fgDarkGray     = 0x08;
    public const byte fgLightBlue    = 0x09;
    public const byte fgLightGreen   = 0x0A;
    public const byte fgLightCyan    = 0x0B;
    public const byte fgLightRed     = 0x0C;
    public const byte fgLightMagenta = 0x0D;
    public const byte fgYellow       = 0x0E;
    public const byte fgWhite        = 0x0F;

    // Background colors (high nibble of color byte). Bright bit is normally
    // unavailable on background — values 0..7 only.
    public const byte bgBlack     = 0x00;
    public const byte bgBlue      = 0x10;
    public const byte bgGreen     = 0x20;
    public const byte bgCyan      = 0x30;
    public const byte bgRed       = 0x40;
    public const byte bgMagenta   = 0x50;
    public const byte bgBrown     = 0x60;
    public const byte bgLightGray = 0x70;

    // Blink bit (high bit of color byte) — preserved for fidelity, rarely
    // honored by modern terminals.
    public const byte blink = 0x80;

    // Nibble masks
    public const byte fgMask = 0x0F;
    public const byte bgMask = 0xF0;
}
