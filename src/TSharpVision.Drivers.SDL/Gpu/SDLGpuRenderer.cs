using System.Diagnostics;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// SDL_GPU renderer for TSharpVision.
///
/// Rendering strategy per frame:
///   1. Composite entire screen buffer into a CPU-side RGBA8 pixel buffer
///      (background fill + SDL_ttf glyph alpha-blend).
///   2. Upload that buffer to a persistent GPU "screen texture" via a
///      transfer buffer + copy pass.
///   3. Acquire the swapchain texture (<c>WaitAndAcquireGPUSwapchainTexture</c>
///      — the key latency measurement point for macOS investigation).
///   4. Blit the screen texture to the swapchain with <c>BlitGPUTexture</c>
///      (format-converting blit; no custom shaders required).
///   5. Submit the command buffer.
/// </summary>
internal sealed class SDLGpuRenderer : IRenderer, IDisposable, IGpuGlyphSource
{
    private const int DefaultFontPtSize   = 20;
    private const int MaxGlyphCacheSize   = 4096;

    private readonly IntPtr  _window;
    private readonly SDLGpuOptions _options;
    private readonly SDLGpuPerformanceDiagnostics _diag;

    // Font params (passed in from SDLGpuDriver / ScreenDriverFactory)
    private readonly string? _fontName;
    private readonly int?    _fontSize;

    // Font / cell metrics (populated in Initialize)
    private IntPtr _font       = IntPtr.Zero;
    private IntPtr _symbolFont = IntPtr.Zero;
    private int    _cellWidth  = 8;
    private int    _cellHeight = 16;
    private int    _ascent;

    public int CellWidth  => _cellWidth;
    public int CellHeight => _cellHeight;

    // GPU device
    private IntPtr _device      = IntPtr.Zero;
    private bool   _windowClaimed;
    private bool   _disposed;
    private string _backend     = "(unknown)";

    public string Backend => _backend;

    // Persistent GPU "screen texture" + upload transfer buffer; re-created on resize.
    private IntPtr _screenTex   = IntPtr.Zero;
    private IntPtr _transferBuf = IntPtr.Zero;
    private uint   _screenTexW;
    private uint   _screenTexH;

    // CPU pixel buffer for screen compositing (RGBA8: R=byte 0, G=1, B=2, A=3).
    private byte[] _cpuPixels = Array.Empty<byte>();

    // Cursor state
    private int    _cursorX;
    private int    _cursorY;
    private ushort _cursorType;

    // Glyph alpha cache: GpuGlyphKey → alpha-only byte[cellW×cellH] (or null).
    // Colour is excluded from the key — it is applied by the shader — so one cached shape serves
    // all 16 VGA colours.
    private readonly Dictionary<GpuGlyphKey, byte[]?> _glyphCache = new();

    // GPU shader pipeline (shader-based, replaces CPU compositing when available).
    private TerminalGpuPipeline? _termPipeline;
    private bool _ttfInitialized;

    // Cell-sized CP437 B0-DF bitmaps, built in LoadFont. Same generator and shading mode as
    // SDLRenderer, so both back-ends draw identical bitmaps.
    private CellGlyphBitmapCache _generatedGlyphs = null!;

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    internal SDLGpuRenderer(IntPtr window, SDLGpuOptions options)
        : this(window, options, null, null, new SDLGpuPerformanceDiagnostics(options)) { }

    internal SDLGpuRenderer(IntPtr window, SDLGpuOptions options, string? fontName, int? fontSize)
        : this(window, options, fontName, fontSize, new SDLGpuPerformanceDiagnostics(options)) { }

    // Used by unit tests with injectable diagnostics.
    internal SDLGpuRenderer(IntPtr window, SDLGpuOptions options, SDLGpuPerformanceDiagnostics diag)
        : this(window, options, null, null, diag) { }

    private SDLGpuRenderer(
        IntPtr window, SDLGpuOptions options,
        string? fontName, int? fontSize,
        SDLGpuPerformanceDiagnostics diag)
    {
        _window   = window;
        _options  = options;
        _fontName = fontName;
        _fontSize = fontSize;
        _diag     = diag;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Font probing / metrics
    // ─────────────────────────────────────────────────────────────────────────
    // Font path lookup lives in SdlFontLocator and cell metrics in SdlFontMetrics, shared with
    // SDLRenderer and the glyph-diagnostic tool.

    private void TryAddSymbolFallback(int ptSize)
    {
        string? symbolPath = SdlFontLocator.ProbeSymbolFontPath();
        if (symbolPath == null) return;

        _symbolFont = SDL3.TTF.OpenFont(symbolPath, ptSize);
        if (_symbolFont == IntPtr.Zero) return;

        SDL3.TTF.AddFallbackFont(_font, _symbolFont);
        if (_options.DiagnosticsEnabled)
            Console.Error.WriteLine($"[SDLGpu] symbolFallback='{symbolPath}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Initialize / diagnostics
    // ─────────────────────────────────────────────────────────────────────────

    private const SDL3.SDL.GPUShaderFormat AllShaderFormats =
        SDL3.SDL.GPUShaderFormat.Private  |
        SDL3.SDL.GPUShaderFormat.SPIRV    |
        SDL3.SDL.GPUShaderFormat.DXBC     |
        SDL3.SDL.GPUShaderFormat.DXIL     |
        SDL3.SDL.GPUShaderFormat.MSL      |
        SDL3.SDL.GPUShaderFormat.MetalLib;

    public void Initialize()
    {
        // GPU device
        string? backendName = _options.Backend switch
        {
            "metal"      => "metal",
            "vulkan"     => "vulkan",
            "direct3d12" => "direct3d12",
            "d3d12"      => "direct3d12",
            { } other    => other,
            null         => null,
        };

        _device = SDL3.SDL.CreateGPUDevice(AllShaderFormats, debugMode: false, backendName);
        if (_device == IntPtr.Zero)
            throw new InvalidOperationException(
                $"[SDLGpu] SDL_CreateGPUDevice failed: {SDL3.SDL.GetError()}");

        _backend = SDL3.SDL.GetGPUDeviceDriver(_device) ?? "(unknown)";

        if (_options.AllowedFramesInFlight is uint frames)
        {
            if (!SDL3.SDL.SetGPUAllowedFramesInFlight(_device, frames))
                Console.Error.WriteLine(
                    $"[SDLGpu] SetGPUAllowedFramesInFlight({frames}) failed: {SDL3.SDL.GetError()}");
        }

        if (!SDL3.SDL.ClaimWindowForGPUDevice(_device, _window))
            throw new InvalidOperationException(
                $"[SDLGpu] SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL.GetError()}");
        _windowClaimed = true;

        if (_options.PresentMode is SDL3.SDL.GPUPresentMode presentMode)
            ApplyPresentMode(presentMode);

        // Font
        LoadFont();

        // GPU shader pipeline (optional; falls back to CPU compositing on failure)
        TryCreateGpuPipeline();
    }

    private void TryCreateGpuPipeline()
    {
        if (_font == IntPtr.Zero) return;
        try
        {
            _termPipeline = new TerminalGpuPipeline(
                _device, _window, _cellWidth, _cellHeight,
                glyphSource: this);
            if (_options.DiagnosticsEnabled)
                Console.Error.WriteLine("[SDLGpu] GPU pipeline active (shader-based rendering).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[SDLGpu] GPU pipeline unavailable ({ex.Message}); using CPU compositing.");
            _termPipeline = null;
        }
    }

    private void LoadFont()
    {
        try
        {
            if (!SDL3.TTF.Init())
            {
                throw new InvalidOperationException($"SDL_ttf init failed: {SDL3.SDL.GetError()}");
            }

            _ttfInitialized = true;
            string? fontPath = null;
            if (!string.IsNullOrWhiteSpace(_fontName))
            {
                fontPath = SdlFontLocator.ProbeFontPathByName(_fontName);
                if (fontPath == null)
                    Console.Error.WriteLine(
                        $"[SDLGpu] Font '{_fontName}' not found; falling back to default.");
            }
            fontPath ??= SdlFontLocator.ProbeFontPath();

            if (fontPath == null)
            {
                throw new InvalidOperationException("No suitable monospace font found.");
            }

            int ptSize = _fontSize ?? DefaultFontPtSize;
            _font = SDL3.TTF.OpenFont(fontPath, ptSize);
            if (_font == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"[SDLGpu] Could not open font '{fontPath}': {SDL3.SDL.GetError()}");
            }

            SdlFontMetrics.ComputeMetrics(_font, out _cellWidth, out _cellHeight);
            _ascent = SDL3.TTF.GetFontAscent(_font);

            if (_options.DiagnosticsEnabled)
                Console.Error.WriteLine(
                    $"[SDLGpu] font='{fontPath}' ptSize={ptSize} " +
                    $"cell={_cellWidth}x{_cellHeight} ascent={_ascent}");

            TryAddSymbolFallback(ptSize);

            _generatedGlyphs = new CellGlyphBitmapCache(
                new Cp437GlyphGenerator(_cellWidth, _cellHeight));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[SDLGpu] Font load failed: {ex.Message}", ex);
        }
    }

    private void ApplyPresentMode(SDL3.SDL.GPUPresentMode presentMode)
    {
        if (!SDL3.SDL.WindowSupportsGPUPresentMode(_device, _window, presentMode))
        {
            Console.Error.WriteLine(
                $"[SDLGpu] Present mode '{presentMode}' not supported; using default.");
            return;
        }

        bool ok = SDL3.SDL.SetGPUSwapchainParameters(
            _device, _window, SDL3.SDL.GPUSwapchainComposition.SDR, presentMode);

        if (!ok)
            Console.Error.WriteLine(
                $"[SDLGpu] SetGPUSwapchainParameters failed: {SDL3.SDL.GetError()}");
        else if (_options.DiagnosticsEnabled)
            Console.Error.WriteLine($"[SDLGpu] presentMode={presentMode}");
    }

    public void LogDiagnostics(IntPtr window)
    {
        if (!_options.DiagnosticsEnabled) return;

        Console.Error.WriteLine(
            $"[SDLGpu] backend={_backend} " +
            $"videoDriver={SDL3.SDL.GetCurrentVideoDriver() ?? "(unavailable)"} " +
            $"platform={RuntimeInformation.OSDescription} " +
            $"arch={RuntimeInformation.ProcessArchitecture}");

        SDL3.SDL.GetWindowSize(window, out int logW, out int logH);
        SDL3.SDL.GetWindowSizeInPixels(window, out int pixW, out int pixH);
        Console.Error.WriteLine(
            $"[SDLGpu] window logical={logW}x{logH} pixels={pixW}x{pixH} " +
            $"density={SDL3.SDL.GetWindowPixelDensity(window):F2} " +
            $"scale={SDL3.SDL.GetWindowDisplayScale(window):F2}");

        Console.Error.WriteLine(
            $"[SDLGpu] mode={(_options.Continuous ? "continuous" : "on-demand")} " +
            $"allowedFrames={_options.AllowedFramesInFlight?.ToString() ?? "(default)"} " +
            $"presentMode={_options.PresentMode?.ToString() ?? "(default)"}");

        Console.Error.WriteLine(
            $"[SDLGpu] swapchainFormat={SDL3.SDL.GetGPUSwapchainTextureFormat(_device, window)}");

        if (_termPipeline != null)
            Console.Error.WriteLine(
                $"[SDLGpu] atlas capacity={_termPipeline.AtlasCapacity} slots at {_cellWidth}x{_cellHeight}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cursor
    // ─────────────────────────────────────────────────────────────────────────

    public void SetCursor(int x, int y, ushort cursorType)
    {
        _cursorX    = x;
        _cursorY    = y;
        _cursorType = cursorType;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Render thread — compositing and GPU work on a background thread
    // ─────────────────────────────────────────────────────────────────────────

    // Render() copies a snapshot of the ScreenBuffer, signals the render thread,
    // and returns immediately so the driver's event loop is never blocked by CPU
    // compositing or GPU submit latency. The render thread also owns all SDL_TTF
    // glyph-rendering calls, which must come from a single thread.

    private Thread?                          _renderThread;
    private readonly SemaphoreSlim           _frameReady = new(0, 1);
    private readonly CancellationTokenSource _renderCts  = new();

    // Snapshot — written by main thread, copied by render thread (both under _snapLock).
    private readonly object _snapLock = new();
    private ScreenBuffer?   _snap;
    private uint            _snapCols;
    private uint            _snapRows;

    // Private render-thread buffer — populated from snapshot; main thread never touches it.
    private ScreenBuffer? _privateBuf;
    private uint          _privateCols;
    private uint          _privateRows;

    // Timestamp (Stopwatch ticks) written by SubmitFrame, read by RenderLoop to measure
    // render-thread wake latency (OS scheduling delay between signal and actual wakeup).
    private long _submitTimestamp;

    internal void StartRenderThread()
    {
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name         = "SDLGpu.Render",
        };
        _renderThread.Start();
    }

    private void SubmitFrame(ScreenBuffer screenBuffer, uint cols, uint rows)
    {
        lock (_snapLock)
        {
            if (_snap == null || _snapCols != cols || _snapRows != rows)
                _snap = new ScreenBuffer((ushort)cols, (ushort)rows);
            _snapCols = cols;
            _snapRows = rows;
            for (uint row = 0; row < rows; row++)
                for (uint col = 0; col < cols; col++)
                    _snap.SetChar(col, row, screenBuffer.GetChar(col, row));
        }
        if (_frameReady.CurrentCount == 0)
        {
            Interlocked.Exchange(ref _submitTimestamp, Stopwatch.GetTimestamp());
            _frameReady.Release();
        }
    }

    private void RenderLoop()
    {
        while (true)
        {
            try   { _frameReady.Wait(_renderCts.Token); }
            catch (OperationCanceledException) { break; }

            if (_options.DiagnosticsEnabled)
            {
                long wakeTs   = Stopwatch.GetTimestamp();
                long submitTs = Interlocked.Read(ref _submitTimestamp);
                if (submitTs != 0)
                    _diag.RecordWakeLatency(Stopwatch.GetElapsedTime(submitTs, wakeTs));
            }

            uint cols, rows;
            lock (_snapLock)
            {
                cols = _snapCols;
                rows = _snapRows;
                if (_privateBuf == null || _privateCols != cols || _privateRows != rows)
                {
                    _privateBuf  = new ScreenBuffer((ushort)cols, (ushort)rows);
                    _privateCols = cols;
                    _privateRows = rows;
                }
                for (uint row = 0; row < rows; row++)
                    for (uint col = 0; col < cols; col++)
                        _privateBuf.SetChar(col, row, _snap!.GetChar(col, row));
            }
            RenderSync(_privateBuf!, 0, 0, cols, rows);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IRenderer — non-blocking submit; actual work runs on the render thread
    // ─────────────────────────────────────────────────────────────────────────

    public void Render(
        ScreenBuffer screenBuffer,
        uint regionX, uint regionY, uint regionWidth, uint regionHeight)
    {
        SubmitFrame(screenBuffer, regionWidth, regionHeight);
    }

    private void RenderSync(
        ScreenBuffer screenBuffer,
        uint regionX, uint regionY, uint regionWidth, uint regionHeight)
    {
        if (_device == IntPtr.Zero) return;

        var frameSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;

        if (_termPipeline != null)
        {
            // Grow vertex buffers before acquiring a command buffer (requires WaitForGPUIdle).
            _termPipeline.EnsureCapacity(regionWidth, regionHeight);

            // GPU pipeline path: no CPU compositing, vertex data built on GPU.
            IntPtr cmdBuf = SDL3.SDL.AcquireGPUCommandBuffer(_device);
            if (cmdBuf == IntPtr.Zero)
            {
                Console.Error.WriteLine($"[SDLGpu] AcquireGPUCommandBuffer failed: {SDL3.SDL.GetError()}");
                return;
            }

            var swapSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
            bool acquired = SDL3.SDL.WaitAndAcquireGPUSwapchainTexture(
                cmdBuf, _window, out IntPtr swapTex, out uint swapW, out uint swapH);
            swapSw?.Stop();

            if (!acquired || swapTex == IntPtr.Zero)
            {
                SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf);
                _diag.RecordSwapchainSkipped();
                return;
            }
            if (swapSw != null) _diag.RecordAcquireSwapchain(swapSw.Elapsed);

            GpuGridLayout shaderLayout = GpuGridLayout.Compute(
                (int)swapW, (int)swapH, (int)regionWidth, (int)regionHeight,
                _cellWidth, _cellHeight);
            LogGridScalingIfChanged(shaderLayout, regionWidth, regionHeight, "shader");

            var (vertexBuild, gpuRecord) = _termPipeline.RenderFrame(
                cmdBuf, swapTex, swapW, swapH,
                screenBuffer, regionWidth, regionHeight,
                _cursorX, _cursorY, _cursorType);
            if (_options.DiagnosticsEnabled)
            {
                _diag.RecordVertexBuild(vertexBuild);
                _diag.RecordRenderPass(gpuRecord);
            }

            var submitSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
            SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf);
            submitSw?.Stop();
            if (submitSw != null) _diag.RecordSubmit(submitSw.Elapsed);

            frameSw?.Stop();
            if (frameSw != null) _diag.RecordFrame(frameSw.Elapsed);
            _diag.MaybeReportSummary();
            return;
        }

        // CPU compositing fallback (original path).
        uint screenW = regionWidth  * (uint)_cellWidth;
        uint screenH = regionHeight * (uint)_cellHeight;
        EnsureScreenTexture(screenW, screenH);
        if (_screenTex == IntPtr.Zero) return;

        // Step 1: composite full screen into CPU pixel buffer.
        var compositeSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        CompositeScreen(screenBuffer, regionX, regionY, regionWidth, regionHeight, screenW);
        compositeSw?.Stop();
        if (compositeSw != null) _diag.RecordComposite(compositeSw.Elapsed);

        // Step 2: acquire GPU command buffer + swapchain texture.
        IntPtr cmdBuf2 = SDL3.SDL.AcquireGPUCommandBuffer(_device);
        if (cmdBuf2 == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[SDLGpu] AcquireGPUCommandBuffer failed: {SDL3.SDL.GetError()}");
            return;
        }

        var swapSw2 = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        bool acquired2 = SDL3.SDL.WaitAndAcquireGPUSwapchainTexture(
            cmdBuf2, _window, out IntPtr swapTex2, out uint swapW2, out uint swapH2);
        swapSw2?.Stop();

        if (!acquired2 || swapTex2 == IntPtr.Zero)
        {
            // GPU error or window minimized — no swapchain image to render into.
            SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf2);
            _diag.RecordSwapchainSkipped();
            return;
        }

        if (swapSw2 != null) _diag.RecordAcquireSwapchain(swapSw2.Elapsed);

        // Step 3: upload CPU pixels → screen texture; clear the target; blit 1:1 → swapchain.
        var passSw2 = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        UploadScreenTexture(cmdBuf2, screenW, screenH);

        GpuGridLayout fallbackLayout = GpuGridLayout.Compute(
            (int)swapW2, (int)swapH2, (int)regionWidth, (int)regionHeight, _cellWidth, _cellHeight);
        LogGridScalingIfChanged(fallbackLayout, regionWidth, regionHeight, "cpu-fallback");

        ClearSwapchain(cmdBuf2, swapTex2);
        BlitToSwapchain(cmdBuf2, swapTex2, in fallbackLayout);
        passSw2?.Stop();
        if (passSw2 != null) _diag.RecordRenderPass(passSw2.Elapsed);

        // Step 4: submit.
        var submitSw2 = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf2);
        submitSw2?.Stop();
        if (submitSw2 != null) _diag.RecordSubmit(submitSw2.Elapsed);

        frameSw?.Stop();
        if (frameSw != null) _diag.RecordFrame(frameSw.Elapsed);
        _diag.MaybeReportSummary();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Grid / swapchain scaling diagnostics  (reporting only; the fix itself is the viewport)
    // ─────────────────────────────────────────────────────────────────────────

    private uint   _lastLoggedSwapW;
    private uint   _lastLoggedSwapH;
    private uint   _lastLoggedCols;
    private uint   _lastLoggedRows;
    private string _lastLoggedPath = string.Empty;

    /// <summary>
    /// Reports how the logical cell grid maps onto the swapchain, whenever either changes.
    /// <para>
    /// The grid is confined to an exact grid-sized viewport and leftover pixels are letterboxed,
    /// so a non-zero remainder is normal at any window size that is not a multiple of the cell
    /// size. The signal that matters is <c>pixelExact=yes</c>: no cell is fractionally scaled.
    /// </para>
    /// </summary>
    private void LogGridScalingIfChanged(
        in GpuGridLayout layout, uint cols, uint rows, string path)
    {
        if (!_options.DiagnosticsEnabled) return;

        uint swapW = (uint)layout.SwapchainWidth;
        uint swapH = (uint)layout.SwapchainHeight;

        if (swapW == _lastLoggedSwapW && swapH == _lastLoggedSwapH &&
            cols  == _lastLoggedCols  && rows  == _lastLoggedRows  &&
            path  == _lastLoggedPath)
            return;

        _lastLoggedSwapW = swapW;
        _lastLoggedSwapH = swapH;
        _lastLoggedCols  = cols;
        _lastLoggedRows  = rows;
        _lastLoggedPath  = path;

        Console.Error.WriteLine(
            $"[SDLGpu] gridScale path={path} " +
            layout.Describe(_cellWidth, _cellHeight, (int)cols, (int)rows));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CPU compositing
    // ─────────────────────────────────────────────────────────────────────────

    private void CompositeScreen(
        ScreenBuffer buf,
        uint regionX, uint regionY, uint regionWidth, uint regionHeight,
        uint screenW)
    {
        for (uint row = regionY; row < regionY + regionHeight; row++)
        {
            for (uint col = regionX; col < regionX + regionWidth; col++)
            {
                TScreenChar cell = buf.GetChar(col, row);
                byte attr = (byte)(cell.Attr & 0xFF);
                var (fgArgb, bgArgb) = DecodeAttr(attr);

                bool isCursor = _cursorType != 0 && (int)col == _cursorX && (int)row == _cursorY;
                if (isCursor) (fgArgb, bgArgb) = (bgArgb, fgArgb);

                // Use absolute col/row for pixel addressing — _cpuPixels spans the full screen.
                FillCellRect(col, row, bgArgb, screenW);

                if (isCursor && _cursorType < 100)
                {
                    var (origFg, origBg) = DecodeAttr(attr);
                    FillCellRect(col, row, origBg, screenW);
                    FillUnderlineBar(col, row, origFg, screenW);
                    fgArgb = origFg;
                }

                char ch = cell.Character;
                if (ch is ' ' or '\0') continue;

                // Same shared phase helper, same key construction and same cache as the shader
                // path — there is one generated-glyph implementation, not one per path.
                GlyphPhase phase =
                    GlyphPhase.ForCell((int)col, (int)row, _cellWidth, _cellHeight);

                byte[]? alphaGlyph = GetOrCreateGlyphAlpha(ch, phase);
                if (alphaGlyph != null)
                    BlendGlyph(col, row, alphaGlyph, fgArgb, screenW);
            }
        }
    }

    private void FillCellRect(uint col, uint row, uint argb, uint screenW)
    {
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >>  8) & 0xFF);
        byte b = (byte)( argb        & 0xFF);

        int x0 = (int)(col * (uint)_cellWidth);
        int y0 = (int)(row * (uint)_cellHeight);

        for (int dy = 0; dy < _cellHeight; dy++)
        {
            int rowBase = ((y0 + dy) * (int)screenW + x0) * 4;
            for (int dx = 0; dx < _cellWidth; dx++)
            {
                int off = rowBase + dx * 4;
                _cpuPixels[off    ] = r;
                _cpuPixels[off + 1] = g;
                _cpuPixels[off + 2] = b;
                _cpuPixels[off + 3] = 255;
            }
        }
    }

    private void FillUnderlineBar(uint col, uint row, uint argb, uint screenW)
    {
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >>  8) & 0xFF);
        byte b = (byte)( argb        & 0xFF);

        int x0 = (int)(col * (uint)_cellWidth);
        int y0 = (int)(row * (uint)_cellHeight) + _cellHeight - 2;
        int totalBytes = _cpuPixels.Length;

        for (int dy = 0; dy < 2; dy++)
        {
            int rowBase = ((y0 + dy) * (int)screenW + x0) * 4;
            if (rowBase < 0 || rowBase >= totalBytes) continue;
            for (int dx = 0; dx < _cellWidth; dx++)
            {
                int off = rowBase + dx * 4;
                if (off + 3 >= totalBytes) break;
                _cpuPixels[off    ] = r;
                _cpuPixels[off + 1] = g;
                _cpuPixels[off + 2] = b;
                _cpuPixels[off + 3] = 255;
            }
        }
    }

    private void BlendGlyph(uint col, uint row, byte[] alphaGlyph, uint fgArgb, uint screenW)
    {
        byte fgR = (byte)((fgArgb >> 16) & 0xFF);
        byte fgG = (byte)((fgArgb >>  8) & 0xFF);
        byte fgB = (byte)( fgArgb        & 0xFF);

        int x0 = (int)(col * (uint)_cellWidth);
        int y0 = (int)(row * (uint)_cellHeight);

        for (int dy = 0; dy < _cellHeight; dy++)
        {
            int dstBase = ((y0 + dy) * (int)screenW + x0) * 4;
            int srcBase = dy * _cellWidth;
            for (int dx = 0; dx < _cellWidth; dx++)
            {
                byte a = alphaGlyph[srcBase + dx];
                if (a == 0) continue;

                int dstOff = dstBase + dx * 4;
                if (a == 255)
                {
                    _cpuPixels[dstOff    ] = fgR;
                    _cpuPixels[dstOff + 1] = fgG;
                    _cpuPixels[dstOff + 2] = fgB;
                    _cpuPixels[dstOff + 3] = 255;
                }
                else
                {
                    _cpuPixels[dstOff    ] = GpuAlphaBlend.Blend(fgR, _cpuPixels[dstOff    ], a);
                    _cpuPixels[dstOff + 1] = GpuAlphaBlend.Blend(fgG, _cpuPixels[dstOff + 1], a);
                    _cpuPixels[dstOff + 2] = GpuAlphaBlend.Blend(fgB, _cpuPixels[dstOff + 2], a);
                    _cpuPixels[dstOff + 3] = 255;
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Glyph pixel cache
    // ─────────────────────────────────────────────────────────────────────────

    // ── IGpuGlyphSource ─────────────────────────────────────────────────
    // TerminalGpuPipeline and TerminalAtlas go through this interface so they never need to know
    // which characters are phase-dependent — that knowledge stays here, with the generator.

    /// <summary>
    /// Builds the atlas/cache key, normalising the lattice phase away for every glyph that cannot
    /// depend on it — only the three shading characters vary with phase, so everything else keeps
    /// one atlas slot per codepoint.
    /// </summary>
    public GpuGlyphKey CreateKey(char ch, GlyphPhase phase)
    {
        if (!_generatedGlyphs.Generator.RequiresPhase(ch))
            return GpuGlyphKey.PhaseIndependent(ch);

        GlyphPhase normalized = phase.Normalized();
        return new GpuGlyphKey((uint)ch, normalized.X, normalized.Y);
    }

    /// <summary>Cell-sized alpha mask for <paramref name="key"/>, memoised.</summary>
    public byte[]? GetAlpha(GpuGlyphKey key)
    {
        if (_glyphCache.TryGetValue(key, out byte[]? cached)) return cached;

        if (_glyphCache.Count >= MaxGlyphCacheSize) _glyphCache.Clear();

        char ch = key.Character;
        byte[]? alpha = TryGenerateGlyphAlpha(ch, key.Phase) ?? RenderGlyphToAlpha(ch);

        _glyphCache[key] = alpha;
        return alpha;
    }

    /// <summary>
    /// Convenience overload for the CPU-compositing fallback, which works in cell coordinates
    /// rather than atlas keys. Routes through exactly the same key construction and cache as the
    /// shader path, so both paths are guaranteed to see the same bytes.
    /// </summary>
    private byte[]? GetOrCreateGlyphAlpha(char ch, GlyphPhase phase) =>
        GetAlpha(CreateKey(ch, phase));

    /// <summary>Generated CP437 B0-DF alpha, or <c>null</c> when the character is out of range.</summary>
    private byte[]? TryGenerateGlyphAlpha(char ch, GlyphPhase phase)
    {
        if (!_generatedGlyphs.TryGet(ch, phase, out AlphaBitmap bitmap))
            return null;

        GeneratedGlyphValidator.EnsureExpectedSize(bitmap, _cellWidth, _cellHeight, ch);
        return bitmap.Alpha;
    }

    private byte[]? RenderGlyphToAlpha(char ch)
    {
        if (_font == IntPtr.Zero) return null;

        // Render with white so the alpha channel carries the pure glyph shape.
        // Foreground colour is applied at blend time — one cached shape serves all 16 VGA colours.
        SDL3.SDL.Color white = new() { R = 255, G = 255, B = 255, A = 255 };
        IntPtr glyphSurf = SDL3.TTF.RenderGlyphBlended(_font, (ushort)ch, white);
        if (glyphSurf == IntPtr.Zero) return null;

        IntPtr canvas = SDL3.SDL.CreateSurface(_cellWidth, _cellHeight, SDL3.SDL.PixelFormat.ARGB8888);
        if (canvas == IntPtr.Zero) { SDL3.SDL.DestroySurface(glyphSurf); return null; }

        try
        {
            SDL3.SDL.FillSurfaceRect(canvas, IntPtr.Zero, 0u);
            SDL3.SDL.SetSurfaceBlendMode(glyphSurf, SDL3.SDL.BlendMode.None);

            // Same cell placement SDLRenderer uses, so both back-ends agree about where a font
            // glyph sits in its cell. Generated B0-DF glyphs never reach this method.
            SdlFontCellPlacement.Compute(_font, ch, _ascent, out int srcClipTop, out int dstX);
            SdlFontCellPlacement.BlitToCanvas(glyphSurf, canvas, srcClipTop, dstX, dstY: 0);

            if (!SDL3.SDL.LockSurface(canvas)) return null;

            SDL3.SDL.Surface surfData = Marshal.PtrToStructure<SDL3.SDL.Surface>(canvas);
            IntPtr pixelPtr = surfData.Pixels;
            int pitch = surfData.Pitch;

            // Copy the whole surface at once, then extract the alpha channel.
            // ARGB8888 on little-endian (x86/ARM64): memory order is B, G, R, A per pixel,
            // so alpha is at byte offset 3 within each 4-byte pixel.
            int totalBytes = pitch * _cellHeight;
            byte[] raw = new byte[totalBytes];
            Marshal.Copy(pixelPtr, raw, 0, totalBytes);
            SDL3.SDL.UnlockSurface(canvas);

            byte[] alpha = new byte[_cellWidth * _cellHeight];
            for (int y = 0; y < _cellHeight; y++)
                for (int x = 0; x < _cellWidth; x++)
                    alpha[y * _cellWidth + x] = raw[y * pitch + x * 4 + 3];

            return alpha;
        }
        finally
        {
            SDL3.SDL.DestroySurface(canvas);
            SDL3.SDL.DestroySurface(glyphSurf);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPU screen texture management
    // ─────────────────────────────────────────────────────────────────────────

    private void EnsureScreenTexture(uint w, uint h)
    {
        if (_screenTex != IntPtr.Zero && _screenTexW == w && _screenTexH == h) return;

        if (_screenTex != IntPtr.Zero)
        {
            SDL3.SDL.WaitForGPUIdle(_device);
            SDL3.SDL.ReleaseGPUTexture(_device, _screenTex);
            _screenTex = IntPtr.Zero;
        }
        if (_transferBuf != IntPtr.Zero)
        {
            SDL3.SDL.ReleaseGPUTransferBuffer(_device, _transferBuf);
            _transferBuf = IntPtr.Zero;
        }

        // GPU texture: RGBA8, sampler usage (source for BlitGPUTexture).
        var texInfo = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type              = SDL3.SDL.GPUTextureType.TextureType2D,
            Format            = SDL3.SDL.GPUTextureFormat.R8G8B8A8Unorm,
            Usage             = SDL3.SDL.GPUTextureUsageFlags.Sampler,
            Width             = w,
            Height            = h,
            LayerCountOrDepth = 1,
            NumLevels         = 1,
        };
        _screenTex = SDL3.SDL.CreateGPUTexture(_device, in texInfo);

        if (_screenTex == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[SDLGpu] CreateGPUTexture({w}x{h}) failed: {SDL3.SDL.GetError()}");
            return;
        }

        _screenTexW = w;
        _screenTexH = h;

        uint byteSize = w * h * 4;
        var tbInfo = new SDL3.SDL.GPUTransferBufferCreateInfo
        {
            Usage = SDL3.SDL.GPUTransferBufferUsage.Upload,
            Size  = byteSize,
        };
        _transferBuf = SDL3.SDL.CreateGPUTransferBuffer(_device, in tbInfo);

        if (_transferBuf == IntPtr.Zero)
            Console.Error.WriteLine($"[SDLGpu] CreateGPUTransferBuffer failed: {SDL3.SDL.GetError()}");

        _cpuPixels = new byte[byteSize];
    }

    private void UploadScreenTexture(IntPtr cmdBuf, uint w, uint h)
    {
        if (_transferBuf == IntPtr.Zero || _screenTex == IntPtr.Zero) return;

        IntPtr mapped = SDL3.SDL.MapGPUTransferBuffer(_device, _transferBuf, false);
        if (mapped == IntPtr.Zero) return;

        Marshal.Copy(_cpuPixels, 0, mapped, _cpuPixels.Length);
        SDL3.SDL.UnmapGPUTransferBuffer(_device, _transferBuf);

        IntPtr copyPass = SDL3.SDL.BeginGPUCopyPass(cmdBuf);
        var src = new SDL3.SDL.GPUTextureTransferInfo
        {
            TransferBuffer = _transferBuf,
            Offset         = 0,
            PixelsPerRow   = w,
            RowsPerLayer   = h,
        };
        var dst = new SDL3.SDL.GPUTextureRegion
        {
            Texture  = _screenTex,
            MipLevel = 0,
            Layer    = 0,
            X = 0, Y = 0, Z = 0,
            W = w, H = h, D = 1,
        };
        SDL3.SDL.UploadToGPUTexture(copyPass, in src, in dst, false);
        SDL3.SDL.EndGPUCopyPass(copyPass);
    }

    /// <summary>
    /// Copies the grid-sized screen texture into the swapchain 1:1.
    /// <para>
    /// Source and destination rectangles are the same size, so no scaling happens. The letterbox
    /// area keeps the clear colour written by <see cref="ClearSwapchain"/> just before this.
    /// </para>
    /// </summary>
    private void BlitToSwapchain(IntPtr cmdBuf, IntPtr swapTex, in GpuGridLayout layout)
    {
        // Never sample outside the screen texture, and never write outside the swapchain.
        // Source and destination use the SAME size — that is what makes this copy unscaled.
        (int w, int h) = layout.ComputeOneToOneCopySize((int)_screenTexW, (int)_screenTexH);
        if (w == 0 || h == 0) return;

        uint copyW = (uint)w;
        uint copyH = (uint)h;

        var blitInfo = new SDL3.SDL.GPUBlitInfo
        {
            Source = new SDL3.SDL.GPUBlitRegion
            {
                Texture           = _screenTex,
                MipLevel          = 0,
                LayerOrDepthPlane = 0,
                X = 0, Y = 0, W = copyW, H = copyH,
            },
            Destination = new SDL3.SDL.GPUBlitRegion
            {
                Texture           = swapTex,
                MipLevel          = 0,
                LayerOrDepthPlane = 0,
                // Same anchor as the shader path's viewport: top-left, letterbox right/bottom.
                X = (uint)layout.ViewportX, Y = (uint)layout.ViewportY, W = copyW, H = copyH,
            },
            LoadOp   = SDL3.SDL.GPULoadOp.Load,   // keep the letterbox clear from ClearSwapchain
            FlipMode = SDL3.SDL.FlipMode.None,
            Filter   = OneToOneBlitFilter,
            Cycle    = false,
        };
        SDL3.SDL.BlitGPUTexture(cmdBuf, in blitInfo);
    }

    /// <summary>
    /// Filter for the 1:1 swapchain copy. Equal rectangles should mean no resampling either way;
    /// Nearest leaves no room for a driver to interpolate at the edges.
    /// </summary>
    internal const SDL3.SDL.GPUFilter OneToOneBlitFilter = SDL3.SDL.GPUFilter.Nearest;

    /// <summary>
    /// Clears the entire swapchain texture so the letterbox strip can never show stale contents.
    /// <para>
    /// A blit's <c>LoadOp</c> applies only to its destination region, so it cannot clear the
    /// letterbox; an empty render pass whose colour target uses <c>LoadOp.Clear</c> covers the
    /// whole attachment. This mirrors what the shader path gets for free from its own render
    /// pass clear.
    /// </para>
    /// </summary>
    private static void ClearSwapchain(IntPtr cmdBuf, IntPtr swapTex)
    {
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

        IntPtr pass = SDL3.SDL.BeginGPURenderPass(cmdBuf, colorTargets, 1u, IntPtr.Zero);
        SDL3.SDL.EndGPURenderPass(pass);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VGA 16-color palette (0xAARRGGBB)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly uint[] Vga16 =
    {
        0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
        0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF,
        0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
    };

    private static (uint fg, uint bg) DecodeAttr(byte attr) =>
        (Vga16[attr & 0x0F], Vga16[(attr >> 4) & 0x0F]);

    // ─────────────────────────────────────────────────────────────────────────
    // Clear-only frame (fallback before AllocateScreenBuffer is called)
    // ─────────────────────────────────────────────────────────────────────────

    // Navy-blue clear — visually confirms the GPU path is active.
    private SDL3.SDL.FColor _clearColor = new() { R = 0.12f, G = 0.20f, B = 0.45f, A = 1.0f };

    public bool RenderFrame()
    {
        var frameSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;

        IntPtr cmdBuf = SDL3.SDL.AcquireGPUCommandBuffer(_device);
        if (cmdBuf == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[SDLGpu] AcquireGPUCommandBuffer failed: {SDL3.SDL.GetError()}");
            return false;
        }

        var swapSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        bool acquired = SDL3.SDL.WaitAndAcquireGPUSwapchainTexture(
            cmdBuf, _window, out IntPtr swapTex, out _, out _);
        swapSw?.Stop();

        if (!acquired || swapTex == IntPtr.Zero)
        {
            SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf);
            _diag.RecordSwapchainSkipped();
            return false;
        }

        if (swapSw != null) _diag.RecordAcquireSwapchain(swapSw.Elapsed);

        var passSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        SDL3.SDL.GPUColorTargetInfo[] colorTargets = { new()
        {
            Texture           = swapTex,
            MipLevel          = 0,
            LayerOrDepthPlane = 0,
            ClearColor        = _clearColor,
            LoadOp            = SDL3.SDL.GPULoadOp.Clear,
            StoreOp           = SDL3.SDL.GPUStoreOp.Store,
            Cycle             = false,
        }};
        IntPtr pass = SDL3.SDL.BeginGPURenderPass(cmdBuf, colorTargets, 1u, IntPtr.Zero);
        SDL3.SDL.EndGPURenderPass(pass);
        passSw?.Stop();
        if (passSw != null) _diag.RecordRenderPass(passSw.Elapsed);

        var submitSw = _options.DiagnosticsEnabled ? Stopwatch.StartNew() : null;
        SDL3.SDL.SubmitGPUCommandBuffer(cmdBuf);
        submitSw?.Stop();
        if (submitSw != null) _diag.RecordSubmit(submitSw.Elapsed);

        frameSw?.Stop();
        if (frameSw != null) _diag.RecordFrame(frameSw.Elapsed);
        _diag.MaybeReportSummary();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disposal
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop the render thread before releasing any GPU or TTF resources.
        _renderCts.Cancel();
        try { _frameReady.Release(); } catch (SemaphoreFullException) { }
        _renderThread?.Join(TimeSpan.FromSeconds(2));
        _renderThread = null;

        _glyphCache.Clear();

        _termPipeline?.Dispose();
        _termPipeline = null;

        if (_symbolFont != IntPtr.Zero)
        {
            SDL3.TTF.CloseFont(_symbolFont);
            _symbolFont = IntPtr.Zero;
        }

        if (_font != IntPtr.Zero)
        {
            SDL3.TTF.CloseFont(_font);
            _font = IntPtr.Zero;
        }
        if (_ttfInitialized)
        {
            SDL3.TTF.Quit();
            _ttfInitialized = false;
        }

        if (_device != IntPtr.Zero)
        {
            SDL3.SDL.WaitForGPUIdle(_device);

            if (_screenTex != IntPtr.Zero)
            {
                SDL3.SDL.ReleaseGPUTexture(_device, _screenTex);
                _screenTex = IntPtr.Zero;
            }
            if (_transferBuf != IntPtr.Zero)
            {
                SDL3.SDL.ReleaseGPUTransferBuffer(_device, _transferBuf);
                _transferBuf = IntPtr.Zero;
            }
            if (_windowClaimed)
            {
                SDL3.SDL.ReleaseWindowFromGPUDevice(_device, _window);
                _windowClaimed = false;
            }

            SDL3.SDL.DestroyGPUDevice(_device);
            _device = IntPtr.Zero;
        }

        _diag.Dispose();
    }
}
