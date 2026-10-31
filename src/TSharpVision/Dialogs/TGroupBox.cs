using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Horizontal placement of a group box title in its top border.</summary>
public enum TGroupBoxTitleAlignment
{
    /// <summary>Place the title next to the left corner.</summary>
    Left,
    /// <summary>Center the title between the corners.</summary>
    Center,
    /// <summary>Place the title next to the right corner.</summary>
    Right
}

/// <summary>A titled, optionally framed group using normal group-local child coordinates.</summary>
/// <remarks>Insert children within <see cref="ContentRect"/>. Coordinates include the border;
/// the first framed content cell is (1, 1). Children are clipped to that region, never repositioned.
/// Tab visits children in insertion order and leaves the group at either end; any other entry restores the
/// group's current child. Focused events the focused branch leaves unhandled reach the pre- and post-processing
/// children of a group box that is not focused, so mnemonics work across sibling and nested groups. A group box is
/// marked <see cref="Views.ofValidate"/>, so a validating child is consulted when focus leaves the group.</remarks>
public class TGroupBox : TGroup
{
    private string _title;
    private bool _showBorder = true;
    private TGroupBoxTitleAlignment _titleAlignment;
    // Direction of a Tab that may enter this group: false forwards, true backwards, null when none is pending.
    private bool? _tabEntry;
    // The child an explicit selection or a click enters; it takes priority over Tab and the current child.
    private TView? _entry;
    // Set while an event is offered to the children of this group, which is not on the focused branch.
    private bool _offering;

    /// <summary>Creates a group with a visible single-line border and a left-aligned title.</summary>
    public TGroupBox(TRect bounds, string title) : base(bounds)
    {
        _title = title ?? string.Empty;
        // ofValidate passes the release check on to the current child (TGroup.Valid), which TView.Focus otherwise
        // only asks of the group itself when focus moves to another group.
        options |= Views.ofPreProcess | Views.ofPostProcess | Views.ofFirstClick | Views.ofValidate;
        Insert(new Background(GetExtent()));
    }

    /// <summary>Presentation text, without mnemonic interpretation.</summary>
    public string Title
    {
        get => _title;
        set { _title = value ?? string.Empty; DrawView(); }
    }

    /// <summary>Title placement between the top corners.</summary>
    public TGroupBoxTitleAlignment TitleAlignment
    {
        get => _titleAlignment;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _titleAlignment = value;
            DrawView();
        }
    }

    /// <summary>Whether the frame and its title are drawn. Changing this never moves children.</summary>
    public bool ShowBorder
    {
        get => _showBorder;
        set { _showBorder = value; DrawView(); }
    }

    /// <summary>Available child bounds, in group-local cells; empty for a frame with no interior.</summary>
    public TRect ContentRect
    {
        get
        {
            int inset = ShowBorder ? 1 : 0;
            int left = Math.Min(inset, Math.Max(0, size.x));
            int top = Math.Min(inset, Math.Max(0, size.y));
            return new TRect(left, top, Math.Max(left, size.x - inset), Math.Max(top, size.y - inset));
        }
    }

    internal override TRect ChildClip
    {
        get { TRect result = clip; result.Intersect(ContentRect); return result; }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (size.x <= 0 || size.y <= 0) return;
        GetBuffer();
        Span<TScreenChar> cells = size.x <= TDrawBuffer.MaxViewWidth
            ? stackalloc TScreenChar[size.x] : new TScreenChar[size.x];
        var row = new TDrawBuffer(cells);
        // Identity palette inheritance keeps the complete dialog control palette available
        // to children. Entry 6 is static text/background; 1/2 are frame/title colors.
        ushort background = GetColor(6);
        ushort border = GetColor(1);
        ushort title = GetColor(2);
        for (int y = 0; y < size.y; y++)
        {
            cells.Fill(new TScreenChar(' ', (TColorAttr)background));
            if (ShowBorder)
            {
                if (y == 0 || y == size.y - 1)
                {
                    row.moveChar(0, TFrame.FrameChars[10], border, size.x);
                    row.moveChar(0, TFrame.FrameChars[y == 0 ? 6 : 3], border, 1);
                    if (size.x > 1)
                        row.moveChar(size.x - 1, TFrame.FrameChars[y == 0 ? 12 : 9], border, 1);
                    if (y == 0) DrawTitle(row, title);
                }
                else
                {
                    row.moveChar(0, TFrame.FrameChars[5], border, 1);
                    row.moveChar(size.x - 1, TFrame.FrameChars[5], border, 1);
                }
            }
            if (LiveBuffer is { } live) cells.CopyTo(live.Data.Slice(y * size.x, size.x));
            else WriteLine(0, y, size.x, 1, row);
        }

        // Use the existing drawing locks and dispatch; no second child list or renderer.
        bool buffered = LiveBuffer != null;
        if (buffered) lockFlag++;
        try { Redraw(); }
        finally { if (buffered) lockFlag--; }
        if (LiveBuffer is { } image) WriteBuf(0, 0, size.x, size.y, image);
    }

    private void DrawTitle(TDrawBuffer row, ushort color)
    {
        int budget = size.x - 4; // two corners and one space on either side
        if (budget <= 0 || Title.Length == 0) return;
        int length = Math.Min(budget, Title.Length);
        if (length < Title.Length && length > 0 && char.IsHighSurrogate(Title[length - 1])) length--;
        if (length == 0) return;
        int block = length + 2;
        int start = TitleAlignment switch
        {
            TGroupBoxTitleAlignment.Center => (size.x - block) / 2,
            TGroupBoxTitleAlignment.Right => size.x - block - 1,
            _ => 1
        };
        row.moveChar(start, ' ', color, block);
        row.moveBuf(start + 1, Title.AsSpan(0, length), color, length);
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        if ((aState & Views.sfFocused) != 0 && enable && (state & Views.sfFocused) == 0)
        {
            if (EntryTarget() is TView target)
            {
                // A nested group a Tab enters is entered from the same end.
                if (target is TGroupBox box && _tabEntry is bool backwards && !ReferenceEquals(_entry, target))
                    box._tabEntry = backwards;
                SetCurrent(target, selectMode.normalSelect);
            }
            _entry = null;
            _tabEntry = null;
        }
        else if ((aState & Views.sfFocused) != 0 && !enable)
            _tabEntry = null;
        base.SetState(aState, enable);
    }

    // Where focus goes when the group gains it: an explicit target, the end a Tab enters from, or the
    // current child while it is still eligible.
    private TView? EntryTarget()
    {
        var eligible = EligibleChildren();
        if (eligible.Count == 0) return null;
        if (_entry != null && eligible.Contains(_entry)) return _entry;
        if (_tabEntry is bool backwards) return backwards ? eligible[^1] : eligible[0];
        return current != null && eligible.Contains(current) ? current : eligible[0];
    }

    /// <summary>
    /// Keeps a still-eligible current child when a child is shown or hidden; otherwise the first eligible child in
    /// insertion order. The background, not a control, is the bottommost child, so the inherited rule would pick the
    /// most recently inserted control.
    /// </summary>
    internal override TView? ResetCurrentTarget()
    {
        var eligible = EligibleChildren();
        if (current != null && eligible.Contains(current)) return current;
        if (eligible.Count > 0) return eligible[0];
        foreach (TView child in ChildrenInInsertionOrder())
            if ((child.state & Views.sfVisible) != 0 && (child.options & Views.ofSelectable) != 0) return child;
        return null;
    }

    /// <summary>
    /// Focusing a child of a group box that is not focused enters that child directly: each enclosing group box gaining
    /// focus on the way enters its path child, so no other child is focused on the way and the focused leaf and its
    /// ancestors agree.
    /// </summary>
    internal override bool FocusFor(TView child)
    {
        TView? saved = _entry;
        _entry = child;
        try { return Focus(); }
        finally { _entry = saved; }
    }

    /// <summary>Clears a Tab direction recorded for a key whose focus move was refused, here and in nested group boxes.</summary>
    internal void ForgetTabEntry()
    {
        _tabEntry = null;
        ForEachView(v => (v as TGroupBox)?.ForgetTabEntry());
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        bool tab = @event.What == Events.evKeyDown
            && (@event.keyDown.keyCode == Keys.kbTab || @event.keyDown.keyCode == Keys.kbShiftTab);
        if (owner != null && owner.phase != phaseType.phFocused)
        {
            // The parent's preprocessing phase records the direction of a Tab that may enter this group without
            // delivering a focused key twice. Nested groups need the same direction before selection.
            if (owner.phase == phaseType.phPreProcess && @event.What == Events.evKeyDown)
            {
                bool? direction = tab ? @event.keyDown.keyCode == Keys.kbShiftTab : null;
                _tabEntry = direction;
                ForEachView(v => { if (v is TGroupBox box) box._tabEntry = direction; });
            }
            // After the focused branch, what it left reaches this unfocused group's children as their own pre- and
            // post-processing, as if they were siblings of the focused control. A group on the focused branch
            // handles its children itself when its owner dispatches the focused phase.
            else if (owner.phase == phaseType.phPostProcess && !tab && !OnFocusedBranch())
                OfferToChildren(ref @event);
            return;
        }
        if (((@event.What & Views.positionalEvents) != 0 || @event.What == Events.evMouseWheel)
            && !ContentRect.Contains(MakeLocal(@event.mouse.where))) return;

        // A click that enters this group focuses the child under the mouse directly.
        bool entering = @event.What == Events.evMouseDown && (state & Views.sfFocused) == 0;
        if (entering) PrepareEntryAt(@event);
        try { base.HandleEvent(ref @event); }
        finally { if (entering) ClearEntries(); }
        if (!tab || @event.What != Events.evKeyDown) return;
        bool backwards = @event.keyDown.keyCode == Keys.kbShiftTab;
        var children = EligibleChildren();
        int index = children.IndexOf(current!);
        int next = index + (backwards ? -1 : 1);
        if (next >= 0 && next < children.Count)
        {
            if (children[next] is TGroupBox box) box._tabEntry = backwards;
            if (!children[next].Focus() && children[next] is TGroupBox refused) refused.ForgetTabEntry();
            ClearEvent(ref @event);
        }
        // At the boundary, let the containing group/window apply its navigation policy.
    }

    // The owner dispatches its focused phase to its current child, unless the owner is itself only offering.
    private bool OnFocusedBranch() =>
        ReferenceEquals(owner!.current, this) && owner is not TGroupBox { _offering: true };

    private void OfferToChildren(ref TEvent @event)
    {
        if ((@event.What & Views.focusedEvents) == 0
            || (state & (Views.sfVisible | Views.sfDisabled)) != Views.sfVisible) return;
        TEvent local = @event;
        phaseType savedPhase = phase;
        bool savedOffering = _offering;
        _offering = true;
        try
        {
            foreach (var (pass, required) in OfferPasses)
            {
                phase = pass;
                ForEachView(v =>
                {
                    if (local.What == Events.evNothing || (v.options & required) == 0
                        || (v.state & (Views.sfVisible | Views.sfDisabled)) != Views.sfVisible
                        || (local.What & v.eventMask) == 0) return;
                    v.HandleEvent(ref local);
                });
            }
        }
        finally
        {
            phase = savedPhase;
            _offering = savedOffering;
        }
        @event = local;
    }

    private static readonly (phaseType Phase, ushort Option)[] OfferPasses =
        [(phaseType.phPreProcess, Views.ofPreProcess), (phaseType.phPostProcess, Views.ofPostProcess)];

    private void PrepareEntryAt(TEvent click)
    {
        TView? child = FirstThat((v, e) => (v.state & Views.sfVisible) != 0 && v.ContainsMouse(e), click);
        if (child == null || !EligibleChildren().Contains(child)) return;
        _entry = child;
        if (child is TGroupBox box && (box.state & Views.sfFocused) == 0) box.PrepareEntryAt(click);
    }

    private void ClearEntries()
    {
        if (_entry is TGroupBox box) box.ClearEntries();
        _entry = null;
    }

    private List<TView> ChildrenInInsertionOrder()
    {
        var result = new List<TView>();
        if (last == null) return result;
        TView child = last;
        do
        {
            result.Add(child);
            child = child.Prev();
        } while (child != last);
        return result;
    }

    private List<TView> EligibleChildren()
    {
        var result = new List<TView>();
        foreach (TView child in ChildrenInInsertionOrder())
            if ((child.options & Views.ofSelectable) != 0
                && (child.state & (Views.sfVisible | Views.sfDisabled)) == Views.sfVisible
                && (child is not TGroupBox box || box.EligibleChildren().Count > 0)) result.Add(child);
        return result;
    }

    // An ordinary bottommost decoration restores uncovered cells on child Hide,
    // Remove and Move, which redraw underlying siblings without calling group.Draw.
    private sealed class Background : TView
    {
        internal Background(TRect bounds) : base(bounds)
            => growMode = Views.gfGrowHiX | Views.gfGrowHiY;

        public override void Draw()
        {
            Span<TScreenChar> cells = size.x <= TDrawBuffer.MaxViewWidth
                ? stackalloc TScreenChar[Math.Max(0, size.x)] : new TScreenChar[size.x];
            cells.Fill(new TScreenChar(' ', (TColorAttr)(owner?.GetColor(6) ?? 0)));
            WriteLine(0, 0, size.x, size.y, cells);
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Group box streaming is not supported.</exception>
    public override void Write(Opstream os)
        => throw new NotSupportedException("TGroupBox does not support streaming.");

    /// <inheritdoc />
    public override string StreamableName() => nameof(TGroupBox);
}
