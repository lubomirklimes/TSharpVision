namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>Compiles an embedded SPIR-V shader for the device (see <c>TerminalGpuPipeline.LoadShaderFromSpirv</c>).</summary>
internal delegate IntPtr GpuShaderLoader(
    string resourceName, SDL3.ShaderCross.ShaderStage stage, uint numSamplers, uint numUniformBuffers);

/// <summary>
/// GPU resources of the post-process display effects (<see cref="GpuDisplayEffects.IsPostProcess"/>),
/// owned by <see cref="TerminalGpuPipeline"/> and created lazily on the first post-effect frame,
/// always outside a render pass:
/// <list type="bullet">
/// <item>the offscreen grid texture: viewport-sized, swapchain format, ColorTarget | Sampler;
/// recreated when the viewport size changes;</item>
/// <item>one linear, clamp-to-edge sampler;</item>
/// <item>one graphics pipeline per effect (PostVert + the effect's fragment shader from
/// <see cref="GpuPostEffectCatalog"/>, no vertex input, no blending).</item>
/// </list>
/// Everything is released by <see cref="Dispose"/> (the owning pipeline is disposed on shutdown and
/// on a density rebuild). A creation failure is reported once on stderr, recorded in
/// <see cref="UnavailableReason"/>, and the caller renders Default. Nothing here allocates managed
/// memory per frame.
/// </summary>
internal sealed unsafe class GpuPostProcess : IDisposable
{
    /// <summary>Resource name of the shared full-screen-triangle vertex shader.</summary>
    internal const string VertexShaderResource = GpuPostEffectCatalog.ShaderResourcePrefix + "PostVert.spv";

    private static readonly int EffectSlots = Enum.GetValues<GpuDisplayEffect>().Length;
    private static int s_liveResources;

    /// <summary>Post-process GPU objects alive in the process (all instances). Tests.</summary>
    internal static int TotalLiveResources => Volatile.Read(ref s_liveResources);

    private readonly IntPtr _device;
    private readonly SDL3.SDL.GPUTextureFormat _format;
    private readonly GpuShaderLoader _loadShader;

    private IntPtr _texture;
    private int    _textureWidth;
    private int    _textureHeight;
    private IntPtr _sampler;
    private readonly IntPtr[] _pipelines;
    private readonly bool[]   _failedEffects;
    private bool    _infrastructureFailed;
    private string? _unavailableReason;
    private int     _liveResources;
    private bool    _disposed;

    internal GpuPostProcess(IntPtr device, SDL3.SDL.GPUTextureFormat format, GpuShaderLoader loadShader)
    {
        _device        = device;
        _format        = format;
        _loadShader    = loadShader;
        _pipelines     = new IntPtr[EffectSlots];
        _failedEffects = new bool[EffectSlots];
    }

    /// <summary>The offscreen grid texture (zero until the first post frame).</summary>
    internal IntPtr Texture => _texture;

    /// <summary>The linear clamp sampler (zero until the first post frame).</summary>
    internal IntPtr Sampler => _sampler;

    /// <summary>Size of the current offscreen texture.</summary>
    internal (int Width, int Height) TextureSize => (_textureWidth, _textureHeight);

    /// <summary>Offscreen textures created so far (first creation and every resize). Tests.</summary>
    internal int TextureCreations { get; private set; }

    /// <summary>Post-process GPU objects this instance currently owns (texture, sampler, pipelines).</summary>
    internal int LiveResources => _liveResources;

    /// <summary>True once the pipeline of <paramref name="effect"/> exists.</summary>
    internal bool HasPipeline(GpuDisplayEffect effect) => _pipelines[(int)effect] != IntPtr.Zero;

    /// <summary>The last creation failure (logged once each), or null.</summary>
    internal string? UnavailableReason => _unavailableReason;

    /// <summary>Makes the next preparation fail as an infrastructure failure. Tests only.</summary>
    internal bool SimulateFailureForTesting { get; set; }

    /// <summary>
    /// Outside any pass: makes sure the sampler, an offscreen texture of
    /// <paramref name="width"/> x <paramref name="height"/> and the pipeline of
    /// <paramref name="effect"/> exist. Returns the pipeline, or zero when the effect cannot be drawn
    /// (the caller then renders Default).
    /// </summary>
    internal IntPtr Prepare(GpuDisplayEffect effect, GpuPostEffectSpec spec, int width, int height)
    {
        if (_disposed || _infrastructureFailed || width <= 0 || height <= 0) return IntPtr.Zero;
        int slot = (int)effect;
        if (_failedEffects[slot]) return IntPtr.Zero;

        // The effect's own pipeline first: when it cannot be built, nothing else is allocated.
        if (_pipelines[slot] == IntPtr.Zero && !SimulateFailureForTesting)
        {
            try
            {
                _pipelines[slot] = CreatePipeline(spec);
                _liveResources++;
                Interlocked.Increment(ref s_liveResources);
            }
            catch (Exception ex)
            {
                _failedEffects[slot] = true;
                _unavailableReason = ex.Message;
                Console.Error.WriteLine(
                    $"[SDLGpu] {GpuDisplayEffects.Id(effect)} display effect unavailable ({ex.Message}); rendering Default.");
                return IntPtr.Zero;
            }
        }

        // Shared resources: the sampler once, the offscreen texture per viewport size.
        try
        {
            if (SimulateFailureForTesting)
                throw new InvalidOperationException("post-process resources disabled for testing");
            if (_sampler == IntPtr.Zero) CreateSampler();
            if (_texture == IntPtr.Zero || width != _textureWidth || height != _textureHeight)
                CreateTexture(width, height);
        }
        catch (Exception ex)
        {
            _infrastructureFailed = true;
            _unavailableReason = ex.Message;
            Console.Error.WriteLine(
                $"[SDLGpu] Post-process display effects unavailable ({ex.Message}); rendering Default.");
            ReleaseAll();
            return IntPtr.Zero;
        }
        return _pipelines[slot];
    }

    private void CreateSampler()
    {
        var info = new SDL3.SDL.GPUSamplerCreateInfo
        {
            MinFilter    = SDL3.SDL.GPUFilter.Linear,
            MagFilter    = SDL3.SDL.GPUFilter.Linear,
            MipmapMode   = SDL3.SDL.GPUSamplerMipmapMode.Nearest,
            AddressModeU = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
            AddressModeV = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
            AddressModeW = SDL3.SDL.GPUSamplerAddressMode.ClampToEdge,
        };
        _sampler = SDL3.SDL.CreateGPUSampler(_device, in info);
        if (_sampler == IntPtr.Zero)
            throw new InvalidOperationException($"CreateGPUSampler (post) failed: {SDL3.SDL.GetError()}");
        _liveResources++;
        Interlocked.Increment(ref s_liveResources);
    }

    private void CreateTexture(int width, int height)
    {
        // A replaced texture may still be read by an in-flight command buffer; SDL defers the release.
        ReleaseTexture();
        var info = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type              = SDL3.SDL.GPUTextureType.TextureType2D,
            Format            = _format,
            Usage             = SDL3.SDL.GPUTextureUsageFlags.ColorTarget | SDL3.SDL.GPUTextureUsageFlags.Sampler,
            Width             = (uint)width,
            Height            = (uint)height,
            LayerCountOrDepth = 1,
            NumLevels         = 1,
            SampleCount       = SDL3.SDL.GPUSampleCount.SampleCount1,
        };
        _texture = SDL3.SDL.CreateGPUTexture(_device, in info);
        if (_texture == IntPtr.Zero)
            throw new InvalidOperationException($"CreateGPUTexture (post {width}x{height}) failed: {SDL3.SDL.GetError()}");
        _textureWidth  = width;
        _textureHeight = height;
        TextureCreations++;
        _liveResources++;
        Interlocked.Increment(ref s_liveResources);
    }

    private IntPtr CreatePipeline(GpuPostEffectSpec spec)
    {
        IntPtr vs = IntPtr.Zero, ps = IntPtr.Zero;
        try
        {
            vs = _loadShader(VertexShaderResource, SDL3.ShaderCross.ShaderStage.Vertex, 0, 0);
            ps = _loadShader(spec.FragmentShaderResource, SDL3.ShaderCross.ShaderStage.Fragment, spec.NumSamplers, 1);

            var colorTarget = new SDL3.SDL.GPUColorTargetDescription
            {
                Format     = _format,
                BlendState = default,   // opaque: the effect writes the final pixel
            };
            var info = new SDL3.SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader     = vs,
                FragmentShader   = ps,
                PrimitiveType    = SDL3.SDL.GPUPrimitiveType.TriangleList,
                VertexInputState = default,  // SV_VertexID only: no buffers, no attributes
                TargetInfo = new SDL3.SDL.GPUGraphicsPipelineTargetInfo
                {
                    ColorTargetDescriptions = (IntPtr)(&colorTarget),
                    NumColorTargets         = 1,
                },
            };
            IntPtr p = SDL3.SDL.CreateGPUGraphicsPipeline(_device, in info);
            if (p == IntPtr.Zero)
                throw new InvalidOperationException($"CreateGPUGraphicsPipeline (post) failed: {SDL3.SDL.GetError()}");
            return p;
        }
        finally
        {
            if (vs != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, vs);
            if (ps != IntPtr.Zero) SDL3.SDL.ReleaseGPUShader(_device, ps);
        }
    }

    private void ReleaseTexture()
    {
        if (_texture == IntPtr.Zero) return;
        SDL3.SDL.ReleaseGPUTexture(_device, _texture);
        _texture = IntPtr.Zero;
        _textureWidth = _textureHeight = 0;
        _liveResources--;
        Interlocked.Decrement(ref s_liveResources);
    }

    private void ReleaseAll()
    {
        ReleaseTexture();
        if (_sampler != IntPtr.Zero)
        {
            SDL3.SDL.ReleaseGPUSampler(_device, _sampler);
            _sampler = IntPtr.Zero;
            _liveResources--;
            Interlocked.Decrement(ref s_liveResources);
        }
        for (int i = 0; i < _pipelines.Length; i++)
        {
            if (_pipelines[i] == IntPtr.Zero) continue;
            SDL3.SDL.ReleaseGPUGraphicsPipeline(_device, _pipelines[i]);
            _pipelines[i] = IntPtr.Zero;
            _liveResources--;
            Interlocked.Decrement(ref s_liveResources);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAll();
    }
}
