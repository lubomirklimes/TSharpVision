using System;
using SharpVision.Constants;

namespace SharpVision;

// TMemo: a fixed-size TEditor variant whose buffer round-trips
// through TMemoData (a length-prefixed flat byte array). Used by dialogs
// to embed a small free-text field. Stream Read/Write/Build deferred.

// `struct TMemoData { uint32 length; char buffer[N]; }`.
// Upstream's `[65536]` is a trailing flexible array in practice — the dialog
// allocates `bufSize + sizeof(ushort)` bytes; we model that with a managed
// byte[] of `data` length and a separate `length` field.
public sealed class TMemoData : IInfo
{
    public uint length;
    public byte[] buffer;

    public TMemoData(uint capacity)
    {
        length = 0;
        buffer = new byte[capacity];
    }
}

public class TMemo : TEditor
{
    private const string CpMemo = "\x1A\x1B";
    private static readonly TPalette Palette = new TPalette(CpMemo, CpMemo.Length);

    public TMemo(TRect bounds, TScrollBar aHScrollBar, TScrollBar aVScrollBar,
                 TIndicator aIndicator, uint aBufSize)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, aBufSize)
    {
    }

    public virtual uint DataSize() => bufSize + sizeof(ushort);

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

    public virtual void SetData(TMemoData data)
    {
        if (data.length > 0)
            Array.Copy(data.buffer, 0,
                       buffer, (int)(bufSize - data.length),
                       (int)data.length);
        SetBufLen(data.length);
    }

    public override TPalette GetPalette() => Palette;

    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What != Events.evKeyDown
            || ev.keyDown.keyCode != Keys.kbTab)
        {
            base.HandleEvent(ref ev);
        }
    }

    // Wire: TEditor base + bufLen(uint32) + buffer content (curPtr bytes + bufLen-curPtr bytes).
    protected TMemo(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt(bufLen);
        // Write the two live regions of the gap buffer contiguously.
        if (curPtr > 0)
            os.WriteBytes(buffer, (int)curPtr);
        uint right = bufLen - curPtr;
        if (right > 0)
            os.WriteBytes(buffer, (int)(curPtr + gapLen), (int)right);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        uint length = isStream.ReadInt();
        if (isValid)
        {
            // Upstream places live data at the end: buffer + bufSize - length.
            isStream.ReadBytes(buffer, (int)(bufSize - length), (int)length);
            SetBufLen(length);
        }
        else
        {
            // Skip the content bytes even when buffer allocation failed.
            isStream.Seekg(isStream.Tellg() + (long)length);
        }
        return this;
    }

    public new static TStreamable Build() => new TMemo(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTMemo =
        new TStreamableClass("TMemo", () => new TMemo(StreamableInit.streamableInit), 0);
}
