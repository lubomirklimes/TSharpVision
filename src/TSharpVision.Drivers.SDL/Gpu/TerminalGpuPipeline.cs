using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Gpu;

// Two-pass GPU renderer for the terminal grid.
//   Pass 1 (background): one opaque quad per cell, vertex color = bg VGA color.
//   Pass 2 (glyph):      one blended quad per non-space cell, UV into R8 atlas,
//                         vertex color = fg VGA color; GPU blends via alpha.
//
// Vertex data is rebuilt from the ScreenBuffer every frame and uploaded via
// staging (transfer) buffers. The glyph atlas is uploaded only when new
// codepoints are added.
internal sealed unsafe class TerminalGpuPipeline : IDisposable
{
    // Current vertex buffer capacity in cells. Grows on demand (EnsureCapacity).
    // Initial value covers typical terminal sizes; 4K at 10×18 = 384×120 = 46 080.
    private int _maxCells = 256 * 100;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BgVertex    // 12 bytes
    {
        public float X, Y;
        public byte  R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct GlyphVertex  // 20 bytes
    {
        public float X, Y;
        public float U, V;
        public byte  R, G, B, A;
    }

    private readonly IntPtr _device;
    private readonly int    _cellWidth;
    private readonly int    _cellHeight;
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

    private bool _disposed;
    private bool _shaderCrossInitialized;

    // ─── Shadercross native dependency loader ────────────────────────────────

    // .NET 5+ uses SetDefaultDllDirectories which removes the loaded DLL's own
    // directory from the Windows DLL search. SDL3_shadercross.dll ships companion
    // DLLs (spirv-cross-c-shared, dxcompiler, dxil) in the same NuGet native dir —
    // they won't be found unless we act first. Three layers so at least one works:
    //   1. NativeLibrary.TryLoad with explicit full paths for companion DLLs.
    //      Windows reuses already-loaded modules for import resolution, bypassing search.
    //   2. AddDllDirectory on the native dir (backup for indirect loads).
    //   3. SetDllImportResolver on the ShaderCross assembly so SDL3_shadercross itself
    //      loads from the exact computed path, not via probing.

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string lpPathName);

    private static string? _shadercrossNativeDir;

    private static void PreloadShadercrossCompanions()
    {
        if (!OperatingSystem.IsWindows()) return;

        string nugetHome = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");

        string rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X86   => "win-x86",
            _                  => "win-x64",
        };

        string pkgDir = Path.Combine(nugetHome, "sdl3-cs.windows.shadercross");
        if (!Directory.Exists(pkgDir))
        {
            return;
        }

        string? verDir = Directory.GetDirectories(pkgDir)
            .OrderByDescending(static d => d).FirstOrDefault();
        if (verDir is null) return;

        string nativeDir = Path.Combine(verDir, "runtimes", rid, "native");
        if (!Directory.Exists(nativeDir))
        {
            return;
        }

        _shadercrossNativeDir = nativeDir;

        // Layer 1: pre-load companions into the process so Windows finds them in module list
        foreach (string dep in new[] { "spirv-cross-c-shared", "dxcompiler", "dxil" })
        {
            string path = Path.Combine(nativeDir, dep + ".dll");
            if (File.Exists(path))
            {
                NativeLibrary.TryLoad(path, out _);
            }
        }

        // Layer 2: add dir to Windows DLL search path
        AddDllDirectory(nativeDir);

        // Layer 3: resolve SDL3_shadercross itself from the exact computed path
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SDL3.ShaderCross).Assembly,
                (name, _, _) =>
                {
                    if (name == "SDL3_shadercross" && _shadercrossNativeDir is { } dir)
                    {
                        string fullPath = Path.Combine(dir, "SDL3_shadercross.dll");
                        if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out IntPtr h))
                            return h;
                    }
                    return IntPtr.Zero;
                });
        }
        catch (InvalidOperationException)
        {
            // Resolver already registered for this assembly (retry after earlier failure)
        }
    }

    internal TerminalGpuPipeline(
        IntPtr device,
        IntPtr window,
        int    cellWidth,
        int    cellHeight,
        IGpuGlyphSource glyphSource)
    {
        _device      = device;
        _cellWidth   = cellWidth;
        _cellHeight  = cellHeight;
        _glyphSource = glyphSource;

        _maxBgBytes    = (_maxCells + 1) * 6 * sizeof(BgVertex);
        _maxGlyphBytes = _maxCells       * 6 * sizeof(GlyphVertex);

        PreloadShadercrossCompanions();
        _shaderCrossInitialized = SDL3.ShaderCross.Init();
        if (!_shaderCrossInitialized)
            throw new InvalidOperationException(
                $"[SDLGpu] SDL_ShaderCross_Init failed: {SDL3.SDL.GetError()}");

        SDL3.SDL.GPUTextureFormat swapFmt =
            SDL3.SDL.GetGPUSwapchainTextureFormat(device, window);

        IntPtr bgVs    = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgVert.spv",    SDL3.ShaderCross.ShaderStage.Vertex);
        IntPtr bgPs    = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.BgFrag.spv",    SDL3.ShaderCross.ShaderStage.Fragment);
        IntPtr glyphVs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GlyphVert.spv", SDL3.ShaderCross.ShaderStage.Vertex);
        IntPtr glyphPs = LoadShaderFromSpirv("TSharpVision.Drivers.SDL.Gpu.Shaders.GlyphFrag.spv", SDL3.ShaderCross.ShaderStage.Fragment);

        _bgPipeline    = CreateBgPipeline(bgVs, bgPs, swapFmt);
        _glyphPipeline = CreateGlyphPipeline(glyphVs, glyphPs, swapFmt);

        SDL3.SDL.ReleaseGPUShader(device, bgVs);
        SDL3.SDL.ReleaseGPUShader(device, bgPs);
        SDL3.SDL.ReleaseGPUShader(device, glyphVs);
        SDL3.SDL.ReleaseGPUShader(device, glyphPs);

        _atlas = new TerminalAtlas(device, cellWidth, cellHeight);

        CreateVertexBuffers();
    }

    // ─── Shader loading ──────────────────────────────────────────────────────

    private IntPtr LoadShaderFromSpirv(string resourceName, SDL3.ShaderCross.ShaderStage stage)
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
                var resourceInfo = new SDL3.ShaderCross.GraphicsShaderResourceInfo();
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

    private IntPtr CreateBgPipeline(IntPtr vs, IntPtr ps, SDL3.SDL.GPUTextureFormat swapFmt)
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
            BlendState = default,  // no blending; bg is opaque
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
                    $"[SDLGpu] CreateGPUGraphicsPipeline (bg) failed: {SDL3.SDL.GetError()}");
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
        var colorTarget = new SDL3.SDL.GPUColorTargetDescription
        {
            Format     = swapFmt,
            BlendState = blendState,
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

    // ─── Per-frame rendering ─────────────────────────────────────────────────

    // Renders one terminal frame into an already-acquired swapchain texture.
    // cmdBuf must be open (acquired but not yet submitted by the caller).
    // Returns (vertexBuild, gpuRecord) timings for diagnostics.
    /// <summary>Glyph slots the atlas can hold at the current cell size. Diagnostics only.</summary>
    internal int AtlasCapacity => _atlas.Capacity;

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
        ushort       cursorType)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // The grid spans the full NDC range; the render pass constrains that range to an exact
        // grid-sized viewport so nothing is fractionally scaled.
        GpuGridLayout layout = GpuGridLayout.Compute(
            (int)swapW, (int)swapH, (int)cols, (int)rows, _cellWidth, _cellHeight);

        float invW = 2f / (cols * _cellWidth);
        float invH = 2f / (rows * _cellHeight);

        // Map staging buffers
        BgVertex*    bgPtr    = (BgVertex*)   SDL3.SDL.MapGPUTransferBuffer(_device, _bgTransferBuf,    true);
        GlyphVertex* glyphPtr = (GlyphVertex*)SDL3.SDL.MapGPUTransferBuffer(_device, _glyphTransferBuf, true);
        if (bgPtr == null || glyphPtr == null)
        {
            if (bgPtr    != null) SDL3.SDL.UnmapGPUTransferBuffer(_device, _bgTransferBuf);
            if (glyphPtr != null) SDL3.SDL.UnmapGPUTransferBuffer(_device, _glyphTransferBuf);
            return (TimeSpan.Zero, TimeSpan.Zero);
        }

        int bgCount    = 0;
        int glyphCount = 0;

        for (uint row = 0; row < rows; row++)
        {
            for (uint col = 0; col < cols; col++)
            {
                TScreenChar cell = buf.GetChar(col, row);
                byte attr = (byte)(cell.Attr & 0xFF);
                (byte fgR, byte fgG, byte fgB) = ArgbToRgb(Vga16[attr & 0x0F]);
                (byte bgR, byte bgG, byte bgB) = ArgbToRgb(Vga16[(attr >> 4) & 0x0F]);

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
                float cx0 = col  * _cellWidth  * invW - 1f;
                float cy0 = 1f - row  * _cellHeight * invH;
                float cx1 = cx0 + _cellWidth  * invW;
                float cy1 = cy0 - _cellHeight * invH;

                EmitQuad(bgPtr, ref bgCount, cx0, cy0, cx1, cy1, cellBgR, cellBgG, cellBgB);

                if (underlineCursor)
                {
                    // 2-pixel tall underline bar inside the cell (bottom 2 rows)
                    float barY0 = cy0 - (_cellHeight - 2) * (invH);
                    EmitQuad(bgPtr, ref bgCount, cx0, barY0, cx1, cy1, cellFgR, cellFgG, cellFgB);
                }

                char ch = cell.Character;
                if (ch is not (' ' or '\0'))
                {
                    // PhasedDither needs the cell's grid position. The shared helper is the
                    // single phase formula in the codebase; CreateKey then normalises it away for
                    // every glyph that cannot depend on it, so only B0/B1/B2 gain extra slots.
                    GlyphPhase phase =
                        GlyphPhase.ForCell((int)col, (int)row, _cellWidth, _cellHeight);
                    GpuGlyphKey glyphKey = _glyphSource.CreateKey(ch, phase);

                    var (u0, v0, u1, v1) = _atlas.GetOrAllocateSlot(glyphKey, _glyphSource.GetAlpha);
                    if (u0 != u1)  // valid atlas slot
                        EmitGlyphQuad(glyphPtr, ref glyphCount, cx0, cy0, cx1, cy1,
                            u0, v0, u1, v1, cellFgR, cellFgG, cellFgB);
                }
            }
        }

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

        SDL3.SDL.EndGPUCopyPass(copyPass);

        // Render pass: clear → bg pass → glyph pass
        SDL3.SDL.GPUColorTargetInfo[] colorTargets =
        {
            new()
            {
                Texture           = swapTex,
                MipLevel          = 0,
                LayerOrDepthPlane = 0,
                ClearColor        = new SDL3.SDL.FColor { R = 0f, G = 0f, B = 0f, A = 1f },
                LoadOp            = SDL3.SDL.GPULoadOp.Clear,
                StoreOp           = SDL3.SDL.GPUStoreOp.Store,
                Cycle             = false,
            },
        };

        IntPtr renderPass = SDL3.SDL.BeginGPURenderPass(cmdBuf, colorTargets, 1u, IntPtr.Zero);

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
        SDL3.SDL.BindGPUGraphicsPipeline(renderPass, _bgPipeline);
        SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u,
            new SDL3.SDL.GPUBufferBinding[] { new() { Buffer = _bgVertexBuf, Offset = 0 } }, 1u);
        SDL3.SDL.DrawGPUPrimitives(renderPass, (uint)bgCount, 1u, 0u, 0u);

        // Glyph pass
        if (glyphCount > 0)
        {
            SDL3.SDL.BindGPUGraphicsPipeline(renderPass, _glyphPipeline);
            SDL3.SDL.BindGPUVertexBuffers(renderPass, 0u,
                new SDL3.SDL.GPUBufferBinding[] { new() { Buffer = _glyphVertexBuf, Offset = 0 } }, 1u);
            SDL3.SDL.BindGPUFragmentSamplers(renderPass, 0u,
                new SDL3.SDL.GPUTextureSamplerBinding[]
                {
                    new() { Texture = _atlas.Texture, Sampler = _atlas.Sampler },
                }, 1u);
            SDL3.SDL.DrawGPUPrimitives(renderPass, (uint)glyphCount, 1u, 0u, 0u);
        }

        SDL3.SDL.EndGPURenderPass(renderPass);

        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();  // end of GPU command recording
        return (
            System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1),
            System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2));
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

    // ─── VGA palette ────────────────────────────────────────────────────────

    private static readonly uint[] Vga16 =
    {
        0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
        0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF,
        0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (byte r, byte g, byte b) ArgbToRgb(uint argb) =>
        ((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    // ─── Disposal ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _atlas.Dispose();

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
