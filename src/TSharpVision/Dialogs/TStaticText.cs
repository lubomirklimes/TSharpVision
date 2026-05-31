namespace TSharpVision;

public class TStaticText : TView
{
    public new static readonly string Name = "TStaticText";

    protected string Text;

    public TStaticText(TRect bounds, string aText)
        : base(bounds)
    {
        Text = aText;
    }

    ~TStaticText()
    {
    }

    public override void Draw()
    {
        ushort color = GetColor(1);
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        GetText(out string s);
        s ??= string.Empty;
        int l = s.Length;
        int p = 0;
        int y = 0;
        bool center = false;
        while (y < size.y)
        {
            b.moveChar(0, ' ', color, size.x);
            if (p < l)
            {
                if (s[p] == (char)3) { center = true; p++; }
                int i = p;
                int j;
                do
                {
                    j = p;
                    while (p < l && s[p] == ' ') p++;
                    while (p < l && s[p] != ' ' && s[p] != '\n') p++;
                } while (p < l && p < i + size.x && s[p] != '\n');
                if (p > i + size.x)
                {
                    p = j > i ? j : i + size.x;
                }
                int xOff = center ? (size.x - p + i) / 2 : 0;
                if (p > i)
                    b.moveBuf(xOff, s.AsSpan(i, p - i), color, p - i);
                while (p < l && s[p] == ' ') p++;
                if (p < l && s[p] == '\n')
                {
                    center = false;
                    p++;
                    // Do NOT consume a second '\n' here — let the next iteration
                    // render an empty row (blank line). C++ historically skipped
                    // the second LF for CR+LF pairs; C# strings use plain '\n'.
                }
            }
            WriteLine(0, y++, size.x, 1, b);
        }
    }

    private static readonly TPalette _palette = new TPalette("\x06", 1);
    public override TPalette GetPalette() => _palette;

    public virtual void GetText(out string result)
    {
        result = Text;
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteString(text).
    public static readonly TStreamableClass StreamableClassTStaticText =
        new TStreamableClass("TStaticText", () => new TStaticText(StreamableInit.streamableInit), 0);

    protected TStaticText(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(Text);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Text = isStream.ReadString();
        return this;
    }

    public new static TStreamable Build() => new TStaticText(StreamableInit.streamableInit);
}
