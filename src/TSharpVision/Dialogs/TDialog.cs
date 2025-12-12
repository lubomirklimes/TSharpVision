using TSharpVision.Constants;
namespace TSharpVision;

public class TDialog : TWindow
{
    public new static readonly string Name = "TDialog";

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
    public override TPalette GetPalette() => _dialogPalette;

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

    public override bool Valid(ushort command)
    {
        if (command == Views.cmCancel) return true;
        return base.Valid(command);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    public static readonly TStreamableClass StreamableClassTDialog =
        new TStreamableClass("TDialog", () => new TDialog(StreamableInit.streamableInit), 0);

    protected TDialog(StreamableInit init) : base(init) { }

    public override void Write(Opstream os) => base.Write(os);
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }

    public new static TStreamable Build() => new TDialog(StreamableInit.streamableInit);
}
