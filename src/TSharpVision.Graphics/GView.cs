using System.Runtime.InteropServices;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Graphics;

/// <summary>
/// A <see cref="TView"/> with a graphics display list; the <c>gView</c> of the original TVGraph.
/// <para><b>Drawing.</b> On a driver implementing <see cref="IPixelGraphicsDriver"/> the view fills its cells with
/// <see cref="BackgroundAttr"/> and submits its display list as the driver layer it owns, clipped to the part of the
/// view that is on screen (owner clipping, overlapping views and their shadows removed). On any other driver it shows
/// <see cref="UnavailableText"/> instead; no pixels are simulated in text cells.</para>
/// <para><b>Synchronization.</b> A window, dialog or menu opened, moved, hidden or closed over the view does not
/// redraw it. While at least one graphics view is exposed, the views keep one synchronizer registered with
/// <see cref="PixelGraphicsSynchronization"/>; every event loop (application, modal <see cref="TGroup.ExecView"/>
/// loops, menus) runs it right before the driver presents a frame, so the presented clip always matches the view
/// tree. Applications need not call anything. Synchronization is scoped to the presenting program: views of another
/// program (coexisting <see cref="TProgram"/>s in one process) are not refreshed, do not submit, pause their timer and
/// withdraw any layer they hold in the presenting driver, until their own program presents again.</para>
/// <para><b>Animation.</b> While the view owns a layer a timer posts one coalesced tick to the
/// view at <see cref="TickHz"/>. The post is delivered on the UI thread, also while a menu or modal dialog runs its
/// own loop; the view then calls <see cref="GObject.GraphProd"/> on every object and resubmits its layer. The timer
/// pauses while nothing of the view is visible (fully covered, zero size, hidden, unexposed, text driver) and resumes
/// when the synchronizer finds it visible again. All display-list access is UI-thread only.</para>
/// <para><b>Lifetime.</b> Hiding, unexposing, <see cref="ShutDown"/> (window close) and disposal remove the layer;
/// shutdown and disposal also release the timer, so no graphics or ticks outlive the view.</para>
/// </summary>
public class GView : TView
{
    // The animation tick command, posted to this view only (value kept from STYX). Internal: ticks are recognised by
    // their private info object, so the number reserves nothing in the application's command space.
    internal const ushort CmGraphProd = 192;

    /// <summary>Animation ticks per second.</summary>
    public const int TickHz = 60;

    private static readonly TimeSpan s_period = TimeSpan.FromMilliseconds(1000.0 / TickHz);

    // Exposed, visible, not shut-down views; the synchronizer revisits them. UI thread only. The synchronizer is
    // registered exactly while this list is not empty.
    private static readonly List<GView> s_exposed = new();
    private static readonly Action<TProgram> s_synchronizer = SynchronizeProgram;

    // The program of the last synchronization pass: the program whose loop presents. Weak, so a finished program is
    // not kept alive. Unknown (no target) until the first present; then views of other programs do not submit.
    private static readonly WeakReference<TProgram?> s_presenting = new(null);

    private readonly GraphicsCanvas _canvas = new();
    private readonly List<TRect> _cells = new();
    private readonly List<TRect> _scratch = new();
    private readonly List<PixelRect> _clip = new();
    private readonly List<PixelRect> _submittedClip = new();

    private IPixelGraphicsDriver? _layerDriver;
    private int _submittedX = int.MinValue, _submittedY, _submittedW, _submittedH, _submittedCellW, _submittedCellH;

    // The timer is created on first use and paused (not released) while the view has no layer. _timerLock makes a
    // pause or release final: once it returns, the timer thread posts nothing more.
    private readonly object _timerLock = new();
    private Timer? _timer;
    private bool _timerRunning;
    private int _tickPending;
    private bool _exposedMember;
    private bool _shutDown;

    /// <summary>
    /// Objects drawn by the view, in drawing order. UI thread only. <see cref="Insert(GObject)"/> and
    /// <see cref="Delete(GObject)"/> redraw; after changing the list or an object directly, call
    /// <see cref="TView.DrawView"/>.
    /// </summary>
    public List<GObject> DisplayList { get; } = new();

    /// <summary>Cell attribute of the area behind the graphics. Default: light gray on black.</summary>
    public TColorAttr BackgroundAttr { get; set; } = (byte)0x07;

    /// <summary>
    /// Text shown, centred, when the active driver has no pixel graphics. Until the application sets it, this is the
    /// framework string <c>Graphics_Unavailable</c> looked up through <see cref="TSharpVisionIntl"/> on every read
    /// (English: "Pixel graphics need an SDL display driver."), so a language switch shows on the next draw. A value
    /// the application sets wins and is never translated; an empty string shows no text; null restores the
    /// framework string.
    /// </summary>
    public string UnavailableText
    {
        get => _unavailableText ?? TSharpVisionIntl.Get(UnavailableTextKey, UnavailableTextEnglish);
        set => _unavailableText = value;
    }

    // The built-in fallback text: key in the core localization inventory, English used when the provider lacks it.
    internal const string UnavailableTextKey = "Graphics_Unavailable";
    internal const string UnavailableTextEnglish = "Pixel graphics need an SDL display driver.";
    private string? _unavailableText;

    // Diagnostics and tests: the primitives and clip region (logical pixels) of the last submission.
    internal ReadOnlySpan<GraphicsPrimitive> SubmittedPrimitives => _layerDriver != null ? _canvas.Primitives : default;
    internal ReadOnlySpan<PixelRect> SubmittedClip => CollectionsMarshal.AsSpan(_submittedClip);

    /// <summary>
    /// True while the view owns a driver layer: its pixels are on screen. False on drivers without
    /// <see cref="IPixelGraphicsDriver"/> (the view shows <see cref="UnavailableText"/>), while nothing of it is visible,
    /// and while another program presents.
    /// </summary>
    public bool HasGraphicsLayer => _layerDriver != null;

    // Diagnostics and tests: the animation timer runs (the view owns a layer and is not shut down).
    internal bool IsAnimating => _timerRunning;

    // Diagnostics and tests: the shared synchronizer and the number of views it revisits.
    internal static Action<TProgram> Synchronizer => s_synchronizer;
    internal static int ExposedCount => s_exposed.Count;

    /// <summary>Creates a selectable graphics view.</summary>
    public GView(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
    }

    /// <summary>Adds an object to the display list and redraws.</summary>
    public void Insert(GObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        DisplayList.Add(obj);
        DrawView();
    }

    /// <summary>Removes an object from the display list and redraws.</summary>
    public void Delete(GObject obj)
    {
        DisplayList.Remove(obj);
        DrawView();
    }

    /// <inheritdoc />
    public override void Draw()
    {
        var graphics = TDisplay.driver as IPixelGraphicsDriver;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort attr = (ushort)BackgroundAttr;
        b.moveChar(0, ' ', attr, size.x);
        WriteLine(0, 0, size.x, size.y, b);

        if (graphics == null)
        {
            DrawUnavailableText(b, attr);
            RemoveLayer();
            return;
        }
        Submit(graphics, redraw: true);
    }

    private void DrawUnavailableText(TDrawBuffer b, ushort attr)
    {
        string text = UnavailableText;
        if (size.y <= 0 || string.IsNullOrEmpty(text)) return;
        if (text.Length > size.x) text = text[..Math.Max(0, size.x)];
        b.moveChar(0, ' ', attr, size.x);
        b.moveStr(Math.Max(0, (size.x - text.Length) / 2), text, attr);
        WriteLine(0, size.y / 2, size.x, 1, b);
    }

    /// <summary>
    /// Recomputes the on-screen region and resubmits the layer when the region or the view's screen position
    /// changed; removes the layer when nothing is visible. Does not call the driver when nothing changed.
    /// </summary>
    public void Refresh()
    {
        if (TDisplay.driver is IPixelGraphicsDriver graphics) Submit(graphics, redraw: false);
        else RemoveLayer();
    }

    /// <summary>
    /// One synchronization pass for the program that presented last (or for every exposed view before any program
    /// presented). Rarely needed, because the views already run this pass before every presented frame (see
    /// <see cref="PixelGraphicsSynchronization"/>); kept for hosts that change the view tree and present without
    /// <see cref="TProgram.GetEvent"/>. UI thread only.
    /// </summary>
    public static void RefreshVisibleRegions()
    {
        if (s_presenting.TryGetTarget(out TProgram? program) && program != null) SynchronizeProgram(program);
        else for (int i = 0; i < s_exposed.Count; i++) s_exposed[i].Refresh();
    }

    // The registered synchronizer. Refresh never changes membership (only SetState, ShutDown and Dispose do), so
    // the list is walked in place, without a copy or an allocation.
    private static void SynchronizeProgram(TProgram program)
    {
        s_presenting.SetTarget(program);
        List<GView> views = s_exposed;
        IDriver? presentingDriver = TDisplay.driver;
        for (int i = 0; i < views.Count; i++)
        {
            GView view = views[i];
            if (ReferenceEquals(view.Root(), program)) view.Refresh();
            else if (view._layerDriver != null && ReferenceEquals(view._layerDriver, presentingDriver)) view.RemoveLayer();
            else view.SetTimerRunning(false);
        }
    }

    // The top of the owner chain (an application when the view is on screen).
    private TView Root()
    {
        TView top = this;
        while (top.owner != null) top = top.owner;
        return top;
    }

    // Only views of the presenting program submit. Before any program presented, after the last graphics view left,
    // or once the presenting program shut down (its desktop is gone), every program may.
    private bool BelongsToPresentingProgram() =>
        !s_presenting.TryGetTarget(out TProgram? program) || program == null || program.DeskTop == null
        || ReferenceEquals(Root(), program);

    // Draws the display list (when asked, or when the pixel geometry changed) and hands the visible part to the
    // driver. Skips the driver call when neither the primitives nor the clip region changed.
    private void Submit(IPixelGraphicsDriver graphics, bool redraw)
    {
        if (_shutDown) { RemoveLayer(); return; }
        if (!BelongsToPresentingProgram()) { SetTimerRunning(false); return; }
        VisibleRegion.Compute(this, _cells, _scratch);
        if (_cells.Count == 0) { RemoveLayer(); return; }

        int cw = graphics.CellPixelWidth, ch = graphics.CellPixelHeight;
        int px = 0, py = 0;
        for (TGroup? g = owner; g != null; g = g.owner) { px += g.origin.x; py += g.origin.y; }
        px = (px + origin.x) * cw;
        py = (py + origin.y) * ch;
        int pw = size.x * cw, ph = size.y * ch;

        bool geometryChanged = !ReferenceEquals(graphics, _layerDriver)
            || px != _submittedX || py != _submittedY || pw != _submittedW || ph != _submittedH
            || cw != _submittedCellW || ch != _submittedCellH;
        if (redraw || geometryChanged)
        {
            _canvas.Clear();
            for (int i = 0; i < DisplayList.Count; i++)
                DisplayList[i].GraphDraw(_canvas, px, py, pw, ph);
        }

        _clip.Clear();
        foreach (TRect r in _cells)
            _clip.Add(new PixelRect(r.a.x * cw, r.a.y * ch, (r.b.x - r.a.x) * cw, (r.b.y - r.a.y) * ch));
        if (!redraw && !geometryChanged
            && CollectionsMarshal.AsSpan(_clip).SequenceEqual(CollectionsMarshal.AsSpan(_submittedClip)))
            return;

        if (_layerDriver != null && !ReferenceEquals(_layerDriver, graphics)) RemoveLayer();
        graphics.SetGraphicsLayer(this, CollectionsMarshal.AsSpan(_clip), _canvas.Primitives, _canvas.Images);
        _layerDriver = graphics;
        _submittedClip.Clear();
        _submittedClip.AddRange(_clip);
        (_submittedX, _submittedY, _submittedW, _submittedH, _submittedCellW, _submittedCellH) = (px, py, pw, ph, cw, ch);
        SetTimerRunning(true);
    }

    private void RemoveLayer()
    {
        SetTimerRunning(false);
        if (_layerDriver == null) return;
        _layerDriver.RemoveGraphicsLayer(this);
        _layerDriver = null;
        _submittedClip.Clear();
        _submittedX = int.MinValue;
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evCommand && @event.message.command == CmGraphProd
            && ReferenceEquals(@event.message.infoPtr, TickInfo.Instance))
        {
            Volatile.Write(ref _tickPending, 0);
            Tick();
            ClearEvent(ref @event);
        }
    }

    /// <summary>
    /// One animation step: <see cref="GObject.GraphProd"/> on every object, then a resubmission. The view's own timer
    /// calls it at <see cref="TickHz"/>; applications may call it to step an animation manually (UI thread).
    /// </summary>
    public void Tick()
    {
        for (int i = 0; i < DisplayList.Count; i++) DisplayList[i].GraphProd();
        if (TDisplay.driver is IPixelGraphicsDriver graphics) Submit(graphics, redraw: true);
        else RemoveLayer();
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfVisible | Views.sfExposed)) == 0) return;
        if (!enable) RemoveLayer();
        UpdateMembership();
    }

    // Joins or leaves the synchronized set; the shared synchronizer is registered while the set is not empty.
    private void UpdateMembership()
    {
        const ushort shown = Views.sfExposed | Views.sfVisible;
        bool member = !_shutDown && (state & shown) == shown;
        if (member == _exposedMember) return;
        _exposedMember = member;
        if (member)
        {
            // A post dropped while the view was out of the tree must not block the ticks.
            Volatile.Write(ref _tickPending, 0);
            s_exposed.Add(this);
            if (s_exposed.Count == 1) PixelGraphicsSynchronization.Register(s_synchronizer);
        }
        else
        {
            s_exposed.Remove(this);
            if (s_exposed.Count == 0)
            {
                PixelGraphicsSynchronization.Unregister(s_synchronizer);
                s_presenting.SetTarget(null);
            }
        }
    }

    // Starts or pauses the tick timer; a no-op in the state it is already in, so steady frames do no timer work.
    private void SetTimerRunning(bool run)
    {
        if (_shutDown) run = false;
        if (run == _timerRunning) return;
        lock (_timerLock)
        {
            _timerRunning = run;
            if (run)
            {
                _timer ??= new Timer(static s => ((GView)s!).OnTimer(), this, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(s_period, s_period);
            }
            else
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    // Timer thread: only posts, at most one tick in flight, and never after a pause or release has returned.
    private void OnTimer()
    {
        lock (_timerLock)
        {
            if (!_timerRunning) return;
            if (Interlocked.Exchange(ref _tickPending, 1) == 0)
                Post(CmGraphProd, TickInfo.Instance);
        }
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        StopAndRemove();
        base.ShutDown();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) StopAndRemove();
        base.Dispose(disposing);
    }

    private void StopAndRemove()
    {
        _shutDown = true;
        UpdateMembership();
        RemoveLayer();
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    // Marks posts that come from this view's own timer.
    private sealed class TickInfo : IInfo
    {
        public static readonly TickInfo Instance = new();
    }
}
