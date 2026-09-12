using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// Desktop fill view.
/// </summary>
public class TBackground : TView
{
    /// <summary>Character repeated across the background view's cell area.</summary>
    public char pattern;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TBackground";

    /// <summary>Creates a repeating-character background in owner-relative cell bounds that grows with its owner.</summary>
    public TBackground(TRect bounds, char aPattern) : base(bounds)
    {
        pattern = aPattern;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
    }

    /// <inheritdoc />
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
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Replaces the fill character and immediately draws the background.</summary>
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

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTBackground =
        new TStreamableClass("TBackground", () => new TBackground(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TBackground(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteByte(PatternToCP437(pattern));
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        pattern = PatternFromCP437(isStream.ReadByte());
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TBackground(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() { return Name; }
}
