using System.IO;
using SharpVision.Constants;
namespace SharpVision;

// TInputLine subclass that keeps itself in sync with the focused entry
// in the matching TFileList. Stream Read/Write/Build deferred.
public class TFileInputLine : TInputLine
{
    public new static readonly string Name = "TFileInputLine";

    public TFileInputLine(TRect bounds, int aMaxLen) : base(bounds, aMaxLen)
    {
        eventMask |= Events.evBroadcast;
    }

    // Refresh the buffer from cmFileFocused unless we're the currently selected
    // control. Directories show as "<name>/<wildCard>"; plain files just show their name.
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

    protected TFileInputLine(StreamableInit init) : base(init) { }
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    public override void Write(Opstream os) { base.Write(os); }
    public new static TStreamable Build() => new TFileInputLine(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTFileInputLine =
        new TStreamableClass("TFileInputLine", () => new TFileInputLine(StreamableInit.streamableInit), 0);
}
