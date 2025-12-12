using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// Root group hosting application windows over a TBackground.
/// </summary>
public class TDeskTop : TGroup
{
    public TBackground background;

    // CP437 0xB0 = light shade '░'. See TSharpVisionGlyphs.BackgroundFillLight.
    public static char defaultBkgrnd  = TSharpVisionGlyphs.BackgroundFillLight;
    public static char odefaultBkgrnd = TSharpVisionGlyphs.BackgroundFillLight;

    public new static readonly string Name = "TDeskTop";

    // Tile partition priority.
    protected uint flagsOptions;
    public uint GetOptions() => flagsOptions;
    public void  SetOptions(uint aFlags) { flagsOptions = aFlags; }

    public TDeskTop(TRect bounds) : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);

        // Upstream: TScreen::setCursorPos( bounds.a.x, bounds.b.y ); skipped
        // (no shared cursor API yet — driver-dependent).

        background = InitBackground(GetExtent());
        if (background != null)
            Insert(background);
    }

    public override void ShutDown()
    {
        background = null;
        base.ShutDown();
    }

    public static TBackground InitBackgroundDefault(TRect r)
        => new TBackground(r, defaultBkgrnd);

    // Virtual factory hook.
    public virtual TBackground InitBackground(TRect r) => InitBackgroundDefault(r);

    private static bool Tileable(TView p)
        => (p.options & Views.ofTileable) != 0
        && (p.state   & Views.sfVisible)  != 0;

    public void Cascade(TRect r)
    {
        int cascadeNum = 0;
        TView lastView = null;
        ForEachView(p =>
        {
            if (Tileable(p)) { cascadeNum++; lastView = p; }
        });
        if (cascadeNum > 0)
        {
            TPoint min = default, max = default;
            lastView.SizeLimits(ref min, ref max);
            if (min.x > r.b.x - r.a.x - cascadeNum
             || min.y > r.b.y - r.a.y - cascadeNum)
            {
                TileError();
            }
            else
            {
                cascadeNum--;
                Lock();
                int n = cascadeNum;
                ForEachView(p =>
                {
                    if (Tileable(p) && n >= 0)
                    {
                        TRect nr = new TRect(r.a.x + n, r.a.y + n, r.b.x, r.b.y);
                        p.Locate(nr);
                        n--;
                    }
                });
                Unlock();
            }
        }
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (@event.What == Events.evCommand)
        {
            switch (@event.message.command)
            {
                case Views.cmNext:
                    if (Valid(Views.cmReleasedFocus))
                        SelectNext(false);
                    break;
                case Views.cmPrev:
                    if (Valid(Views.cmReleasedFocus))
                        current?.PutInFrontOf(background);
                    break;
                default:
                    return;
            }
            ClearEvent(ref @event);
        }
    }

    private static uint ISqr(uint i)
    {
        uint res1 = 2;
        uint res2 = i / res1;
        while (Math.Abs((int)res1 - (int)res2) > 1)
        {
            res1 = (res1 + res2) / 2;
            res2 = i / res1;
        }
        return res1 < res2 ? res1 : res2;
    }

    private static void MostEqualDivisors(int n, ref int x, ref int y)
    {
        int i = (int)ISqr((uint)n);
        if (n % i != 0)
            if (n % (i + 1) == 0)
                i++;
        if (i < (n / i)) i = n / i;
        x = n / i;
        y = i;
    }

    private static int DividerLoc(int lo, int hi, int num, int pos)
        => (int)((long)(hi - lo) * pos / (long)num + lo);

    private static TRect CalcTileRect(int pos, TRect r,
        int numCols, int numRows, int leftOver)
    {
        int x, y;
        TRect nRect = new TRect(0, 0, 0, 0);
        int d = (numCols - leftOver) * numRows;
        if (pos < d)
        {
            x = pos / numRows;
            y = pos % numRows;
        }
        else
        {
            x = (pos - d) / (numRows + 1) + (numCols - leftOver);
            y = (pos - d) % (numRows + 1);
        }
        nRect.a.x = DividerLoc(r.a.x, r.b.x, numCols, x);
        nRect.b.x = DividerLoc(r.a.x, r.b.x, numCols, x + 1);
        if (pos >= d)
        {
            nRect.a.y = DividerLoc(r.a.y, r.b.y, numRows + 1, y);
            nRect.b.y = DividerLoc(r.a.y, r.b.y, numRows + 1, y + 1);
        }
        else
        {
            nRect.a.y = DividerLoc(r.a.y, r.b.y, numRows, y);
            nRect.b.y = DividerLoc(r.a.y, r.b.y, numRows, y + 1);
        }
        return nRect;
    }

    public void Tile(TRect r)
    {
        int numTileable = 0;
        ForEachView(p => { if (Tileable(p)) numTileable++; });
        if (numTileable > 0)
        {
            int numCols = 0, numRows = 0;
            // Upstream trick: reverse partitioning when dsktTileVertical.
            if ((flagsOptions & Views.dsktTileVertical) != 0)
                MostEqualDivisors(numTileable, ref numRows, ref numCols);
            else
                MostEqualDivisors(numTileable, ref numCols, ref numRows);
            if ((r.b.x - r.a.x) / numCols == 0
             || (r.b.y - r.a.y) / numRows == 0)
            {
                TileError();
            }
            else
            {
                int leftOver = numTileable % numCols;
                int tileNum = numTileable - 1;
                int colsCap = numCols, rowsCap = numRows, leftCap = leftOver;
                Lock();
                int n = tileNum;
                ForEachView(p =>
                {
                    if (Tileable(p))
                    {
                        TRect tr = CalcTileRect(n, r, colsCap, rowsCap, leftCap);
                        p.Locate(tr);
                        n--;
                    }
                });
                Unlock();
            }
        }
    }

    public virtual void TileError() { }

    public virtual bool CanShowCursor() => lockFlag == 0;

    // Status-line cursor stash on empty desktop deferred until TScreen.setCursorPos exists.
    public override ushort ExecView(TView p) => base.ExecView(p);

    protected TDeskTop(StreamableInit init) : base(init) { }

    // TDeskTop has only build();
    // no own write/read. Streams as TGroup. After reading, restore the
    // `background` convenience pointer by searching the child list.

    public static readonly TStreamableClass StreamableClassTDeskTop =
        new TStreamableClass("TDeskTop", () => new TDeskTop(StreamableInit.streamableInit), 0);

    public override void Write(Opstream os)
    {
        base.Write(os);   // TGroup.Write
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TGroup.Read: restores child list
        // Restore background convenience pointer from the restored child list.
        background = null;
        ForEachView(p => { if (p is TBackground bg && background == null) background = bg; });
        return this;
    }

    public new static TStreamable Build() { return new TDeskTop(StreamableInit.streamableInit); }
    public override string StreamableName() => Name;
}
