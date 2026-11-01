using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// One GPU instance of the pixel graphics pass: a Line, FillRect or Image primitive (32 bytes, instance-rate vertex
/// buffer). <c>GraphicsVert.hlsl</c> turns it into a quad over <see cref="X0"/>..<see cref="Y1"/>; the fragment
/// shaders decide coverage inside that box (see <see cref="GpuGraphicsBatch"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GpuGraphicsInstance
{
    /// <summary>Byte size of one instance (the vertex buffer pitch).</summary>
    internal const int Size = 32;

    /// <summary>Device-pixel box, half-open, inside the grid and the layer's clip bounds. Short4 at offset 0.</summary>
    public short X0, Y0, X1, Y1;

    /// <summary>Line: ordered logical end points (x1, y1, x2, y2). Image: device destination (x0, y0, width,
    /// height). FillRect: zero. Int4 at offset 8.</summary>
    public int P0, P1, P2, P3;

    /// <summary>Straight-alpha colour; an image is white with its opacity in <see cref="A"/>. Ubyte4Norm at offset 24.</summary>
    public byte R, G, B, A;

    /// <summary>Kind / filter bits (<see cref="GpuGraphicsBatch"/> constants). Uint at offset 28.</summary>
    public uint Flags;
}

/// <summary>What a <see cref="GpuGraphicsDraw"/> draws: shape instances (Line/FillRect) or image instances of one texture.</summary>
internal enum GpuGraphicsBatchKind : byte
{
    Shapes,
    Image,
}

/// <summary>
/// One draw of the graphics pass: <see cref="InstanceCount"/> consecutive instances from <see cref="FirstInstance"/>
/// under one scissor (a layer clip rectangle in grid device pixels), with the shape or the image pipeline.
/// </summary>
internal readonly record struct GpuGraphicsDraw(
    GpuGraphicsBatchKind Kind, int FirstInstance, int InstanceCount, GpuImageTexture? Image, SDL3.SDL.Rect Scissor);

/// <summary>Counters of one <see cref="GpuGraphicsBatch.Build"/>.</summary>
internal readonly record struct GpuGraphicsBuildStats(
    int Instances, int Draws, bool Truncated, int SkippedImages, int LimitedImages, bool GridTooLarge);

/// <summary>
/// Translates a <see cref="PixelGraphicsFrame"/> into GPU instances and draws, without a device. One instance per
/// drawable primitive (no per-pixel or per-run expansion): the shaders reproduce the exact pixel semantics of the
/// CPU reference (<see cref="PixelGraphicsRasterizer"/>), so this only computes boxes and packs parameters.
/// <list type="bullet">
/// <item>Primitive order is drawing order across kinds: consecutive primitives with the same pipeline and texture
/// form one batch; each batch is drawn once per (layer, clip rectangle) under a scissor, batch-major.</item>
/// <item>Boxes are clamped to the grid and to the bounds of the layer's clip rectangles; anything empty, fully
/// transparent, or a line longer than <see cref="GraphicsPrimitive.MaxLineExtent"/> produces no instance.</item>
/// <item>Images: the layer image must exist, the device destination must be at most
/// <see cref="MaxImageDestinationExtent"/> on each side, and the <see cref="GpuImageCache"/> must accept it
/// (dimension and memory limits); otherwise the primitive draws nothing and is counted.</item>
/// <item>The destination span bounds the instance count; a frame with more is truncated (counted).</item>
/// </list>
/// Allocation-free once the caller's lists have grown.
/// </summary>
internal static class GpuGraphicsBatch
{
    internal const uint KindFill = 0, KindLineX = 1, KindLineY = 2;
    internal const uint FlagLinear = 1;

    /// <summary>Largest grid side in device pixels (the box is packed as int16).</summary>
    internal const int MaxGridExtent = short.MaxValue;

    /// <summary>Largest device destination side of an image (keeps the shader's integer nearest math in int32).</summary>
    internal const int MaxImageDestinationExtent = 32768;

    /// <summary>
    /// Writes the instances of <paramref name="frame"/> into <paramref name="destination"/> and the draw list into
    /// <paramref name="draws"/> (cleared first). <paramref name="images"/> resolves image textures; null skips images.
    /// <paramref name="clipScratch"/> is a reusable scratch list.
    /// </summary>
    internal static GpuGraphicsBuildStats Build(
        Span<GpuGraphicsInstance> destination, PixelGraphicsFrame frame, PixelScale scale,
        int gridWidth, int gridHeight, GpuImageCache? images,
        List<DeviceRect> clipScratch, List<GpuGraphicsDraw> draws)
    {
        draws.Clear();
        if (frame.IsEmpty || gridWidth <= 0 || gridHeight <= 0) return default;
        if (gridWidth > MaxGridExtent || gridHeight > MaxGridExtent)
            return new GpuGraphicsBuildStats(0, 0, false, 0, 0, GridTooLarge: true);

        var grid = new DeviceRect(0, 0, gridWidth, gridHeight);
        var map = new DeviceMap(scale);
        int n = 0, skippedImages = 0, limitedImages = 0;
        bool truncated = false;
        foreach (PixelGraphicsLayer layer in frame.Layers)
        {
            if (layer.Clip.Length == 0) continue;
            clipScratch.Clear();
            DeviceRect bounds = default;
            foreach (PixelRect clip in layer.Clip)
            {
                DeviceRect c = map.Box(clip.X, clip.Y, (long)clip.X + clip.W, (long)clip.Y + clip.H).Intersect(grid);
                if (c.IsEmpty) continue;
                bounds = clipScratch.Count == 0 ? c : new DeviceRect(
                    Math.Min(bounds.X0, c.X0), Math.Min(bounds.Y0, c.Y0), Math.Max(bounds.X1, c.X1), Math.Max(bounds.Y1, c.Y1));
                clipScratch.Add(c);
            }
            if (clipScratch.Count == 0) continue;

            GpuGraphicsBatchKind runKind = GpuGraphicsBatchKind.Shapes;
            GpuImageTexture? runImage = null;
            int runFirst = n;
            foreach (GraphicsPrimitive p in layer.Primitives)
            {
                GpuGraphicsInstance instance;
                GpuGraphicsBatchKind kind = GpuGraphicsBatchKind.Shapes;
                GpuImageTexture? texture = null;
                switch (p.Kind)
                {
                    case GraphicsPrimitiveKind.FillRect:
                        if (!TryFill(p, map, bounds, out instance)) continue;
                        break;
                    case GraphicsPrimitiveKind.Line:
                        if (!TryLine(p, map, bounds, out instance)) continue;
                        break;
                    case GraphicsPrimitiveKind.Image:
                    {
                        PixelImage? image = layer.ImageOf(p);
                        if (image == null) { skippedImages++; continue; }
                        switch (TryImage(p, map, bounds, out instance))
                        {
                            case ImageFit.Invisible: continue;
                            case ImageFit.TooLarge: limitedImages++; continue;
                        }
                        // Only now (visible, within limits) does the image get a texture.
                        texture = images?.Acquire(image);
                        if (texture == null) { limitedImages++; continue; }
                        kind = GpuGraphicsBatchKind.Image;
                        break;
                    }
                    default:
                        continue;
                }

                if (n == destination.Length) { truncated = true; break; }
                if (n > runFirst && (kind != runKind || !ReferenceEquals(texture, runImage)))
                {
                    AddDraws(runKind, runImage, runFirst, n - runFirst, clipScratch, draws);
                    runFirst = n;
                }
                runKind = kind;
                runImage = texture;
                destination[n++] = instance;
            }
            if (n > runFirst) AddDraws(runKind, runImage, runFirst, n - runFirst, clipScratch, draws);
            if (truncated) break;
        }
        return new GpuGraphicsBuildStats(n, draws.Count, truncated, skippedImages, limitedImages, false);
    }

    private static void AddDraws(
        GpuGraphicsBatchKind kind, GpuImageTexture? image, int first, int count, List<DeviceRect> clips, List<GpuGraphicsDraw> draws)
    {
        foreach (DeviceRect c in clips)
            draws.Add(new GpuGraphicsDraw(kind, first, count, image,
                new SDL3.SDL.Rect { X = c.X0, Y = c.Y0, W = c.X1 - c.X0, H = c.Y1 - c.Y0 }));
    }

    // ── Per-primitive instances ─────────────────────────────────────────────

    private static bool TryFill(in GraphicsPrimitive p, in DeviceMap map, DeviceRect bounds, out GpuGraphicsInstance instance)
    {
        instance = default;
        if ((byte)p.Rgba == 0 || p.X2 <= p.X1 || p.Y2 <= p.Y1) return false;
        DeviceRect box = map.Box(p.X1, p.Y1, p.X2, p.Y2).Intersect(bounds);
        if (box.IsEmpty) return false;
        instance = Make(box, 0, 0, 0, 0, p.Rgba, KindFill);
        return true;
    }

    private static bool TryLine(in GraphicsPrimitive p, in DeviceMap map, DeviceRect bounds, out GpuGraphicsInstance instance)
    {
        instance = default;
        if ((byte)p.Rgba == 0) return false;
        long adx = Math.Abs((long)p.X2 - p.X1), ady = Math.Abs((long)p.Y2 - p.Y1);
        if (adx > GraphicsPrimitive.MaxLineExtent || ady > GraphicsPrimitive.MaxLineExtent) return false;
        int x1 = p.X1, y1 = p.Y1, x2 = p.X2, y2 = p.Y2;
        uint kind;
        if (adx >= ady)
        {
            if (x1 > x2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            kind = KindLineX;
        }
        else
        {
            if (y1 > y2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            kind = KindLineY;
        }
        DeviceRect box = map.Box(Math.Min(x1, x2), Math.Min(y1, y2),
            (long)Math.Max(x1, x2) + 1, (long)Math.Max(y1, y2) + 1).Intersect(bounds);
        if (box.IsEmpty) return false;
        instance = Make(box, x1, y1, x2, y2, p.Rgba, kind);
        return true;
    }

    private enum ImageFit : byte { Visible, Invisible, TooLarge }

    private static ImageFit TryImage(in GraphicsPrimitive p, in DeviceMap map, DeviceRect bounds, out GpuGraphicsInstance instance)
    {
        instance = default;
        byte opacity = (byte)p.Rgba;
        if (opacity == 0 || p.X2 <= p.X1 || p.Y2 <= p.Y1) return ImageFit.Invisible;
        long x0 = map.X(p.X1), y0 = map.Y(p.Y1);
        long w = map.X(p.X2) - x0, h = map.Y(p.Y2) - y0;
        if (w <= 0 || h <= 0) return ImageFit.Invisible;
        if (w > MaxImageDestinationExtent || h > MaxImageDestinationExtent) return ImageFit.TooLarge;
        DeviceRect box = Clamp(x0, y0, x0 + w, y0 + h).Intersect(bounds);
        if (box.IsEmpty) return ImageFit.Invisible;
        // Visible and at most 32768 wide: x0 > -32768 - 32768, so the destination fits int32.
        instance = Make(box, (int)x0, (int)y0, (int)w, (int)h, 0xFFFFFF00u | opacity,
            p.Filter == GraphicsImageFilter.Linear ? FlagLinear : 0u);
        return ImageFit.Visible;
    }

    private static GpuGraphicsInstance Make(DeviceRect box, int p0, int p1, int p2, int p3, uint rgba, uint flags) => new()
    {
        X0 = (short)box.X0, Y0 = (short)box.Y0, X1 = (short)box.X1, Y1 = (short)box.Y1,
        P0 = p0, P1 = p1, P2 = p2, P3 = p3,
        R = (byte)(rgba >> 24), G = (byte)(rgba >> 16), B = (byte)(rgba >> 8), A = (byte)rgba,
        Flags = flags,
    };

    // ── Logical -> device (floor(v * D / L), exact in 64 bits for every int input) ──

    internal static long DeviceX(PixelScale s, long v) => FloorDiv(v * s.DeviceWidth, s.LogicalWidth);
    internal static long DeviceY(PixelScale s, long v) => FloorDiv(v * s.DeviceHeight, s.LogicalHeight);

    private static long FloorDiv(long n, long d) => n >= 0 ? n / d : -((d - 1 - n) / d);

    // The mapping of one build: an integer density (1x, 2x, ...) is a multiplication, anything else divides.
    private readonly struct DeviceMap
    {
        private readonly PixelScale _scale;
        private readonly int _fx, _fy;

        internal DeviceMap(PixelScale scale)
        {
            _scale = scale;
            _fx = scale.DeviceWidth % scale.LogicalWidth == 0 ? scale.DeviceWidth / scale.LogicalWidth : 0;
            _fy = scale.DeviceHeight % scale.LogicalHeight == 0 ? scale.DeviceHeight / scale.LogicalHeight : 0;
        }

        internal long X(long v) => _fx != 0 ? v * _fx : DeviceX(_scale, v);
        internal long Y(long v) => _fy != 0 ? v * _fy : DeviceY(_scale, v);

        internal DeviceRect Box(long x0, long y0, long x1, long y1) => Clamp(X(x0), Y(y0), X(x1), Y(y1));
    }

    // Saturates to int; callers intersect with the grid afterwards.
    private static DeviceRect Clamp(long x0, long y0, long x1, long y1) => new(
        (int)Math.Clamp(x0, int.MinValue, int.MaxValue), (int)Math.Clamp(y0, int.MinValue, int.MaxValue),
        (int)Math.Clamp(x1, int.MinValue, int.MaxValue), (int)Math.Clamp(y1, int.MinValue, int.MaxValue));
}
