using System.IO;
using TSharpVision.Constants;
namespace TSharpVision;

// TInputLine subclass that keeps itself in sync with the focused entry
// in the matching TFileList. Stream Read/Write/Build deferred.
/// <summary>Filename input that follows file-list focus while the input itself is not selected.</summary>
public class TFileInputLine : TInputLine
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TFileInputLine";

    /// <summary>Creates filename input at owner-relative cell bounds; the supplied capacity reserves one character for the terminator convention.</summary>
    public TFileInputLine(TRect bounds, int aMaxLen) : base(bounds, aMaxLen)
    {
        eventMask |= Events.evBroadcast;
    }

    // Refresh the buffer from cmFileFocused unless we're the currently selected
    // control. Directories show as "<name>/<wildCard>"; plain files just show their name.
    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmFileFocused
            && (state & Views.sfSelected) == 0
            && @event.message.infoPtr is TSearchRec rec)
        {
            string text;
            if ((rec.attr & FileAttr.faDirec) != 0)
            {
                string wild = (owner is IFileDialogContext ctx)
                    ? (ctx.WildCard ?? string.Empty)
                    : string.Empty;
                text = (rec.name ?? string.Empty)
                     + Path.DirectorySeparatorChar
                     + wild;
            }
            else
            {
                text = rec.name ?? string.Empty;
            }
            if (text.Length > MaxLen) text = text.Substring(0, MaxLen);
            Data = text;
            CurPos = Data.Length;
            FirstPos = 0;
            SelStart = 0;
            SelEnd = 0;
            DrawView();
        }
    }

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TFileInputLine(StreamableInit init) : base(init) { }
    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    /// <inheritdoc />
    public override void Write(Opstream os) { base.Write(os); }
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TFileInputLine(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFileInputLine =
        new TStreamableClass("TFileInputLine", () => new TFileInputLine(StreamableInit.streamableInit), 0);
}
