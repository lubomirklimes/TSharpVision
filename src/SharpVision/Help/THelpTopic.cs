using System;

namespace SharpVision;

// THelpTopic — a single help screen. Owns a linked list of paragraphs and
// an array of cross-refs. Knows how to wrap each paragraph at the current
// `width` (set by THelpViewer.Draw to its viewport width).
//
// Wire format (write order, see helpbase.cc:30):
//   writeParagraphs:
//     int   count
//     for each: ushort size; int wrap; byte[size] text
//   writeCrossRefs:
//     int   numRefs
//     for each: int ref; int offset; byte length
public class THelpTopic : TStreamable
{
    public const string TypeName = "THelpTopic";
    public override string streamableName => TypeName;

    /// Optional cross-ref serialiser hook. Upstream
    /// <c>TCrossRefHandler</c> defaults to <c>notAssigned</c>; when set to
    /// a custom handler, the help compiler can substitute a symbolic name
    /// for the numeric topic id at write time.
    public delegate void TCrossRefHandler(Opstream s, int value);
    public static TCrossRefHandler crossRefHandler = NotAssigned;

    public TParagraph paragraphs;
    public int numRefs;
    public TCrossRef[] crossRefs;

    private int _width;
    private int _lastOffset;
    private int _lastLine;
    private TParagraph _lastParagraph;

    // Used by `getLine` to skip the line-wrapping CRLF that upstream
    // injects via fixed 256-byte stack buffers; we mirror the limit.
    private const int LineBufLen = 256;

    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(TypeName, () => new THelpTopic(), 0);

    public THelpTopic()
    {
        paragraphs = null;
        numRefs = 0;
        crossRefs = null;
        _width = 0;
        _lastOffset = 0;
        _lastLine = int.MaxValue;
        _lastParagraph = null;
    }

    public void AddCrossRef(TCrossRef r)
    {
        var p = new TCrossRef[numRefs + 1];
        if (numRefs > 0)
            Array.Copy(crossRefs, p, numRefs);
        crossRefs = p;
        crossRefs[numRefs] = r;
        numRefs++;
    }

    public void AddParagraph(TParagraph p)
    {
        if (paragraphs == null)
        {
            paragraphs = p;
        }
        else
        {
            var pp = paragraphs;
            while (pp.next != null) pp = pp.next;
            pp.next = p;
        }
        p.next = null;
    }

    public void GetCrossRef(int i, ref TPoint loc, out byte length, out int @ref)
    {
        int paraOffset = 0, curOffset = 0, oldOffset = 0;
        int line = 0;
        var c = crossRefs[i];
        int offset = c.offset;
        var p = paragraphs;
        while (p != null && paraOffset + curOffset < offset)
        {
            var lbuf = new byte[LineBufLen];
            oldOffset = paraOffset + curOffset;
            WrapText(p.text, p.size, ref curOffset, p.wrap, lbuf);
            line++;
            if (curOffset >= p.size)
            {
                paraOffset += p.size;
                p = p.next;
                curOffset = 0;
            }
        }
        loc.x = offset - oldOffset;
        loc.y = line;
        length = c.length;
        @ref = c.@ref;
    }

    public byte[] GetLine(int line, byte[] buffer)
    {
        int offset;
        TParagraph p;

        if (_lastLine < line)
        {
            int i = line;
            line -= _lastLine;
            _lastLine = i;
            offset = _lastOffset;
            p = _lastParagraph;
        }
        else
        {
            p = paragraphs;
            offset = 0;
            _lastLine = line;
        }
        if (buffer.Length > 0) buffer[0] = 0;
        while (p != null)
        {
            while (offset < p.size)
            {
                line--;
                int len = WrapText(p.text, p.size, ref offset, p.wrap, buffer);
                if (line == 0)
                {
                    _lastOffset = offset;
                    _lastParagraph = p;
                    return buffer;
                }
                _ = len; // upstream truncates via strncpy; we already cap.
            }
            p = p.next;
            offset = 0;
        }
        if (buffer.Length > 0) buffer[0] = 0;
        return buffer;
    }

    public int GetNumCrossRefs() => numRefs;

    public int NumLines()
    {
        int lines = 0;
        var p = paragraphs;
        while (p != null)
        {
            int offset = 0;
            while (offset < p.size)
            {
                lines++;
                var lbuf = new byte[LineBufLen];
                WrapText(p.text, p.size, ref offset, p.wrap, lbuf);
            }
            p = p.next;
        }
        return lines;
    }

    public void SetCrossRef(int i, TCrossRef r)
    {
        if (i < numRefs) crossRefs[i] = r;
    }

    public void SetNumCrossRefs(int i)
    {
        if (numRefs == i) return;
        var p = new TCrossRef[i];
        if (numRefs > 0)
        {
            int copy = Math.Min(i, numRefs);
            Array.Copy(crossRefs, p, copy);
        }
        crossRefs = p;
        numRefs = i;
    }

    public void SetWidth(int aWidth) => _width = aWidth;

    // Returns the number of bytes consumed; writes the line into lineBuf
    // (NUL-terminated within Length, with any trailing newline stripped).
    private int WrapText(byte[] text, int size, ref int offset, bool wrap, byte[] lineBuf)
    {
        // Defensive: clamp size to actual array length so text[i] is always
        // within bounds. Handles the case where p.size > p.text.Length.
        if (size > text.Length) size = text.Length;
        // Guard against exhausted or empty input (callers use while(offset<p.size)
        // but protect defensively to avoid zero-advance infinite loops).
        if (size <= 0 || offset >= size)
        {
            if (lineBuf.Length > 0) lineBuf[0] = 0;
            return 0;
        }

        int i = ScanForNewline(text, offset);
        if (i + offset > size) i = size - offset;
        if ((i >= _width) && wrap && _width > 0)
        {
            i = offset + _width;
            if (i > size)
            {
                i = size;
            }
            else
            {
                // Crash fix: when i == size, text[i] is out of
                // bounds (text.Length == size). Clamp to the last valid index
                // before the backward word-boundary scan.
                if (i >= size) i = size - 1;
                while (i > offset && !IsBlank(text[i])) i--;
                if (i == offset)
                {
                    i = offset + _width;
                    while (i < size && !IsBlank(text[i])) i++;
                    if (i < size) i++;
                }
                else
                {
                    i++;
                }
            }
            if (i == offset) i = offset + _width;
            i -= offset;
        }
        TextToLine(text, offset, Math.Min(i, lineBuf.Length - 1), lineBuf);
        // Strip the trailing newline if present (mirrors upstream's CRLF
        // strip on the way out of wrapText).
        int len = ByteLen(lineBuf);
        if (len > 0 && lineBuf[len - 1] == (byte)'\n')
            lineBuf[len - 1] = 0;
        offset += Math.Min(i, lineBuf.Length - 1);
        return i;
    }

    private static int ScanForNewline(byte[] p, int offset)
    {
        int limit = Math.Min(256, p.Length - offset);
        for (int j = 0; j < limit; j++)
            if (p[offset + j] == (byte)'\n') return j + 1;
        return 256;
    }

    private static void TextToLine(byte[] text, int offset, int length, byte[] line)
    {
        if (length < 0) length = 0;
        if (length > line.Length - 1) length = line.Length - 1;
        Array.Copy(text, offset, line, 0, length);
        line[length] = 0;
    }

    private static bool IsBlank(byte ch)
        => ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r'
        || ch == '\v' || ch == '\f';

    private static int ByteLen(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
            if (b[i] == 0) return i;
        return b.Length;
    }

    public override void Write(Opstream os)
    {
        WriteParagraphs(os);
        WriteCrossRefs(os);
    }

    public override object Read(Ipstream s)
    {
        ReadParagraphs(s);
        ReadCrossRefs(s);
        _width = 0;
        _lastLine = int.MaxValue;
        return this;
    }

    private void ReadParagraphs(Ipstream s)
    {
        int i = (int)s.ReadInt();
        TParagraph head = null;
        TParagraph tail = null;
        while (i > 0)
        {
            ushort sz = s.ReadShort();
            int wrapInt = (int)s.ReadInt();
            var p = new TParagraph
            {
                size = sz,
                wrap = wrapInt != 0,
                text = new byte[sz],
            };
            s.ReadBytes(p.text, sz);
            if (head == null) head = p; else tail.next = p;
            tail = p;
            i--;
        }
        if (tail != null) tail.next = null;
        paragraphs = head;
    }

    private void ReadCrossRefs(Ipstream s)
    {
        numRefs = (int)s.ReadInt();
        crossRefs = new TCrossRef[numRefs];
        for (int i = 0; i < numRefs; i++)
        {
            crossRefs[i] = new TCrossRef
            {
                @ref = (int)s.ReadInt(),
                offset = (int)s.ReadInt(),
                length = s.ReadByte(),
            };
        }
    }

    private void WriteParagraphs(Opstream s)
    {
        int count = 0;
        for (var p = paragraphs; p != null; p = p.next) count++;
        s.WriteInt((uint)count);
        for (var p = paragraphs; p != null; p = p.next)
        {
            s.WriteShort(p.size);
            s.WriteInt((uint)(p.wrap ? 1 : 0));
            s.WriteBytes(p.text, p.size);
        }
    }

    private void WriteCrossRefs(Opstream s)
    {
        s.WriteInt((uint)numRefs);
        if (crossRefHandler == NotAssigned)
        {
            for (int i = 0; i < numRefs; i++)
            {
                var c = crossRefs[i];
                s.WriteInt((uint)c.@ref);
                s.WriteInt((uint)c.offset);
                s.WriteByte(c.length);
            }
        }
        else
        {
            for (int i = 0; i < numRefs; i++)
            {
                var c = crossRefs[i];
                crossRefHandler(s, c.@ref);
                s.WriteInt((uint)c.offset);
                s.WriteByte(c.length);
            }
        }
    }

    public static void NotAssigned(Opstream _, int __) { }
}
