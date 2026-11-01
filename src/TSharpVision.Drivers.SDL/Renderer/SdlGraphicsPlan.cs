using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Renderer;

/// <summary>Kind of an <see cref="SdlGraphicsCommand"/>.</summary>
internal enum SdlGraphicsCommandKind
{
    /// <summary>One <c>SDL_RenderFillRects</c> call: a primitive's device rectangles already clipped to one clip rectangle.</summary>
    Fill,
    /// <summary>One <c>SDL_RenderTexture</c> call under <c>SDL_SetRenderClipRect</c>.</summary>
    Image,
}

/// <summary>
/// One SDL_Renderer call of the pixel-graphics pass. Fill: <see cref="Rgba"/> and the range
/// <c>[FirstRect, FirstRect + RectCount)</c> of <see cref="SdlGraphicsPlan.Rects"/>. Image: <see cref="Image"/>
/// drawn into <see cref="Destination"/> (device pixels) from <see cref="Source"/> (texels; the whole texture when
/// <see cref="WholeSource"/>) with <see cref="Filter"/> and opacity (alpha byte of <see cref="Rgba"/>), under the
/// clip <see cref="Clip"/>; <see cref="Visible"/> is the part of the destination that the clip leaves.
/// </summary>
internal readonly record struct SdlGraphicsCommand(
    SdlGraphicsCommandKind Kind,
    uint Rgba,
    int FirstRect = 0,
    int RectCount = 0,
    PixelImage? Image = null,
    GraphicsImageFilter Filter = GraphicsImageFilter.Nearest,
    DeviceRect Clip = default,
    DeviceRect Visible = default,
    SDL3.SDL.FRect Destination = default,
    SDL3.SDL.FRect Source = default,
    bool WholeSource = true);

/// <summary>
/// Translates a <see cref="PixelGraphicsFrame"/> into the SDL_Renderer calls of the classic SDL driver, without a
/// device so the batching, clipping and image placement are unit-testable. Same pixel semantics as
/// <see cref="PixelGraphicsRasterizer"/>: lines and rectangles become <see cref="PixelGraphicsGeometry"/> rectangles
/// intersected with each clip rectangle (and the output) on the CPU, batched into one fill call per primitive per
/// clip rectangle; images keep their full destination rectangle and are clipped by the renderer's clip rectangle,
/// so SDL samples texel <c>(k + 0.5) * w / W</c> exactly like the CPU reference. A destination too far outside the
/// float-exact range (|coordinate| &gt; 2^24) is replaced by its visible part and the matching fractional source
/// rectangle, which is the same mapping. Allocation-free once the buffers have grown.
/// </summary>
internal sealed class SdlGraphicsPlan
{
    // Coordinates up to 2^24 are exact in float; beyond that SDL's float rectangles would move pixels.
    internal const int FloatExactLimit = 1 << 24;

    private readonly List<DeviceRect> _scratch = new();
    private SDL3.SDL.FRect[] _rects = new SDL3.SDL.FRect[64];

    /// <summary>The calls, in drawing order (primitive order across kinds).</summary>
    internal List<SdlGraphicsCommand> Commands { get; } = new();

    /// <summary>Fill rectangles of all <see cref="SdlGraphicsCommandKind.Fill"/> commands (first <see cref="RectCount"/>).</summary>
    internal ReadOnlySpan<SDL3.SDL.FRect> Rects => _rects.AsSpan(0, RectCount);

    internal int RectCount { get; private set; }

    /// <summary>Rebuilds <see cref="Commands"/> for an output of <paramref name="outputWidth"/> x <paramref name="outputHeight"/> device pixels.</summary>
    internal void Build(PixelGraphicsFrame frame, PixelScale scale, int outputWidth, int outputHeight)
    {
        Commands.Clear();
        RectCount = 0;
        if (frame.IsEmpty || outputWidth <= 0 || outputHeight <= 0) return;
        var bounds = new DeviceRect(0, 0, outputWidth, outputHeight);
        foreach (PixelGraphicsLayer layer in frame.Layers)
        {
            if (layer.Clip.Length == 0) continue;
            foreach (GraphicsPrimitive primitive in layer.Primitives)
            {
                if ((byte)primitive.Rgba == 0) continue;
                if (primitive.Kind == GraphicsPrimitiveKind.Image) AddImage(layer, primitive, scale, bounds);
                else AddFills(layer, primitive, scale, bounds);
            }
        }
    }

    private void AddFills(PixelGraphicsLayer layer, in GraphicsPrimitive primitive, PixelScale scale, DeviceRect bounds)
    {
        _scratch.Clear();
        if (PixelGraphicsGeometry.AppendRects(primitive, scale, _scratch) == 0) return;
        foreach (PixelRect clip in layer.Clip)
        {
            DeviceRect c = PixelGraphicsRasterizer.ClipRect(clip, scale).Intersect(bounds);
            if (c.IsEmpty) continue;
            int first = RectCount;
            foreach (DeviceRect r in _scratch)
            {
                DeviceRect d = r.Intersect(c);
                if (d.IsEmpty) continue;
                if (RectCount == _rects.Length) Array.Resize(ref _rects, _rects.Length * 2);
                _rects[RectCount++] = new SDL3.SDL.FRect { X = d.X0, Y = d.Y0, W = d.X1 - d.X0, H = d.Y1 - d.Y0 };
            }
            if (RectCount > first)
                Commands.Add(new SdlGraphicsCommand(SdlGraphicsCommandKind.Fill, primitive.Rgba, first, RectCount - first, Clip: c));
        }
    }

    private void AddImage(PixelGraphicsLayer layer, in GraphicsPrimitive primitive, PixelScale scale, DeviceRect bounds)
    {
        PixelImage? image = layer.ImageOf(primitive);
        if (image == null) return;
        DeviceRect dest = PixelGraphicsRasterizer.ImageDestination(primitive, scale);
        if (dest.IsEmpty) return;
        bool exact = Math.Abs((long)dest.X0) <= FloatExactLimit && Math.Abs((long)dest.Y0) <= FloatExactLimit
            && Math.Abs((long)dest.X1) <= FloatExactLimit && Math.Abs((long)dest.Y1) <= FloatExactLimit;
        foreach (PixelRect clip in layer.Clip)
        {
            DeviceRect c = PixelGraphicsRasterizer.ClipRect(clip, scale).Intersect(bounds);
            DeviceRect visible = dest.Intersect(c);
            if (visible.IsEmpty) continue;
            SDL3.SDL.FRect destination, source = default;
            if (exact) destination = ToFRect(dest);
            else
            {
                // Same texel mapping restricted to the visible part: texel = (px - dest.X0 + 0.5) * w / W.
                double sx = (double)image.Width / ((long)dest.X1 - dest.X0), sy = (double)image.Height / ((long)dest.Y1 - dest.Y0);
                destination = ToFRect(visible);
                source = new SDL3.SDL.FRect
                {
                    X = (float)(((long)visible.X0 - dest.X0) * sx),
                    Y = (float)(((long)visible.Y0 - dest.Y0) * sy),
                    W = (float)((visible.X1 - visible.X0) * sx),
                    H = (float)((visible.Y1 - visible.Y0) * sy),
                };
            }
            Commands.Add(new SdlGraphicsCommand(SdlGraphicsCommandKind.Image, primitive.Rgba,
                Image: image, Filter: primitive.Filter, Clip: c, Visible: visible,
                Destination: destination, Source: source, WholeSource: exact));
        }
    }

    private static SDL3.SDL.FRect ToFRect(DeviceRect r) =>
        new() { X = r.X0, Y = r.Y0, W = (float)((long)r.X1 - r.X0), H = (float)((long)r.Y1 - r.Y0) };
}

/// <summary>Creates and destroys the SDL textures of <see cref="SdlImageTextureCache"/> (a fake in unit tests).</summary>
internal interface ISdlImageTextureBackend
{
    /// <summary>Creates a static RGBA texture holding the image's pixels (blend mode blend); zero on failure.</summary>
    IntPtr Create(PixelImage image);

    void Destroy(IntPtr texture);

    /// <summary>Why the last <see cref="Create"/> failed (SDL_GetError), for the log.</summary>
    string LastError { get; }
}

/// <summary>
/// One SDL texture per <see cref="PixelImage.Id"/>, uploaded once on first use. When the drawn frame changes,
/// textures of images no layer of the new frame refers to are destroyed. An image larger than the renderer's maximum
/// texture size or whose creation failed is skipped (draws nothing; logged once per reason) until the frame no longer
/// refers to it; one that would exceed the memory budget is skipped while it does not fit. <see cref="Reset"/> forgets everything (device
/// reset: the next use recreates). UI thread only.
/// </summary>
internal sealed class SdlImageTextureCache : IDisposable
{
    /// <summary>Default upper bound on cached texture bytes (4 bytes per texel).</summary>
    internal const long DefaultBudgetBytes = 256L * 1024 * 1024;

    private readonly ISdlImageTextureBackend _backend;
    private readonly Action<string> _log;
    private readonly Dictionary<long, (IntPtr Texture, long Bytes)> _textures = new();
    private readonly HashSet<long> _skipped = new();
    private readonly HashSet<long> _referenced = new();
    private readonly List<long> _evict = new();
    private PixelGraphicsFrame? _retainedFrame;
    private bool _loggedTooLarge, _loggedBudget, _loggedCreate;

    internal SdlImageTextureCache(ISdlImageTextureBackend backend, int maxTextureSize = 0,
        long budgetBytes = DefaultBudgetBytes, Action<string>? log = null)
    {
        _backend = backend;
        MaxTextureSize = maxTextureSize;
        BudgetBytes = budgetBytes;
        _log = log ?? (message => Console.Error.WriteLine(message));
    }

    /// <summary>Largest texture side the renderer accepts; 0 when unknown (creation failure is then the limit).</summary>
    internal int MaxTextureSize { get; set; }

    internal long BudgetBytes { get; }

    internal int Count => _textures.Count;
    internal long BytesInUse { get; private set; }

    /// <summary>Textures created since construction (uploads). Tests and diagnostics.</summary>
    internal int Uploads { get; private set; }

    /// <summary>Draws skipped because the image could not get a texture.</summary>
    internal int Skips { get; private set; }

    /// <summary>The texture of <paramref name="image"/>, created on first use; zero when the image is skipped.</summary>
    internal IntPtr Get(PixelImage image)
    {
        if (_textures.TryGetValue(image.Id, out var entry)) return entry.Texture;
        if (_skipped.Contains(image.Id)) { Skips++; return IntPtr.Zero; }

        long bytes = (long)image.Width * image.Height * 4;
        if (MaxTextureSize > 0 && (image.Width > MaxTextureSize || image.Height > MaxTextureSize))
            return Skip(image, ref _loggedTooLarge,
                $"{image.Width} x {image.Height} exceeds the renderer's maximum texture size {MaxTextureSize}");
        // Not remembered: the budget may free up when other images are released.
        if (BytesInUse + bytes > BudgetBytes)
            return Skip(image, ref _loggedBudget,
                $"{bytes} bytes would exceed the texture budget ({BytesInUse} of {BudgetBytes} bytes in use)", remember: false);

        IntPtr texture = _backend.Create(image);
        if (texture == IntPtr.Zero)
            return Skip(image, ref _loggedCreate, $"texture creation failed: {_backend.LastError}");
        _textures[image.Id] = (texture, bytes);
        BytesInUse += bytes;
        Uploads++;
        return texture;
    }

    private IntPtr Skip(PixelImage image, ref bool logged, string reason, bool remember = true)
    {
        if (remember) _skipped.Add(image.Id);
        Skips++;
        if (!logged)
        {
            logged = true;
            _log($"[SDLRenderer] Pixel image {image.Id} not drawn: {reason}. Further images skipped for this reason are not logged.");
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Keeps only the textures (and skip decisions) of images some layer of <paramref name="frame"/> refers to.
    /// Costs one reference comparison while the frame is unchanged.
    /// </summary>
    internal void Retain(PixelGraphicsFrame frame)
    {
        if (ReferenceEquals(frame, _retainedFrame)) return;
        _retainedFrame = frame;
        if (_textures.Count == 0 && _skipped.Count == 0) return;

        _referenced.Clear();
        foreach (PixelGraphicsLayer layer in frame.Layers)
            foreach (PixelImage image in layer.Images) _referenced.Add(image.Id);

        _evict.Clear();
        foreach (var (id, _) in _textures) if (!_referenced.Contains(id)) _evict.Add(id);
        foreach (long id in _evict)
        {
            var entry = _textures[id];
            _textures.Remove(id);
            BytesInUse -= entry.Bytes;
            _backend.Destroy(entry.Texture);
        }
        _skipped.RemoveWhere(id => !_referenced.Contains(id));
    }

    /// <summary>Destroys every texture and forgets every decision (device reset, shutdown).</summary>
    internal void Reset()
    {
        foreach (var entry in _textures.Values) _backend.Destroy(entry.Texture);
        _textures.Clear();
        _skipped.Clear();
        BytesInUse = 0;
        _retainedFrame = null;
    }

    public void Dispose() => Reset();
}
