using SharpVision.Constants;

namespace SharpVision;

// THelpWindow — modal window wrapping a THelpViewer with a horizontal
// and vertical standard scrollbar. Centered, fixed at 50x18.
public class THelpWindow : TWindow
{
    public new static readonly string Name = "THelpWindow";

    // SharpVision / RHIDE palette remap for a fully cyan-themed help window.
    // The original cHelpWindow ("\x80...\x87") used app-palette entries 128–135
    // which do not exist in the 63-entry RHIDE cpColor.  We remap every slot to
    // in-range cpColor entries that produce the classic Turbo Vision help style:
    // a cyan-background window with black body text, yellow keywords and
    // a white-on-blue selected link.
    //
    //  Slot  cpColor idx  Final attr  Meaning
    //   1    17  (0x11)   0x3F        frame inactive  — white on dark cyan
    //   2    17  (0x11)   0x3F        frame active + title — white on dark cyan
    //   3    17  (0x11)   0x3F        high-byte of active cFrame (close/zoom icons)
    //   4    47  (0x2F)   0x30        scrollbar track — black on dark cyan
    //   5    17  (0x11)   0x3F        scrollbar arrows + thumb — white on dark cyan
    //   6    47  (0x2F)   0x30        THelpViewer normal text — black on dark cyan
    //   7    21  (0x15)   0x3E        THelpViewer keyword — yellow on dark cyan
    //   8    50  (0x32)   0x1F        THelpViewer selected keyword — white on dark blue
    private static readonly TPalette _palette =
        new TPalette("\x11\x11\x11\x2F\x11\x2F\x15\x32", 8);

    public THelpWindow(THelpFile hFile, ushort context)
        : base(new TRect(0, 0, 50, 18), "Help", Views.wnNoNumber)
    {
    }

    public override TPalette GetPalette() => _palette;
}
