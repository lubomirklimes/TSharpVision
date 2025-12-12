using TSharpVision.Constants;
namespace TSharpVision;

public class TWindow : TGroup
{
    public new static readonly string Name = "TWindow";

    public static readonly TPoint MinWinSize = new TPoint(16, 6);

    public byte flags;
    public TRect zoomRect;
    public ushort number;
    public short palette;
    public TFrame frame;
    public string title;

    public TWindow(TRect bounds, string aTitle, ushort aNumber) : base(bounds)
    {
        flags = (byte)(Views.wfMove | Views.wfGrow | Views.wfClose | Views.wfZoom);
        zoomRect = GetBounds();
        number = aNumber;
        palette = (short)Views.wpBlueWindow;
        title = aTitle;

        state |= Views.sfShadow;
        options |= (ushort)(Views.ofSelectable | Views.ofTopSelect);
        growMode = (byte)(Views.gfGrowAll | Views.gfGrowRel);
        eventMask |= Events.evMouseUp;

        frame = InitFrame(GetExtent());
        if (frame != null) Insert(frame);
    }

    ~TWindow() { }

    public virtual void Close()
    {
        if (Valid(Views.cmClose))
        {
            // Notify the application that we're closing.
            // Upstream: message(TProgram::application, evBroadcast, cmClosingWindow, this).
            PutEvent(Events.evBroadcast, Views.cmClosingWindow, this as IInfo);
            frame = null;
            owner?.Remove(this);
        }
    }

    public override void ShutDown()
    {
        frame = null;
        // Call TGroup.ShutDown() so that child views (incl. the
        // TFrame slot) are shut down and this window removes itself from its
        // owner's circular list.  Previously this was a no-op ("not yet
        // ported"), which caused TGroup.ShutDown() to loop forever when the
        // group contained open windows.
        base.ShutDown();
    }

    private static readonly TPalette _blue = new TPalette("\x08\x09\x0A\x0B\x0C\x0D\x0E\x0F", 8);
    private static readonly TPalette _cyan = new TPalette("\x10\x11\x12\x13\x14\x15\x16\x17", 8);
    private static readonly TPalette _gray = new TPalette("\x18\x19\x1A\x1B\x1C\x1D\x1E\x1F", 8);
    public override TPalette GetPalette()
    {
        return palette switch
        {
            (short)Views.wpCyanWindow => _cyan,
            (short)Views.wpGrayWindow => _gray,
            _ => _blue,
        };
    }

    public virtual string GetTitle(short maxSize) => title;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (@event.What == Events.evCommand)
        {
            switch (@event.message.command)
            {
                case Views.cmResize:
                    if ((flags & (Views.wfMove | Views.wfGrow)) != 0 && owner != null)
                    {
                        TRect limits = owner.GetExtent();
                        TPoint min = default, max = default;
                        SizeLimits(ref min, ref max);
                        DragView(@event,
                            (byte)(dragMode | (flags & (Views.wfMove | Views.wfGrow))),
                            ref limits, min, max);
                        ClearEvent(ref @event);
                    }
                    break;
                case Views.cmClose:
                    if ((flags & Views.wfClose) != 0
                        && (@event.message.infoPtr == null
                            || ReferenceEquals(@event.message.infoPtr, this)))
                    {
                        if ((state & Views.sfModal) == 0)
                            Close();
                        else
                        {
                            @event.What = Events.evCommand;
                            @event.message.command = Views.cmCancel;
                            PutEvent(ref @event);
                        }
                        ClearEvent(ref @event);
                    }
                    break;
                case Views.cmZoom:
                    if ((flags & Views.wfZoom) != 0
                        && (@event.message.infoPtr == null
                            || ReferenceEquals(@event.message.infoPtr, this)))
                    {
                        Zoom();
                        ClearEvent(ref @event);
                    }
                    break;
            }
        }
        else if (@event.What == Events.evKeyDown)
        {
            switch (@event.keyDown.keyCode)
            {
                case Keys.kbTab:
                case Keys.kbDown:
                case Keys.kbRight:
                    SelectNext(false);
                    ClearEvent(ref @event);
                    break;
                case Keys.kbShiftTab:
                case Keys.kbUp:
                case Keys.kbLeft:
                    SelectNext(true);
                    ClearEvent(ref @event);
                    break;
            }
        }
        else if (@event.What == Events.evBroadcast
                 && @event.message.command == Views.cmSelectWindowNum
                 && @event.message.infoInt == number
                 && (options & Views.ofSelectable) != 0)
        {
            Select();
            ClearEvent(ref @event);
        }
    }

    public static TFrame InitFrame(TRect r) => new TFrame(r);

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & Views.sfSelected) != 0)
        {
            SetState(Views.sfActive, enable);
            if (frame != null) frame.SetState(Views.sfActive, enable);

            void C(ushort cmd) { if (enable) EnableCommand(cmd); else DisableCommand(cmd); }
            C(Views.cmNext);
            C(Views.cmPrev);
            if ((flags & (Views.wfGrow | Views.wfMove)) != 0) C(Views.cmResize);
            if ((flags & Views.wfClose) != 0) C(Views.cmClose);
            if ((flags & Views.wfZoom)  != 0) C(Views.cmZoom);
        }
    }

    public TScrollBar StandardScrollBar(ushort aOptions)
    {
        TRect r = GetExtent();
        if ((aOptions & Views.sbVertical) != 0)
            r = new TRect(r.b.x - 1, r.a.y + 1, r.b.x, r.b.y - 1);
        else
            r = new TRect(r.a.x + 2, r.b.y - 1, r.b.x - 2, r.b.y);
        var s = new TScrollBar(r);
        Insert(s);
        if ((aOptions & Views.sbHandleKeyboard) != 0)
            s.options |= Views.ofPostProcess;
        return s;
    }

    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min = MinWinSize;
    }

    public virtual void Zoom()
    {
        TPoint minSize = default, maxSize = default;
        SizeLimits(ref minSize, ref maxSize);
        if (size != maxSize)
        {
            zoomRect = GetBounds();
            TRect r = new TRect(0, 0, maxSize.x, maxSize.y);
            Locate(r);
        }
        else
            Locate(zoomRect);
    }

    // Wire layout (after TGroup base):
    //   flags    (1B WriteByte)
    //   zoomRect (16B WriteTRect)
    //   number   (2B WriteShort — ushort)
    //   palette  (2B WriteShort — signed short)
    //   frame    (pointer — ptIndexed since it is already in child list)
    //   title    (WriteString)
    //
    // Frame pointer identity: TGroup.Write writes the TFrame as one of the
    // children (ptObject). TWindow.Write then writes os.WritePointer(frame)
    // which emits ptIndexed referencing the already-registered TFrame object.
    // On Read: TGroup.Read restores children first (registering TFrame),
    // then ReadPointer for frame returns the same TFrame reference.

    public static readonly TStreamableClass StreamableClassTWindow =
        new TStreamableClass("TWindow", () => new TWindow(StreamableInit.streamableInit), 0);

    protected TWindow(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);          // TGroup.Write: TView fields + children + currentIndex
        os.WriteByte(flags);
        os.WriteTRect(zoomRect);
        os.WriteShort(number);
        os.WriteShort((ushort)palette);
        os.WritePointer(frame);  // ptIndexed — frame already written in child list
        os.WriteString(title);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);     // TGroup.Read: restores children (including TFrame)
        flags    = isStream.ReadByte();
        zoomRect = isStream.ReadTRect();
        number   = isStream.ReadShort();
        palette  = (short)isStream.ReadShort();
        frame    = isStream.ReadPointer() as TFrame;
        title    = isStream.ReadString();
        // intlTitle (RHIDE i18n) has no C# equivalent; nothing to restore.
        return this;
    }

    public new static TStreamable Build() { return new TWindow(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
