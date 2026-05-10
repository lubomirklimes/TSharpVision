using TSharpVision.Constants;
namespace TSharpVision;

// TColorDialog — the full color-editing dialog.
// Contains a group list, item list, foreground selector, background
// selector, mono selector (hidden), preview display, and Try/OK/Cancel buttons.
public class TColorDialog : TDialog
{
    public new static readonly string Name = "TColorDialog";

    /// <summary>The palette being edited (mutable — changes are in-place).</summary>
    public TPalette Pal { get; private set; }

    private TColorDisplay    _display;
    private TColorGroupList  _groups;
    private TLabel           _forLabel;
    private TColorSelector   _forSel;
    private TLabel           _bakLabel;
    private TColorSelector   _bakSel;
    private TLabel           _monoLabel;
    private TMonoSelector    _monoSel;

    // Layout uses ib=0 (blink-enabled, 8 background colors).
    // Dialog is 77×18, centered on screen.
    public TColorDialog(TPalette aPalette, TColorGroup aGroups)
        : base(new TRect(0, 0, 77, 18), TSharpVisionIntl.Get("Color_Title", "Colors"))
    {
        options |= Views.ofCentered;
        Pal = aPalette;

        // ── Group list (left panel) ──────────────────────────────────────
        var sbGroups = new TScrollBar(new TRect(31, 3, 32, 14));
        Insert(sbGroups);

        _groups = new TColorGroupList(new TRect(3, 3, 31, 14), sbGroups, aGroups);
        Insert(_groups);
        Insert(new TLabel(new TRect(2, 2, 11, 3), TSharpVisionIntl.Get("Color_Lbl_Group", "~G~roup"), _groups));

        // ── Item list (centre panel) ─────────────────────────────────────
        var sbItems   = new TScrollBar(new TRect(57, 3, 58, 13));
        var sbItemsH  = new TScrollBar(new TRect(34, 13, 57, 14));
        sbItemsH.SetParams(0, 0, 40, 5, 1);
        Insert(sbItems);
        Insert(sbItemsH);

        var itemList = new TColorItemList(
            new TRect(34, 3, 57, 13), sbItems,
            aGroups?.Items,           // start with first group's items
            sbItemsH);
        Insert(itemList);
        Insert(new TLabel(new TRect(33, 2, 39, 3), TSharpVisionIntl.Get("Color_Lbl_Item", "~I~tem"), itemList));

        // ── Foreground selector (right panel, upper) ─────────────────────
        _forSel = new TColorSelector(
            new TRect(61, 3, 73, 7),
            TColorSelector.ColorSel.csForeground);
        Insert(_forSel);
        _forLabel = new TLabel(new TRect(61, 2, 73, 3), TSharpVisionIntl.Get("Color_Lbl_Foreground", "~F~oreground"), _forSel);
        Insert(_forLabel);

        // ── Background selector (right panel, lower — 2 rows = 8 colors) ─
        _bakSel = new TColorSelector(
            new TRect(61, 9, 73, 11),
            TColorSelector.ColorSel.csBackground);
        Insert(_bakSel);
        _bakLabel = new TLabel(new TRect(61, 8, 73, 9), TSharpVisionIntl.Get("Color_Lbl_Background", "~B~ackground"), _bakSel);
        Insert(_bakLabel);

        // ── Color preview display ────────────────────────────────────────
        _display = new TColorDisplay(
            new TRect(60, 12, 74, 14),
            TSharpVisionIntl.Get("Color_PreviewText", "Text "));
        Insert(_display);

        // ── Mono selector (hidden by default — for mono-mode terminals) ───
        _monoSel = new TMonoSelector(new TRect(60, 3, 75, 7));
        _monoSel.Hide();
        Insert(_monoSel);
        _monoLabel = new TLabel(
            new TRect(59, 2, 66, 3),
            TSharpVisionIntl.Get("Color_Lbl_Color", "Color"),
            _monoSel);
        _monoLabel.Hide();
        Insert(_monoLabel);

        // ── Seed display with first group's first item ───────────────────
        if (aGroups?.Items != null && aPalette != null
            && aGroups.Items.Index <= aPalette.Size)
        {
            _display.SetColor(aPalette.Data, aGroups.Items.Index);
        }

        // ── Buttons ──────────────────────────────────────────────────────
        Insert(new TButton(new TRect(31, 15, 44, 17),
            TSharpVisionIntl.Get("Color_Btn_Try", "~T~ry"), Views.cmTryColors, ButtonConstants.bfNormal));
        Insert(new TButton(new TRect(46, 15, 59, 17),
            TSharpVisionIntl.Get("Btn_OK", "~O~K"), Views.cmOK, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(61, 15, 74, 17),
            TSharpVisionIntl.Get("Btn_Cancel", "Cancel"), Views.cmCancel, ButtonConstants.bfNormal));

        SelectNext(false);
    }

    // Intercepts cmNewColorIndex to update the display pointer,
    // and cmTryColors to broadcast a redraw notification.
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmNewColorIndex)
        {
            int idx = (int)@event.message.infoLong;
            if (Pal != null && idx >= 1 && idx <= Pal.Size)
                _display.SetColor(Pal.Data, idx);
        }
        else if (@event.What == Events.evCommand
                 && @event.message.command == Views.cmTryColors)
        {
            // Broadcast so any view that caches palette entries can refresh.
            TEvent upd = default;
            upd.What = Events.evBroadcast;
            upd.message.command = Views.cmUpdateColorsChanged;
            owner?.HandleEvent(ref upd);
        }
    }

    public virtual int DataSize() => (Pal != null) ? Pal.Data[0] + 1 : 0;

    public virtual void GetData(out byte[] rec)
    {
        rec = (Pal != null) ? (byte[])Pal.Data.Clone() : System.Array.Empty<byte>();
    }

    public virtual void SetData(byte[] rec)
    {
        if (Pal == null || rec == null || rec.Length < 1) return;
        int count = System.Math.Min(rec[0], Pal.Data[0]);
        System.Array.Copy(rec, 0, Pal.Data, 0, count + 1);
        if (Pal.Size >= 1)
            _display.SetColor(Pal.Data, 1);
        _groups.FocusItem(0);
    }

    // ── Streaming ────────────────────────────────────────────────────────────
    // Wire: TDialog base + WritePointer x8 (display, groups, forLabel, forSel,
    //       bakLabel, bakSel, monoLabel, monoSel). On read: pal = null.
    public static readonly TStreamableClass StreamableClassTColorDialog =
        new TStreamableClass("TColorDialog",
            () => new TColorDialog(StreamableInit.streamableInit), 0);

    protected TColorDialog(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);   // TDialog → TWindow → TGroup
        os.WritePointer(_display);
        os.WritePointer(_groups);
        os.WritePointer(_forLabel);
        os.WritePointer(_forSel);
        os.WritePointer(_bakLabel);
        os.WritePointer(_bakSel);
        os.WritePointer(_monoLabel);
        os.WritePointer(_monoSel);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TDialog → TWindow → TGroup
        _display   = isStream.ReadPointer() as TColorDisplay;
        _groups    = isStream.ReadPointer() as TColorGroupList;
        _forLabel  = isStream.ReadPointer() as TLabel;
        _forSel    = isStream.ReadPointer() as TColorSelector;
        _bakLabel  = isStream.ReadPointer() as TLabel;
        _bakSel    = isStream.ReadPointer() as TColorSelector;
        _monoLabel = isStream.ReadPointer() as TLabel;
        _monoSel   = isStream.ReadPointer() as TMonoSelector;
        Pal = null;
        return this;
    }

    public new static TStreamable Build() =>
        new TColorDialog(StreamableInit.streamableInit);

    // Test-only accessors for smoke-check pointer identity verification.
    public TColorGroupList GroupsForTest  => _groups;
    public TColorDisplay   DisplayForTest => _display;
}
