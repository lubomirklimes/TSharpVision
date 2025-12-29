using System;
using TSharpVision.Constants;

namespace TSharpVision;

public class TEditor : TView
{
    public static byte[] clipboardBuffer;
    public static TEditor clipboard;
    public static ushort editorFlags = (ushort)(Views.efBackupFiles | Views.efPromptOnReplace);
    public static byte[] findStr = new byte[Views.maxFindStrLen];
    public static byte[] replaceStr = new byte[Views.maxReplaceStrLen];
    public static uint tabSize = 8;

    // editorDialog callback — upstream `TEditorDialog`. Default returns
    // cmCancel, mirroring `defEditorDialog`
    public delegate ushort TEditorDialog(int dialog, object info);
    public static TEditorDialog editorDialog =
        (int _dlg, object _info) => Views.cmCancel;

    public TScrollBar hScrollBar;
    public TScrollBar vScrollBar;
    public TIndicator indicator;
    public byte[] buffer;
    public uint bufSize;
    public uint bufLen;
    public uint gapLen;
    public uint selStart;
    public uint selEnd;
    public uint curPtr;
    public TPoint curPos;
    public TPoint delta;
    public TPoint limit;
    public int drawLine;
    public uint drawPtr;
    public uint delCount;
    public uint insCount;
    public bool isValid;
    public bool canUndo = true;
    public bool modified;
    public bool selecting;
    public bool overwrite;
    public bool autoIndent;
    public byte updateFlags;
    public int lockCount;
    public int keyState;
    public string errorInfo = string.Empty;

    private const string CpEditor = "\x06\x07";
    private static readonly TPalette Palette = new TPalette(CpEditor, CpEditor.Length);
    private const int WheelStep = 3;

    // keyMap[0]=firstKeys, keyMap[1]=quickKeys (Ctrl-Q), keyMap[2]=blockKeys (Ctrl-K)
    private const int KeyStateQuick = 1; // Ctrl-Q prefix active
    private const int KeyStateBlock = 2; // Ctrl-K prefix active

    public TEditor(TRect bounds, TScrollBar aHScrollBar, TScrollBar aVScrollBar,
                   TIndicator aIndicator, uint aBufSize)
        : base(bounds)
    {
        hScrollBar = aHScrollBar;
        vScrollBar = aVScrollBar;
        indicator  = aIndicator;
        bufSize    = aBufSize;
        growMode   = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        options    = (ushort)(options | Views.ofSelectable);
        eventMask  = (ushort)(Events.evMouseDown | Events.evKeyDown
                              | Events.evCommand | Events.evBroadcast
                              | Events.evMouseWheel);
        ShowCursor();
        InitBuffer();
        if (buffer != null)
            isValid = true;
        else
        {
            bufSize = 0;
            isValid = false;
        }
        SetBufLen(0);
    }

    public override void ShutDown()
    {
        DoneBuffer();
        base.ShutDown();
    }

    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        delta.x = Math.Max(0, Math.Min(delta.x, limit.x - size.x));
        delta.y = Math.Max(0, Math.Min(delta.y, limit.y - size.y));
        Update(Views.ufView);
    }

    public byte BufChar(uint p)
    {
        if (p >= curPtr) p += gapLen;
        return buffer[p];
    }

    public uint BufPtr(uint p) => p < curPtr ? p : p + gapLen;

    public int CharPos(uint p, uint target)
    {
        int pos = 0;
        while (p < target)
        {
            if (BufChar(p) == 0x09)
                pos += (int)tabSize - (pos % (int)tabSize) - 1;
            pos++;
            p++;
        }
        return pos;
    }

    public uint CharPtr(uint p, int target)
    {
        int pos = 0;
        while (pos < target && p < bufLen
            && BufChar(p) != 0x0D && BufChar(p) != 0x0A)
        {
            if (BufChar(p) == 0x09)
                pos += (int)tabSize - (pos % (int)tabSize) - 1;
            pos++;
            p++;
        }
        if (pos > target) p--;
        return p;
    }

    public uint LineEnd(uint p)
    {
        if (p == bufLen) return p;
        byte c = BufChar(p);
        while (c != 0x0D && c != 0x0A)
        {
            p++;
            if (p == bufLen) return p;
            c = BufChar(p);
        }
        return p;
    }

    public uint LineStart(uint p)
    {
        while (p > 0)
        {
            byte c = BufChar(p - 1);
            if (c == 0x0D || c == 0x0A) return p;
            p--;
        }
        return 0;
    }

    public uint NextChar(uint p)
    {
        if (p == bufLen) return p;
        p++;
        return p;
    }

    public uint PrevChar(uint p)
    {
        if (p == 0) return p;
        return p - 1;
    }

    public uint NextLine(uint p) => NextChar(LineEnd(p));

    public uint PrevLine(uint p) => LineStart(PrevChar(p));

    private static bool IsWordChar(byte ch)
        => (ch >= 'a' && ch <= 'z')
        || (ch >= 'A' && ch <= 'Z')
        || (ch >= '0' && ch <= '9')
        || ch == '_';

    public uint NextWord(uint p)
    {
        if (IsWordChar(BufChar(p)))
            while (p < bufLen && IsWordChar(BufChar(p)))
                p = NextChar(p);
        else if (p < bufLen)
            p = NextChar(p);
        while (p < bufLen && !IsWordChar(BufChar(p)))
            p = NextChar(p);
        return p;
    }

    public uint PrevWord(uint p)
    {
        while (p > 0 && !IsWordChar(BufChar(PrevChar(p))))
            p = PrevChar(p);
        while (p > 0 && IsWordChar(BufChar(PrevChar(p))))
            p = PrevChar(p);
        return p;
    }

    public uint LineMove(uint p, int count)
    {
        uint i = p;
        p = LineStart(p);
        int pos = CharPos(p, i);
        while (count != 0)
        {
            i = p;
            if (count < 0) { p = PrevLine(p); count++; }
            else           { p = NextLine(p); count--; }
        }
        if (p != i) p = CharPtr(p, pos);
        return p;
    }

    public bool HasSelection() => selStart != selEnd;

    public void HideSelect()
    {
        selecting = false;
        SetSelect(curPtr, curPtr, false);
    }

    public bool CursorVisible()
        => curPos.x >= delta.x && curPos.x < delta.x + size.x
           && curPos.y >= delta.y && curPos.y < delta.y + size.y;

    private static int CountLines(byte[] buf, uint offset, uint count)
    {
        int n = 0;
        for (uint i = 0; i < count; i++)
            if (buf[offset + i] == 0x0A) n++;
        return n;
    }

    public void SetSelect(uint newStart, uint newEnd, bool curStart)
    {
        uint p = curStart ? newStart : newEnd;
        byte flags = Views.ufUpdate;
        if (newStart != selStart || newEnd != selEnd)
            if (newStart != newEnd || selStart != selEnd)
                flags = Views.ufView;

        if (p != curPtr)
        {
            if (p > curPtr)
            {
                uint l = p - curPtr;
                Array.Copy(buffer, curPtr + gapLen, buffer, curPtr, l);
                curPos.y += CountLines(buffer, curPtr, l);
                curPtr = p;
            }
            else
            {
                uint l = curPtr - p;
                curPtr = p;
                curPos.y -= CountLines(buffer, curPtr, l);
                Array.Copy(buffer, curPtr, buffer, curPtr + gapLen, l);
            }
            drawLine = curPos.y;
            drawPtr  = LineStart(p);
            curPos.x = CharPos(drawPtr, p);
            delCount = 0;
            insCount = 0;
            SetBufSize(bufLen);
        }
        selStart = newStart;
        selEnd   = newEnd;
        Update(flags);
    }

    public void SetCurPtr(uint p, byte selectMode)
    {
        uint anchor;
        if ((selectMode & Views.smExtend) == 0)
            anchor = p;
        else if (curPtr == selStart)
            anchor = selEnd;
        else
            anchor = selStart;

        if (p < anchor)
        {
            if ((selectMode & Views.smDouble) != 0)
            {
                p = PrevLine(NextLine(p));
                anchor = NextLine(PrevLine(anchor));
            }
            SetSelect(p, anchor, true);
        }
        else
        {
            if ((selectMode & Views.smDouble) != 0)
            {
                p = NextLine(p);
                anchor = PrevLine(NextLine(anchor));
            }
            SetSelect(anchor, p, false);
        }
    }

    public void ScrollTo(int x, int y)
    {
        x = Math.Max(0, Math.Min(x, limit.x - size.x));
        y = Math.Max(0, Math.Min(y, limit.y - size.y));
        if (x != delta.x || y != delta.y)
        {
            delta.x = x;
            delta.y = y;
            Update(Views.ufView);
        }
    }

    public void TrackCursor(bool center)
    {
        if (center)
            ScrollTo(curPos.x - size.x + 1, curPos.y - size.y / 2);
        else
            ScrollTo(
                Math.Max(curPos.x - size.x + 1, Math.Min(delta.x, curPos.x)),
                Math.Max(curPos.y - size.y + 1, Math.Min(delta.y, curPos.y)));
    }

    public void StartSelect()
    {
        HideSelect();
        selecting = true;
    }

    public void ToggleInsMode()
    {
        overwrite = !overwrite;
        SetState(Views.sfCursorIns, !GetState(Views.sfCursorIns));
    }

    public void InitBuffer() => buffer = new byte[bufSize];

    public void DoneBuffer() => buffer = null;

    public bool InsertBuffer(byte[] p, uint offset, uint length,
                             bool allowUndo, bool selectText)
    {
        selecting = false;
        uint selLen = selEnd - selStart;
        if (selLen == 0 && length == 0) return true;

        uint delLen = 0;
        if (allowUndo)
        {
            if (curPtr == selStart) delLen = selLen;
            else if (selLen > insCount) delLen = selLen - insCount;
        }

        uint newSize = bufLen + delCount - selLen + delLen + length;
        if (newSize > bufLen + delCount)
            if (!SetBufSize(newSize))
                return false;

        // Snapshot the input so that, if `p` aliases our own buffer
        // (insertFrom uses the source editor's buffer directly), the
        // subsequent in-place memmoves cannot corrupt it.
        byte[] src = null;
        if (length > 0)
        {
            src = new byte[length];
            Array.Copy(p, (int)offset, src, 0, (int)length);
        }

        int selLines = CountLines(buffer, BufPtr(selStart), selLen);
        if (curPtr == selEnd)
        {
            if (allowUndo)
            {
                if (delLen > 0)
                    Array.Copy(buffer, selStart,
                               buffer, curPtr + gapLen - delCount - delLen,
                               delLen);
                insCount -= selLen - delLen;
            }
            curPtr = selStart;
            curPos.y -= selLines;
        }
        if (delta.y > curPos.y)
        {
            delta.y -= selLines;
            if (delta.y < curPos.y) delta.y = curPos.y;
        }

        if (length > 0)
            Array.Copy(src, 0, buffer, (int)curPtr, (int)length);

        int lines = length > 0 ? CountLines(buffer, curPtr, length) : 0;
        curPtr += length;
        curPos.y += lines;
        drawLine = curPos.y;
        drawPtr  = LineStart(curPtr);
        curPos.x = CharPos(drawPtr, curPtr);
        if (!selectText) selStart = curPtr;
        selEnd = curPtr;
        bufLen += length - selLen;
        gapLen -= length - selLen;
        if (allowUndo)
        {
            delCount += delLen;
            insCount += length;
        }
        limit.y += lines - selLines;
        delta.y = Math.Max(0, Math.Min(delta.y, limit.y - size.y));
        if (!IsClipboard()) modified = true;
        SetBufSize(bufLen + delCount);
        Update(selLines == 0 && lines == 0 ? Views.ufLine : Views.ufView);
        return true;
    }

    public bool InsertFrom(TEditor editor)
        => InsertBuffer(editor.buffer,
                        editor.BufPtr(editor.selStart),
                        editor.selEnd - editor.selStart,
                        canUndo, IsClipboard());

    public bool InsertText(byte[] text, uint length, bool selectText)
        => InsertBuffer(text ?? Array.Empty<byte>(), 0, length, canUndo, selectText);

    public bool IsClipboard() => ReferenceEquals(clipboard, this);

    public void DeleteRange(uint startPtr, uint endPtr, bool delSelect)
    {
        if (HasSelection() && delSelect)
            DeleteSelect();
        else
        {
            SetSelect(curPtr, endPtr, true);
            DeleteSelect();
            SetSelect(startPtr, curPtr, false);
            DeleteSelect();
        }
    }

    public void DeleteSelect() => InsertText(null, 0, false);

    public void NewLine()
    {
        uint p = LineStart(curPtr);
        uint i = p;
        while (i < curPtr && (buffer[BufPtr(i)] == ' ' || buffer[BufPtr(i)] == 0x09))
            i++;
        InsertText(new byte[] { 0x0A }, 1, false);
        if (autoIndent)
        {
            uint indentLen = i - p;
            var indent = new byte[indentLen];
            for (uint k = 0; k < indentLen; k++)
                indent[k] = buffer[BufPtr(p + k)];
            InsertText(indent, indentLen, false);
        }
    }

    // In addition to the in-process clipboard, mirror the copied
    // selection to ClipboardService.Current as a best-effort write. OS
    // clipboard failures are silent and never block the internal copy.
    public bool ClipCopy()
    {
        bool res = false;
        if (clipboard != null && clipboard != this)
        {
            // Capture the selected slice as a Latin-1 string before the
            // internal copy runs (selStart/selEnd are not modified by
            // InsertFrom, but capturing first keeps the contract clear).
            string mirrored = SelectionAsClipboardString();
            res = clipboard.InsertFrom(this);
            selecting = false;
            Update(Views.ufUpdate);
            if (res && mirrored.Length > 0)
            {
                try { ClipboardService.Current?.SetText(mirrored); }
                catch { /* best-effort — never fail editor copy on OS error */ }
            }
        }
        return res;
    }

    public void ClipCut()
    {
        if (ClipCopy()) DeleteSelect();
    }

    // Paste policy P1 — try OS clipboard first, then fall back
    // to the in-process clipboard editor.
    public void ClipPaste()
    {
        var svc = ClipboardService.Current;
        if (svc != null && svc.IsAvailable && svc.TryGetText(out string text))
        {
            byte[] bytes = ClipboardEncoding.ClipboardStringToBytes(text);
            if (bytes.Length > 0)
            {
                InsertText(bytes, (uint)bytes.Length, false);
                return;
            }
            // Empty payload from the OS clipboard counts as a no-op rather
            // than falling through to the internal clipboard, matching
            // platform expectations ("paste returned nothing").
            if (text != null && text.Length > 0)
            {
                // Source had only unsupported chars / oversized — still
                // do not fall through; the user asked for OS paste.
                return;
            }
        }
        if (clipboard != null && clipboard != this)
            InsertFrom(clipboard);
    }

    // Extract the current selection as a Latin-1 string
    // suitable for passing to IClipboardService.SetText. Newlines remain LF;
    // OS-specific normalisation is the service's responsibility.
    private string SelectionAsClipboardString()
    {
        if (selStart >= selEnd) return string.Empty;
        var sb = new System.Text.StringBuilder((int)(selEnd - selStart));
        for (uint p = selStart; p < selEnd; p++)
            sb.Append((char)BufChar(p));
        return sb.ToString();
    }

    public void Undo()
    {
        if (delCount != 0 || insCount != 0)
        {
            selStart = curPtr - insCount;
            selEnd = curPtr;
            uint length = delCount;
            delCount = 0;
            insCount = 0;
            InsertBuffer(buffer, curPtr + gapLen - length, length, false, true);
        }
    }

    // Length of a null-terminated upstream `findStr` / `replaceStr` byte
    // buffer (the TEditor statics keep the C-string convention).
    private static uint ByteStrLen(byte[] s)
    {
        if (s == null) return 0;
        for (uint i = 0; i < s.Length; i++)
            if (s[i] == 0) return i;
        return (uint)s.Length;
    }

    private static uint Scan(TEditor ed, uint startPos, byte[] needle)
    {
        uint nlen = ByteStrLen(needle);
        if (nlen == 0) return Views.sfSearchFailed;
        if (ed.bufLen < nlen) return Views.sfSearchFailed;
        uint last = ed.bufLen - nlen;
        for (uint i = startPos; i <= last; i++)
        {
            uint j = 0;
            while (j < nlen && ed.BufChar(i + j) == needle[j])
                j++;
            if (j == nlen) return i - startPos;
        }
        return Views.sfSearchFailed;
    }

    private static uint IScan(TEditor ed, uint startPos, byte[] needle)
    {
        uint nlen = ByteStrLen(needle);
        if (nlen == 0) return Views.sfSearchFailed;
        if (ed.bufLen < nlen) return Views.sfSearchFailed;
        uint last = ed.bufLen - nlen;
        for (uint i = startPos; i <= last; i++)
        {
            uint j = 0;
            while (j < nlen
                   && ToUpperByte(ed.BufChar(i + j))
                      == ToUpperByte(needle[j]))
                j++;
            if (j == nlen) return i - startPos;
        }
        return Views.sfSearchFailed;
    }

    private static byte ToUpperByte(byte b)
        => (b >= (byte)'a' && b <= (byte)'z') ? (byte)(b - 32) : b;

    public bool Search(byte[] needle, ushort opts)
    {
        uint pos = curPtr;
        uint i;
        uint nlen = ByteStrLen(needle);
        do
        {
            i = ((opts & Views.efCaseSensitive) != 0)
                ? Scan(this, pos, needle)
                : IScan(this, pos, needle);

            if (i != Views.sfSearchFailed)
            {
                i += pos;
                bool wholeWordsOnly = (opts & Views.efWholeWordsOnly) != 0;
                bool boundaryViolated = wholeWordsOnly && (
                       (i != 0 && IsWordChar(BufChar(i - 1)))
                    || (i + nlen != bufLen && IsWordChar(BufChar(i + nlen)))
                );
                if (!boundaryViolated)
                {
                    Lock();
                    SetSelect(i, i + nlen, false);
                    TrackCursor(!CursorVisible());
                    Unlock();
                    return true;
                }
                pos = i + 1;
            }
        } while (i != Views.sfSearchFailed);
        return false;
    }

    public void DoSearchReplace()
    {
        ushort i;
        do
        {
            i = Views.cmCancel;
            if (!Search(findStr, editorFlags))
            {
                if ((editorFlags & (Views.efReplaceAll | Views.efDoReplace))
                    != (Views.efReplaceAll | Views.efDoReplace))
                    editorDialog(Views.edSearchFailed, null);
            }
            else if ((editorFlags & Views.efDoReplace) != 0)
            {
                i = Views.cmYes;
                if ((editorFlags & Views.efPromptOnReplace) != 0)
                {
                    var c = MakeGlobal(cursor);
                    i = editorDialog(Views.edReplacePrompt, c);
                }
                if (i == Views.cmYes)
                {
                    Lock();
                    InsertText(replaceStr, ByteStrLen(replaceStr), false);
                    TrackCursor(false);
                    Unlock();
                }
            }
        } while (i != Views.cmCancel
                 && (editorFlags & Views.efReplaceAll) != 0);
    }

    public void Find()
    {
        var rec = new TFindDialogRec(findStr, editorFlags);
        if (editorDialog(Views.edFind, rec) != Views.cmCancel)
        {
            CopyToFixedBuffer(rec.Find, findStr);
            editorFlags = (ushort)(rec.Options & ~Views.efDoReplace);
            DoSearchReplace();
        }
    }

    public void Replace()
    {
        var rec = new TReplaceDialogRec(findStr, replaceStr, editorFlags);
        if (editorDialog(Views.edReplace, rec) != Views.cmCancel)
        {
            CopyToFixedBuffer(rec.Find, findStr);
            CopyToFixedBuffer(rec.Replace, replaceStr);
            editorFlags = (ushort)(rec.Options | Views.efDoReplace);
            DoSearchReplace();
        }
    }

    private static void CopyToFixedBuffer(byte[] src, byte[] dst)
    {
        Array.Clear(dst, 0, dst.Length);
        if (src == null) return;
        int n = Math.Min(src.Length, dst.Length - 1);
        Array.Copy(src, 0, dst, 0, n);
    }


    // newSize <= bufSize. TFileEditor overrides for realloc.
    public virtual bool SetBufSize(uint newSize) => newSize <= bufSize;

    public void SetBufLen(uint length)
    {
        bufLen   = length;
        gapLen   = bufSize - length;
        selStart = 0;
        selEnd   = 0;
        curPtr   = 0;
        delta    = default;
        curPos   = delta;
        limit.x  = Views.maxLineLength;
        limit.y  = (buffer != null && bufLen > 0
                       ? CountLines(buffer, gapLen, bufLen)
                       : 0) + 1;
        drawLine = 0;
        drawPtr  = 0;
        delCount = 0;
        insCount = 0;
        modified = false;
        Update(Views.ufView);
    }

    public override bool Valid(ushort _) => isValid;
    
    public void Lock() => lockCount++;

    public void Unlock()
    {
        if (lockCount > 0)
        {
            lockCount--;
            if (lockCount == 0) DoUpdate();
        }
    }

    public void Update(byte aFlags)
    {
        updateFlags |= aFlags;
        if (lockCount == 0) DoUpdate();
    }

    public void DoUpdate()
    {
        if (updateFlags != 0)
        {
            SetCursor(curPos.x - delta.x, curPos.y - delta.y);
            if ((updateFlags & Views.ufView) != 0)
                DrawView();
            else if ((updateFlags & Views.ufLine) != 0)
                DrawLines(curPos.y - delta.y, 1, LineStart(curPtr));
            hScrollBar?.SetParams(delta.x, 0,
                limit.x - size.x, size.x / 2, 1);
            vScrollBar?.SetParams(delta.y, 0,
                limit.y - size.y, size.y - 1, 1);
            indicator?.SetValue(curPos, modified);
            if ((state & Views.sfActive) != 0) UpdateCommands();
            updateFlags = 0;
        }
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        switch (aState)
        {
            case Views.sfActive:
                hScrollBar?.SetState(Views.sfVisible, enable);
                vScrollBar?.SetState(Views.sfVisible, enable);
                indicator?.SetState(Views.sfVisible, enable);
                UpdateCommands();
                break;
            case Views.sfExposed:
                if (enable) Unlock();
                break;
        }
    }

    public virtual void UpdateCommands()
    {
        SetCmdState(Views.cmUndo, delCount != 0 || insCount != 0);
        if (!IsClipboard())
        {
            SetCmdState(Views.cmCut,  HasSelection());
            SetCmdState(Views.cmCopy, HasSelection());
            SetCmdState(Views.cmPaste,
                clipboard != null && clipboard.HasSelection());
        }
        SetCmdState(Views.cmClear,        HasSelection());
        SetCmdState(Views.cmFind,         true);
        SetCmdState(Views.cmReplace,      true);
        SetCmdState(Views.cmSearchAgain,  true);
    }

    public void SetCmdState(ushort command, bool enable)
    {
        var s = new TCommandSet();
        s.EnableCmd(command);
        if (enable && (state & Views.sfActive) != 0)
            EnableCommands(s);
        else
            DisableCommands(s);
    }

    public override void Draw()
    {
        if (drawLine != delta.y)
        {
            drawPtr = LineMove(drawPtr, delta.y - drawLine);
            drawLine = delta.y;
        }
        DrawLines(0, size.y, drawPtr);
    }

    public void DrawLines(int y, int count, uint linePtr)
    {
        ushort color = GetColor(0x0201);
        while (count-- > 0)
        {
            var b = new TDrawBuffer();
            FormatLine(b, linePtr, delta.x + size.x, color);
            // Match upstream `&b[delta.x]`: write the slice of the formatted
            // buffer starting at delta.x, preserving per-cell attributes.
            WriteLine(0, y, size.x, 1, ExtractRange(b, delta.x, size.x));
            linePtr = NextLine(linePtr);
            y++;
        }
    }

    // Helper for DrawLines: pull `count` chars from `b.Data` starting at
    // `start`. Prevents allocating outside the printable window.
    private static TScreenChar[] ExtractRange(TDrawBuffer b, int start, int count)
    {
        var dst = new TScreenChar[count];
        for (int i = 0; i < count; i++)
        {
            int idx = start + i;
            dst[i] = idx < b.Data.Length ? b.Data[idx] : default;
        }
        return dst;
    }

    // FormatLine helper inlined here (originally in tvedit3.cc; this is
    // the colour-flat editor variant).
    // Walks the line at `linePtr` expanding tabs and applying the selection
    // highlight (selStart..selEnd) from the high byte of `color`.
    private void FormatLine(TDrawBuffer b, uint linePtr, int width, ushort color)
    {
        ushort normal = (byte)color;
        ushort selected = (byte)(color >> 8);
        int x = 0;
        uint p = linePtr;
        while (x < width)
        {
            if (p >= bufLen) { b.moveChar(x, ' ', normal, 1); x++; continue; }
            byte c = BufChar(p);
            if (c == 0x0D || c == 0x0A) { b.moveChar(x, ' ', normal, 1); x++; continue; }
            ushort attr = (p >= selStart && p < selEnd) ? selected : normal;
            if (c == 0x09)
            {
                int next = x + (int)tabSize - (x % (int)tabSize);
                while (x < next && x < width) { b.moveChar(x, ' ', attr, 1); x++; }
            }
            else
            {
                b.moveChar(x, (char)c, attr, 1);
                x++;
            }
            p++;
        }
    }

    public uint GetMousePtr(TPoint m)
    {
        TPoint mouse = MakeLocal(m);
        mouse.x = Math.Max(0, Math.Min(mouse.x, size.x - 1));
        mouse.y = Math.Max(0, Math.Min(mouse.y, size.y - 1));
        return CharPtr(LineMove(drawPtr, mouse.y + delta.y - drawLine),
            mouse.x + delta.x);
    }

    public void CheckScrollBar(ref TEvent ev, TScrollBar p, ref int d)
    {
        if (ev.message.infoPtr == p && p.value != d)
        {
            d = p.value;
            Update(Views.ufView);
        }
    }

    // full Ctrl-K / Ctrl-Q prefix state machine added.
    // keyState == 0  → normal key lookup (firstKeys)
    // keyState == KeyStateQuick (1) → Ctrl-Q prefix (quickKeys)
    // keyState == KeyStateBlock (2) → Ctrl-K prefix (blockKeys)
    public void ConvertEvent(ref TEvent ev)
    {
        if (ev.What != Events.evKeyDown) return;
        ushort key = ev.keyDown.keyCode;
        byte charCode = ev.keyDown.charScan.charCode;

        if (keyState != 0)
        {
            // In prefix mode: normalise the incoming key to an uppercase letter
            // for secondary-table lookup. 
            // convertEvent — kbCtA..kbCtZ are shifted to kbA..kbZ before scan.
            byte letter;
            if (key >= Keys.kbCtrlA && key <= Keys.kbCtrlZ)
                letter = (byte)('A' + (key - Keys.kbCtrlA));
            else
            {
                letter = ev.keyDown.charScan.charCode;
                if (letter >= 'a' && letter <= 'z')
                    letter = (byte)(letter - 32);
            }

            int savedState = keyState;
            keyState = 0; // always clear prefix state regardless of outcome

            if (key == Keys.kbEsc)
            {
                // Esc cancels prefix mode silently.
                ClearEvent(ref ev);
                return;
            }

            ushort cmd = savedState == KeyStateQuick
                ? MapQuickKey(letter)
                : savedState == KeyStateBlock
                    ? MapBlockKey(letter)
                    : (ushort)0;

            if (cmd != 0)
            {
                ev.What = Events.evCommand;
                ev.message.command = cmd;
            }
            else
            {
                // Unrecognised prefix sequence — consume safely (no insert).
                // v1 intentionally deviates from upstream which passes through;
                // the spec requires buffer unchanged on unrecognised sequences.
                ClearEvent(ref ev);
            }
            return;
        }

        if (charCode == 9 || (charCode >= 32 && charCode < 255))
            return;

        // keyState == 0: normal key mapping.
        // firstKeys table (abridged — only the movement/editing keys that are
        // not intercepted by a higher layer such as the status line or menu bar).
        switch (key)
        {
            case Keys.kbCtrlK:
                // Enter Ctrl-K prefix (blockKeys). Source: firstKeys kbCtK→0xFF02
                keyState = KeyStateBlock;
                ClearEvent(ref ev);
                return;
            case Keys.kbCtrlQ:
                // Enter Ctrl-Q prefix (quickKeys). Source: firstKeys kbCtQ→0xFF01
                keyState = KeyStateQuick;
                ClearEvent(ref ev);
                return;
            // Modern editor shortcuts. These are intentionally
            // only matched when keyState == 0; the Ctrl-K / Ctrl-Q prefix
            // cases above run first, so a Ctrl-K followed by Ctrl+C still
            // dispatches as the prefix sequence (block paste), not as an
            // OS-style Copy.
            case Keys.kbCtrlC:
                ev.What = Events.evCommand; ev.message.command = Views.cmCopy; break;
            case Keys.kbCtrlV:
                ev.What = Events.evCommand; ev.message.command = Views.cmPaste; break;
            case Keys.kbCtrlX:
                ev.What = Events.evCommand; ev.message.command = Views.cmCut; break;
            case Keys.kbLeft:      ev.What = Events.evCommand; ev.message.command = Views.cmCharLeft;  break;
            case Keys.kbRight:     ev.What = Events.evCommand; ev.message.command = Views.cmCharRight; break;
            case Keys.kbCtrlLeft:  ev.What = Events.evCommand; ev.message.command = Views.cmWordLeft;  break;
            case Keys.kbCtrlRight: ev.What = Events.evCommand; ev.message.command = Views.cmWordRight; break;
            case Keys.kbHome:      ev.What = Events.evCommand; ev.message.command = Views.cmLineStart; break;
            case Keys.kbEnd:       ev.What = Events.evCommand; ev.message.command = Views.cmLineEnd;   break;
            case Keys.kbUp:        ev.What = Events.evCommand; ev.message.command = Views.cmLineUp;    break;
            case Keys.kbDown:      ev.What = Events.evCommand; ev.message.command = Views.cmLineDown;  break;
            case Keys.kbPgUp:      ev.What = Events.evCommand; ev.message.command = Views.cmPageUp;    break;
            case Keys.kbPgDn:      ev.What = Events.evCommand; ev.message.command = Views.cmPageDown;  break;
            case Keys.kbCtrlPgUp:  ev.What = Events.evCommand; ev.message.command = Views.cmTextStart; break;
            case Keys.kbCtrlPgDn:  ev.What = Events.evCommand; ev.message.command = Views.cmTextEnd;   break;
            case Keys.kbIns:       ev.What = Events.evCommand; ev.message.command = Views.cmInsMode;   break;
            case Keys.kbDel:       ev.What = Events.evCommand; ev.message.command = Views.cmDelChar;   break;
            case Keys.kbBack:      ev.What = Events.evCommand; ev.message.command = Views.cmBackSpace;  break;
            case Keys.kbEnter:     ev.What = Events.evCommand; ev.message.command = Views.cmNewLine;    break;
        }
    }

    // Secondary key map for Ctrl-Q prefix.
    private static ushort MapQuickKey(byte letter) => letter switch
    {
        (byte)'A' => Views.cmReplace,    // Ctrl-Q A → Replace
        (byte)'C' => Views.cmTextEnd,    // Ctrl-Q C → move to end of file
        (byte)'D' => Views.cmLineEnd,    // Ctrl-Q D → move to end of line
        (byte)'F' => Views.cmFind,       // Ctrl-Q F → Find
        (byte)'H' => Views.cmDelStart,   // Ctrl-Q H → delete to start of line
        (byte)'R' => Views.cmTextStart,  // Ctrl-Q R → move to start of file
        (byte)'S' => Views.cmLineStart,  // Ctrl-Q S → move to start of line
        (byte)'Y' => Views.cmDelEnd,     // Ctrl-Q Y → delete to end of line
        _ => 0
    };

    // Secondary key map for Ctrl-K prefix.
    private static ushort MapBlockKey(byte letter) => letter switch
    {
        (byte)'B' => Views.cmStartSelect, // Ctrl-K B → mark block beginning
        (byte)'C' => Views.cmPaste,       // Ctrl-K C → insert block (paste from clipboard)
        (byte)'H' => Views.cmHideSelect,  // Ctrl-K H → hide/show selection
        (byte)'K' => Views.cmCopy,        // Ctrl-K K → mark block end (copy to clipboard)
        (byte)'Y' => Views.cmCut,         // Ctrl-K Y → delete/cut selected block
        _ => 0
    };

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        ConvertEvent(ref ev);
        bool centerCursor = !CursorVisible();
        byte selectMode = 0;
        if (selecting) selectMode = Views.smExtend;

        switch (ev.What)
        {
            case Events.evMouseDown:
                if (ev.mouse.doubleClick) selectMode |= Views.smDouble;
                do
                {
                    Lock();
                    if (ev.What == Events.evMouseAuto)
                    {
                        TPoint mouse = MakeLocal(ev.mouse.where);
                        TPoint d = delta;
                        if (mouse.x < 0) d.x--;
                        if (mouse.x >= size.x) d.x++;
                        if (mouse.y < 0) d.y--;
                        if (mouse.y >= size.y) d.y++;
                        ScrollTo(d.x, d.y);
                    }
                    SetCurPtr(GetMousePtr(ev.mouse.where), selectMode);
                    selectMode |= Views.smExtend;
                    Unlock();
                } while (MouseEvent(ref ev,
                    (ushort)(Events.evMouseMove | Events.evMouseAuto)));
                break;

            case Events.evKeyDown:
                if (ev.keyDown.charScan.charCode == 9
                    || (ev.keyDown.charScan.charCode >= 32
                        && ev.keyDown.charScan.charCode < 255))
                {
                    Lock();
                    if (overwrite && !HasSelection())
                        if (curPtr != LineEnd(curPtr))
                            selEnd = NextChar(curPtr);
                    InsertText(new byte[] { ev.keyDown.charScan.charCode }, 1, false);
                    TrackCursor(centerCursor);
                    Unlock();
                }
                else return;
                break;

            case Events.evCommand:
                switch (ev.message.command)
                {
                    case Views.cmInsertText:
                        if (ev.message.infoPtr is TextInfo ti)
                            InsertText(ti.Bytes, (uint)ti.Bytes.Length, false);
                        break;
                    default:
                        Lock();
                        switch (ev.message.command)
                        {
                            case Views.cmCut:        ClipCut(); break;
                            case Views.cmCopy:       ClipCopy(); break;
                            case Views.cmPaste:      ClipPaste(); break;
                            case Views.cmUndo:       Undo(); break;
                            case Views.cmClear:      DeleteSelect(); break;
                            case Views.cmCharLeft:
                                SetCurPtr(PrevChar(curPtr), selectMode); break;
                            case Views.cmCharRight:
                                SetCurPtr(NextChar(curPtr), selectMode); break;
                            case Views.cmWordLeft:
                                SetCurPtr(PrevWord(curPtr), selectMode); break;
                            case Views.cmWordRight:
                                SetCurPtr(NextWord(curPtr), selectMode); break;
                            case Views.cmLineStart:
                                SetCurPtr(LineStart(curPtr), selectMode); break;
                            case Views.cmLineEnd:
                                SetCurPtr(LineEnd(curPtr), selectMode); break;
                            case Views.cmLineUp:
                                SetCurPtr(LineMove(curPtr, -1), selectMode); break;
                            case Views.cmLineDown:
                                SetCurPtr(LineMove(curPtr, 1), selectMode); break;
                            case Views.cmPageUp:
                                SetCurPtr(LineMove(curPtr, -(size.y - 1)), selectMode); break;
                            case Views.cmPageDown:
                                SetCurPtr(LineMove(curPtr, size.y - 1), selectMode); break;
                            case Views.cmTextStart:
                                SetCurPtr(0, selectMode); break;
                            case Views.cmTextEnd:
                                SetCurPtr(bufLen, selectMode); break;
                            case Views.cmNewLine:
                                NewLine(); break;
                            case Views.cmBackSpace:
                                DeleteRange(PrevChar(curPtr), curPtr, true); break;
                            case Views.cmDelChar:
                                DeleteRange(curPtr, NextChar(curPtr), true); break;
                            case Views.cmDelWord:
                                DeleteRange(curPtr, NextWord(curPtr), false); break;
                            case Views.cmDelStart:
                                DeleteRange(LineStart(curPtr), curPtr, false); break;
                            case Views.cmDelEnd:
                                DeleteRange(curPtr, LineEnd(curPtr), false); break;
                            case Views.cmDelLine:
                                DeleteRange(LineStart(curPtr), NextLine(curPtr), false); break;
                            case Views.cmInsMode:
                                ToggleInsMode(); break;
                            case Views.cmStartSelect:
                                StartSelect(); break;
                            case Views.cmHideSelect:
                                HideSelect(); break;
                            case Views.cmIndentMode:
                                autoIndent = !autoIndent; break;
                            case Views.cmFind:
                                Find(); break;
                            case Views.cmReplace:
                                Replace(); break;
                            case Views.cmSearchAgain:
                                DoSearchReplace(); break;
                            default:
                                Unlock(); return;
                        }
                        TrackCursor(centerCursor);
                        Unlock();
                        break;
                }
                break;

            case Events.evBroadcast:
                switch (ev.message.command)
                {
                    case Views.cmScrollBarChanged:
                        CheckScrollBar(ref ev, hScrollBar, ref delta.x);
                        CheckScrollBar(ref ev, vScrollBar, ref delta.y);
                        break;
                    default:
                        return;
                }
                break;

            case Events.evMouseWheel:
            {
                // Wheel scrolls the viewport without moving the
                // cursor or touching the buffer contents.
                bool up = (ev.mouse.buttons & Events.mbButton4) != 0;
                ScrollTo(delta.x, delta.y + (up ? -WheelStep : WheelStep));
                break;
            }
        }
        ClearEvent(ref ev);
    }

    public override TPalette GetPalette() => Palette;

    // Wire: TView base + hScrollBar(ptr) + vScrollBar(ptr) + indicator(ptr)
    //       + bufSize(uint32) + canUndo(short).
    protected TEditor(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(hScrollBar);
        os.WritePointer(vScrollBar);
        os.WritePointer(indicator);
        os.WriteInt(bufSize);
        os.WriteShort((ushort)(canUndo ? 1 : 0));
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        hScrollBar = (TScrollBar)isStream.ReadPointer();
        vScrollBar = (TScrollBar)isStream.ReadPointer();
        indicator  = (TIndicator)isStream.ReadPointer();
        bufSize    = isStream.ReadInt();
        canUndo    = isStream.ReadShort() != 0;
        selecting  = false;
        overwrite  = false;
        autoIndent = false;
        lockCount  = 0;
        keyState   = 0;
        InitBuffer();
        if (buffer != null)
            isValid = true;
        else
        {
            editorDialog(Views.edOutOfMemory, null);
            bufSize = 0;
        }
        lockCount = 0;
        Lock();
        SetBufLen(0);
        return this;
    }

    public static TStreamable Build() => new TEditor(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTEditor =
        new TStreamableClass("TEditor", () => new TEditor(StreamableInit.streamableInit), 0);
}

// Carrier for cmInsertText evCommand payload. The upstream `infoPtr` was a
// raw `void *` to a C string; our IInfo marker requires a typed wrapper.
public sealed class TextInfo : IInfo
{
    public byte[] Bytes;
    public TextInfo(byte[] b) { Bytes = b; }
}

public sealed class TFindDialogRec : IInfo
{
    public byte[] Find;
    public ushort Options;
    public TFindDialogRec(byte[] f, ushort opts)
    {
        Find = (byte[])f.Clone();
        Options = opts;
    }
}

public sealed class TReplaceDialogRec : IInfo
{
    public byte[] Find;
    public byte[] Replace;
    public ushort Options;
    public TReplaceDialogRec(byte[] f, byte[] r, ushort opts)
    {
        Find = (byte[])f.Clone();
        Replace = (byte[])r.Clone();
        Options = opts;
    }
}
