// VGA 16-color palette used by the IBM PC text-mode display. The SDL
// renderer maps the four-bit foreground and four-bit background
// nibbles of a TColorAttr to entries in this palette and then
// draws solid background rectangles + glyphs.
namespace SharpVision.Drivers.SDL;

public static class SdlPalette
{
    /// <summary>16-color VGA palette as 0xAARRGGBB.</summary>
    public static readonly uint[] Vga16 = new uint[]
    {
        0xFF000000, // 0  Black
        0xFF0000AA, // 1  Blue
        0xFF00AA00, // 2  Green
        0xFF00AAAA, // 3  Cyan
        0xFFAA0000, // 4  Red
        0xFFAA00AA, // 5  Magenta
        0xFFAA5500, // 6  Brown
        0xFFAAAAAA, // 7  Light Gray
        0xFF555555, // 8  Dark Gray
        0xFF5555FF, // 9  Bright Blue
        0xFF55FF55, // 10 Bright Green
        0xFF55FFFF, // 11 Bright Cyan
        0xFFFF5555, // 12 Bright Red
        0xFFFF55FF, // 13 Bright Magenta
        0xFFFFFF55, // 14 Yellow
        0xFFFFFFFF, // 15 White
    };

    /// <summary>
    /// Decode the 8-bit attribute byte into separate foreground and
    /// background palette colors.
    /// </summary>
    public static (uint fg, uint bg) DecodeAttr(byte attrByte)
    {
        int fg = attrByte & 0x0F;
        int bg = (attrByte >> 4) & 0x0F;
        return (Vga16[fg], Vga16[bg]);
    }
}
