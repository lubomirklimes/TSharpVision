namespace TSharpVision.Constants;

/// <summary>
/// Standard 16-color BIOS palette indices. A TColorAttr packs foreground in
/// the low nibble and background in the high nibble, exactly as VGA text mode.
/// </summary>
public static class Colors
{
    // Foreground colors (low nibble of color byte)
    /// <summary>Foreground palette index for black; occupies the low attribute nibble.</summary>
    public const byte fgBlack        = 0x00;
    /// <summary>Foreground palette index for blue; occupies the low attribute nibble.</summary>
    public const byte fgBlue         = 0x01;
    /// <summary>Foreground palette index for green; occupies the low attribute nibble.</summary>
    public const byte fgGreen        = 0x02;
    /// <summary>Foreground palette index for cyan; occupies the low attribute nibble.</summary>
    public const byte fgCyan         = 0x03;
    /// <summary>Foreground palette index for red; occupies the low attribute nibble.</summary>
    public const byte fgRed          = 0x04;
    /// <summary>Foreground palette index for magenta; occupies the low attribute nibble.</summary>
    public const byte fgMagenta      = 0x05;
    /// <summary>Foreground palette index for brown; occupies the low attribute nibble.</summary>
    public const byte fgBrown        = 0x06;
    /// <summary>Foreground palette index for light gray; occupies the low attribute nibble.</summary>
    public const byte fgLightGray    = 0x07;
    /// <summary>Foreground palette index for dark gray; occupies the low attribute nibble.</summary>
    public const byte fgDarkGray     = 0x08;
    /// <summary>Foreground palette index for light blue; occupies the low attribute nibble.</summary>
    public const byte fgLightBlue    = 0x09;
    /// <summary>Foreground palette index for light green; occupies the low attribute nibble.</summary>
    public const byte fgLightGreen   = 0x0A;
    /// <summary>Foreground palette index for light cyan; occupies the low attribute nibble.</summary>
    public const byte fgLightCyan    = 0x0B;
    /// <summary>Foreground palette index for light red; occupies the low attribute nibble.</summary>
    public const byte fgLightRed     = 0x0C;
    /// <summary>Foreground palette index for light magenta; occupies the low attribute nibble.</summary>
    public const byte fgLightMagenta = 0x0D;
    /// <summary>Foreground palette index for yellow; occupies the low attribute nibble.</summary>
    public const byte fgYellow       = 0x0E;
    /// <summary>Foreground palette index for white; occupies the low attribute nibble.</summary>
    public const byte fgWhite        = 0x0F;

    // Background colors (high nibble of color byte). Bright bit is normally
    // unavailable on background — values 0..7 only.
    /// <summary>Background attribute bits for black; combine with a foreground index using bitwise OR.</summary>
    public const byte bgBlack     = 0x00;
    /// <summary>Background attribute bits for blue; combine with a foreground index using bitwise OR.</summary>
    public const byte bgBlue      = 0x10;
    /// <summary>Background attribute bits for green; combine with a foreground index using bitwise OR.</summary>
    public const byte bgGreen     = 0x20;
    /// <summary>Background attribute bits for cyan; combine with a foreground index using bitwise OR.</summary>
    public const byte bgCyan      = 0x30;
    /// <summary>Background attribute bits for red; combine with a foreground index using bitwise OR.</summary>
    public const byte bgRed       = 0x40;
    /// <summary>Background attribute bits for magenta; combine with a foreground index using bitwise OR.</summary>
    public const byte bgMagenta   = 0x50;
    /// <summary>Background attribute bits for brown; combine with a foreground index using bitwise OR.</summary>
    public const byte bgBrown     = 0x60;
    /// <summary>Background attribute bits for light gray; combine with a foreground index using bitwise OR.</summary>
    public const byte bgLightGray = 0x70;

    // Blink bit (high bit of color byte) — preserved for fidelity, rarely
    // honored by modern terminals.
    /// <summary>Blink attribute bit; display support depends on the backend.</summary>
    public const byte blink = 0x80;

    // Nibble masks
    /// <summary>Mask selecting the foreground nibble of a packed color attribute.</summary>
    public const byte fgMask = 0x0F;
    /// <summary>Mask selecting the background nibble of a packed color attribute.</summary>
    public const byte bgMask = 0xF0;
}
