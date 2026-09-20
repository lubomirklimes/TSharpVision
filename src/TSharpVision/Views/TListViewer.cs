using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Scrollable, column-based list view with focus tracking and owner broadcasts for focus and activation.</summary>
public class TListViewer : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TListViewer";

    private static readonly TPalette _palette = new TPalette("\x1A\x1A\x1B\x1C\x1D", 5);

    /// <summary>Glyph drawn between adjacent list columns.</summary>
    public static char ColumnSeparator = '│';

    /// <summary>Optional associated horizontal scrollbar controlling the text offset.</summary>
    public TScrollBar? hScrollBar;
    /// <summary>Optional associated scrollbar tracking the focused item and list navigation.</summary>
    public TScrollBar? vScrollBar;
    /// <summary>Number of displayed item columns; callers should use SetNumCols with a positive value.</summary>
    public int numCols;
    /// <summary>Zero-based item index at the beginning of the visible page.</summary>
    public int topItem;
    /// <summary>Zero-based focused item index; initialized to zero even for an empty list.</summary>
    public int focused;
    /// <summary>Number of available list items, defining valid indices from zero through range minus one.</summary>
    public int range;

    /// <summary>Creates an empty selectable list in owner-relative cell bounds with a positive column count and optional referenced scrollbars.</summary>
    public TListViewer(TRect bounds, ushort aNumCols, TScrollBar? aHScrollBar, TScrollBar? aVScrollBar)
        : base(bounds)
    {
        topItem = 0;
        focused = 0;
        range = 0;
        options |= (ushort)(Views.ofFirstClick | Views.ofSelectable);
        eventMask |= Events.evBroadcast;
        eventMask |= Events.evMouseWheel;
        hScrollBar = aHScrollBar;
        vScrollBar = aVScrollBar;
        SetNumCols(aNumCols);
    }

    /// <summary>Sets the positive column count and updates scrollbar arrow and page steps for the layout.</summary>
    public void SetNumCols(int aNumCols)
    {
        int arStep, pgStep;
        numCols = aNumCols;
        if (vScrollBar != null)
        {
            if (numCols == 1)
            {
                pgStep = size.y - 1;
                arStep = 1;
            }
            else
            {
                pgStep = size.y * numCols;
                arStep = size.y;
            }
            vScrollBar.SetStep(pgStep, arStep);
        }
        if (hScrollBar != null)
            hScrollBar.SetStep(size.x / numCols, 1);
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        base.ChangeBounds(bounds);
        if (hScrollBar != null)
            hScrollBar.SetStep(size.x / numCols, 1);
    }

    /// <inheritdoc />
    public override void Draw()
    {
        int i, j;
        int item;
        ushort normalColor, selectedColor, focusedColor = 0, color;
        int colWidth, curCol, indent;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        TDrawBuffer b = new TDrawBuffer(row);

        if ((state & (Views.sfSelected | Views.sfActive)) == (Views.sfSelected | Views.sfActive))
        {
            normalColor = GetColor(1);
            focusedColor = GetColor(3);
            selectedColor = GetColor(4);
        }
        else
        {
            normalColor = GetColor(2);
            selectedColor = GetColor(4);
        }

        indent = (hScrollBar != null) ? hScrollBar.value : 0;
        colWidth = size.x / numCols + 1;

        for (i = 0; i < size.y; i++)
        {
            for (j = 0; j < numCols; j++)
            {
                int width;
                item = j * size.y + i + topItem;
                curCol = j * colWidth;
                width = (j == numCols - 1) ? (size.x - curCol + 1) : colWidth;

                if ((state & (Views.sfSelected | Views.sfActive)) == (Views.sfSelected | Views.sfActive)
                    && focused == item && range > 0)
                {
                    color = focusedColor;
                    SetCursor(curCol + 1, i);
                }
                else if (item < range && IsSelected(item))
                    color = selectedColor;
                else
                    color = normalColor;

                b.moveChar(curCol, ' ', color, width);
                if (item < range)
                {
                    string text = GetText(item, width + indent);
                    string buf;
                    if (text.Length <= indent)
                        buf = string.Empty;
                    else
                    {
                        int avail = Math.Min(width, text.Length - indent);
                        buf = text.Substring(indent, avail);
                    }
                    if (buf.Length > 0)
                        b.moveStr(curCol + 1, buf, color);
                    // showMarkers / specialChars deferred.
                }
                else if (i == 0 && j == 0)
                    b.moveStr(
                        curCol + 1,
                        TSharpVisionIntl.Get("List_Empty", "<empty>"),
                        GetColor(1));

                b.moveChar(curCol + width - 1, ColumnSeparator, GetColor(5), 1);
            }
            WriteLine(0, i, size.x, 1, b);
        }
    }

    /// <summary>Focuses the supplied zero-based item, brings it into view, updates its scrollbar, and broadcasts cmListItemFocused when owned.</summary>
    public virtual void FocusItem(int item)
    {
        focused = item;
        if (item < topItem)
        {
            topItem = (numCols == 1) ? item : item - item % size.y;
        }
        else if (item >= topItem + size.y * numCols)
        {
            if (numCols == 1)
                topItem = item - size.y + 1;
            else
                topItem = item - item % size.y - (size.y * (numCols - 1));
        }
        if (vScrollBar != null)
            vScrollBar.SetValue(item);
        else
            DrawView();
        if (owner != null)
            Message(owner, Events.evBroadcast, Views.cmListItemFocused, this);
    }

    /// <summary>Clamps an index to the nonempty list's range before focusing it; does nothing for an empty list.</summary>
    public virtual void FocusItemNum(int item)
    {
        if (item < 0) item = 0;
        else if (item >= range && range > 0) item = range - 1;
        if (range != 0)
            FocusItem(item);
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Supplies display text for a zero-based item with the requested maximum length; the base implementation returns empty text.</summary>
    public virtual string GetText(int item, int maxLen) => string.Empty;

    /// <summary>Returns whether the zero-based item is selected; the base implementation compares it with the focused index.</summary>
    public virtual bool IsSelected(int item) => item == focused;

    private const int WheelStep = 3;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        TPoint mouse;
        int colWidth;
        int oldItem, newItem;
        int count;
        const int mouseAutosToSkip = 4;

        base.HandleEvent(ref @event);

        if (@event.What == Events.evMouseDown)
        {
            if (@event.mouse.doubleClick && range > focused)
            {
                SelectItem(focused);
                ClearEvent(ref @event);
                return;
            }
            colWidth = size.x / numCols + 1;
            oldItem = focused;
            mouse = MakeLocal(@event.mouse.where);
            newItem = mouse.y + (size.y * (mouse.x / colWidth)) + topItem;
            count = 0;
            do
            {
                if (newItem != oldItem)
                    FocusItemNum(newItem);
                oldItem = newItem;
                mouse = MakeLocal(@event.mouse.where);
                if (MouseInView(@event.mouse.where))
                    newItem = mouse.y + (size.y * (mouse.x / colWidth)) + topItem;
                else
                {
                    if (numCols == 1)
                    {
                        if (@event.What == Events.evMouseAuto) count++;
                        if (count == mouseAutosToSkip)
                        {
                            count = 0;
                            if (mouse.y < 0) newItem = focused - 1;
                            else if (mouse.y >= size.y) newItem = focused + 1;
                        }
                    }
                    else
                    {
                        if (@event.What == Events.evMouseAuto) count++;
                        if (count == mouseAutosToSkip)
                        {
                            count = 0;
                            if (mouse.x < 0) newItem = focused - size.y;
                            else if (mouse.x >= size.x) newItem = focused + size.y;
                            else if (mouse.y < 0) newItem = focused - focused % size.y;
                            else if (mouse.y > size.y) newItem = focused - focused % size.y + size.y - 1;
                        }
                    }
                }
            } while (MouseEvent(ref @event, (ushort)(Events.evMouseMove | Events.evMouseAuto)));
            FocusItemNum(newItem);
            if (@event.mouse.doubleClick && range > focused)
                SelectItem(focused);
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evKeyDown)
        {
            newItem = focused;
            if (@event.keyDown.charScan.charCode == ' ' && focused < range)
            {
                SelectItem(focused);
            }
            else
            {
                switch (@event.keyDown.keyCode)
                {
                    case Keys.kbUp:    newItem = focused - 1; break;
                    case Keys.kbDown:  newItem = focused + 1; break;
                    case Keys.kbRight:
                        if (numCols > 1) newItem = focused + size.y;
                        else { if (hScrollBar != null) hScrollBar.HandleEvent(ref @event); return; }
                        break;
                    case Keys.kbLeft:
                        if (numCols > 1) newItem = focused - size.y;
                        else { if (hScrollBar != null) hScrollBar.HandleEvent(ref @event); return; }
                        break;
                    case Keys.kbPgDn:     newItem = focused + size.y * numCols; break;
                    case Keys.kbPgUp:     newItem = focused - size.y * numCols; break;
                    case Keys.kbHome:     newItem = topItem; break;
                    case Keys.kbEnd:      newItem = topItem + (size.y * numCols) - 1; break;
                    case Keys.kbCtrlPgDn: newItem = range - 1; break;
                    case Keys.kbCtrlPgUp: newItem = 0; break;
                    default: return;
                }
                FocusItemNum(newItem);
            }
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evMouseWheel)
        {
            // Vertical wheel scrolls the focused item.
            bool up = (@event.mouse.eventFlags & Events.meWheelUp) != 0;
            bool down = (@event.mouse.eventFlags & Events.meWheelDown) != 0;
            if (!up && !down) return;
            FocusItemNum(focused + (up ? -WheelStep : WheelStep));
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evBroadcast)
        {
            if ((options & Views.ofSelectable) != 0)
            {
                if (@event.message.command == Views.cmScrollBarClicked &&
                    (ReferenceEquals(@event.message.infoPtr, hScrollBar) ||
                     ReferenceEquals(@event.message.infoPtr, vScrollBar)))
                {
                    Select();
                }
                else if (@event.message.command == Views.cmScrollBarChanged)
                {
                    if (vScrollBar != null && ReferenceEquals(vScrollBar, @event.message.infoPtr))
                    {
                        FocusItemNum(vScrollBar.value);
                        DrawView();
                    }
                    else if (ReferenceEquals(hScrollBar, @event.message.infoPtr))
                        DrawView();
                }
            }
        }
    }

    /// <summary>Broadcasts cmListItemSelected to the owner with this view as payload; the base implementation does not change focus.</summary>
    public virtual void SelectItem(int item)
    {
        Message(owner, Events.evBroadcast, Views.cmListItemSelected, this);
    }

    /// <summary>Sets the item count and updates scrollbar limits; resets focus to zero only when it exceeds the supplied count.</summary>
    public void SetRange(int aRange)
    {
        range = aRange;
        if (vScrollBar != null)
            vScrollBar.SetParams(focused, 0, aRange - 1, size.y - 1, 1);
        if (focused > aRange)
            focused = 0;
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfSelected | Views.sfActive)) != 0)
        {
            DrawView();
            if (hScrollBar != null)
            {
                if (GetState((ushort)(Views.sfActive | Views.sfSelected))) hScrollBar.Show();
                else hScrollBar.Hide();
            }
            if (vScrollBar != null)
            {
                if (GetState((ushort)(Views.sfActive | Views.sfSelected))) vScrollBar.Show();
                else vScrollBar.Hide();
            }
        }
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        hScrollBar = null;
        vScrollBar = null;
        base.ShutDown();
    }

    // Wire layout (after base): hScrollBar ptr, vScrollBar ptr,
    // numCols, topItem, focused, range (all 4B WriteInt).

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTListViewer =
        new TStreamableClass("TListViewer", () => new TListViewer(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TListViewer(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(hScrollBar);
        os.WritePointer(vScrollBar);
        os.WriteInt((uint)numCols);
        os.WriteInt((uint)topItem);
        os.WriteInt((uint)focused);
        os.WriteInt((uint)range);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        hScrollBar = isStream.ReadPointer() as TScrollBar;
        vScrollBar = isStream.ReadPointer() as TScrollBar;
        numCols  = (int)isStream.ReadInt();
        topItem  = (int)isStream.ReadInt();
        focused  = (int)isStream.ReadInt();
        range    = (int)isStream.ReadInt();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TListViewer(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() { return Name; }
}
