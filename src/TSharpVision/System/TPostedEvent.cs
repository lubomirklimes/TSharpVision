namespace TSharpVision;

/// <summary>
/// A command posted to one specific view, waiting to be delivered on the UI thread.
///
/// Posted events do not travel through the ordinary <see cref="TEvent"/> queue: an event
/// taken from that queue is handed to whichever group currently owns the event loop, so a
/// modal view executing through <see cref="TGroup.ExecView"/> would swallow anything meant
/// for a view underneath it. A posted event names its target instead and is delivered
/// straight to it — see <see cref="TEventQueue.DeliverPostedEvent"/>.
/// </summary>
internal readonly struct TPostedEvent
{
    public TPostedEvent(TView target, ushort command, IInfo? info)
    {
        Target = target;
        Command = command;
        Info = info;
    }

    /// <summary>The view the command is for.</summary>
    public TView Target { get; }

    /// <summary>Command code, delivered as <c>message.command</c> of an <c>evCommand</c>.</summary>
    public ushort Command { get; }

    /// <summary>Optional payload, delivered as <c>message.infoPtr</c>.</summary>
    public IInfo? Info { get; }
}
