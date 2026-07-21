using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Gpu;

// R8Unorm glyph atlas: one slot per GpuGlyphKey, cellWidth×cellHeight pixels each.
// The CPU buffer is kept in sync with allocated slots. UploadIfDirty copies it
// to the GPU texture inside an already-open copy pass.
//
// Slot content is immutable once allocated: GetOrAllocateSlot invokes the glyph source only the
// first time it sees a key. That is what lets SDLGpuRenderer clear its own alpha cache on
// overflow while atlas slots persist — the same key resolves to the uploaded slot, and glyph
// generation is deterministic in any case.
internal sealed class TerminalAtlas : IDisposable
{
    private const int AtlasWidth  = 1024;
    private const int AtlasHeight = 1024;

    private readonly IntPtr _device;
    private readonly int    _cellWidth;
    private readonly int    _cellHeight;
    private readonly int    _atlasColCount;  // how many glyph columns fit
    private readonly int    _atlasRowCount;  // how many glyph rows fit

    private IntPtr _texture;
    private IntPtr _sampler;
    private IntPtr _transferBuf;

    private readonly byte[]                           _cpu;
    private readonly Dictionary<GpuGlyphKey, int> _slots;
    private int  _nextSlot;
    private bool _dirty;
    private bool _disposed;

    internal IntPtr Texture => _texture;
    internal IntPtr Sampler => _sampler;

    /// <summary>Total slots this atlas can hold at the current cell size.</summary>
    internal int Capacity => _atlasColCount * _atlasRowCount;

    /// <summary>Slots allocated so far. Diagnostics only.</summary>
    internal int AllocatedSlots => _nextSlot;

    internal TerminalAtlas(IntPtr device, int cellWidth, int cellHeight)
    {
        _device        = device;
        _cellWidth     = cellWidth;
        _cellHeight    = cellHeight;
        _atlasColCount = AtlasWidth  / cellWidth;
        _atlasRowCount = AtlasHeight / cellHeight;
        _cpu           = new byte[AtlasWidth * AtlasHeight];
        _slots         = new Dictionary<GpuGlyphKey, int>();
        CreateGpuResources();
        _dirty = true;
    }

    private void CreateGpuResources()
    {
        var texInfo = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type              = SDL3.SDL.GPUTextureType.TextureType2D,
            Format            = SDL3.SDL.GPUTextureFormat.R8Unorm,
            Usage             = SDL3.SDL.GPUTextureUsageFlags.Sampler,
            Width             = (uint)AtlasWidth,
            Height            = (uint)AtlasHeight,
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
            Size  = (uint)(AtlasWidth * AtlasHeight),
        };
        _transferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device, in xferInfo);
    }

    // Returns UV rect for the glyph. Allocates a new slot if this is the first
    // time this key is seen, calling getAlpha() to get the alpha mask.
    internal (float u0, float v0, float u1, float v1) GetOrAllocateSlot(
        GpuGlyphKey key, Func<GpuGlyphKey, byte[]?> getAlpha)
    {
        if (!_slots.TryGetValue(key, out int slot))
        {
            if (_nextSlot >= Capacity)
                return default;  // atlas full; glyph won't render

            slot = _nextSlot++;
            _slots[key] = slot;

            byte[]? alpha = getAlpha(key);
            if (alpha != null)
                BlitGlyphToAtlas(slot, alpha, key);
            _dirty = true;
        }
        return SlotUV(slot);
    }

    private void BlitGlyphToAtlas(int slot, byte[] alpha, GpuGlyphKey key)
    {
        // The copy below walks cellHeight rows of cellWidth bytes into a shared 1024x1024
        // buffer, so a wrong length reads past the source or writes into a neighbouring slot.
        GeneratedGlyphValidator.EnsureExpectedLength(alpha, _cellWidth, _cellHeight, key.Codepoint);

        int col  = slot % _atlasColCount;
        int row  = slot / _atlasColCount;
        int xOff = col * _cellWidth;
        int yOff = row * _cellHeight;

        for (int y = 0; y < _cellHeight; y++)
        {
            int dstBase = (yOff + y) * AtlasWidth + xOff;
            int srcBase = y * _cellWidth;
            alpha.AsSpan(srcBase, _cellWidth).CopyTo(_cpu.AsSpan(dstBase));
        }
    }

    private (float u0, float v0, float u1, float v1) SlotUV(int slot)
    {
        int col = slot % _atlasColCount;
        int row = slot / _atlasColCount;
        float u0 = (float)(col * _cellWidth)  / AtlasWidth;
        float v0 = (float)(row * _cellHeight) / AtlasHeight;
        float u1 = u0 + (float)_cellWidth  / AtlasWidth;
        float v1 = v0 + (float)_cellHeight / AtlasHeight;
        return (u0, v0, u1, v1);
    }

    // Upload CPU atlas buffer to GPU if any new glyphs were added since last upload.
    // Must be called while a copy pass is open.
    internal unsafe void UploadIfDirty(IntPtr copyPass)
    {
        if (!_dirty || copyPass == IntPtr.Zero) return;

        IntPtr mapped = SDL3.SDL.MapGPUTransferBuffer(_device, _transferBuf, true);
        if (mapped == IntPtr.Zero) return;
        _cpu.AsSpan().CopyTo(new Span<byte>((void*)mapped, _cpu.Length));
        SDL3.SDL.UnmapGPUTransferBuffer(_device, _transferBuf);

        var src = new SDL3.SDL.GPUTextureTransferInfo
        {
            TransferBuffer = _transferBuf,
            Offset         = 0,
            PixelsPerRow   = (uint)AtlasWidth,
            RowsPerLayer   = (uint)AtlasHeight,
        };
        var dst = new SDL3.SDL.GPUTextureRegion
        {
            Texture  = _texture,
            MipLevel = 0,
            Layer    = 0,
            X = 0, Y = 0, Z = 0,
            W = (uint)AtlasWidth,
            H = (uint)AtlasHeight,
            D = 1,
        };
        SDL3.SDL.UploadToGPUTexture(copyPass, in src, in dst, false);
        _dirty = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_transferBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _transferBuf); _transferBuf = IntPtr.Zero; }
        if (_sampler     != IntPtr.Zero) { SDL3.SDL.ReleaseGPUSampler(_device, _sampler);             _sampler     = IntPtr.Zero; }
        if (_texture     != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTexture(_device, _texture);              _texture     = IntPtr.Zero; }
    }
}
