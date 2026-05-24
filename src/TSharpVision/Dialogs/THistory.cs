using TSharpVision.Constants;
namespace TSharpVision;

public class THistory : TView
{
    public new static readonly string Name = "THistory";

    public TInputLine Link;
    public ushort HistoryId;

    protected static string Icon = " \x19 ";

    public THistory(TRect bounds, TInputLine aLink, ushort aHistoryId)
        : base(bounds)
    {
        Link = aLink;
        HistoryId = aHistoryId;
        options |= Views.ofPostProcess;
        eventMask |= Events.evBroadcast;
    }

    public override void ShutDown()
    {
        Link = null;
        base.ShutDown();
    }

    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveCStr(0, Icon, GetColor(0x0102));
        WriteLine(0, 0, size.x, size.y, b);
    }

    private static readonly TPalette _palette = new TPalette("\x16\x17", 2);
    public override TPalette GetPalette() => _palette;

    private static ushort CtrlToArrow(ushort code) => code;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        bool openHistory =
            @event.What == Events.evMouseDown
            || (@event.What == Events.evKeyDown
                && CtrlToArrow(@event.keyDown.keyCode) == Keys.kbDown
                && Link != null && (Link.state & Views.sfFocused) != 0);

        if (openHistory)
        {
            if (Link != null) Link.Select();
            if (Link != null) THistoryList.Add(HistoryId, Link.Data);
            if (Link != null && owner != null)
            {
                TRect r = Link.GetBounds();
                r.a.x--; r.b.x++;
                r.b.y += 7;
                r.a.y--;
                TRect p = owner.GetExtent();
                r.Intersect(p);
                r.b.y--;
                THistoryWindow hw = InitHistoryWindow(r);
                if (hw != null)
                {
                    ushort c = owner.ExecView(hw);
                    if (c == Views.cmOK)
                    {
                        string sel = hw.GetSelection();
                        if (sel != null)
                        {
                            if (sel.Length > Link.MaxLen)
                                sel = sel.Substring(0, Link.MaxLen);
                            Link.Data = sel;
                            Link.SelectAll(true);
                            Link.DrawView();
                        }
                    }
                }
            }
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evBroadcast
                 && Link != null
                 && ((@event.message.command == Views.cmReleasedFocus
                       && ReferenceEquals(@event.message.infoPtr, Link))
                  || @event.message.command == Views.cmRecordHistory))
        {
            THistoryList.Add(HistoryId, Link.Data);
        }
    }

    public virtual THistoryWindow InitHistoryWindow(TRect bounds)
    {
        var w = new THistoryWindow(bounds, HistoryId);
        if (Link != null) w.helpCtx = Link.helpCtx;
        return w;
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WritePointer(link) + WriteShort(historyId).
    public static readonly TStreamableClass StreamableClassTHistory =
        new TStreamableClass("THistory", () => new THistory(StreamableInit.streamableInit), 0);

    protected THistory(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(Link);
        os.WriteShort(HistoryId);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Link      = isStream.ReadPointer() as TInputLine;
        HistoryId = isStream.ReadShort();
        return this;
    }

    public new static TStreamable Build() => new THistory(StreamableInit.streamableInit);
}
