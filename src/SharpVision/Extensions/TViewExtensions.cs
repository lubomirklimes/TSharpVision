using SharpVision.Constants;

namespace SharpVision;

public static class TViewExtensions
{
    public static IInfo Message(this TView receiver, ushort what, ushort command, IInfo info)
    {
        if (receiver == null)
            return null;

        TEvent @event = new TEvent();
        @event.What = what;
        @event.message.command = command;
        @event.message.infoPtr = info;
        receiver.HandleEvent(ref @event);
        if (@event.What == Events.evNothing )
            return @event.message.infoPtr;
        else
            return null;
    }
}
