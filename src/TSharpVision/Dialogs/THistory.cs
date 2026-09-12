using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>History button associated with an input line and a shared history ID.</summary>
public class THistory : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "THistory";

    /// <summary>Associated input line whose text is recorded and restored; the button does not own it.</summary>
    public TInputLine Link;
    /// <summary>Identifier selecting the shared input-history list.</summary>
    public ushort HistoryId;

    /// <summary>Text drawn for the history dropdown button.</summary>
    protected static string Icon = " \x19 ";

    /// <summary>Creates a history button at owner-relative cell bounds linked to an input and history ID.</summary>
    public THistory(TRect bounds, TInputLine aLink, ushort aHistoryId)
        : base(bounds)
    {
        Link = aLink;
        HistoryId = aHistoryId;
        options |= Views.ofPostProcess;
        eventMask |= Events.evBroadcast;
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        Link = null;
        base.ShutDown();
    }

    /// <inheritdoc />
    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveCStr(0, Icon, GetColor(0x0102));
        WriteLine(0, 0, size.x, size.y, b);
    }

    private static readonly TPalette _palette = new TPalette("\x16\x17", 2);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    private static ushort CtrlToArrow(ushort code) => code;

    /// <inheritdoc />
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

    /// <summary>Creates a history popup at the supplied cell bounds, inheriting the linked input's help context.</summary>
    public virtual THistoryWindow InitHistoryWindow(TRect bounds)
    {
        var w = new THistoryWindow(bounds, HistoryId);
        if (Link != null) w.helpCtx = Link.helpCtx;
        return w;
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WritePointer(link) + WriteShort(historyId).
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTHistory =
        new TStreamableClass("THistory", () => new THistory(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected THistory(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(Link);
        os.WriteShort(HistoryId);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Link      = isStream.ReadPointer() as TInputLine;
        HistoryId = isStream.ReadShort();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new THistory(StreamableInit.streamableInit);
}
