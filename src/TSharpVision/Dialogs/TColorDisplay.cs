using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Preview of sample text whose color changes update a referenced palette entry in place.</summary>
public class TColorDisplay : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TColorDisplay";

    // Reference into TPalette.Data (the byte[] array) + offset (1-based index).
    // Mirrors upstream's  uchar* color  pointer into pal->data.
    private byte[]? _data;
    private int    _offset;
    private string _text;

    /// <summary>Creates a color preview at owner-relative cell bounds; null sample text uses the default sample.</summary>
    public TColorDisplay(TRect bounds, string aText) : base(bounds)
    {
        _text   = aText ?? "Text ";
        _data   = null;
        _offset = 0;
        eventMask |= Events.evBroadcast;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        byte c = (_data != null) ? _data[_offset] : (byte)0;
        if (c == 0) c = errorAttr;
        int len = _text.Length;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        if (len > 0)
            for (int i = 0; i <= size.x / len; i++)
                b.moveStr(i * len, _text, c);
        WriteLine(0, 0, size.x, size.y, b);
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast && _data is byte[] data)
        {
            switch (@event.message.command)
            {
                case Views.cmColorBackgroundChanged:
                    data[_offset] = (byte)((data[_offset] & 0x0F)
                                          | (byte)((@event.message.infoLong << 4) & 0xF0));
                    DrawView();
                    break;

                case Views.cmColorForegroundChanged:
                    data[_offset] = (byte)((data[_offset] & 0xF0)
                                          | (byte)(@event.message.infoLong & 0x0F));
                    DrawView();
                    break;
            }
        }
    }

    //   stores pointer, broadcasts cmColorSet with current value, redraws.
    // In C# we store the array reference + offset instead of a raw pointer.
    /// <summary>References an entry in the supplied palette array, broadcasts its current color, and redraws the preview.</summary>
    public void SetColor(byte[] data, int offset)
    {
        _data   = data;
        _offset = offset;
        BroadcastColorSet();
        DrawView();
    }

    // Broadcast cmColorSet so TColorSelector and TMonoSelector sync up.
    private void BroadcastColorSet()
    {
        if (owner is not TGroup ownerGroup || _data is not byte[] data) return;
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command  = Views.cmColorSet;
        ev.message.infoLong = data[_offset];
        ownerGroup.HandleEvent(ref ev);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteString(text).
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTColorDisplay =
        new TStreamableClass("TColorDisplay", () => new TColorDisplay(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TColorDisplay(StreamableInit init) : base(init) { _text = "Text "; }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(_text);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        _text  = isStream.ReadString() ?? "Text ";
        _data  = null;
        _offset = 0;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TColorDisplay(StreamableInit.streamableInit);
}
