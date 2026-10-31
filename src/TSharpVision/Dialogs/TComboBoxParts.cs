using TSharpVision.Constants;
namespace TSharpVision;

// The views a TComboBox is made of. They are internal: the combo box owns them, creates them in its constructor and
// shuts them down with itself, and the dropdown lives only while TComboBox.OpenDropDown runs.

/// <summary>Read-only text area of a non-editable combo box showing the selected item.</summary>
internal sealed class TComboBoxField : TView
{
    // Input-line roles (normal, focused, selected, overflow arrow) plus the dialog's disabled-cluster role.
    private static readonly TPalette _palette = new TPalette("\x13\x13\x14\x15\x1F", 5);
    private readonly TComboBox _combo;

    public TComboBoxField(TComboBox combo, TRect bounds) : base(bounds)
    {
        _combo = combo;
        options |= Views.ofSelectable;
    }

    public override TPalette GetPalette() => _palette;

    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort color = _combo.IsDisabled ? GetColor(5)
                     : (state & Views.sfFocused) != 0 ? GetColor(3)
                     : GetColor(1);
        b.moveChar(0, ' ', color, size.x);

        string text = _combo.SelectedItem ?? string.Empty;
        int room = size.x - 2;
        if (room > 0 && text.Length > 0)
        {
            string shown = TComboBox.Clip(text, room);
            b.moveStr(1, shown, color);
            if (shown.Length < text.Length && !_combo.IsDisabled)
                b.moveChar(size.x - 1, '>', GetColor(4), 1);
        }
        WriteLine(0, 0, size.x, size.y, b);
    }
}

/// <summary>Edit area of an editable combo box; reports every text change to the combo box.</summary>
internal sealed class TComboBoxInputLine : TInputLine
{
    private static readonly TPalette _palette = new TPalette("\x13\x13\x14\x15\x1F", 5);
    private readonly TComboBox _combo;

    public TComboBoxInputLine(TComboBox combo, TRect bounds, int aMaxLen) : base(bounds, aMaxLen)
    {
        _combo = combo;
    }

    public override TPalette GetPalette() => _palette;

    public override void HandleEvent(ref TEvent @event)
    {
        string before = Data;
        base.HandleEvent(ref @event);
        if (!string.Equals(before, Data, StringComparison.Ordinal))
            _combo.OnInputEdited();
    }

    public override void Draw()
    {
        if (!_combo.IsDisabled)
        {
            base.Draw();
            return;
        }

        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort color = GetColor(5);
        b.moveChar(0, ' ', color, size.x);
        if (size.x > 2 && FirstPos < Data.Length)
            b.moveStr(1, TComboBox.Clip(Data.Substring(FirstPos), size.x - 2), color);
        WriteLine(0, 0, size.x, size.y, b);
    }

    // Replaces the text as a selection from the list does: everything selected, caret at the end.
    internal void Assign(string text)
    {
        Data = text;
        SelectAll(true);
    }
}

/// <summary>Dropdown arrow of a combo box: one button-colored surface with the arrow in its middle cell.</summary>
internal sealed class TComboBoxButton : TView
{
    // Button roles (normal, shortcut) plus the dialog's disabled-cluster role. The surface and the normal arrow use the
    // button's normal role (black on green); the arrow of a focused or open combo box uses the shortcut role (yellow on
    // green), which shares that background in every standard palette. A disabled combo box uses the disabled role.
    private static readonly TPalette _palette = new TPalette("\x0A\x0E\x1F", 3);
    private readonly TComboBox _combo;

    public TComboBoxButton(TComboBox combo, TRect bounds) : base(bounds)
    {
        _combo = combo;
    }

    public override TPalette GetPalette() => _palette;

    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort surface, arrow;
        if (_combo.IsDisabled)
            surface = arrow = GetColor(3);
        else
        {
            surface = GetColor(1);
            arrow = _combo.IsArrowHighlighted ? GetColor(2) : surface;
        }
        b.moveChar(0, ' ', surface, size.x);
        b.moveChar(size.x >= 3 ? size.x / 2 : 0, TSharpVisionGlyphs.ScrollArrowDown, arrow, 1);
        WriteLine(0, 0, size.x, size.y, b);
    }
}

/// <summary>
/// Modal popup of a combo box: a frameless-icon window holding the item list and, when the items do not fit,
/// a vertical scrollbar. It is inserted into the desktop, but maps its colors through the combo box, so it
/// looks like the THistory window of the dialog that holds the control.
/// </summary>
internal sealed class TComboBoxDropDown : TWindow
{
    // THistoryWindow roles: frame passive/active/icons, scrollbar page/controls, list normal/selected.
    private static readonly TPalette _palette = new TPalette("\x13\x13\x15\x18\x17\x13\x14", 7);
    private readonly TComboBox _combo;
    private TScrollBar? _scrollBar;

    public TComboBoxDropDown(TComboBox combo, TRect bounds, int highlight)
        : base(bounds, null, Views.wnNoNumber)
    {
        _combo = combo;
        flags = 0;
        growMode = 0;
        List = new TComboBoxList(combo, this, new TRect(1, 1, Math.Max(1, size.x - 1), Math.Max(1, size.y - 1)));
        Insert(List);
        Relayout(bounds, highlight);
    }

    /// <summary>The item list; its focused item is the highlight that Enter commits.</summary>
    public TComboBoxList List { get; }

    /// <summary>Tab or Shift+Tab that closed the dropdown, to be handed back to the dialog afterwards.</summary>
    public TEvent? ForwardedKey { get; private set; }

    internal TScrollBar? ScrollBar => _scrollBar;

    public override TPalette GetPalette() => _palette;

    public override byte MapColor(int index)
    {
        // Map through the combo box rather than the desktop the popup is inserted in.
        if (_combo.owner == null) return base.MapColor(index);
        TPalette p = GetPalette();
        if (index <= 0 || index > p.Size) return errorAttr;
        byte color = p[index];
        return color == 0 ? errorAttr : _combo.MapColor(color);
    }

    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min = new TPoint(3, 3);
    }

    public override void GetEvent(ref TEvent @event)
    {
        base.GetEvent(ref @event);
        if (endState == 0 && !_combo.SyncDropDown(this))
        {
            EndModal(Views.cmCancel);
            @event.What = Events.evNothing;
        }
    }

    public override void HandleEvent(ref TEvent @event)
    {
        switch (@event.What)
        {
            case Events.evMouseDown:
                if (!MouseInView(@event.mouse.where))
                {
                    // A click outside closes the dropdown and is consumed, so it changes nothing under it.
                    EndModal(Views.cmCancel);
                    ClearEvent(ref @event);
                    return;
                }
                break;

            case Events.evKeyDown:
            {
                ushort key = @event.keyDown.keyCode;
                bool alt = (@event.keyDown.controlKeyState & Keys.kbAltShift) != 0;
                if (key == Keys.kbEsc)
                {
                    EndModal(Views.cmCancel);
                    ClearEvent(ref @event);
                    return;
                }
                if (key == Keys.kbEnter || key == Keys.kbF4 || (alt && (key == Keys.kbDown || key == Keys.kbUp)))
                {
                    Commit();
                    ClearEvent(ref @event);
                    return;
                }
                if (key == Keys.kbTab || key == Keys.kbShiftTab)
                {
                    ForwardedKey = @event;
                    Commit();
                    ClearEvent(ref @event);
                    return;
                }
                break;
            }

            case Events.evCommand:
                if (@event.message.command == Views.cmCancel)
                {
                    EndModal(Views.cmCancel);
                    ClearEvent(ref @event);
                    return;
                }
                if (@event.message.command == Views.cmScreenResized)
                {
                    // The resize belongs to the program; close and let the loops below see it.
                    EndModal(Views.cmCancel);
                    PutEvent(ref @event);
                    ClearEvent(ref @event);
                    return;
                }
                break;

            case Events.evBroadcast:
                if (@event.message.command == Views.cmListItemSelected && ReferenceEquals(@event.message.infoPtr, List))
                {
                    Commit();
                    ClearEvent(ref @event);
                    return;
                }
                break;
        }
        base.HandleEvent(ref @event);
    }

    /// <summary>Ends the dropdown with the highlighted item, or cancels it when there is none.</summary>
    public void Commit() => EndModal(List.range > 0 ? Views.cmOK : Views.cmCancel);

    /// <summary>Moves and resizes the dropdown, adds or drops the scrollbar, and keeps the highlight in view.</summary>
    public void Relayout(TRect bounds) => Relayout(bounds, List.focused);

    private void Relayout(TRect bounds, int highlight)
    {
        if (bounds != GetBounds())
            Locate(bounds);

        int count = _combo.ItemCount;
        int rows = Math.Max(1, size.y - 2);
        TRect barBounds = new TRect(size.x - 1, 1, size.x, Math.Max(2, size.y - 1));
        if (count > rows && _scrollBar == null)
        {
            _scrollBar = new TScrollBar(barBounds);
            Insert(_scrollBar);
            List.vScrollBar = _scrollBar;
        }
        else if (count <= rows && _scrollBar != null)
        {
            TScrollBar old = _scrollBar;
            _scrollBar = null;
            List.vScrollBar = null;
            old.ShutDown();
        }
        else if (_scrollBar != null && _scrollBar.GetBounds() != barBounds)
        {
            _scrollBar.ChangeBounds(barBounds);
        }

        TRect inner = new TRect(1, 1, Math.Max(1, size.x - 1), Math.Max(1, size.y - 1));
        if (List.GetBounds() != inner) List.ChangeBounds(inner);
        List.Reset(count, highlight);
        if (!ReferenceEquals(current, List)) List.Select();
        DrawView();
    }
}

/// <summary>Item list of a combo box dropdown.</summary>
internal sealed class TComboBoxList : TListViewer
{
    // THistoryViewer roles, mapped through the dropdown window.
    private static readonly TPalette _palette = new TPalette("\x06\x06\x07\x06\x06", 5);
    private readonly TComboBox _combo;
    private readonly TComboBoxDropDown _popup;

    public TComboBoxList(TComboBox combo, TComboBoxDropDown popup, TRect bounds)
        : base(bounds, 1, null, null)
    {
        _combo = combo;
        _popup = popup;
        growMode = 0;
    }

    public override TPalette GetPalette() => _palette;

    public override string GetText(int item, int maxChars)
    {
        // The text starts one cell in; keep it, and any surrogate pair, inside the list.
        return TComboBox.Clip(_combo.ItemText(item), Math.Min(maxChars, size.x - 1));
    }

    public void Reset(int count, int highlight)
    {
        SetRange(count);
        SetNumCols(1);
        if (count == 0)
        {
            topItem = 0;
            focused = 0;
            DrawView();
            return;
        }
        topItem = Math.Clamp(topItem, 0, Math.Max(0, count - size.y));
        FocusItem(Math.Clamp(highlight, 0, count - 1));
    }

    public override void HandleEvent(ref TEvent @event)
    {
        if (@event.What == Events.evMouseDown)
        {
            TrackMouse(ref @event);
            return;
        }
        if (@event.What == Events.evKeyDown
            && (@event.keyDown.controlKeyState & (Keys.kbAltShift | Keys.kbCtrlShift)) == 0)
        {
            switch (@event.keyDown.keyCode)
            {
                case Keys.kbHome:
                    FocusItemNum(0);
                    ClearEvent(ref @event);
                    return;
                case Keys.kbEnd:
                    FocusItemNum(range - 1);
                    ClearEvent(ref @event);
                    return;
            }
            string text = KeyText.PrintableText(@event.keyDown);
            if (text.Length > 0 && text != " ")
            {
                int found = range > 0 ? TComboBox.FindByPrefix(_combo.Items, text, focused) : -1;
                if (found >= 0) FocusItemNum(found);
                ClearEvent(ref @event);
                return;
            }
        }
        base.HandleEvent(ref @event);
    }

    // Highlights the item under the pointer while the button is held, scrolling when it is dragged past an edge;
    // releasing it over the list commits the item.
    private void TrackMouse(ref TEvent @event)
    {
        int autos = 0;
        while (true)
        {
            if ((@event.What & Events.evMouse) != 0)
            {
                TPoint local = MakeLocal(@event.mouse.where);
                if (MouseInView(@event.mouse.where))
                {
                    int item = topItem + local.y;
                    if (item < range && item != focused) FocusItemNum(item);
                }
                else if (@event.What == Events.evMouseAuto && ++autos % 2 == 0 && range > 0)
                {
                    if (local.y < 0) FocusItemNum(focused - 1);
                    else if (local.y >= size.y) FocusItemNum(focused + 1);
                }
                if (@event.What == Events.evMouseUp) break;
            }
            GetEvent(ref @event);
            if (@event.What == Events.evCommand
                && @event.message.command is Views.cmCancel or Views.cmScreenResized)
                _popup.HandleEvent(ref @event); // share cancellation and exactly-once resize requeue
            if (_popup.endState != 0)
            {
                ClearEvent(ref @event);
                return;
            }
        }

        TPoint release = MakeLocal(@event.mouse.where);
        bool onItem = MouseInView(@event.mouse.where) && topItem + release.y < range;
        ClearEvent(ref @event);
        if (onItem) _popup.Commit();
    }
}
