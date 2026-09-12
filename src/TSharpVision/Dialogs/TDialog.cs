using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>A movable, closable window that handles standard dialog acceptance, cancellation, and default-button commands.</summary>
public class TDialog : TWindow
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TDialog";

    /// <summary>Creates an unnumbered dialog at owner-relative cell bounds with movement and closure enabled but resizing disabled.</summary>
    public TDialog(TRect bounds, string aTitle)
        : base(bounds, aTitle, Views.wnNoNumber)
    {
        growMode = 0;
        flags = (byte)(Views.wfMove | Views.wfClose);
    }

    private static readonly TPalette _dialogPalette = new TPalette(
        "\x20\x21\x22\x23\x24\x25\x26\x27\x28\x29\x2A\x2B\x2C\x2D\x2E\x2F" +
        "\x30\x31\x32\x33\x34\x35\x36\x37\x38\x39\x3A\x3B\x3C\x3D\x3E\x3F",
        32);
    /// <inheritdoc />
    public override TPalette GetPalette() => _dialogPalette;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        switch (@event.What)
        {
            case Events.evKeyDown:
                switch (@event.keyDown.keyCode)
                {
                    case Keys.kbEsc:
                        @event.What = Events.evCommand;
                        @event.message.command = Views.cmCancel;
                        @event.message.infoPtr = null;
                        PutEvent(ref @event);
                        ClearEvent(ref @event);
                        break;
                    case Keys.kbEnter:
                        @event.What = Events.evBroadcast;
                        @event.message.command = Views.cmDefault;
                        @event.message.infoPtr = null;
                        PutEvent(ref @event);
                        ClearEvent(ref @event);
                        break;
                }
                break;

            case Events.evCommand:
                switch (@event.message.command)
                {
                    case Views.cmOK:
                    case Views.cmCancel:
                    case Views.cmYes:
                    case Views.cmNo:
                        if ((state & Views.sfModal) != 0)
                        {
                            EndModal(@event.message.command);
                            ClearEvent(ref @event);
                        }
                        break;
                }
                break;
        }
    }

    /// <inheritdoc />
    public override bool Valid(ushort command)
    {
        if (command == Views.cmCancel) return true;
        return base.Valid(command);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTDialog =
        new TStreamableClass("TDialog", () => new TDialog(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TDialog(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os) => base.Write(os);
    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TDialog(StreamableInit.streamableInit);
}
