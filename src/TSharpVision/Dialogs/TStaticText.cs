namespace TSharpVision;

/// <summary>Noninteractive text view that wraps text within character-cell bounds.</summary>
public class TStaticText : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TStaticText";

    /// <summary>Stored text used for drawing and serialization.</summary>
    protected string Text;

    /// <summary>Creates a text view at owner-relative cell bounds with the supplied content.</summary>
    public TStaticText(TRect bounds, string aText)
        : base(bounds)
    {
        Text = aText;
    }

    /// <summary>Finalizer hook; performs no resource cleanup.</summary>
    ~TStaticText()
    {
    }

    /// <inheritdoc />
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
                NextRow(s, ref p, size.x, ref center, out int i, out int end, out bool centered);
                int xOff = centered ? (size.x - end + i) / 2 : 0;
                if (end > i)
                    b.moveBuf(xOff, s.AsSpan(i, end - i), color, end - i);
            }
            WriteLine(0, y++, size.x, 1, b);
        }
    }

    // Lays out the display row of s that starts at p, for a view `width` cells wide: the row
    // shows s[start..end) and p moves past it, its trailing spaces and its line break. Draw and
    // MsgBox's automatic sizing both go through here, so the rows counted are the rows drawn.
    internal static void NextRow(string s, ref int p, int width, ref bool center,
                                 out int start, out int end, out bool centered)
    {
        int l = s.Length;
        if (s[p] == (char)3) { center = true; p++; }
        int i = p;
        int j;
        do
        {
            j = p;
            while (p < l && s[p] == ' ') p++;
            while (p < l && s[p] != ' ' && s[p] != '\n') p++;
        } while (p < l && p < i + width && s[p] != '\n');
        if (p > i + width)
        {
            p = j > i ? j : i + width;
        }
        start = i;
        end = p;
        centered = center;
        while (p < l && s[p] == ' ') p++;
        if (p < l && s[p] == '\n')
        {
            center = false;
            p++;
            // Do NOT consume a second '\n' here — let the next row be an empty one
            // (blank line). C++ historically skipped the second LF for CR+LF pairs;
            // C# strings use plain '\n'.
        }
    }

    // Number of rows Draw needs to show all of s in a view `width` cells wide.
    internal static int CountRows(string? s, int width)
    {
        if (string.IsNullOrEmpty(s) || width < 1) return 0;
        int rows = 0;
        int p = 0;
        bool center = false;
        while (p < s.Length)
        {
            NextRow(s, ref p, width, ref center, out _, out _, out _);
            rows++;
        }
        return rows;
    }

    private static readonly TPalette _palette = new TPalette("\x06", 1);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Returns the stored text without layout wrapping or truncation.</summary>
    public virtual void GetText(out string result)
    {
        result = Text;
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteString(text).
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTStaticText =
        new TStreamableClass("TStaticText", () => new TStaticText(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TStaticText(StreamableInit init) : base(init) { Text = string.Empty; }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(Text);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Text = isStream.ReadString() ?? string.Empty;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TStaticText(StreamableInit.streamableInit);
}
