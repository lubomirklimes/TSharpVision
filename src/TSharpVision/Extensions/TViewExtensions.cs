using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Convenience methods for synchronous event delivery to views.</summary>
public static class TViewExtensions
{
    /// <summary>Synchronously dispatches a message and returns its resulting object payload if consumed; returns null for an absent receiver or unconsumed event.</summary>
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
