using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// Root group hosting application windows over a TBackground.
/// </summary>
public class TDeskTop : TGroup
{
    /// <summary>Background child inserted beneath desktop windows, or null when no background was created.</summary>
    public TBackground? background;

    // CP437 0xB0 = light shade '░'. See TSharpVisionGlyphs.BackgroundFillLight.
    /// <summary>Fill character used by the default background factory for newly created desktops.</summary>
    public static char defaultBkgrnd  = TSharpVisionGlyphs.BackgroundFillLight;
    /// <summary>Compatibility storage for the original default background glyph; the current desktop factory uses defaultBkgrnd.</summary>
    public static char odefaultBkgrnd = TSharpVisionGlyphs.BackgroundFillLight;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TDeskTop";

    /// <summary>Desktop layout option bits, including dsktTileVertical for tile orientation.</summary>
    protected uint flagsOptions;
    /// <summary>Returns the current desktop layout option bits.</summary>
    public uint GetOptions() => flagsOptions;
    /// <summary>Replaces desktop layout option bits without immediately rearranging windows.</summary>
    public void  SetOptions(uint aFlags) { flagsOptions = aFlags; }

    /// <summary>Creates a desktop group in owner-relative cell bounds and inserts the background returned by its factory, if any.</summary>
    public TDeskTop(TRect bounds) : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);

        // Upstream: TScreen::setCursorPos( bounds.a.x, bounds.b.y ); skipped
        // (no shared cursor API yet — driver-dependent).

        background = InitBackground(GetExtent());
        if (background != null)
            Insert(background);
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        background = null;
        base.ShutDown();
    }

    /// <summary>Creates a background using the default fill character and desktop-local cell bounds.</summary>
    public static TBackground InitBackgroundDefault(TRect r)
        => new TBackground(r, defaultBkgrnd);

    /// <summary>Creates the desktop background in local cell bounds; override to customize it or return null to omit it.</summary>
    public virtual TBackground? InitBackground(TRect r) => InitBackgroundDefault(r);

    private static bool Tileable(TView p)
        => (p.options & Views.ofTileable) != 0
        && (p.state   & Views.sfVisible)  != 0;

    /// <summary>Cascades visible tileable child views within the desktop-local cell rectangle; calls TileError when the layout cannot fit.</summary>
    public void Cascade(TRect r)
    {
        int cascadeNum = 0;
        TView? lastView = null;
        ForEachView(p =>
        {
            if (Tileable(p)) { cascadeNum++; lastView = p; }
        });
        if (cascadeNum > 0 && lastView != null)
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

    /// <inheritdoc />
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

    /// <summary>Tiles visible tileable child views within the desktop-local cell rectangle using the configured orientation; calls TileError when the layout cannot fit.</summary>
    public void Tile(TRect r)
    {
        int numTileable = 0;
        ForEachView(p => { if (Tileable(p)) numTileable++; });
        if (numTileable > 0)
        {
            // Choose axes after computing the unchanged, orientation-neutral partition.
            int shortAxis = 0, longAxis = 0;
            MostEqualDivisors(numTileable, ref shortAxis, ref longAxis);
            bool vertical = (flagsOptions & Views.dsktTileVertical) != 0;
            int numCols = vertical ? longAxis : shortAxis;
            int numRows = vertical ? shortAxis : longAxis;
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

    /// <summary>Notification hook for a failed tile or cascade layout; the base implementation does nothing.</summary>
    public virtual void TileError() { }

    /// <summary>Returns whether the desktop is unlocked and therefore permits cursor display.</summary>
    public virtual bool CanShowCursor() => lockFlag == 0;

    // Status-line cursor stash on empty desktop deferred until TScreen.setCursorPos exists.
    /// <inheritdoc />
    public override ushort ExecView(TView p) => base.ExecView(p);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TDeskTop(StreamableInit init) : base(init) { }

    // TDeskTop has only build();
    // no own write/read. Streams as TGroup. After reading, restore the
    // `background` convenience pointer by searching the child list.

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTDeskTop =
        new TStreamableClass("TDeskTop", () => new TDeskTop(StreamableInit.streamableInit), 0);

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);   // TGroup.Write
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TGroup.Read: restores child list
        // Restore background convenience pointer from the restored child list.
        background = null;
        ForEachView(p => { if (p is TBackground bg && background == null) background = bg; });
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TDeskTop(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() => Name;
}
