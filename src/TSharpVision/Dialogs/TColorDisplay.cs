using TSharpVision.Constants;
namespace TSharpVision;

// TColorDisplay — preview pane showing a text sample in the selected color.
// Holds a reference into a TPalette data array; foreground/background change
// broadcasts update that entry in-place (matching the C++ pointer semantics).
public class TColorDisplay : TView
{
    public new static readonly string Name = "TColorDisplay";

    // Reference into TPalette.Data (the byte[] array) + offset (1-based index).
    // Mirrors upstream's  uchar* color  pointer into pal->data.
    private byte[] _data;
    private int    _offset;
    private string _text;

    public TColorDisplay(TRect bounds, string aText) : base(bounds)
    {
        _text   = aText ?? "Text ";
        _data   = null;
        _offset = 0;
        eventMask |= Events.evBroadcast;
    }

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

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast && _data != null)
        {
            switch (@event.message.command)
            {
                case Views.cmColorBackgroundChanged:
                    _data[_offset] = (byte)((_data[_offset] & 0x0F)
                                          | (byte)((@event.message.infoLong << 4) & 0xF0));
                    DrawView();
                    break;

                case Views.cmColorForegroundChanged:
                    _data[_offset] = (byte)((_data[_offset] & 0xF0)
                                          | (byte)(@event.message.infoLong & 0x0F));
                    DrawView();
                    break;
            }
        }
    }

    //   stores pointer, broadcasts cmColorSet with current value, redraws.
    // In C# we store the array reference + offset instead of a raw pointer.
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
        if (owner == null || _data == null) return;
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command  = Views.cmColorSet;
        ev.message.infoLong = _data[_offset];
        owner.HandleEvent(ref ev);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteString(text).
    public static readonly TStreamableClass StreamableClassTColorDisplay =
        new TStreamableClass("TColorDisplay", () => new TColorDisplay(StreamableInit.streamableInit), 0);

    protected TColorDisplay(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(_text);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        _text  = isStream.ReadString();
        _data  = null;
        _offset = 0;
        return this;
    }

    public new static TStreamable Build() => new TColorDisplay(StreamableInit.streamableInit);
}
