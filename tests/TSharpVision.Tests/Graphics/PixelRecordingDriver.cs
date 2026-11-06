using TSharpVision.Drivers;
using TSharpVision.Samples.TVDemo;

namespace TSharpVision.Tests.Graphics;

/// <summary>
/// A NullDriver decorator that adds the pixel capability and records the layers it receives, plus a snapshot of every
/// layer's clip taken at each <see cref="IDriver.PumpMessages"/> call: the frame a real driver would present.
/// </summary>
internal sealed class PixelRecordingDriver : IDriver, IPixelGraphicsDriver
{
    internal const int CellW = 8, CellH = 16;

    private readonly IDriver _inner;
    private readonly Dictionary<object, (PixelRect[] Clip, GraphicsPrimitive[] Prims)> _layers = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, PixelRect[]> _presented = new(ReferenceEqualityComparer.Instance);

    public PixelRecordingDriver(IDriver inner) => _inner = inner;

    public int Submissions { get; private set; }
    public int Removals { get; private set; }
    public int Presents { get; private set; }
    public int LayerCount => _layers.Count;
    public bool HasLayer(object owner) => _layers.ContainsKey(owner);
    public PixelRect[] Clip(object owner) => _layers[owner].Clip;
    public GraphicsPrimitive[] Primitives(object owner) => _layers[owner].Prims;

    /// <summary>Layers held at the last present.</summary>
    public int PresentedLayerCount => _presented.Count;

    /// <summary>True when the owner had a layer at the last present.</summary>
    public bool PresentedHasLayer(object owner) => _presented.ContainsKey(owner);

    /// <summary>The owner's clip at the last present; fails the test when it had no layer then.</summary>
    public PixelRect[] PresentedClip(object owner) =>
        _presented.TryGetValue(owner, out PixelRect[]? clip) ? clip : throw new Xunit.Sdk.XunitException($"{owner} had no layer at the last present");

    public int CellPixelWidth => CellW;
    public int CellPixelHeight => CellH;

    public void SetGraphicsLayer(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
        ReadOnlySpan<PixelImage> images = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Submissions++;
        _layers[owner] = (clip.ToArray(), primitives.ToArray());
    }

    public void RemoveGraphicsLayer(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_layers.Remove(owner)) Removals++;
    }

    // The present point of SDL drivers: what is held now is what the user sees.
    public void PumpMessages()
    {
        Presents++;
        var presented = new Dictionary<object, PixelRect[]>(ReferenceEqualityComparer.Instance);
        foreach (var (owner, layer) in _layers) presented[owner] = layer.Clip;
        _presented = presented;
        _inner.PumpMessages();
    }

    public void Initialize() => _inner.Initialize();
    public void Suspend() => _inner.Suspend();
    public void Resume() => _inner.Resume();
    public void Shutdown() => _inner.Shutdown();
    public ushort GetCols() => _inner.GetCols();
    public ushort GetRows() => _inner.GetRows();
    public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
    public void SetScreenMode(TDisplay.SM mode) => _inner.SetScreenMode(mode);
    public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
    public void ClearScreen(ushort cols, ushort rows) => _inner.ClearScreen(cols, rows);
    public ushort GetCursorType() => _inner.GetCursorType();
    public void SetCursorType(ushort cursorType) => _inner.SetCursorType(cursorType);
    public void SetCaretPosition(int x, int y) => _inner.SetCaretPosition(x, y);
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) => _inner.WriteBuf(x, y, w, h, buf);
    public void MakeBeep() => _inner.MakeBeep();
    public bool ReadKeyEvent(out TEvent ev) => _inner.ReadKeyEvent(out ev);
    public bool SupportsMouse => _inner.SupportsMouse;
    public bool SupportsTrueColor => _inner.SupportsTrueColor;
    public bool SupportsGraphics => true;
    public KeyboardCapabilities KeyboardCapabilities => _inner.KeyboardCapabilities;
}

/// <summary>Pixel geometry helpers shared by the graphics view tests.</summary>
internal static class PixelGeometry
{
    /// <summary>A view's on-screen cells in logical pixels.</summary>
    public static PixelRect Of(TView view)
    {
        TPoint g = view.MakeGlobal(new TPoint(0, 0));
        return new PixelRect(g.x * PixelRecordingDriver.CellW, g.y * PixelRecordingDriver.CellH,
            view.size.x * PixelRecordingDriver.CellW, view.size.y * PixelRecordingDriver.CellH);
    }

    /// <summary>The view's shadow rectangle (its bounds shifted by the shadow size).</summary>
    public static PixelRect ShadowOf(TView view)
    {
        PixelRect r = Of(view);
        TPoint s = TView.shadowSize;
        return r with { X = r.X + s.x * PixelRecordingDriver.CellW, Y = r.Y + s.y * PixelRecordingDriver.CellH };
    }

    public static long Area(ReadOnlySpan<PixelRect> rects)
    {
        long area = 0;
        foreach (PixelRect r in rects) area += (long)r.W * r.H;
        return area;
    }

    public static bool Intersects(PixelRect a, PixelRect b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    /// <summary>True when no rectangle of the clip touches the view or its shadow.</summary>
    public static bool Excludes(ReadOnlySpan<PixelRect> clip, TView cover)
    {
        PixelRect body = Of(cover), shadow = ShadowOf(cover);
        bool shadowed = (cover.state & Constants.Views.sfShadow) != 0;
        foreach (PixelRect r in clip)
            if (Intersects(r, body) || (shadowed && Intersects(r, shadow))) return false;
        return true;
    }

    /// <summary>One pass of the application loop: synchronize, present, deliver at most one post, Idle.</summary>
    public static void Present(TVDemoApp app)
    {
        TEvent ev = default;
        app.GetEvent(ref ev);
    }
}
