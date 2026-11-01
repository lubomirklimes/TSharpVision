using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Gpu;

// Two-pass GPU renderer for the terminal grid.
//   Pass 1 (background): one opaque quad per cell, vertex color = bg VGA color.
//   Pass 2 (glyph):      one blended quad per non-space cell, UV into R8 atlas,
//                         vertex color = fg VGA color; GPU blends via alpha.
//   Optional pass 3 (GpuDisplayEffect.Scanlines only): translucent black strips over the
//                         grid (Bg shaders, blended pipeline created lazily on first use).
//                         The Default effect records exactly the two passes above.
//   Optional graphics pass (IPixelGraphicsDriver layers, TerminalGpuPipeline.Graphics.cs): one blended
//                         instance per Line/FillRect/Image primitive under per-clip scissors, after the
//                         glyphs and before Scanlines; recorded only for a frame with a non-empty layer.
//   Post-process effects (Glow, ...; GpuPostEffectCatalog): the passes above render into a
//                         viewport-sized offscreen texture (GpuPostProcess), then a second render
//                         pass clears the target and draws one full-screen triangle with the
//                         effect's fragment shader sampling that texture, in the same viewport.
//
// No per-frame managed allocation on any path: bindings and colour targets are stack structs
// passed by pointer, the glyph-alpha delegate is cached.
//
// Vertex data is rebuilt from the ScreenBuffer every frame and uploaded via
// staging (transfer) buffers. The glyph atlas is uploaded only when new
// codepoints are added.
internal sealed unsafe partial class TerminalGpuPipeline : IDisposable
{
    // Current vertex buffer capacity in cells. Grows on demand (EnsureCapacity).
    // Initial value covers typical terminal sizes; 4K at 10×18 = 384×120 = 46 080.
    private int _maxCells = 256 * 100;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BgVertex    // 12 bytes
    {
        public float X, Y;
        public byte  R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct GlyphVertex  // 20 bytes
    {
        public float X, Y;
        public float U, V;
        public byte  R, G, B, A;
    }

    private readonly IntPtr _device;
    private readonly int    _cellWidth;
    private readonly int    _cellHeight;
    private readonly int    _underlineHeight;
    private readonly IGpuGlyphSource _glyphSource;

    private readonly TerminalAtlas _atlas;

    // GPU pipelines
    private IntPtr _bgPipeline;
    private IntPtr _glyphPipeline;

    // GPU vertex buffers (device-local)
    private IntPtr _bgVertexBuf;
    private IntPtr _glyphVertexBuf;

    // CPU staging buffers (mapped each frame for upload)
    private IntPtr _bgTransferBuf;
    private IntPtr _glyphTransferBuf;

    private int _maxBgBytes;
    private int _maxGlyphBytes;

    // Colour-target format the pipelines were built for (the swapchain's). Kept so the Scanlines
    // pipeline can be created lazily with the same target description.
    private readonly SDL3.SDL.GPUTextureFormat _colorTargetFormat;

    // ── Scanlines display effect (GpuDisplayEffect.Scanlines) ─────────────────
    // Nothing here exists until the first Scanlines frame: the Default effect creates no pipeline,
    // no buffer, uploads nothing and draws nothing extra. See GpuScanlineGeometry.
    private IntPtr _scanlinePipeline;
    private IntPtr _scanlineVertexBuf;
    private IntPtr _scanlineTransferBuf;
    private int    _scanlineCapacityBytes;
    private int    _scanlineVertexCount;
    // Cache key of the strip vertices currently in _scanlineVertexBuf (viewport height, density).
    private int    _scanlineKeyHeight = -1;
    private float  _scanlineKeyDensity = float.NaN;
    private bool   _scanlineUploadPending;
    private string? _scanlinesUnavailableReason;

    // ── Post-process display effects (GpuDisplayEffects.IsPostProcess) ─────────
    // Created on the first post-effect frame; Default and Scanlines never touch it.
    private GpuPostProcess? _post;

    // Cached so the per-frame vertex build allocates no delegate per glyph.
    private readonly Func<GpuGlyphKey, byte[]?> _getAlpha;

    private bool _disposed;
    private bool _shaderCrossInitialized;

    internal TerminalGpuPipeline(
        IntPtr device,
        IntPtr window,
        int    cellWidth,
        int    cellHeight,
        IGpuGlyphSource glyphSource,
        int    underlineHeight = SdlCellMetrics.LogicalUnderlineHeight)
    {
        _device      = device;
        _cellWidth   = cellWidth;
        _cellHeight  = cellHeight;
        _underlineHeight = Math.Clamp(underlineHeight, 1, cellHeight);
        _glyphSource = glyphSource;
        _getAlpha    = glyphSource.GetAlpha;

        _maxBgBytes   = (_maxCells + 1) * 6 * sizeof(BgVertex);
        _maxGlyphBytes = _maxCells       * 6 * sizeof(GlyphVertex);

        IntPtr bgVs = IntPtr.Zero, bgPs = IntPtr.Zero, glyphVs = IntPtr.Zero, glyphPs = IntPtr.Zero;
        try
        {
            // SDL3-CS imports use the application's resolved native runtime assets.
            _shaderCrossInitialized = SDL3.ShaderCross.Init();
            if (!_shaderCrossInitialized)
                throw new InvalidOperationException(
                    $"[SDLGpu] SDL_ShaderCross_Init failed: {SDL3.SDL.GetError()}");

            SDL3.SDL.GPUTextureFormat swapFmt =
                SDL3.SDL.GetGPUSwapchainTextureFormat(device, window);
            _colorTargetFormat = swapFmt;

            bgVs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgVert.spv",    SDL3.ShaderCross.ShaderStage.Vertex);
            bgPs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgFrag.spv",    SDL3.ShaderCross.ShaderStage.Fragment);
            glyphVs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GlyphVert.spv", SDL3.ShaderCross.ShaderStage.Vertex);
            // The glyph fragment shader samples the atlas: one sampler. SDL builds the shader's
            // resource layout from these counts; Vulkan consumes the SPIR-V as is, so an
            // undeclared sampler there means an unbound descriptor (device lost on NVIDIA).
            glyphPs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GlyphFrag.spv", SDL3.ShaderCross.ShaderStage.Fragment, numSamplers: 1);

            _bgPipeline    = CreateBgPipeline(bgVs, bgPs, swapFmt);
            _glyphPipeline = CreateGlyphPipeline(glyphVs, glyphPs, swapFmt);


            _atlas = new TerminalAtlas(device, cellWidth, cellHeight);

            CreateVertexBuffers();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (bgVs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(device, bgVs);
            if (bgPs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(device, bgPs);
            if (glyphVs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(device, glyphVs);
            if (glyphPs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(device, glyphPs);
        }
    }
    // ─── Shader loading ──────────────────────────────────────────────────────

    // Grid shaders: samplers only, no uniform buffers (the call shape the shader-sampler contract
    // test reads from the constructor's IL).
    private IntPtr LoadShaderFromSpirv(string resourceName, SDL3.ShaderCross.ShaderStage stage, uint numSamplers = 0)
    {
        var resourceInfo = new SDL3.ShaderCross.GraphicsShaderResourceInfo { NumSamplers = numSamplers };
        return CompileEmbeddedShader(resourceName, stage, resourceInfo);
    }

    // Post-process shaders (GpuPostProcess): samplers plus fragment uniform buffers.
    private IntPtr LoadPostShaderFromSpirv(
        string resourceName, SDL3.ShaderCross.ShaderStage stage, uint numSamplers, uint numUniformBuffers)
    {
        var resourceInfo = new SDL3.ShaderCross.GraphicsShaderResourceInfo
        {
            NumSamplers       = numSamplers,
            NumUniformBuffers = numUniformBuffers,
        };
        return CompileEmbeddedShader(resourceName, stage, resourceInfo);
    }

    private IntPtr CompileEmbeddedShader(
        string resourceName, SDL3.ShaderCross.ShaderStage stage, SDL3.ShaderCross.GraphicsShaderResourceInfo resourceInfo)
    {
        var assembly = typeof(TerminalGpuPipeline).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"[SDLGpu] Embedded SPIR-V resource not found: {resourceName}");

        byte[] spirv = new byte[stream.Length];
        stream.ReadExactly(spirv);

        unsafe
        {
            fixed (byte* ptr = spirv)
            {
                var spirvInfo = new SDL3.ShaderCross.SPIRVInfo
                {
                    ByteCode          = (IntPtr)ptr,
                    ByteCodeSize      = (UIntPtr)spirv.Length,
                    ManagedEntrypoint = "main",
                    ShaderStage       = stage,
                };
                IntPtr shader = SDL3.ShaderCross.CompileGraphicsShaderFromSPIRV(
                    _device, ref spirvInfo, ref resourceInfo, 0u);

                if (shader == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"[SDLGpu] SPIR-V→GPU shader compile failed ({stage}): {SDL3.SDL.GetError()}");

                return shader;
            }
        }
    }

    // ─── Pipeline state objects ──────────────────────────────────────────────

    // Bg pipeline: opaque (no blending), exactly as before effect support.
    private IntPtr CreateBgPipeline(IntPtr vs, IntPtr ps, SDL3.SDL.GPUTextureFormat swapFmt) =>
        CreateBgLayoutPipeline(vs, ps, swapFmt, blendState: default, "bg");

    /// <summary>
    /// The source-over blend state the glyph pass uses (and the Scanlines pass reuses):
    /// <c>rgb = src * a + dst * (1 - a)</c>, <c>alpha = src + dst * (1 - a)</c>, blending enabled.
    /// </summary>
    internal static SDL3.SDL.GPUColorTargetBlendState SourceOverBlendState()
    {
        var blendState = new SDL3.SDL.GPUColorTargetBlendState
        {
            SrcColorBlendFactor = SDL3.SDL.GPUBlendFactor.SrcAlpha,
            DstColorBlendFactor = SDL3.SDL.GPUBlendFactor.OneMinusSrcAlpha,
            ColorBlendOp        = SDL3.SDL.GPUBlendOp.Add,
            SrcAlphaBlendFactor = SDL3.SDL.GPUBlendFactor.One,
            DstAlphaBlendFactor = SDL3.SDL.GPUBlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp        = SDL3.SDL.GPUBlendOp.Add,
        };
        // _enableBlend is a private byte at offset 25 in SDL3-CS 3.4.10.2 with no public setter.
        // Without this, blending is silently disabled and glyphs render as solid fg-color blocks.
        ((byte*)&blendState)[25] = 1;
        return blendState;
    }

    // BgVertex layout (float2 position + ubyte4norm colour) with the given colour-target blend.
    private IntPtr CreateBgLayoutPipeline(
        IntPtr vs, IntPtr ps, SDL3.SDL.GPUTextureFormat swapFmt,
        SDL3.SDL.GPUColorTargetBlendState blendState, string name)
    {
        var bufDesc = new SDL3.SDL.GPUVertexBufferDescription
        {
            Slot             = 0,
            Pitch            = (uint)sizeof(BgVertex),
            InputRate        = SDL3.SDL.GPUVertexInputRate.Vertex,
            InstanceStepRate = 0,
        };
        var attribs = new SDL3.SDL.GPUVertexAttribute[]
        {
            new() { Location = 0, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Float2,    Offset = 0 },
            new() { Location = 1, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Ubyte4Norm, Offset = 8 },
        };
        var colorTarget = new SDL3.SDL.GPUColorTargetDescription
        {
            Format     = swapFmt,
            BlendState = blendState,  // default for bg: no blending, bg is opaque
        };

        fixed (SDL3.SDL.GPUVertexAttribute* pAttribs = attribs)
        {
            SDL3.SDL.GPUVertexBufferDescription* pBufDesc = &bufDesc;
            SDL3.SDL.GPUColorTargetDescription*  pTargets  = &colorTarget;
            var info = new SDL3.SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader   = vs,
                FragmentShader = ps,
                PrimitiveType  = SDL3.SDL.GPUPrimitiveType.TriangleList,
                VertexInputState = new SDL3.SDL.GPUVertexInputState
                {
                    VertexBufferDescriptions = (IntPtr)pBufDesc, NumVertexBuffers    = 1,
                    VertexAttributes         = (IntPtr)pAttribs, NumVertexAttributes = 2,
                },
                TargetInfo = new SDL3.SDL.GPUGraphicsPipelineTargetInfo
                {
                    ColorTargetDescriptions = (IntPtr)pTargets,
                    NumColorTargets         = 1,
                },
            };
            IntPtr p = SDL3.SDL.CreateGPUGraphicsPipeline(_device, in info);
            if (p == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"[SDLGpu] CreateGPUGraphicsPipeline ({name}) failed: {SDL3.SDL.GetError()}");
            return p;
        }
    }

    private IntPtr CreateGlyphPipeline(IntPtr vs, IntPtr ps, SDL3.SDL.GPUTextureFormat swapFmt)
    {
        var bufDesc = new SDL3.SDL.GPUVertexBufferDescription
        {
            Slot             = 0,
            Pitch            = (uint)sizeof(GlyphVertex),
            InputRate        = SDL3.SDL.GPUVertexInputRate.Vertex,
            InstanceStepRate = 0,
        };
        var attribs = new SDL3.SDL.GPUVertexAttribute[]
        {
            new() { Location = 0, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Float2,    Offset = 0  },
            new() { Location = 1, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Float2,    Offset = 8  },
            new() { Location = 2, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Ubyte4Norm, Offset = 16 },
        };
        var colorTarget = new SDL3.SDL.GPUColorTargetDescription
        {
            Format     = swapFmt,
            BlendState = SourceOverBlendState(),
        };

        fixed (SDL3.SDL.GPUVertexAttribute* pAttribs = attribs)
        {
            SDL3.SDL.GPUVertexBufferDescription* pBufDesc = &bufDesc;
            SDL3.SDL.GPUColorTargetDescription*  pTargets  = &colorTarget;
            var info = new SDL3.SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader   = vs,
                FragmentShader = ps,
                PrimitiveType  = SDL3.SDL.GPUPrimitiveType.TriangleList,
                VertexInputState = new SDL3.SDL.GPUVertexInputState
                {
                    VertexBufferDescriptions = (IntPtr)pBufDesc, NumVertexBuffers    = 1,
                    VertexAttributes         = (IntPtr)pAttribs, NumVertexAttributes = 3,
                },
                TargetInfo = new SDL3.SDL.GPUGraphicsPipelineTargetInfo
                {
                    ColorTargetDescriptions = (IntPtr)pTargets,
                    NumColorTargets         = 1,
                },
            };
            IntPtr p = SDL3.SDL.CreateGPUGraphicsPipeline(_device, in info);
            if (p == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"[SDLGpu] CreateGPUGraphicsPipeline (glyph) failed: {SDL3.SDL.GetError()}");
            return p;
        }
    }

    // ─── Vertex buffer allocation ────────────────────────────────────────────

    private void CreateVertexBuffers()
    {
        _bgVertexBuf = SDL3.SDL.CreateGPUBuffer(_device,
            new SDL3.SDL.GPUBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUBufferUsageFlags.Vertex,
                Size  = (uint)_maxBgBytes,
            });

        _glyphVertexBuf = SDL3.SDL.CreateGPUBuffer(_device,
            new SDL3.SDL.GPUBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUBufferUsageFlags.Vertex,
                Size  = (uint)_maxGlyphBytes,
            });

        _bgTransferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device,
            new SDL3.SDL.GPUTransferBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
                Size  = (uint)_maxBgBytes,
            });

        _glyphTransferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device,
            new SDL3.SDL.GPUTransferBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
                Size  = (uint)_maxGlyphBytes,
            });

        if (_bgVertexBuf    == IntPtr.Zero || _glyphVertexBuf    == IntPtr.Zero ||
            _bgTransferBuf  == IntPtr.Zero || _glyphTransferBuf  == IntPtr.Zero)
            throw new InvalidOperationException(
                $"[SDLGpu] Failed to create vertex buffers: {SDL3.SDL.GetError()}");
    }

    // Called from SDLGpuRenderer before acquiring a command buffer so that buffer
    // reallocation (which requires WaitForGPUIdle) happens outside a render pass.
    internal void EnsureCapacity(uint cols, uint rows)
    {
        int needed = (int)(cols * rows);
        if (needed <= _maxCells) return;

        // Double current capacity or use needed, whichever is larger.
        int newMax = Math.Max(needed, _maxCells * 2);
        Console.Error.WriteLine(
            $"[SDLGpu] Growing vertex buffers: {_maxCells} → {newMax} cells ({cols}×{rows})");
        GrowVertexBuffers(newMax);
    }

    private void GrowVertexBuffers(int newMax)
    {
        // GPU may still be reading old buffers from the previous frame.
        SDL3.SDL.WaitForGPUIdle(_device);

        if (_glyphTransferBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _glyphTransferBuf); _glyphTransferBuf = IntPtr.Zero; }
        if (_bgTransferBuf    != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _bgTransferBuf);    _bgTransferBuf    = IntPtr.Zero; }
        if (_glyphVertexBuf   != IntPtr.Zero) { SDL3.SDL.ReleaseGPUBuffer(_device, _glyphVertexBuf);           _glyphVertexBuf   = IntPtr.Zero; }
        if (_bgVertexBuf      != IntPtr.Zero) { SDL3.SDL.ReleaseGPUBuffer(_device, _bgVertexBuf);              _bgVertexBuf      = IntPtr.Zero; }

        _maxCells     = newMax;
        _maxBgBytes   = (_maxCells + 1) * 6 * sizeof(BgVertex);
        _maxGlyphBytes = _maxCells      * 6 * sizeof(GlyphVertex);

        CreateVertexBuffers();
    }

    // ─── Scanlines effect resources (lazy) ───────────────────────────────────

    /// <summary>
    /// Set when the Scanlines pipeline or its strip buffer could not be created. Frames then render
    /// as <see cref="GpuDisplayEffect.Default"/> (still correct, just without the effect); the
    /// reason was written to stderr once. Null while Scanlines is available or was never requested.
    /// </summary>
    internal string? ScanlinesUnavailableReason => _scanlinesUnavailableReason;

    /// <summary>True once the lazily created Scanlines pipeline exists. Diagnostics and tests.</summary>
    internal bool ScanlinePipelineCreated => _scanlinePipeline != IntPtr.Zero;

    /// <summary>Strip vertices currently uploaded for the Scanlines pass. Diagnostics and tests.</summary>
    internal int ScanlineVertexCount => _scanlineVertexCount;

    /// <summary>Makes the lazy Scanlines pipeline creation fail, to qualify the no-silent-fallback path.</summary>
    internal bool SimulateScanlineFailureForTesting { get; set; }

    /// <summary>
    /// Outside any pass: creates the Scanlines pipeline on first use and rebuilds the strip
    /// vertices when the viewport height or density changed. Returns false when the effect cannot
    /// be drawn (unavailable, or no strip fits), in which case nothing extra is recorded.
    /// </summary>
    private bool PrepareScanlines(int viewportHeight, float density)
    {
        if (_scanlinesUnavailableReason != null) return false;
        try
        {
            if (_scanlinePipeline == IntPtr.Zero)
            {
                if (SimulateScanlineFailureForTesting)
                    throw new InvalidOperationException("Scanlines pipeline disabled for testing");
                IntPtr vs = IntPtr.Zero, ps = IntPtr.Zero;
                try
                {
                    vs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgVert.spv", SDL3.ShaderCross.ShaderStage.Vertex);
                    ps = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgFrag.spv", SDL3.ShaderCross.ShaderStage.Fragment);
                    _scanlinePipeline = CreateBgLayoutPipeline(vs, ps, _colorTargetFormat, SourceOverBlendState(), "scanlines");
                }
                finally
                {
                    if (vs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, vs);
                    if (ps != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, ps);
                }
            }

            if (viewportHeight != _scanlineKeyHeight || !density.Equals(_scanlineKeyDensity))
                RebuildScanlineStrips(viewportHeight, density);
        }
        catch (Exception ex)
        {
            _scanlinesUnavailableReason = ex.Message;
            Console.Error.WriteLine(
                $"[SDLGpu] Scanlines display effect unavailable ({ex.Message}); rendering without it.");
            ReleaseScanlineResources();
            return false;
        }
        return _scanlineVertexCount > 0;
    }

    private void RebuildScanlineStrips(int viewportHeight, float density)
    {
        int vertices = ScanlineVertexCapacity(viewportHeight, density);
        int bytes = vertices * sizeof(BgVertex);
        if (bytes > _scanlineCapacityBytes)
        {
            // Released resources stay alive until in-flight command buffers finish (SDL defers).
            ReleaseScanlineBuffers();
            int capacity = Math.Max(bytes, 64 * 6 * sizeof(BgVertex));
            _scanlineVertexBuf = SDL3.SDL.CreateGPUBuffer(_device, new SDL3.SDL.GPUBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUBufferUsageFlags.Vertex,
                Size  = (uint)capacity,
            });
            _scanlineTransferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device, new SDL3.SDL.GPUTransferBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
                Size  = (uint)capacity,
            });
            if (_scanlineVertexBuf == IntPtr.Zero || _scanlineTransferBuf == IntPtr.Zero)
                throw new InvalidOperationException($"Scanline strip buffers: {SDL3.SDL.GetError()}");
            _scanlineCapacityBytes = capacity;
        }

        int count = 0;
        if (vertices > 0)
        {
            BgVertex* dst = (BgVertex*)SDL3.SDL.MapGPUTransferBuffer(_device, _scanlineTransferBuf, true);
            if (dst == null)
                throw new InvalidOperationException($"MapGPUTransferBuffer (scanlines): {SDL3.SDL.GetError()}");
            count = BuildScanlineVertices(new Span<BgVertex>(dst, vertices), viewportHeight, density);
            SDL3.SDL.UnmapGPUTransferBuffer(_device, _scanlineTransferBuf);
        }

        _scanlineVertexCount   = count;
        _scanlineUploadPending = count > 0;
        _scanlineKeyHeight     = viewportHeight;
        _scanlineKeyDensity    = density;
    }

    private void ReleaseScanlineBuffers()
    {
        if (_scanlineTransferBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _scanlineTransferBuf); _scanlineTransferBuf = IntPtr.Zero; }
        if (_scanlineVertexBuf   != IntPtr.Zero) { SDL3.SDL.ReleaseGPUBuffer(_device, _scanlineVertexBuf);           _scanlineVertexBuf   = IntPtr.Zero; }
        _scanlineCapacityBytes = 0;
        _scanlineVertexCount   = 0;
        _scanlineUploadPending = false;
        _scanlineKeyHeight     = -1;
        _scanlineKeyDensity    = float.NaN;
    }

    private void ReleaseScanlineResources()
    {
        ReleaseScanlineBuffers();
        if (_scanlinePipeline != IntPtr.Zero) { SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _scanlinePipeline); _scanlinePipeline = IntPtr.Zero; }
    }

    /// <summary>Vertices <see cref="BuildScanlineVertices"/> writes for this viewport height and density.</summary>
    internal static int ScanlineVertexCapacity(int viewportHeight, float density) =>
        GpuScanlineGeometry.StripCount(viewportHeight, density) * 6;

    /// <summary>
    /// Writes one quad (two triangles, same winding as the cell quads) per
    /// <see cref="GpuScanlineGeometry"/> strip into <paramref name="destination"/>, in the clip space
    /// of a grid viewport <paramref name="viewportHeight"/> device pixels high: x spans -1..1, a
    /// strip <c>[Y, Y + H)</c> spans <c>1 - 2Y/height .. 1 - 2(Y + H)/height</c>. Colour is black at
    /// <see cref="GpuScanlineGeometry.StripAlpha"/>. Returns the vertex count. Allocation-free.
    /// </summary>
    internal static int BuildScanlineVertices(Span<BgVertex> destination, int viewportHeight, float density)
    {
        int strips = GpuScanlineGeometry.StripCount(viewportHeight, density);
        if (destination.Length < strips * 6)
            throw new ArgumentException("Destination is too short for the strip vertices.", nameof(destination));
        if (strips == 0) return 0;

        int t = GpuScanlineGeometry.Thickness(density);
        float invH = 2f / viewportHeight;
        byte a = GpuScanlineGeometry.StripAlpha;
        int n = 0;
        for (int i = 0, y = t; i < strips; i++, y += 2 * t)
        {
            int h = Math.Min(t, viewportHeight - y);
            float y0 = 1f - y * invH;
            float y1 = 1f - (y + h) * invH;
            destination[n++] = new BgVertex { X = -1f, Y = y0, A = a };
            destination[n++] = new BgVertex { X =  1f, Y = y0, A = a };
            destination[n++] = new BgVertex { X = -1f, Y = y1, A = a };
            destination[n++] = new BgVertex { X =  1f, Y = y0, A = a };
            destination[n++] = new BgVertex { X =  1f, Y = y1, A = a };
            destination[n++] = new BgVertex { X = -1f, Y = y1, A = a };
        }
        return n;
    }

    // ─── Post-process effect resources (lazy) ────────────────────────────────

    /// <summary>
    /// Last post-process creation failure (logged once on stderr); frames then render Default.
    /// Null while post-processing is available or was never requested.
    /// </summary>
    internal string? PostProcessUnavailableReason => _post?.UnavailableReason;

    /// <summary>The post-process resources, once a post effect was first requested. Tests.</summary>
    internal GpuPostProcess? PostProcessForTesting => _post;

    /// <summary>Makes the lazy post-process preparation fail, to qualify the fallback to Default.</summary>
    internal bool SimulatePostProcessFailureForTesting { get; set; }

    /// <summary>
    /// The effect the last <see cref="RenderFrame"/> actually drew: the requested one, or
    /// <see cref="GpuDisplayEffect.Default"/> when it is unavailable, not implemented yet (no
    /// <see cref="GpuPostEffectCatalog"/> entry) or the viewport is empty.
    /// </summary>
    internal GpuDisplayEffect LastDrawnEffect { get; private set; }

    /// <summary>
    /// The post-process decision, without a device: true (with the effect's spec) only for an
    /// implemented post-process effect and a non-empty viewport. Default and Scanlines: false.
    /// </summary>
    internal static bool SelectPostEffect(GpuDisplayEffect effect, in GpuGridLayout layout, out GpuPostEffectSpec spec)
    {
        spec = null!;
        return GpuDisplayEffects.IsPostProcess(effect) && layout.IsRenderable
            && GpuPostEffectCatalog.TryGet(effect, out spec);
    }

    // Outside any pass: lazy resources for a post effect. Zero means "render Default".
    private IntPtr PreparePostProcess(GpuDisplayEffect effect, GpuPostEffectSpec spec, int width, int height)
    {
        _post ??= new GpuPostProcess(_device, _colorTargetFormat, LoadPostShaderFromSpirv);
        if (SimulatePostProcessFailureForTesting) _post.SimulateFailureForTesting = true;
        return _post.Prepare(effect, spec, width, height);
    }

    // ─── Per-frame rendering ─────────────────────────────────────────────────

    // Renders one terminal frame into an already-acquired swapchain texture.
    // cmdBuf must be open (acquired but not yet submitted by the caller).
    // Returns (vertexBuild, gpuRecord) timings for diagnostics.
    /// <summary>Glyph slots the atlas can hold at the current cell size. Diagnostics only.</summary>
    internal int AtlasCapacity => _atlas.Capacity;

    /// <summary>Atlas side in pixels. Diagnostics only.</summary>
    internal int AtlasSize => _atlas.Size;

    /// <summary>Glyph slots allocated so far. Diagnostics only.</summary>
    internal int AtlasAllocatedSlots => _atlas.AllocatedSlots;

    internal (TimeSpan VertexBuild, TimeSpan GpuRecord) RenderFrame(
        IntPtr       cmdBuf,
        IntPtr       swapTex,
        uint         swapW,
        uint         swapH,
        ScreenBuffer buf,
        uint         cols,
        uint         rows,
        int          cursorX,
        int          cursorY,
        ushort       cursorType,
        GpuPhysicalPalette? palette = null,
        GpuDisplayEffect    effect  = GpuDisplayEffect.Default,
        float               density = 1f)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // The grid spans the full NDC range; the render pass constrains that range to an exact
        // grid-sized viewport so nothing is fractionally scaled.
        GpuGridLayout layout = GpuGridLayout.Compute(
            (int)swapW, (int)swapH, (int)cols, (int)rows, _cellWidth, _cellHeight);

        // Default: one enum compare, nothing else. Scanlines: lazy pipeline creation and strip
        // rebuild happen here, outside any pass; strips are in viewport device pixels.
        bool scanlines = effect == GpuDisplayEffect.Scanlines
            && PrepareScanlines(layout.ViewportHeight, density);
        GpuDisplayEffect drawn = effect == GpuDisplayEffect.Scanlines && _scanlinesUnavailableReason == null
            ? GpuDisplayEffect.Scanlines
            : GpuDisplayEffect.Default;

        // Post effects: lazy offscreen texture / sampler / pipeline, also outside any pass. Default
        // and Scanlines fail the first test in SelectPostEffect (one enum compare).
        IntPtr postPipeline = IntPtr.Zero;
        if (SelectPostEffect(effect, layout, out GpuPostEffectSpec postSpec))
        {
            postPipeline = PreparePostProcess(effect, postSpec, layout.ViewportWidth, layout.ViewportHeight);
            if (postPipeline != IntPtr.Zero) drawn = effect;
        }
        bool post = postPipeline != IntPtr.Zero;
        LastDrawnEffect = drawn;

        // Graphics: no layer (the normal case) is one empty test plus one resident-texture test; otherwise lazy
        // resources, the instance build and new image uploads are prepared here, outside any pass. The first frame
        // without graphics after some releases the image textures (records nothing).
        PixelGraphicsFrame graphics = _graphics;
        bool drawGraphics = (!graphics.IsEmpty || GraphicsTexturesResident)
            && PrepareGraphics(graphics, _graphicsScale, layout);

        // Map staging buffers
        BgVertex*    bgPtr    = (BgVertex*)   SDL3.SDL.MapGPUTransferBuffer(_device, _bgTransferBuf,    true);
        GlyphVertex* glyphPtr = (GlyphVertex*)SDL3.SDL.MapGPUTransferBuffer(_device, _glyphTransferBuf, true);
        if (bgPtr == null || glyphPtr == null)
        {
            if (bgPtr    != null) SDL3.SDL.UnmapGPUTransferBuffer(_device, _bgTransferBuf);
            if (glyphPtr != null) SDL3.SDL.UnmapGPUTransferBuffer(_device, _glyphTransferBuf);
            return (TimeSpan.Zero, TimeSpan.Zero);
        }

        BuildVertices(bgPtr, glyphPtr, buf, cols, rows, _cellWidth, _cellHeight,
            cursorX, cursorY, cursorType, _glyphSource, _getAlpha, _atlas, out int bgCount, out int glyphCount,
            _underlineHeight, (palette ?? GpuPhysicalPalette.Default).ArgbSpan);

        SDL3.SDL.UnmapGPUTransferBuffer(_device, _bgTransferBuf);
        SDL3.SDL.UnmapGPUTransferBuffer(_device, _glyphTransferBuf);

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();  // end of vertex build

        // Copy pass: upload vertex data and atlas
        uint bgBytes    = (uint)(bgCount    * sizeof(BgVertex));
        uint glyphBytes = (uint)(glyphCount * sizeof(GlyphVertex));

        IntPtr copyPass = SDL3.SDL.BeginGPUCopyPass(cmdBuf);

        SDL3.SDL.UploadToGPUBuffer(copyPass,
            new SDL3.SDL.GPUTransferBufferLocation { TransferBuffer = _bgTransferBuf, Offset = 0 },
            new SDL3.SDL.GPUBufferRegion            { Buffer = _bgVertexBuf, Offset = 0, Size = bgBytes },
            false);

        if (glyphCount > 0)
            SDL3.SDL.UploadToGPUBuffer(copyPass,
                new SDL3.SDL.GPUTransferBufferLocation { TransferBuffer = _glyphTransferBuf, Offset = 0 },
                new SDL3.SDL.GPUBufferRegion            { Buffer = _glyphVertexBuf, Offset = 0, Size = glyphBytes },
                false);

        _atlas.UploadIfDirty(copyPass);

        if (scanlines && _scanlineUploadPending)
        {
            SDL3.SDL.UploadToGPUBuffer(copyPass,
                new SDL3.SDL.GPUTransferBufferLocation { TransferBuffer = _scanlineTransferBuf, Offset = 0 },
                new SDL3.SDL.GPUBufferRegion
                {
                    Buffer = _scanlineVertexBuf, Offset = 0,
                    Size   = (uint)(_scanlineVertexCount * sizeof(BgVertex)),
                },
                true);
            _scanlineUploadPending = false;
        }

        if (drawGraphics) UploadGraphics(copyPass);

        SDL3.SDL.EndGPUCopyPass(copyPass);

        // Render pass: clear → bg pass → glyph pass. Target: the swapchain (or capture) texture,
        // or the offscreen grid texture when a post effect follows. Same commands either way.
        var colorTarget = new SDL3.SDL.GPUColorTargetInfo
        {
            Texture           = post ? _post!.Texture : swapTex,
            MipLevel          = 0,
            LayerOrDepthPlane = 0,
            ClearColor        = new SDL3.SDL.FColor { R = 0f, G = 0f, B = 0f, A = 1f },
            LoadOp            = SDL3.SDL.GPULoadOp.Clear,
            StoreOp           = SDL3.SDL.GPUStoreOp.Store,
            // The offscreen texture is fully rewritten each frame: let SDL cycle it instead of
            // waiting for a frame still sampling it.
            Cycle             = post,
        };

        IntPtr renderPass = SDL3.SDL.BeginGPURenderPass(cmdBuf, (IntPtr)(&colorTarget), 1u, IntPtr.Zero);

        // The LoadOp.Clear above covers the whole attachment, so the letterbox strip is cleared
        // before anything is drawn. The viewport then confines the terminal to exactly
        // gridWidth x gridHeight device pixels, anchored top-left.
        //
        // This is what makes the mapping exact: every quad lies inside the NDC clip volume
        // [-1,+1], and the viewport transform maps that onto [0, ViewportWidth]. With
        // ViewportWidth == cols*cellWidth, a cell boundary at NDC x = col*cellWidth*invW - 1
        // lands on device pixel (x + 1) / 2 * ViewportWidth == col * cellWidth, an integer.
        //
        // No scissor is needed: the clip volume already confines all geometry to NDC [-1,+1], and
        // the viewport transform maps that entirely inside the viewport rectangle.
        //
        // The offscreen texture is exactly ViewportWidth x ViewportHeight and the viewport origin
        // is always (0,0), so the same viewport fills it 1:1.
        var viewport = new SDL3.SDL.GPUViewport
        {
            X        = layout.ViewportX,
            Y        = layout.ViewportY,
            W        = layout.ViewportWidth,
            H        = layout.ViewportHeight,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        SDL3.SDL.SetGPUViewport(renderPass, in viewport);

        // Background pass
        var vertexBinding = new SDL3.SDL.GPUBufferBinding { Buffer = _bgVertexBuf, Offset = 0 };
        SDL3.SDL.BindGPUGraphicsPipeline(renderPass, _bgPipeline);
        SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u, (IntPtr)(&vertexBinding), 1u);
        SDL3.SDL.DrawGPUPrimitives(renderPass, (uint)bgCount, 1u, 0u, 0u);

        // Glyph pass
        if (glyphCount > 0)
        {
            vertexBinding = new SDL3.SDL.GPUBufferBinding { Buffer = _glyphVertexBuf, Offset = 0 };
            var atlasBinding = new SDL3.SDL.GPUTextureSamplerBinding { Texture = _atlas.Texture, Sampler = _atlas.Sampler };
            SDL3.SDL.BindGPUGraphicsPipeline(renderPass, _glyphPipeline);
            SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u, (IntPtr)(&vertexBinding), 1u);
            SDL3.SDL.BindGPUFragmentSamplers(renderPass, 0u, (IntPtr)(&atlasBinding), 1u);
            SDL3.SDL.DrawGPUPrimitives(renderPass, (uint)glyphCount, 1u, 0u, 0u);
        }

        // Graphics pass: instanced primitives over the glyphs, under per-clip scissors (reset afterwards).
        if (drawGraphics) DrawGraphics(cmdBuf, renderPass, layout);

        // Scanlines pass: translucent black strips over the finished grid, same viewport.
        if (scanlines)
        {
            vertexBinding = new SDL3.SDL.GPUBufferBinding { Buffer = _scanlineVertexBuf, Offset = 0 };
            SDL3.SDL.BindGPUGraphicsPipeline(renderPass, _scanlinePipeline);
            SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u, (IntPtr)(&vertexBinding), 1u);
            SDL3.SDL.DrawGPUPrimitives(renderPass, (uint)_scanlineVertexCount, 1u, 0u, 0u);
        }

        SDL3.SDL.EndGPURenderPass(renderPass);

        // Post pass: clear the real target (black letterbox), same viewport, one full-screen
        // triangle sampling the offscreen grid texture through the effect's fragment shader.
        if (post)
        {
            var finalTarget = new SDL3.SDL.GPUColorTargetInfo
            {
                Texture           = swapTex,
                MipLevel          = 0,
                LayerOrDepthPlane = 0,
                ClearColor        = new SDL3.SDL.FColor { R = 0f, G = 0f, B = 0f, A = 1f },
                LoadOp            = SDL3.SDL.GPULoadOp.Clear,
                StoreOp           = SDL3.SDL.GPUStoreOp.Store,
                Cycle             = false,
            };
            IntPtr postPass = SDL3.SDL.BeginGPURenderPass(cmdBuf, (IntPtr)(&finalTarget), 1u, IntPtr.Zero);
            SDL3.SDL.SetGPUViewport(postPass, in viewport);
            SDL3.SDL.BindGPUGraphicsPipeline(postPass, postPipeline);
            if (postSpec.NumSamplers > 0)
            {
                var gridBinding = new SDL3.SDL.GPUTextureSamplerBinding { Texture = _post!.Texture, Sampler = _post.Sampler };
                SDL3.SDL.BindGPUFragmentSamplers(postPass, 0u, (IntPtr)(&gridBinding), 1u);
            }
            GpuPostUniforms uniforms = postSpec.BuildUniforms(
                new GpuPostFrame(layout.ViewportWidth, layout.ViewportHeight, density));
            SDL3.SDL.PushGPUFragmentUniformData(cmdBuf, 0u, (IntPtr)(&uniforms), (uint)sizeof(GpuPostUniforms));
            SDL3.SDL.DrawGPUPrimitives(postPass, 3u, 1u, 0u, 0u);
            SDL3.SDL.EndGPURenderPass(postPass);
        }

        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();  // end of GPU command recording
        return (
            System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1),
            System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2));
    }

    // ─── Vertex build ────────────────────────────────────────────────────────

    /// <summary>
    /// Translates the cell grid into the two vertex streams the render pass draws: one opaque
    /// background quad per cell (plus a bar for an underline cursor) and one blended glyph quad per
    /// non-space cell. Writes into caller-provided memory, so the translation is the same whether
    /// the destination is a mapped GPU transfer buffer or ordinary memory.
    /// Colours come from <see cref="GpuPhysicalPalette.Default"/>.
    /// </summary>
    internal static void BuildVertices(
        BgVertex*       bgPtr,
        GlyphVertex*    glyphPtr,
        ScreenBuffer    buf,
        uint            cols,
        uint            rows,
        int             cellWidth,
        int             cellHeight,
        int             cursorX,
        int             cursorY,
        ushort          cursorType,
        IGpuGlyphSource glyphSource,
        TerminalAtlas   atlas,
        out int         bgCount,
        out int         glyphCount,
        int             underlineHeight = 2) =>
        BuildVertices(bgPtr, glyphPtr, buf, cols, rows, cellWidth, cellHeight, cursorX, cursorY,
            cursorType, glyphSource, atlas, out bgCount, out glyphCount, underlineHeight, Vga16);

    /// <summary>
    /// <see cref="BuildVertices(BgVertex*, GlyphVertex*, ScreenBuffer, uint, uint, int, int, int, int, ushort, IGpuGlyphSource, TerminalAtlas, out int, out int, int)"/>
    /// with an explicit physical palette: <paramref name="palette"/> holds 16 ARGB entries
    /// (<see cref="GpuPhysicalPalette.ArgbSpan"/>), indexed by the attribute's fg/bg nibble. Only
    /// the colour lookup changes; geometry, cursor handling and glyph keys are identical.
    /// </summary>
    internal static void BuildVertices(
        BgVertex*       bgPtr,
        GlyphVertex*    glyphPtr,
        ScreenBuffer    buf,
        uint            cols,
        uint            rows,
        int             cellWidth,
        int             cellHeight,
        int             cursorX,
        int             cursorY,
        ushort          cursorType,
        IGpuGlyphSource glyphSource,
        TerminalAtlas   atlas,
        out int         bgCount,
        out int         glyphCount,
        int             underlineHeight,
        ReadOnlySpan<uint> palette) =>
        BuildVertices(bgPtr, glyphPtr, buf, cols, rows, cellWidth, cellHeight, cursorX, cursorY,
            cursorType, glyphSource, glyphSource.GetAlpha, atlas, out bgCount, out glyphCount,
            underlineHeight, palette);

    // The vertex build proper. getAlpha is glyphSource.GetAlpha, passed in so the per-frame caller
    // reuses one cached delegate instead of allocating one per glyph cell.
    private static void BuildVertices(
        BgVertex*       bgPtr,
        GlyphVertex*    glyphPtr,
        ScreenBuffer    buf,
        uint            cols,
        uint            rows,
        int             cellWidth,
        int             cellHeight,
        int             cursorX,
        int             cursorY,
        ushort          cursorType,
        IGpuGlyphSource glyphSource,
        Func<GpuGlyphKey, byte[]?> getAlpha,
        TerminalAtlas   atlas,
        out int         bgCount,
        out int         glyphCount,
        int             underlineHeight,
        ReadOnlySpan<uint> palette)
    {
        if (palette.Length < 16)
            throw new ArgumentException("A physical palette has 16 entries.", nameof(palette));

        float invW = 2f / (cols * cellWidth);
        float invH = 2f / (rows * cellHeight);

        bgCount    = 0;
        glyphCount = 0;

        for (uint row = 0; row < rows; row++)
        {
            for (uint col = 0; col < cols; col++)
            {
                TScreenChar cell = buf.GetChar(col, row);
                byte attr = (byte)(cell.Attr & 0xFF);
                (byte fgR, byte fgG, byte fgB) = ArgbToRgb(palette[attr & 0x0F]);
                (byte bgR, byte bgG, byte bgB) = ArgbToRgb(palette[(attr >> 4) & 0x0F]);

                bool isCursor = cursorType != 0
                    && (int)col == cursorX && (int)row == cursorY;

                byte cellBgR, cellBgG, cellBgB;
                byte cellFgR, cellFgG, cellFgB;
                bool underlineCursor = false;

                if (isCursor && cursorType >= 100)
                {
                    // Block cursor: invert fg/bg
                    cellBgR = fgR; cellBgG = fgG; cellBgB = fgB;
                    cellFgR = bgR; cellFgG = bgG; cellFgB = bgB;
                }
                else if (isCursor)
                {
                    // Underline cursor: normal bg, but add underline bar in fg color
                    cellBgR = bgR; cellBgG = bgG; cellBgB = bgB;
                    cellFgR = fgR; cellFgG = fgG; cellFgB = fgB;
                    underlineCursor = true;
                }
                else
                {
                    cellBgR = bgR; cellBgG = bgG; cellBgB = bgB;
                    cellFgR = fgR; cellFgG = fgG; cellFgB = fgB;
                }

                // Clip-space cell rect
                float cx0 = col  * cellWidth  * invW - 1f;
                float cy0 = 1f - row  * cellHeight * invH;
                float cx1 = cx0 + cellWidth  * invW;
                float cy1 = cy0 - cellHeight * invH;

                EmitQuad(bgPtr, ref bgCount, cx0, cy0, cx1, cy1, cellBgR, cellBgG, cellBgB);

                if (underlineCursor)
                {
                    // Preserve the drawable-scaled underline used by the production renderer.
                    float barY0 = cy0 - (cellHeight - underlineHeight) * (invH);
                    EmitQuad(bgPtr, ref bgCount, cx0, barY0, cx1, cy1, cellFgR, cellFgG, cellFgB);
                }

                char ch = cell.Character;
                if (ch is not (' ' or '\0'))
                {
                    // PhasedDither needs the cell's grid position. The shared helper is the
                    // single phase formula in the codebase; CreateKey then normalises it away for
                    // every glyph that cannot depend on it, so only B0/B1/B2 gain extra slots.
                    GlyphPhase phase =
                        glyphSource.PhaseForCell((int)col, (int)row, cellWidth, cellHeight);
                    GpuGlyphKey glyphKey = glyphSource.CreateKey(ch, phase);

                    var (u0, v0, u1, v1) = atlas.GetOrAllocateSlot(glyphKey, getAlpha);
                    if (u0 != u1)  // valid atlas slot
                        EmitGlyphQuad(glyphPtr, ref glyphCount, cx0, cy0, cx1, cy1,
                            u0, v0, u1, v1, cellFgR, cellFgG, cellFgB);
                }
            }
        }
    }

    // ─── Vertex emit helpers ─────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitQuad(
        BgVertex* buf, ref int count,
        float x0, float y0, float x1, float y1,
        byte r, byte g, byte b)
    {
        // Triangle 1: TL, TR, BL
        buf[count++] = new BgVertex { X = x0, Y = y0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new BgVertex { X = x1, Y = y0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new BgVertex { X = x0, Y = y1, R = r, G = g, B = b, A = 255 };
        // Triangle 2: TR, BR, BL
        buf[count++] = new BgVertex { X = x1, Y = y0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new BgVertex { X = x1, Y = y1, R = r, G = g, B = b, A = 255 };
        buf[count++] = new BgVertex { X = x0, Y = y1, R = r, G = g, B = b, A = 255 };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitGlyphQuad(
        GlyphVertex* buf, ref int count,
        float x0, float y0, float x1, float y1,
        float u0, float v0, float u1, float v1,
        byte r, byte g, byte b)
    {
        buf[count++] = new GlyphVertex { X = x0, Y = y0, U = u0, V = v0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new GlyphVertex { X = x1, Y = y0, U = u1, V = v0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new GlyphVertex { X = x0, Y = y1, U = u0, V = v1, R = r, G = g, B = b, A = 255 };
        buf[count++] = new GlyphVertex { X = x1, Y = y0, U = u1, V = v0, R = r, G = g, B = b, A = 255 };
        buf[count++] = new GlyphVertex { X = x1, Y = y1, U = u1, V = v1, R = r, G = g, B = b, A = 255 };
        buf[count++] = new GlyphVertex { X = x0, Y = y1, U = u0, V = v1, R = r, G = g, B = b, A = 255 };
    }

    // ─── Palette ────────────────────────────────────────────────────────────
    // The 16-entry tables live in GpuPhysicalPalette. Vga16 is the Default table, kept under its
    // historical name: the no-palette BuildVertices overload uses it, and the TV12 guard
    // SdlMonochromePaletteTests.GpuPathsUseTheRendererVgaTable reads it by reflection.
    private static readonly uint[] Vga16 = GpuPhysicalPalette.Default.Argb.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (byte r, byte g, byte b) ArgbToRgb(uint argb) =>
        ((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    // ─── Disposal ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _atlas?.Dispose();

        ReleaseScanlineResources();
        ReleaseGraphicsResources();

        _post?.Dispose();
        _post = null;

        if (_glyphTransferBuf != IntPtr.Zero) SDL3.SDL.ReleaseGPUTransferBuffer(_device, _glyphTransferBuf);
        if (_bgTransferBuf    != IntPtr.Zero) SDL3.SDL.ReleaseGPUTransferBuffer(_device, _bgTransferBuf);
        if (_glyphVertexBuf   != IntPtr.Zero) SDL3.SDL.ReleaseGPUBuffer(_device, _glyphVertexBuf);
        if (_bgVertexBuf      != IntPtr.Zero) SDL3.SDL.ReleaseGPUBuffer(_device, _bgVertexBuf);
        if (_glyphPipeline    != IntPtr.Zero) SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _glyphPipeline);
        if (_bgPipeline       != IntPtr.Zero) SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _bgPipeline);

        if (_shaderCrossInitialized)
            SDL3.ShaderCross.Quit();
    }
}
