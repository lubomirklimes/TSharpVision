using TSharpVision.Constants;
namespace TSharpVision;

// Three-line metadata pane shown to the right of TFileList in the
// TFileDialog.
public class TFileInfoPane : TView
{
    public new static readonly string Name = "TFileInfoPane";

    public TSearchRec FileBlock = new TSearchRec();

    protected static readonly string[] Months =
    {
        "",
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };
    protected static string AmText = "am";
    protected static string PmText = "pm";

    public TFileInfoPane(TRect bounds) : base(bounds)
    {
        eventMask |= Events.evBroadcast;
    }

    private static readonly TPalette _palette = new TPalette("\x1E", 1);
    public override TPalette GetPalette() => _palette;

    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort color = GetColor(0x01);

        // Row 0: full path (directory + wildCard).
        string path = string.Empty;
        if (owner is IFileDialogContext ctx)
            path = (ctx.Directory ?? string.Empty) + (ctx.WildCard ?? string.Empty);
        b.moveChar(0, ' ', color, size.x);
        b.moveStr(1, path, color);
        WriteLine(0, 0, size.x, 1, b);

        // Row 1: focused file name.
        b.moveChar(0, ' ', color, size.x);
        b.moveStr(1, FileBlock.name ?? string.Empty, color);
        WriteLine(0, 1, size.x, 1, b);

        // Row 2: size / month / day, year / hour:min am-pm.
        b.moveChar(0, ' ', color, size.x);
        if (!string.IsNullOrEmpty(FileBlock.name))
        {
            b.moveStr(14, FileBlock.size.ToString(), color);

            // Upstream uses time_t → struct tm via localtime(). We treat
            // FileBlock.time as Unix seconds; zero/negative leaves the
            // date columns blank to match upstream's null-time guard.
            if (FileBlock.time > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(FileBlock.time).LocalDateTime;
                b.moveStr(25, Months[dt.Month], color);
                b.moveStr(29, dt.Day.ToString("D2"), color);
                b.putChar(31, ',');
                b.moveStr(32, dt.Year.ToString(), color);

                bool pm = dt.Hour >= 12;
                int hour12 = dt.Hour % 12;
                if (hour12 == 0) hour12 = 12;
                b.moveStr(38, hour12.ToString("D2"), color);
                b.putChar(40, ':');
                b.moveStr(41, dt.Minute.ToString("D2"), color);
                b.moveStr(43, pm ? PmText : AmText, color);
            }
        }
        WriteLine(0, 2, size.x, 1, b);

        // Remaining rows: blank fill.
        b.moveChar(0, ' ', color, size.x);
        WriteLine(0, 3, size.x, size.y - 3, b);
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmFileFocused
            && @event.message.infoPtr is TSearchRec rec)
        {
            FileBlock = rec;
            DrawView();
        }
    }

    protected TFileInfoPane(StreamableInit init) : base(init) { }
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    public override void Write(Opstream os) { base.Write(os); }
    public new static TStreamable Build() => new TFileInfoPane(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTFileInfoPane =
        new TStreamableClass("TFileInfoPane", () => new TFileInfoPane(StreamableInit.streamableInit), 0);
}
