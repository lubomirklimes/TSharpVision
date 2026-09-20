using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>A top-level popup menu that dispatches Ctrl mnemonics and menu accelerators before normal menu handling.</summary>
public class TMenuPopup : TMenuBox
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TMenuPopup";

    /// <summary>Creates a parentless popup sized and clipped by <see cref="TMenuBox"/> within the supplied bounds.</summary>
    public TMenuPopup(TRect bounds, TMenu aMenu) : base(bounds, aMenu) { }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        if (@event.What == Events.evKeyDown)
        {
            TMenuItem? item = FindItem(GetCtrlChar(@event.keyDown.keyCode));
            item ??= HotKey(@event.keyDown.keyCode);
            if (item != null && CommandEnabled(item.Command))
            {
                @event.What = Events.evCommand;
                @event.message.command = item.Command;
                @event.message.infoPtr = null;
                PutEvent(ref @event);
                ClearEvent(ref @event);
            }
            else if (GetAltChar(@event.keyDown.keyCode, @event.keyDown.charScan.charCode,
                         @event.keyDown.controlKeyState) != '\0')
            {
                ClearEvent(ref @event);
            }
        }
        base.HandleEvent(ref @event);
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTMenuPopup =
        new TStreamableClass(Name, () => new TMenuPopup(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TMenuPopup(StreamableInit init) : base(init) { }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TMenuPopup(StreamableInit.streamableInit);

    /// <inheritdoc />
    public override string StreamableName() => Name;

    private static char GetCtrlChar(ushort keyCode)
    {
        byte code = (byte)keyCode;
        return code is >= 1 and <= 26 ? (char)('A' + code - 1) : '\0';
    }
}
