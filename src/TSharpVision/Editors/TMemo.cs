using System;
using System.Text;
using TSharpVision.Constants;

namespace TSharpVision;

// TMemo: a fixed-size TEditor variant whose buffer round-trips through
// TMemoData. Used by dialogs to embed a small free-text field.

// `struct TMemoData { uint32 length; char buffer[N]; }`.
// Upstream's `[65536]` is a trailing flexible array in practice - the dialog
// allocates `bufSize + sizeof(ushort)` bytes; we model that with a managed
// char[] of `data` length and a separate `length` field.
/// <summary>Contiguous text record exchanged with a memo control.</summary>
public sealed class TMemoData : IInfo
{
    /// <summary>Number of meaningful UTF-16 code units in the record buffer.</summary>
    public uint length;
    /// <summary>Contiguous UTF-16 storage; capacity must accommodate the transferred text.</summary>
    public char[] buffer;

    /// <summary>Creates an empty memo record with the requested code-unit capacity.</summary>
    public TMemoData(uint capacity)
    {
        length = 0;
        buffer = new char[capacity];
    }
}

/// <summary>Multiline dialog editor that exchanges contiguous text through TMemoData records.</summary>
public class TMemo : TEditor
{
    private const string CpMemo = "\x1A\x1B";
    private static readonly TPalette Palette = new TPalette(CpMemo, CpMemo.Length);

    /// <summary>Creates an empty memo at owner-relative cell bounds with code-unit capacity and associated scroll/status controls.</summary>
    public TMemo(TRect bounds, TScrollBar aHScrollBar, TScrollBar aVScrollBar,
                 TIndicator aIndicator, uint aBufSize)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, aBufSize)
    {
    }

    /// <inheritdoc />
    public override ushort DataSize() => (ushort)(bufSize + sizeof(ushort));

    /// <summary>Copies logical text into a caller-provided record with sufficient capacity, sets its length, and clears unused trailing storage.</summary>
    public virtual void GetData(TMemoData data)
    {
        data.length = bufLen;
        if (curPtr > 0)
            Array.Copy(buffer, 0, data.buffer, 0, (int)curPtr);
        uint right = bufLen - curPtr;
        if (right > 0)
            Array.Copy(buffer, (int)(curPtr + gapLen),
                       data.buffer, (int)curPtr, (int)right);
        if (bufLen < (uint)data.buffer.Length)
            Array.Clear(data.buffer, (int)bufLen,
                        data.buffer.Length - (int)bufLen);
    }

    /// <summary>Replaces memo text from the record and resets editing state; record length must fit the allocated memo capacity.</summary>
    public virtual void SetData(TMemoData data)
    {
        if (data.length > 0)
            Array.Copy(data.buffer, 0,
                       buffer, (int)(bufSize - data.length),
                       (int)data.length);
        SetBufLen(data.length);
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => Palette;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What != Events.evKeyDown
            || ev.keyDown.keyCode != Keys.kbTab)
        {
            base.HandleEvent(ref ev);
        }
    }

    // Wire: TEditor base + buffer content as a UTF-16 stream string.
    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TMemo(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(GetText());
    }

    private string GetText()
    {
        var sb = new StringBuilder((int)bufLen);
        for (uint p = 0; p < curPtr; p++)
            sb.Append(buffer[p]);
        uint right = bufLen - curPtr;
        for (uint p = 0; p < right; p++)
            sb.Append(buffer[curPtr + gapLen + p]);
        return sb.ToString();
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        string text = isStream.ReadString() ?? string.Empty;
        uint length = (uint)Math.Min(text.Length, (int)bufSize);
        if (isValid && length > 0)
        {
            // Upstream places live data at the end: buffer + bufSize - length.
            text.CopyTo(0, buffer, (int)(bufSize - length), (int)length);
            SetBufLen(length);
        }
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TMemo(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTMemo =
        new TStreamableClass("TMemo", () => new TMemo(StreamableInit.streamableInit), 0);
}
