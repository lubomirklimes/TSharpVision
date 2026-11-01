using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Gpu;

// Pixel graphics pass (IPixelGraphicsDriver layers). Drawn after the glyph pass and before Scanlines, in the
// same render pass and viewport, so Scanlines and the post-process effects treat graphics exactly like text.
// Physical palettes do not apply: layer colours are RGBA.
//
// Instanced: GpuGraphicsBatch packs one 32-byte GpuGraphicsInstance per Line/FillRect/Image primitive into an
// instance-rate vertex buffer; GraphicsVert.hlsl generates a quad over each instance's device box, and
//   GraphicsShapeFrag.hlsl keeps exactly the pixels of the CPU reference's integer line formula (fills: the box),
//   GraphicsImageFrag.hlsl samples the image's RGBA8 texture (nearest: integer Load; linear: clamp sampler).
// Consecutive primitives of one pipeline/texture are one batch; every batch is drawn once per (layer, clip
// rectangle) under SDL_SetGPUScissor, so occluded parts are never written and order across kinds is kept.
// Image textures live in a GpuImageCache keyed by PixelImage.Id: uploaded once, released when no layer of the
// current frame holds the image (SDL defers the release until in-flight work completes), all on dispose.
//
// Nothing here exists until the first frame with a non-empty layer: a frame without graphics records exactly
// the passes it recorded before graphics support. Any creation or per-frame failure logs once and disables
// graphics; text keeps rendering.
internal sealed unsafe partial class TerminalGpuPipeline
{
    /// <summary>Instances per frame; a frame with more draws the first ones only (logged once).</summary>
    internal const int MaxGraphicsInstances = 1 << 20;

    /// <summary>The image staging buffer is kept between frames up to this size; larger ones are released after use.</summary>
    internal const int MaxRetainedImageStagingBytes = 64 << 20;

    // D3D12 texture placement alignment; also satisfies every other backend.
    private const int ImageUploadAlignment = 512;

    // The layers the next RenderFrame draws (set by the renderer right before each call; the RenderFrame
    // parameter list is a guarded contract) and their logical-to-device scale.
    private PixelGraphicsFrame _graphics = PixelGraphicsFrame.Empty;
    private PixelScale _graphicsScale = PixelScale.Identity(1, 1);

    /// <summary>Selects the layers the following <see cref="RenderFrame"/> calls draw. Render thread.</summary>
    internal void SetGraphics(PixelGraphicsFrame graphics, PixelScale scale)
    {
        _graphics = graphics ?? PixelGraphicsFrame.Empty;
        _graphicsScale = scale;
    }

    private IntPtr _graphicsShapePipeline;
    private IntPtr _graphicsImagePipeline;
    private IntPtr _graphicsLinearSampler;
    private IntPtr _graphicsInstanceBuf;
    private IntPtr _graphicsTransferBuf;
    private int    _graphicsCapacity;          // instances
    private int    _graphicsInstanceCount;
    private IntPtr _graphicsImageStaging;
    private int    _graphicsImageStagingBytes;
    private GpuImageCache? _graphicsImages;
    private string? _graphicsUnavailableReason;
    private bool   _graphicsTruncationLogged, _graphicsImageIndexLogged, _graphicsImageLimitLogged, _graphicsGridLogged;

    // Reused per frame: device clip rectangles of the current layer, the draw list, and the image upload offsets.
    private readonly List<DeviceRect> _graphicsClips = new();
    private readonly List<GpuGraphicsDraw> _graphicsDraws = new();
    private readonly List<int> _graphicsImageOffsets = new();

    // Vertex uniforms (GraphicsVert.hlsl b0/space1) and fragment uniforms (b0/space3) of the graphics pass.
    private struct GraphicsVertexUniforms { public float InvW, InvH, Z0, Z1; }
    private struct GraphicsFragmentUniforms { public int Lw, Lh, Dw, Dh, OriginX, OriginY, Z0, Z1; }

    /// <summary>Set when the graphics pipelines or buffers could not be created, or a frame failed; graphics are
    /// then not drawn (text is unaffected).</summary>
    internal string? GraphicsUnavailableReason => _graphicsUnavailableReason;

    /// <summary>True once the lazily created graphics pipelines exist. Tests.</summary>
    internal bool GraphicsPipelineCreated => _graphicsShapePipeline != IntPtr.Zero;

    /// <summary>Instances drawn by the last frame's graphics pass (before scissor repetition). Tests and benchmarks.</summary>
    internal int LastGraphicsInstanceCount { get; private set; }

    /// <summary>Draw calls of the last frame's graphics pass. Tests and benchmarks.</summary>
    internal int LastGraphicsDrawCount { get; private set; }

    /// <summary>Bytes the last frame uploaded for graphics: instances plus newly uploaded image pixels.</summary>
    internal long LastGraphicsUploadBytes { get; private set; }

    /// <summary>The image texture cache (null until the first graphics frame). Tests.</summary>
    internal GpuImageCache? GraphicsImagesForTesting => _graphicsImages;

    /// <summary>Makes the lazy graphics pipeline creation fail, to qualify the text-only fallback.</summary>
    internal bool SimulateGraphicsFailureForTesting { get; set; }

    /// <summary>Makes the next graphics frame fail after creation (device loss, mapping failure), to qualify the
    /// fallback. Tests.</summary>
    internal bool SimulateGraphicsFrameFailureForTesting { get; set; }

    // True while textures are held: the no-graphics path then still runs PrepareGraphics once to release them.
    private bool GraphicsTexturesResident => _graphicsImages is { Count: > 0 };

    private sealed class SdlImageTextureBackend(IntPtr device) : IGpuImageTextureBackend
    {
        public IntPtr CreateTexture(int width, int height)
        {
            var info = new SDL3.SDL.GPUTextureCreateInfo
            {
                Type              = SDL3.SDL.GPUTextureType.TextureType2D,
                Format            = SDL3.SDL.GPUTextureFormat.R8G8B8A8Unorm,
                Usage             = SDL3.SDL.GPUTextureUsageFlags.Sampler,
                Width             = (uint)width,
                Height            = (uint)height,
                LayerCountOrDepth = 1,
                NumLevels         = 1,
                SampleCount       = SDL3.SDL.GPUSampleCount.SampleCount1,
            };
            return SDL3.SDL.CreateGPUTexture(device, in info);
        }

        public void ReleaseTexture(IntPtr texture) => SDL3.SDL.ReleaseGPUTexture(device, texture);
    }

    /// <summary>
    /// Outside any pass: creates the pipelines on first use, writes this frame's instances into the transfer buffer,
    /// creates textures for new images and stages their pixels. Returns false (and draws nothing) when there is
    /// nothing to draw or graphics are unavailable; text rendering is never affected.
    /// </summary>
    private bool PrepareGraphics(PixelGraphicsFrame frame, PixelScale scale, in GpuGridLayout layout)
    {
        _graphicsInstanceCount = 0;
        LastGraphicsInstanceCount = 0;
        LastGraphicsDrawCount = 0;
        LastGraphicsUploadBytes = 0;
        _graphicsDraws.Clear();
        if (frame.IsEmpty || !layout.IsRenderable || _graphicsUnavailableReason != null)
        {
            // No layer holds an image any more (or nothing is drawn): release every texture.
            _graphicsImages?.ReleaseAll();
            return false;
        }
        try
        {
            if (_graphicsShapePipeline == IntPtr.Zero) CreateGraphicsPipelines();
            _graphicsImages ??= new GpuImageCache(new SdlImageTextureBackend(_device));
            _graphicsImages.BeginFrame(frame);

            int capacity = Math.Min(frame.PrimitiveCount, MaxGraphicsInstances);
            EnsureGraphicsInstanceCapacity(capacity);
            if (SimulateGraphicsFrameFailureForTesting)
                throw new InvalidOperationException("Graphics frame failure simulated for testing");

            var dst = (GpuGraphicsInstance*)SDL3.SDL.MapGPUTransferBuffer(_device, _graphicsTransferBuf, true);
            if (dst == null)
                throw new InvalidOperationException($"MapGPUTransferBuffer (graphics): {SDL3.SDL.GetError()}");
            GpuGraphicsBuildStats stats;
            try
            {
                stats = GpuGraphicsBatch.Build(new Span<GpuGraphicsInstance>(dst, capacity), frame, scale,
                    layout.GridWidth, layout.GridHeight, _graphicsImages, _graphicsClips, _graphicsDraws);
            }
            finally
            {
                SDL3.SDL.UnmapGPUTransferBuffer(_device, _graphicsTransferBuf);
            }
            LogGraphicsLimits(stats, frame.PrimitiveCount);
            if (stats.Instances == 0 || _graphicsDraws.Count == 0)
            {
                _graphicsImages.CancelPending();
                _graphicsDraws.Clear();
                return false;
            }

            StageImageUploads();
            _graphicsInstanceCount = stats.Instances;
            LastGraphicsInstanceCount = stats.Instances;
            LastGraphicsDrawCount = _graphicsDraws.Count;
            LastGraphicsUploadBytes = (long)stats.Instances * GpuGraphicsInstance.Size + _graphicsImages.PendingBytes;
            return true;
        }
        catch (Exception ex)
        {
            _graphicsUnavailableReason = ex.Message;
            Console.Error.WriteLine($"[SDLGpu] Pixel graphics unavailable ({ex.Message}); rendering text only.");
            ReleaseGraphicsResources();
            _graphicsDraws.Clear();
            _graphicsInstanceCount = 0;
            LastGraphicsInstanceCount = 0;
            LastGraphicsDrawCount = 0;
            LastGraphicsUploadBytes = 0;
            return false;
        }
    }

    private void LogGraphicsLimits(in GpuGraphicsBuildStats stats, int primitives)
    {
        if (stats.Truncated && !_graphicsTruncationLogged)
        {
            _graphicsTruncationLogged = true;
            Console.Error.WriteLine($"[SDLGpu] Graphics frame has {primitives} primitives; drawing the first {MaxGraphicsInstances}.");
        }
        if (stats.SkippedImages > 0 && !_graphicsImageIndexLogged)
        {
            _graphicsImageIndexLogged = true;
            Console.Error.WriteLine("[SDLGpu] Graphics image primitive refers to no layer image; it draws nothing.");
        }
        if (stats.LimitedImages > 0 && !_graphicsImageLimitLogged)
        {
            _graphicsImageLimitLogged = true;
            string reason = _graphicsImages?.LastRefusal
                ?? $"destination larger than {GpuGraphicsBatch.MaxImageDestinationExtent} device pixels";
            Console.Error.WriteLine($"[SDLGpu] Graphics image not drawn ({reason}).");
        }
        if (stats.GridTooLarge && !_graphicsGridLogged)
        {
            _graphicsGridLogged = true;
            Console.Error.WriteLine($"[SDLGpu] Grid larger than {GpuGraphicsBatch.MaxGridExtent} device pixels; graphics are not drawn.");
        }
    }

    private void CreateGraphicsPipelines()
    {
        if (SimulateGraphicsFailureForTesting)
            throw new InvalidOperationException("Graphics pipeline disabled for testing");
        IntPtr vs = IntPtr.Zero, shapePs = IntPtr.Zero, imagePs = IntPtr.Zero;
        try
        {
            // Resource counts: SDL builds the shader resource layout from them (Vulkan consumes the SPIR-V as is,
            // so an undeclared sampler or uniform buffer there is an unbound descriptor).
            vs      = LoadPostShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GraphicsVert.spv",
                SDL3.ShaderCross.ShaderStage.Vertex, numSamplers: 0, numUniformBuffers: 1);
            shapePs = LoadPostShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GraphicsShapeFrag.spv",
                SDL3.ShaderCross.ShaderStage.Fragment, numSamplers: 0, numUniformBuffers: 1);
            imagePs = LoadPostShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GraphicsImageFrag.spv",
                SDL3.ShaderCross.ShaderStage.Fragment, numSamplers: 1, numUniformBuffers: 1);
            _graphicsShapePipeline = CreateGraphicsInstancePipeline(vs, shapePs, "graphics shapes");
            _graphicsImagePipeline = CreateGraphicsInstancePipeline(vs, imagePs, "graphics images");

            var samplerInfo = new SDL3.SDL.GPUSamplerCreateInfo
            {
                MinFilter    = SDL3.SDL.GPUFilter.Linear,
                MagFilter    = SDL3.SDL.GPUFilter.Linear,
                MipmapMode   = SDL3.SDL.GPUSamplerMipmapMode.Nearest,
                AddressModeU = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeV = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeW = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
            };
            _graphicsLinearSampler = SDL3.SDL.CreateGPUSampler(_device, in samplerInfo);
            if (_graphicsLinearSampler == IntPtr.Zero)
                throw new InvalidOperationException($"CreateGPUSampler (graphics): {SDL3.SDL.GetError()}");
        }
        finally
        {
            if (vs      != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, vs);
            if (shapePs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, shapePs);
            if (imagePs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, imagePs);
        }
    }

    // GpuGraphicsInstance layout, instance rate, source-over blending.
    private IntPtr CreateGraphicsInstancePipeline(IntPtr vs, IntPtr ps, string name)
    {
        var bufDesc = new SDL3.SDL.GPUVertexBufferDescription
        {
            Slot             = 0,
            Pitch            = GpuGraphicsInstance.Size,
            InputRate        = SDL3.SDL.GPUVertexInputRate.Instance,
            InstanceStepRate = 0,
        };
        var attribs = stackalloc SDL3.SDL.GPUVertexAttribute[4];
        attribs[0] = new() { Location = 0, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Short4,     Offset = 0  };
        attribs[1] = new() { Location = 1, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Int4,       Offset = 8  };
        attribs[2] = new() { Location = 2, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Ubyte4Norm, Offset = 24 };
        attribs[3] = new() { Location = 3, BufferSlot = 0, Format = SDL3.SDL.GPUVertexElementFormat.Uint,       Offset = 28 };
        var colorTarget = new SDL3.SDL.GPUColorTargetDescription
        {
            Format     = _colorTargetFormat,
            BlendState = SourceOverBlendState(),
        };
        var info = new SDL3.SDL.GPUGraphicsPipelineCreateInfo
        {
            VertexShader   = vs,
            FragmentShader = ps,
            PrimitiveType  = SDL3.SDL.GPUPrimitiveType.TriangleList,
            VertexInputState = new SDL3.SDL.GPUVertexInputState
            {
                VertexBufferDescriptions = (IntPtr)(&bufDesc), NumVertexBuffers    = 1,
                VertexAttributes         = (IntPtr)attribs,    NumVertexAttributes = 4,
            },
            TargetInfo = new SDL3.SDL.GPUGraphicsPipelineTargetInfo
            {
                ColorTargetDescriptions = (IntPtr)(&colorTarget),
                NumColorTargets         = 1,
            },
        };
        IntPtr p = SDL3.SDL.CreateGPUGraphicsPipeline(_device, in info);
        if (p == IntPtr.Zero)
            throw new InvalidOperationException($"CreateGPUGraphicsPipeline ({name}) failed: {SDL3.SDL.GetError()}");
        return p;
    }

    // Geometric growth (at least doubling); kept while graphics are drawn.
    private void EnsureGraphicsInstanceCapacity(int instances)
    {
        if (instances <= _graphicsCapacity) return;
        // Released buffers stay alive until in-flight command buffers finish (SDL defers).
        ReleaseGraphicsInstanceBuffers();
        int capacity = (int)Math.Min(MaxGraphicsInstances, Math.Max(instances, Math.Max(256L, 2L * _graphicsCapacity)));
        uint bytes = (uint)(capacity * GpuGraphicsInstance.Size);
        _graphicsInstanceBuf = SDL3.SDL.CreateGPUBuffer(_device, new SDL3.SDL.GPUBufferCreateInfo
        {
            Usage = SDL3.SDL.GPUBufferUsageFlags.Vertex,
            Size  = bytes,
        });
        _graphicsTransferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device, new SDL3.SDL.GPUTransferBufferCreateInfo
        {
            Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
            Size  = bytes,
        });
        if (_graphicsInstanceBuf == IntPtr.Zero || _graphicsTransferBuf == IntPtr.Zero)
            throw new InvalidOperationException($"Graphics instance buffers: {SDL3.SDL.GetError()}");
        _graphicsCapacity = capacity;
    }

    // Copies the pixels of every texture created this frame into one staging buffer (aligned offsets).
    private void StageImageUploads()
    {
        _graphicsImageOffsets.Clear();
        IReadOnlyList<GpuImageTexture> pending = _graphicsImages!.PendingUploads;
        if (pending.Count == 0) return;
        long total = 0;
        foreach (GpuImageTexture t in pending)
        {
            _graphicsImageOffsets.Add((int)Math.Min(total, int.MaxValue));
            total += Align(t.Bytes);
        }
        if (total > uint.MaxValue)
            throw new InvalidOperationException($"Graphics image upload of {total} bytes in one frame");
        if (total > _graphicsImageStagingBytes)
        {
            ReleaseImageStaging();
            long size = Math.Max(total, Math.Min(2L * _graphicsImageStagingBytes, MaxRetainedImageStagingBytes));
            _graphicsImageStaging = SDL3.SDL.CreateGPUTransferBuffer(_device, new SDL3.SDL.GPUTransferBufferCreateInfo
            {
                Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
                Size  = (uint)size,
            });
            if (_graphicsImageStaging == IntPtr.Zero)
                throw new InvalidOperationException($"Graphics image staging buffer: {SDL3.SDL.GetError()}");
            _graphicsImageStagingBytes = (int)Math.Min(size, int.MaxValue);
        }
        byte* dst = (byte*)SDL3.SDL.MapGPUTransferBuffer(_device, _graphicsImageStaging, true);
        if (dst == null)
            throw new InvalidOperationException($"MapGPUTransferBuffer (graphics images): {SDL3.SDL.GetError()}");
        for (int i = 0; i < pending.Count; i++)
        {
            ReadOnlySpan<byte> pixels = pending[i].Image.Pixels;
            pixels.CopyTo(new Span<byte>(dst + _graphicsImageOffsets[i], pixels.Length));
        }
        SDL3.SDL.UnmapGPUTransferBuffer(_device, _graphicsImageStaging);
    }

    private static long Align(long bytes) => (bytes + ImageUploadAlignment - 1) / ImageUploadAlignment * ImageUploadAlignment;

    // Copy pass: the instances PrepareGraphics wrote and the pixels of new textures.
    private void UploadGraphics(IntPtr copyPass)
    {
        SDL3.SDL.UploadToGPUBuffer(copyPass,
            new SDL3.SDL.GPUTransferBufferLocation { TransferBuffer = _graphicsTransferBuf, Offset = 0 },
            new SDL3.SDL.GPUBufferRegion
            {
                Buffer = _graphicsInstanceBuf, Offset = 0,
                Size   = (uint)(_graphicsInstanceCount * GpuGraphicsInstance.Size),
            },
            true);

        IReadOnlyList<GpuImageTexture> pending = _graphicsImages!.PendingUploads;
        if (pending.Count == 0) return;
        for (int i = 0; i < pending.Count; i++)
        {
            GpuImageTexture t = pending[i];
            var src = new SDL3.SDL.GPUTextureTransferInfo
            {
                TransferBuffer = _graphicsImageStaging,
                Offset         = (uint)_graphicsImageOffsets[i],
                PixelsPerRow   = (uint)t.Image.Width,
                RowsPerLayer   = (uint)t.Image.Height,
            };
            var region = new SDL3.SDL.GPUTextureRegion
            {
                Texture = t.Texture, MipLevel = 0, Layer = 0, X = 0, Y = 0, Z = 0,
                W = (uint)t.Image.Width, H = (uint)t.Image.Height, D = 1,
            };
            SDL3.SDL.UploadToGPUTexture(copyPass, in src, in region, false);
        }
        _graphicsImages.MarkUploaded();
        // A one-off huge upload does not keep its staging memory (SDL defers the release past this frame).
        if (_graphicsImageStagingBytes > MaxRetainedImageStagingBytes) ReleaseImageStaging();
    }

    // Render pass, after the glyph pass: one instanced draw per (batch, clip rectangle), then the scissor is reset
    // to the viewport so the passes that follow (Scanlines) are unaffected.
    private void DrawGraphics(IntPtr cmdBuf, IntPtr renderPass, in GpuGridLayout layout)
    {
        var vertexUniforms = new GraphicsVertexUniforms { InvW = 2f / layout.GridWidth, InvH = 2f / layout.GridHeight };
        var fragmentUniforms = new GraphicsFragmentUniforms
        {
            Lw = _graphicsScale.LogicalWidth, Lh = _graphicsScale.LogicalHeight,
            Dw = _graphicsScale.DeviceWidth,  Dh = _graphicsScale.DeviceHeight,
            OriginX = layout.ViewportX, OriginY = layout.ViewportY,
        };

        IntPtr bound = IntPtr.Zero, boundTexture = IntPtr.Zero;
        int boundFirst = -1;
        foreach (GpuGraphicsDraw draw in _graphicsDraws)
        {
            IntPtr pipeline = draw.Kind == GpuGraphicsBatchKind.Image ? _graphicsImagePipeline : _graphicsShapePipeline;
            if (pipeline != bound)
            {
                SDL3.SDL.BindGPUGraphicsPipeline(renderPass, pipeline);
                SDL3.SDL.PushGPUVertexUniformData(cmdBuf, 0u, (IntPtr)(&vertexUniforms), (uint)sizeof(GraphicsVertexUniforms));
                SDL3.SDL.PushGPUFragmentUniformData(cmdBuf, 0u, (IntPtr)(&fragmentUniforms), (uint)sizeof(GraphicsFragmentUniforms));
                bound = pipeline;
                boundTexture = IntPtr.Zero;
                boundFirst = -1;
            }
            if (draw.FirstInstance != boundFirst)
            {
                // The batch's instances start at the binding offset (first_instance stays 0 on every backend).
                var binding = new SDL3.SDL.GPUBufferBinding
                {
                    Buffer = _graphicsInstanceBuf, Offset = (uint)(draw.FirstInstance * GpuGraphicsInstance.Size),
                };
                SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u, (IntPtr)(&binding), 1u);
                boundFirst = draw.FirstInstance;
            }
            if (draw.Kind == GpuGraphicsBatchKind.Image && draw.Image!.Texture != boundTexture)
            {
                var textureBinding = new SDL3.SDL.GPUTextureSamplerBinding { Texture = draw.Image.Texture, Sampler = _graphicsLinearSampler };
                SDL3.SDL.BindGPUFragmentSamplers(renderPass, 0u, (IntPtr)(&textureBinding), 1u);
                boundTexture = draw.Image.Texture;
            }
            var scissor = draw.Scissor with { X = draw.Scissor.X + layout.ViewportX, Y = draw.Scissor.Y + layout.ViewportY };
            SDL3.SDL.SetGPUScissor(renderPass, in scissor);
            SDL3.SDL.DrawGPUPrimitives(renderPass, 6u, (uint)draw.InstanceCount, 0u, 0u);
        }
        var full = new SDL3.SDL.Rect { X = layout.ViewportX, Y = layout.ViewportY, W = layout.ViewportWidth, H = layout.ViewportHeight };
        SDL3.SDL.SetGPUScissor(renderPass, in full);
    }

    private void ReleaseGraphicsInstanceBuffers()
    {
        if (_graphicsTransferBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _graphicsTransferBuf); _graphicsTransferBuf = IntPtr.Zero; }
        if (_graphicsInstanceBuf != IntPtr.Zero) { SDL3.SDL.ReleaseGPUBuffer(_device, _graphicsInstanceBuf);         _graphicsInstanceBuf = IntPtr.Zero; }
        _graphicsCapacity      = 0;
        _graphicsInstanceCount = 0;
    }

    private void ReleaseImageStaging()
    {
        if (_graphicsImageStaging != IntPtr.Zero) { SDL3.SDL.ReleaseGPUTransferBuffer(_device, _graphicsImageStaging); _graphicsImageStaging = IntPtr.Zero; }
        _graphicsImageStagingBytes = 0;
    }

    private void ReleaseGraphicsResources()
    {
        ReleaseGraphicsInstanceBuffers();
        ReleaseImageStaging();
        _graphicsImages?.ReleaseAll();
        if (_graphicsLinearSampler != IntPtr.Zero) { SDL3.SDL.ReleaseGPUSampler(_device, _graphicsLinearSampler);          _graphicsLinearSampler = IntPtr.Zero; }
        if (_graphicsImagePipeline != IntPtr.Zero) { SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _graphicsImagePipeline); _graphicsImagePipeline = IntPtr.Zero; }
        if (_graphicsShapePipeline != IntPtr.Zero) { SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _graphicsShapePipeline); _graphicsShapePipeline = IntPtr.Zero; }
    }
}
