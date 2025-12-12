using TSharpVision.Constants;
namespace TSharpVision;

public class TLabel : TStaticText
{
    public new static readonly string Name = "TLabel";

    public TView Link;
    public bool Light;

    public TLabel(TRect bounds, string aText, TView aLink)
        : base(bounds, aText)
    {
        Link = aLink;
        Light = false;
        options |= (ushort)(Views.ofPreProcess | Views.ofPostProcess);
        eventMask |= Events.evBroadcast;
    }

    public override void Draw()
    {
        ushort color;
        var b = new TDrawBuffer();
        if ((state & Views.sfDisabled) != 0) color = GetColor(0x0605);
        else if (Light) color = GetColor(0x0402);
        else color = GetColor(0x0301);
        b.moveChar(0, ' ', color, size.x);
        if (!string.IsNullOrEmpty(Text))
            b.moveCStr(1, Text, color);
        WriteLine(0, 0, size.x, 1, b);
    }

    private static readonly TPalette _palette = new TPalette(
        "\x07\x08\x09\x09\x0D\x0D", 6);
    public override TPalette GetPalette() => _palette;

    private static char ExtractHotKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return '\0';
        for (int i = 0; i < s.Length - 1; i++)
            if (s[i] == '~') return char.ToUpperInvariant(s[i + 1]);
        return '\0';
    }

    private static ushort GetAltCode(char c)
    {
        if (c >= 'A' && c <= 'Z') return (ushort)(Keys.kbAltA + (c - 'A'));
        return Keys.kbNoKey;
    }

    private bool ValidLink() =>
        Link != null
        && (Link.options & Views.ofSelectable) != 0
        && (Link.state & Views.sfDisabled) == 0;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evMouseDown)
        {
            if (ValidLink()) Link.Select();
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evKeyDown)
        {
            char c = ExtractHotKey(Text ?? string.Empty);
            bool altMatch = GetAltCode(c) == @event.keyDown.keyCode;
            bool asciiMatch = c != '\0'
                && owner != null && owner.phase == TView.phaseType.phPostProcess
                && char.ToUpperInvariant((char)@event.keyDown.charScan.charCode) == c;
            if (altMatch || asciiMatch)
            {
                if (ValidLink()) Link.Select();
                ClearEvent(ref @event);
            }
        }
        else if (@event.What == Events.evBroadcast
                 && (@event.message.command == Views.cmReceivedFocus
                  || @event.message.command == Views.cmReleasedFocus))
        {
            Light = Link != null && (Link.state & Views.sfFocused) != 0;
            DrawView();
        }
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfDisabled)
        {
            Link?.SetState(aState, enable);
            DrawView();
        }
    }

    public override void ShutDown()
    {
        Link = null;
        base.ShutDown();
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TStaticText base + WritePointer(link).
    public static readonly TStreamableClass StreamableClassTLabel =
        new TStreamableClass("TLabel", () => new TLabel(StreamableInit.streamableInit), 0);

    protected TLabel(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(Link);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Link = isStream.ReadPointer() as TView;
        Light = false;
        return this;
    }

    public new static TStreamable Build() => new TLabel(StreamableInit.streamableInit);
}
