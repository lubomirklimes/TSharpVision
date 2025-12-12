using TSharpVision.Constants;
namespace TSharpVision;

// Stream Read/Write/Build deferred. The shift-state-driven dual-mode
// search (incremental vs. directory-skip with Shift held) is preserved
// modulo Keys.cs not yet defining a kbShiftCode bit; we treat
// shiftKeys as 0 since TEvent.keyDown.shiftState is not surfaced yet.
public class TSortedListBox : TListBox
{
    public new static readonly string Name = "TSortedListBox";

    public byte shiftState;
    public ushort searchPos = 0xFFFF; // USHRT_MAX

    // Backing collection used by GetText. Distinct from the TStringCollection
    // stored on the base TListBox so non-string items are supported.
    protected TSortedCollection sortedItems;

    public TSortedListBox(TRect bounds, ushort aNumCols, TScrollBar aScrollBar)
        : base(bounds, aNumCols, aScrollBar)
    {
        ShowCursor();
        SetCursor(1, 0);
    }

    public virtual void NewList(TSortedCollection aList)
    {
        sortedItems = aList;
        SetRange(aList?.Count ?? 0);
        if (range > 0) FocusItem(0);
        searchPos = 0xFFFF;
        DrawView();
    }

    // Subclass hook: extract a string for display from a sorted-collection
    // entry. TFileList / TDirListBox override this with their own format.
    protected virtual string GetItemText(object item)
        => item?.ToString() ?? string.Empty;

    public override string GetText(int item, int maxChars)
    {
        if (sortedItems != null && item >= 0 && item < sortedItems.Count)
        {
            string s = GetItemText(sortedItems.At(item)) ?? string.Empty;
            if (s.Length > maxChars) s = s.Substring(0, maxChars);
            return s;
        }
        return base.GetText(item, maxChars);
    }

    public virtual object GetKey(string s) => s;

    // Handles ASCII type-ahead search. Backspace unwinds searchPos,
    // '.' jumps to the next dot in the current name,
    // and any other printable character extends the prefix and re-binds
    // the focus to the first matching entry.
    public override void HandleEvent(ref TEvent @event)
    {
        int oldValue = focused;
        base.HandleEvent(ref @event);
        if (oldValue != focused)
            searchPos = 0xFFFF;

        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmListItemSelected
            && ReferenceEquals(@event.message.infoPtr, sortedItems))
        {
            searchPos = 0xFFFF;
            ClearEvent(ref @event);
            return;
        }

        if (@event.What != Events.evKeyDown) return;

        ushort kc = @event.keyDown.keyCode;
        byte ch  = @event.keyDown.charScan.charCode;
        if (kc == Keys.kbEnter) return;
        if (ch == 0 && kc != Keys.kbBack) return;

        char[] cur = new char[256];
        int curLen;
        int value = focused;
        if (value < range)
        {
            string t = GetText(value, 255) ?? string.Empty;
            curLen = Math.Min(t.Length, 255);
            for (int i = 0; i < curLen; i++) cur[i] = t[i];
        }
        else curLen = 0;

        int oldPos = searchPos == 0xFFFF ? -1 : searchPos;

        if (kc == Keys.kbBack)
        {
            if (searchPos == 0xFFFF) return;
            // SET-style trim: drop the last character we appended.
            if (curLen > searchPos) curLen = searchPos;
            searchPos--;
        }
        else if (ch == (byte)'.')
        {
            int from = searchPos == 0xFFFF ? 0 : (int)searchPos;
            int loc = -1;
            for (int i = from; i < curLen; i++) if (cur[i] == '.') { loc = i; break; }
            if (loc >= 0)
            {
                searchPos = (ushort)loc;
                if (oldPos < 0) oldPos = 0;
            }
            else if (searchPos == 0xFFFF)
            {
                searchPos = 0;
                cur[0] = '.';
                curLen = 1;
                oldPos = 0;
            }
        }
        else
        {
            ushort newPos = (ushort)(searchPos == 0xFFFF ? 0 : searchPos + 1);
            if (newPos == 0) oldPos = 0;
            if (newPos < cur.Length)
            {
                cur[newPos] = (char)ch;
                if (curLen <= newPos) curLen = newPos + 1;
            }
            searchPos = newPos;
        }

        string prefix = new string(cur, 0, Math.Min(curLen, searchPos == 0xFFFF ? 0 : searchPos + 1));
        if (sortedItems != null)
        {
            object key = GetKey(prefix);
            if (sortedItems.Search(key, out int found) || found < sortedItems.Count)
                value = found;
            if (value < range)
            {
                string newStr = GetText(value, 255) ?? string.Empty;
                int lim = (searchPos == 0xFFFF) ? 0 : searchPos + 1;
                bool match = newStr.Length >= lim
                    && string.Compare(prefix, 0, newStr, 0, lim,
                                      StringComparison.OrdinalIgnoreCase) == 0;
                if (match)
                {
                    if (value != oldValue)
                    {
                        FocusItem(value);
                        SetCursor(cursor.x + lim - 1, cursor.y);
                    }
                    else
                    {
                        SetCursor(cursor.x + (lim - 1 - oldPos), cursor.y);
                    }
                }
                else
                {
                    searchPos = (ushort)(oldPos < 0 ? 0xFFFF : oldPos);
                }
            }
            else
            {
                searchPos = (ushort)(oldPos < 0 ? 0xFFFF : oldPos);
            }
        }

        bool isAlpha = ch >= (byte)'A' && (ch <= (byte)'Z' || (ch >= (byte)'a' && ch <= (byte)'z'));
        if ((searchPos != 0xFFFF ? searchPos : -1) != oldPos || isAlpha)
            ClearEvent(ref @event);
    }

    protected TSortedListBox(StreamableInit init) : base(init) { }
}
