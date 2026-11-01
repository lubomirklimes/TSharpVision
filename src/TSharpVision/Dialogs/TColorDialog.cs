using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Dialog for editing a palette through grouped entries, color selectors, and a live preview.</summary>
public class TColorDialog : TDialog
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TColorDialog";

    /// <summary>The palette being edited (mutable — changes are in-place).</summary>
    public TPalette? Pal { get; private set; }

    private TColorDisplay?    _display;
    private TColorGroupList?  _groups;
    private TLabel?           _forLabel;
    private TColorSelector?   _forSel;
    private TLabel?           _bakLabel;
    private TColorSelector?   _bakSel;
    private TLabel?           _monoLabel;
    private TMonoSelector?    _monoSel;

    /// <summary>Creates a centered 77-by-18 cell editor that modifies the supplied palette in place using the supplied group descriptions.</summary>
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
            _display?.SetColor(aPalette.Data, aGroups.Items.Index);
        }

        // ── Buttons ──────────────────────────────────────────────────────
        Insert(new TButton(new TRect(31, 15, 44, 17),
            TSharpVisionIntl.Get("Color_Btn_Try", "~T~ry"), Views.cmTryColors, ButtonConstants.bfNormal));
        Insert(new TButton(new TRect(46, 15, 59, 17),
            TSharpVisionIntl.Get("Btn_OK", "~O~K"), Views.cmOK, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(61, 15, 74, 17),
            TSharpVisionIntl.Get("Btn_Cancel", "~C~ancel"), Views.cmCancel, ButtonConstants.bfNormal));

        SelectNext(false);
    }

    // Intercepts cmNewColorIndex to update the display pointer,
    // and cmTryColors to broadcast a redraw notification.
    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmNewColorIndex)
        {
            int idx = (int)@event.message.infoLong;
            if (Pal != null && idx >= 1 && idx <= Pal.Size)
                _display?.SetColor(Pal.Data, idx);
        }
        else if (@event.What == Events.evCommand
                 && @event.message.command == Views.cmTryColors)
        {
            // Opted-in dialogs first copy the edited entries into the bound live palette.
            bool applied = _live != null && !_live.Finished && Pal != null && _live.Apply(Pal);
            // Broadcast so any view that caches palette entries can refresh.
            TEvent upd = default;
            upd.What = Events.evBroadcast;
            upd.message.command = Views.cmUpdateColorsChanged;
            owner?.HandleEvent(ref upd);
            if (applied) _live!.Repaint();
        }
    }

    // ── Live Try / OK / Cancel (opt-in) ─────────────────────────────────────

    private LivePaletteBinding? _live;
    private bool _repaintOnShutDown;

    /// <summary>
    /// Opts this dialog into live preview: <c>Try</c> copies the edited entries into <paramref name="livePalette"/> and
    /// calls <paramref name="repaint"/>; ending the modal run with <c>OK</c> keeps the edited entries (with or without a
    /// prior Try); any other ending (Cancel, Escape, the close box, quit) restores the exact bytes
    /// <paramref name="livePalette"/> held when this method was called.
    /// </summary>
    /// <param name="livePalette">
    /// The palette the application draws with, typically <see cref="TProgram.GetPalette"/>. Its bytes are captured now
    /// and only this palette object is ever written, so a later change of <see cref="TProgram.AppPalette"/> cannot make
    /// the dialog write into another family's table. <see cref="Pal"/> should be an independent copy (for example
    /// <see cref="TPalette.Clone"/>) so edits stay invisible until Try or OK.
    /// </param>
    /// <param name="repaint">
    /// Recomposes everything drawn with <paramref name="livePalette"/>, typically <see cref="TGroup.Redraw"/> of the
    /// program, which also invalidates buffered groups. Called after Try, and after OK or a restore that changed bytes.
    /// </param>
    /// <remarks>
    /// Call once, before the dialog runs. Without this call the dialog behaves exactly as before: it only edits
    /// <see cref="Pal"/> and Try only broadcasts <see cref="Views.cmUpdateColorsChanged"/>. When an opted-in dialog is
    /// used modelessly, closing it restores the snapshot and repaints; a shutdown without a close restores the bytes
    /// only, because the views it would repaint are being torn down.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The dialog is already bound or has no palette.</exception>
    public void BindLivePalette(TPalette livePalette, Action repaint)
    {
        ArgumentNullException.ThrowIfNull(livePalette);
        ArgumentNullException.ThrowIfNull(repaint);
        if (Pal == null) throw new InvalidOperationException("The dialog has no palette to edit.");
        if (_live != null) throw new InvalidOperationException("The dialog is already bound to a live palette.");
        _live = new LivePaletteBinding(livePalette, repaint);
    }

    /// <inheritdoc />
    /// <remarks>A dialog bound with <see cref="BindLivePalette"/> commits on <see cref="Views.cmOK"/> and restores its snapshot on any other result.</remarks>
    public override ushort Execute()
    {
        var live = _live;
        if (live == null || live.Finished) return base.Execute();
        ushort result = Views.cmCancel;
        try
        {
            result = base.Execute();
            return result;
        }
        finally
        {
            bool changed = result == Views.cmOK && Pal != null ? live.Commit(Pal) : live.Restore();
            if (changed) live.Repaint();
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        bool prior = _repaintOnShutDown;
        _repaintOnShutDown = true;
        try { base.Close(); }
        finally { _repaintOnShutDown = prior; }
    }

    /// <inheritdoc />
    /// <remarks>An opted-in dialog that never finished restores the live palette snapshot (Cancel semantics).</remarks>
    public override void ShutDown()
    {
        var live = _live;
        bool changed = live != null && !live.Finished && live.Restore();
        bool repaint = _repaintOnShutDown;
        base.ShutDown();
        if (changed && repaint) live!.Repaint();
    }

    // Owns the live target, its open-time snapshot and the finished flag. Only the captured target object is written.
    private sealed class LivePaletteBinding
    {
        private readonly TPalette _target;
        private readonly byte[] _snapshot;
        private readonly Action _repaint;

        internal LivePaletteBinding(TPalette target, Action repaint)
        {
            _target = target;
            _snapshot = (byte[])target.Data.Clone();
            _repaint = repaint;
        }

        internal bool Finished { get; private set; }

        // Copies the edited mapping entries (never the size byte) into the target; true when a byte changed.
        internal bool Apply(TPalette edited)
        {
            if (ReferenceEquals(edited.Data, _target.Data)) return true;
            int count = Math.Min(edited.Data.Length, _target.Data.Length);
            bool changed = false;
            for (int i = 1; i < count; i++)
            {
                if (_target.Data[i] == edited.Data[i]) continue;
                _target.Data[i] = edited.Data[i];
                changed = true;
            }
            return changed;
        }

        internal bool Commit(TPalette edited)
        {
            Finished = true;
            return Apply(edited);
        }

        internal bool Restore()
        {
            Finished = true;
            bool changed = !_target.Data.AsSpan().SequenceEqual(_snapshot);
            if (changed) Array.Copy(_snapshot, _target.Data, Math.Min(_snapshot.Length, _target.Data.Length));
            return changed;
        }

        internal void Repaint() => _repaint();
    }

    /// <inheritdoc />
    public override ushort DataSize() => (ushort)((Pal != null) ? Pal.Data[0] + 1 : 0);

    /// <summary>Returns a copy of the complete palette record, or an empty array when no palette is attached.</summary>
    public virtual void GetData(out byte[] rec)
    {
        rec = (Pal != null) ? (byte[])Pal.Data.Clone() : System.Array.Empty<byte>();
    }

    /// <summary>Copies a length-prefixed palette record into the attached palette and resets the selected group and preview.</summary>
    public virtual void SetData(byte[]? rec)
    {
        if (Pal == null || rec == null || rec.Length < 1) return;
        int count = System.Math.Min(rec[0], Pal.Data[0]);
        System.Array.Copy(rec, 0, Pal.Data, 0, count + 1);
        if (Pal.Size >= 1)
            _display?.SetColor(Pal.Data, 1);
        _groups?.FocusItem(0);
    }

    // ── Streaming ────────────────────────────────────────────────────────────
    // Wire: TDialog base + WritePointer x8 (display, groups, forLabel, forSel,
    //       bakLabel, bakSel, monoLabel, monoSel). On read: pal = null.
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTColorDialog =
        new TStreamableClass("TColorDialog",
            () => new TColorDialog(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TColorDialog(StreamableInit init) : base(init) { }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TColorDialog(StreamableInit.streamableInit);

    // Test-only accessors for smoke-check pointer identity verification.
    /// <summary>Returns the actual owned group-list control for identity inspection.</summary>
    public TColorGroupList? GroupsForTest  => _groups;
    /// <summary>Returns the actual owned preview control for identity inspection.</summary>
    public TColorDisplay? DisplayForTest => _display;
}
