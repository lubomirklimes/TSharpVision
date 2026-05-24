using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// Desktop fill view.
/// </summary>
public class TBackground : TView
{
    public char pattern;

    public new static readonly string Name = "TBackground";

    public TBackground(TRect bounds, char aPattern) : base(bounds)
    {
        pattern = aPattern;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
    }

    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        TDrawBuffer b = new TDrawBuffer(row);
        // Anti-moire substitution (TScreen::avoidMoire == defaultBkgrnd → noMoireFill)
        // is deferred until TScreen exposes those fields; use raw pattern for now.
        b.moveChar(0, pattern, GetColor(0x01), size.x);
        WriteLine(0, 0, size.x, size.y, b);
    }

    private static readonly TPalette _palette = new TPalette("\x01", 1);
    public override TPalette GetPalette() => _palette;

    public void ChangePattern(char newP) { pattern = newP; Draw(); }

    // Wire layout (after TView base): one raw CP437 byte for 'pattern'.
    // The C# field is stored as Unicode; encode/decode via CP437 table.
    // At minimum supports the full set of box/shade glyphs.

    private static byte PatternToCP437(char c) => c switch
    {
        '░' => 0xB0, '▒' => 0xB1, '▓' => 0xB2,
        '■' => 0xFE, '□' => 0xFF,
        ' ' => 0x20,
        _   => 0x3F, // fallback '?'
    };

    private static char PatternFromCP437(byte b) => b switch
    {
        0xB0 => '░', 0xB1 => '▒', 0xB2 => '▓',
        0xFE => '■', 0xFF => '□',
        0x20 => ' ',
        _    => (char)b,
    };

    public static readonly TStreamableClass StreamableClassTBackground =
        new TStreamableClass("TBackground", () => new TBackground(StreamableInit.streamableInit), 0);

    protected TBackground(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteByte(PatternToCP437(pattern));
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        pattern = PatternFromCP437(isStream.ReadByte());
        return this;
    }

    public new static TStreamable Build() { return new TBackground(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
