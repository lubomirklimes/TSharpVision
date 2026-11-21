using TSharpVision.Drivers.SDL.Rendering.Fonts;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Supplies rasterised grapheme clusters (lead cells and their trails) to <see cref="TerminalGpuPipeline"/>'s cluster
/// pass (TV13m). <see cref="SDLGpuRenderer"/> implements it over <see cref="SdlClusterRasterizer"/>.
/// </summary>
internal interface IGpuClusterSource
{
    /// <summary>
    /// The raster of <paramref name="cluster"/> over <paramref name="cells"/> cells: exactly
    /// <c>cells * cellWidth x cellHeight</c> straight-alpha RGBA pixels, or null when nothing can be drawn.
    /// </summary>
    SdlClusterGlyph? GetCluster(ReadOnlySpan<char> cluster, int cells);
}

/// <summary>Where a cluster lives in <see cref="ClusterAtlas"/>: UVs of its full width and whether it keeps its own colours.</summary>
internal readonly record struct ClusterSlot(float U0, float V0, float U1, float V1, int Cells, bool IsColor, bool Empty);

/// <summary>
/// RGBA8 atlas for grapheme clusters (TV13m): rows one cell high, each cluster takes <c>cells</c> consecutive cell
/// columns of a row (shelf packing). Monochrome clusters are stored white with coverage in alpha and tinted by the
/// vertex colour; colour clusters are stored as they are and drawn with a white vertex colour, so one shader serves both.
/// <para>
/// Bounded: when a cluster does not fit, <see cref="TryGet"/> fails and the pipeline calls <see cref="Reset"/> (every
/// slot evicted, <see cref="Generation"/> advances) and rebuilds the frame's cluster quads. Only a single frame with
/// more distinct clusters than the atlas holds drops clusters, and those are counted in <see cref="DroppedClusters"/>
/// (and reported once on stderr by the pipeline), never silently.
/// </para>
/// Keys are <c>(cells, cluster text)</c>; lookups use a span key and do not allocate.
/// </summary>
internal sealed class ClusterAtlas : IDisposable
{
    /// <summary>Smallest atlas side.</summary>
    internal const int MinAtlasSize = 512;

    /// <summary>Largest atlas side (16 MiB of RGBA8).</summary>
    internal const int MaxAtlasSize = 2048;

    /// <summary>Cell units (one cell wide) the atlas aims to hold before it stops growing.</summary>
    internal const int TargetUnits = 1024;

    /// <summary>Most keys (placed or empty) kept before the atlas reports itself full, bounding the lookup table.</summary>
    internal const int MaxEntries = 8192;

    private readonly IntPtr _device;
    private readonly int _cellWidth, _cellHeight;
    private readonly int _columns, _rows;
    private readonly byte[] _cpu;
    private readonly Dictionary<string, ClusterSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClusterSlot>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private int _row, _column;
    private int _dirtyRowMin = int.MaxValue, _dirtyRowMax = -1;
    private IntPtr _texture, _sampler, _transferBuf;
    private bool _disposed;

    /// <summary>Smallest power-of-two side from <see cref="MinAtlasSize"/> up to <see cref="MaxAtlasSize"/> holding <see cref="TargetUnits"/> cells.</summary>
    internal static int ChooseSize(int cellWidth, int cellHeight)
    {
        int size = MinAtlasSize;
        while (size < MaxAtlasSize &&
               (size / Math.Max(1, cellWidth)) * (size / Math.Max(1, cellHeight)) < TargetUnits)
            size *= 2;
        return size;
    }

    /// <summary>A CPU-only atlas (no texture): slot allocation and pixels work, <see cref="UploadIfDirty"/> must not be called.</summary>
    internal ClusterAtlas(int cellWidth, int cellHeight, int? size = null)
    {
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
        Size = size ?? ChooseSize(cellWidth, cellHeight);
        _columns = Size / Math.Max(1, cellWidth);
        _rows = Size / Math.Max(1, cellHeight);
        if (_columns < TScreenChar.MaxCellWidth || _rows < 1)
            throw new ArgumentException($"A {Size}px cluster atlas cannot hold a {TScreenChar.MaxCellWidth}-cell cluster of {cellWidth}x{cellHeight} cells.");
        _cpu = new byte[Size * Size * 4];
        _lookup = _slots.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>A GPU atlas: an RGBA8 texture, a nearest sampler and an upload buffer.</summary>
    internal ClusterAtlas(IntPtr device, int cellWidth, int cellHeight) : this(cellWidth, cellHeight)
    {
        _device = device;
        try { CreateGpuResources(); }
        catch { Dispose(); throw; }
    }

    internal int Size { get; }
    internal IntPtr Texture => _texture;
    internal IntPtr Sampler => _sampler;

    /// <summary>Cell units the atlas holds (columns x rows).</summary>
    internal int CapacityUnits => _columns * _rows;

    /// <summary>Clusters currently resident.</summary>
    internal int Count => _slots.Count;

    /// <summary>Times every slot was evicted to make room. Diagnostics.</summary>
    internal int Generation { get; private set; }

    /// <summary>Clusters a frame wanted but could not get even after a reset. Diagnostics; never silently dropped.</summary>
    internal int DroppedClusters { get; set; }

    /// <summary>Pixel of the CPU copy (R, G, B, A). Tests.</summary>
    internal (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        int o = (y * Size + x) * 4;
        return (_cpu[o], _cpu[o + 1], _cpu[o + 2], _cpu[o + 3]);
    }

    /// <summary>
    /// The slot of <paramref name="cluster"/> in <paramref name="cells"/> cells, rasterised through
    /// <paramref name="source"/> on first use. False when the atlas is full (call <see cref="Reset"/>).
    /// </summary>
    internal bool TryGet(ReadOnlySpan<char> cluster, int cells, IGpuClusterSource source, out ClusterSlot slot)
    {
        cells = Math.Clamp(cells, 1, TScreenChar.MaxCellWidth);
        int length = cluster.Length + 1;
        Span<char> key = length <= 256 ? stackalloc char[length] : new char[length];
        key[0] = (char)('0' + cells);
        cluster.CopyTo(key[1..]);
        if (_lookup.TryGetValue(key, out slot)) return true;
        if (_slots.Count >= MaxEntries)
        {
            slot = default;
            return false;
        }

        SdlClusterGlyph? glyph = source.GetCluster(cluster, cells);
        if (glyph == null || IsBlank(glyph))
        {
            // Nothing to draw (no glyph, or only ignorable scalars): remember it without using atlas space.
            slot = new ClusterSlot(0, 0, 0, 0, cells, false, Empty: true);
            _slots[key.ToString()] = slot;
            return true;
        }

        if (_column + cells > _columns)
        {
            _row++;
            _column = 0;
        }
        if (_row >= _rows)
        {
            slot = default;
            return false;
        }

        int x0 = _column * _cellWidth, y0 = _row * _cellHeight;
        Blit(glyph, x0, y0);
        float inv = 1f / Size;
        slot = new ClusterSlot(x0 * inv, y0 * inv, (x0 + cells * _cellWidth) * inv, (y0 + _cellHeight) * inv,
            cells, glyph.IsColor, Empty: false);
        _slots[key.ToString()] = slot;
        _dirtyRowMin = Math.Min(_dirtyRowMin, _row);
        _dirtyRowMax = Math.Max(_dirtyRowMax, _row);
        _column += cells;
        return true;
    }

    /// <summary>Evicts every cluster (the next lookups rasterise and re-place them).</summary>
    internal void Reset()
    {
        _slots.Clear();
        Array.Clear(_cpu);
        _row = _column = 0;
        _dirtyRowMin = 0;
        _dirtyRowMax = _rows - 1;
        Generation++;
    }

    private static bool IsBlank(SdlClusterGlyph glyph)
    {
        for (int i = 3; i < glyph.Rgba.Length; i += 4)
            if (glyph.Rgba[i] != 0) return false;
        return true;
    }

    private void Blit(SdlClusterGlyph glyph, int x0, int y0)
    {
        int w = Math.Min(glyph.Width, Size - x0), h = Math.Min(glyph.Height, _cellHeight);
        for (int y = 0; y < h; y++)
            glyph.Rgba.AsSpan(y * glyph.Width * 4, w * 4).CopyTo(_cpu.AsSpan(((y0 + y) * Size + x0) * 4));
    }

    private void CreateGpuResources()
    {
        var texInfo = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type              = SDL3.SDL.GPUTextureType.TextureType2D,
            Format            = SDL3.SDL.GPUTextureFormat.R8G8B8A8Unorm,
            Usage             = SDL3.SDL.GPUTextureUsageFlags.Sampler,
            Width             = (uint)Size,
            Height            = (uint)Size,
            LayerCountOrDepth = 1,
            NumLevels         = 1,
            SampleCount       = SDL3.SDL.GPUSampleCount.SampleCount1,
        };
        _texture = SDL3.SDL.CreateGPUTexture(_device, in texInfo);

        var samplerInfo = new SDL3.SDL.GPUSamplerCreateInfo
        {
            MinFilter    = SDL3.SDL.GPUFilter.Nearest,
            MagFilter    = SDL3.SDL.GPUFilter.Nearest,
            MipmapMode   = SDL3.SDL.GPUSamplerMipmapMode.Nearest,
            AddressModeU = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
            AddressModeV = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
            AddressModeW = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
        };
        _sampler = SDL3.SDL.CreateGPUSampler(_device, in samplerInfo);

        var xferInfo = new SDL3.SDL.GPUTransferBufferCreateInfo
        {
            Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
            Size  = (uint)_cpu.Length,
        };
        _transferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device, in xferInfo);
        if (_texture == IntPtr.Zero || _sampler == IntPtr.Zero || _transferBuf == IntPtr.Zero)
            throw new InvalidOperationException($"[SDLGpu] Failed to create cluster atlas: {SDL3.SDL.GetError()}");

        // The texture starts undefined: upload the (zeroed) CPU copy once.
        _dirtyRowMin = 0;
        _dirtyRowMax = _rows - 1;
    }

    /// <summary>Uploads the rows changed since the last upload. Must be called while a copy pass is open.</summary>
    internal unsafe void UploadIfDirty(IntPtr copyPass)
    {
        if (_dirtyRowMax < 0 || copyPass == IntPtr.Zero || _transferBuf == IntPtr.Zero) return;
        int y0 = _dirtyRowMin * _cellHeight;
        int y1 = Math.Min(Size, (_dirtyRowMax + 1) * _cellHeight);
        if (_dirtyRowMax == _rows - 1) y1 = Size;
        int bytes = (y1 - y0) * Size * 4;

        IntPtr mapped = SDL3.SDL.MapGPUTransferBuffer(_device, _transferBuf, true);
        if (mapped == IntPtr.Zero) return;
        _cpu.AsSpan(y0 * Size * 4, bytes).CopyTo(new Span<byte>((void*)mapped, bytes));
        SDL3.SDL.UnmapGPUTransferBuffer(_device, _transferBuf);

        var src = new SDL3.SDL.GPUTextureTransferInfo
        {
            TransferBuffer = _transferBuf,
            Offset         = 0,
            PixelsPerRow   = (uint)Size,
            RowsPerLayer   = (uint)(y1 - y0),
        };
        var dst = new SDL3.SDL.GPUTextureRegion
        {
            Texture  = _texture,
            MipLevel = 0,
            Layer    = 0,
            X = 0, Y = (uint)y0, Z = 0,
            W = (uint)Size,
            H = (uint)(y1 - y0),
            D = 1,
        };
        SDL3.SDL.UploadToGPUTexture(copyPass, in src, in dst, false);
        _dirtyRowMin = int.MaxValue;
        _dirtyRowMax = -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device == IntPtr.Zero) return;
        if (_transferBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _transferBuf); _transferBuf = IntPtr.Zero; }
        if (_sampler     != IntPtr.Zero) { SDL3.SDL.ReleaseGPUSampler(_device, _sampler);             _sampler     = IntPtr.Zero; }
        if (_texture     != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTexture(_device, _texture);              _texture     = IntPtr.Zero; }
    }
}
