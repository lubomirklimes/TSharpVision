// A draw buffer is a one-row scratch area sized to the maximum view width.
// Views fill it with moveChar/moveStr/moveCStr/moveBuf and then hand it to
// TView::writeLine / writeBuf. The semantics here track upstream so existing
// view drawing code (TStatusLine, TFrame, ...) works unchanged.
using System;

namespace SharpVision;

public ref struct TDrawBuffer
{
    /// <summary>
    /// Maximum supported view width, mirroring upstream's <c>maxViewWidth</c>
    /// constant. Default-constructed buffers size to this value when no
    /// explicit screen width has been negotiated yet.
    /// </summary>
    public const int MaxViewWidth = 256;

    private readonly Span<TScreenChar> _data;

    public Span<TScreenChar> Data => _data;
    public int Length => _data.Length;

    public TDrawBuffer()
    {
        int w = TScreen.ScreenWidth;
        if (w <= 0) w = MaxViewWidth;
        _data = new TScreenChar[w];
    }

    public TDrawBuffer(Span<TScreenChar> backing)
    {
        _data = backing;
    }

    /// <summary>
    /// Fills <paramref name="count"/> cells starting at <paramref name="indent"/>
    /// with character <paramref name="c"/> using <paramref name="attr"/>. When
    /// <paramref name="attr"/> is zero the existing cell attribute is left
    /// untouched; when <paramref name="c"/> is '\0' only the attribute is
    /// updated. Mirrors <c>TDrawBuffer::moveChar</c> with a bounds check.
    /// </summary>
    public void moveChar(int indent, char c, ushort attr, int count)
    {
        if (count <= 0 || indent < 0 || indent >= _data.Length) return;
        if (indent + count > _data.Length)
            count = _data.Length - indent;

        TColorAttr ca = (TColorAttr)attr;
        if (attr != 0)
        {
            if (c != '\0')
                for (int i = 0; i < count; i++)
                    _data[indent + i] = new TScreenChar(c, ca);
            else
                for (int i = 0; i < count; i++)
                    _data[indent + i].Attr = ca;
        }
        else
        {
            for (int i = 0; i < count; i++)
                _data[indent + i].Character = c;
        }
    }

    /// <summary>
    /// Writes <paramref name="str"/> at <paramref name="indent"/> using
    /// <paramref name="attr"/>. If <paramref name="attr"/> is zero the
    /// attribute already in the buffer is preserved. Truncates at end of
    /// buffer. Mirrors <c>TDrawBuffer::moveStr</c>.
    /// </summary>
    public void moveStr(int indent, string str, ushort attr)
    {
        if (str is null || indent < 0 || indent >= _data.Length) return;
        TColorAttr ca = (TColorAttr)attr;
        int max = _data.Length - indent;
        int n = System.Math.Min(str.Length, max);
        if (attr != 0)
            for (int i = 0; i < n; i++)
                _data[indent + i] = new TScreenChar(str[i], ca);
        else
            for (int i = 0; i < n; i++)
                _data[indent + i].Character = str[i];
    }

    /// <summary>
    /// Writes a substring of <paramref name="str"/> starting at
    /// <paramref name="strOffset"/>, padding/truncating to
    /// <paramref name="maxWidth"/>. Mirrors the SET extension overload.
    /// </summary>
    public void moveStr(int indent, string str, ushort attr, int maxWidth, int strOffset)
    {
        if (str is null || indent < 0 || indent >= _data.Length || maxWidth <= 0) return;
        TColorAttr ca = (TColorAttr)attr;
        int max = System.Math.Min(maxWidth, _data.Length - indent);
        int avail = System.Math.Max(0, str.Length - strOffset);
        int n = System.Math.Min(avail, max);
        for (int i = 0; i < n; i++)
            _data[indent + i] = new TScreenChar(str[strOffset + i], ca);
        // Pad remainder with spaces using the same attribute (upstream behavior).
        for (int i = n; i < max; i++)
            _data[indent + i] = new TScreenChar(' ', ca);
    }

    /// <summary>
    /// Writes <paramref name="str"/> with two attributes: the low byte of
    /// <paramref name="attrs"/> is the normal attribute, the high byte is
    /// applied between '~' toggle markers (used for hotkey highlighting).
    /// Mirrors <c>TDrawBuffer::moveCStr</c>.
    /// </summary>
    public ushort moveCStr(int indent, string str, ushort attrs)
    {
        if (str is null || indent < 0 || indent >= _data.Length) return 0;
        byte normal = (byte)(attrs & 0xff);
        byte hot    = (byte)((attrs >> 8) & 0xff);
        if (hot == 0) hot = normal;
        bool inHot = false;
        int dst = indent;
        int end = _data.Length;
        int i = 0;
        while (i < str.Length && dst < end)
        {
            char ch = str[i++];
            if (ch == '~') { inHot = !inHot; continue; }
            _data[dst++] = new TScreenChar(ch, new TColorAttr(inHot ? hot : normal));
        }
        return (ushort)(dst - indent);
    }

    /// <summary>Copies <paramref name="count"/> chars from <paramref name="source"/> into the buffer.</summary>
    public void moveBuf(int indent, System.ReadOnlySpan<char> source, ushort attr, int count)
    {
        if (count <= 0 || indent < 0 || indent >= _data.Length) return;
        if (indent + count > _data.Length) count = _data.Length - indent;
        if (count > source.Length) count = source.Length;
        TColorAttr ca = (TColorAttr)attr;
        if (attr != 0)
            for (int i = 0; i < count; i++)
                _data[indent + i] = new TScreenChar(source[i], ca);
        else
            for (int i = 0; i < count; i++)
                _data[indent + i].Character = source[i];
    }

    /// <summary>Sets only the attribute byte at <paramref name="indent"/>.</summary>
    public void putAttribute(int indent, ushort attr)
    {
        if ((uint)indent >= (uint)_data.Length) return;
        _data[indent].Attr = (TColorAttr)attr;
    }

    /// <summary>Sets only the character at <paramref name="indent"/>.</summary>
    public void putChar(int indent, char c)
    {
        if ((uint)indent >= (uint)_data.Length) return;
        _data[indent].Character = c;
    }
}
