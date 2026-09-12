using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Displays metadata for the focused file entry and updates on file-focus broadcasts.</summary>
public class TFileInfoPane : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TFileInfoPane";

    /// <summary>File metadata currently displayed by the pane.</summary>
    public TSearchRec FileBlock = new TSearchRec();

    /// <summary>Month abbreviations indexed from one through twelve; index zero is empty.</summary>
    protected static readonly string[] Months =
    {
        "",
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };
    /// <summary>Suffix used for times before noon.</summary>
    protected static string AmText = "am";
    /// <summary>Suffix used for times after noon.</summary>
    protected static string PmText = "pm";

    /// <summary>Creates a metadata pane at owner-relative cell bounds and enables file-focus broadcasts.</summary>
    public TFileInfoPane(TRect bounds) : base(bounds)
    {
        eventMask |= Events.evBroadcast;
    }

    private static readonly TPalette _palette = new TPalette("\x1E", 1);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TFileInfoPane(StreamableInit init) : base(init) { }
    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    /// <inheritdoc />
    public override void Write(Opstream os) { base.Write(os); }
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TFileInfoPane(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFileInfoPane =
        new TStreamableClass("TFileInfoPane", () => new TFileInfoPane(StreamableInit.streamableInit), 0);
}
