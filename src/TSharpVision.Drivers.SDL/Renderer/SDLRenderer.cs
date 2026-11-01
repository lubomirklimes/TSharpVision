using TSharpVision.Config;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TSharpVision;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Renderer;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL;

/// <summary>
/// SDL3 renderer for TSharpVision.
///
/// Rendering strategy per cell:
///   1. Background: solid colour rectangle, painted before the glyph.
///   2. CP437 B0–DF: cell-sized bitmap from <see cref="Cp437GlyphGenerator"/>, drawn 1:1.
///   3. Other box drawing and block elements: SDL_ttf glyph on the font baseline, clipped to the cell.
///   4. Normal text: SDL_ttf glyph on the shared text baseline, fitted into the cell.
///   Font glyphs (3 and 4) come from <see cref="SdlCellGlyphRasterizer"/>, shared with the GPU
///   renderer, as cell-sized masks drawn 1:1.
///   5. Space / NUL: background only, no glyph texture.
///
/// Cells are drawn in drawable (output) pixels. On a HiDPI window the cell is the logical cell
/// scaled by the pixel density (<see cref="SdlCellMetrics"/>) and the font is rasterised at
/// <c>pointSize × density</c>, so the renderer emits native-resolution pixels.
///
/// Glyph textures carry foreground ink on a transparent background (alpha-only).
/// Background colour is painted separately per cell.
/// Rendering is dirty/on-demand: <see cref="Render"/> is only called when the
/// screen buffer or cursor state has changed.
/// </summary>
internal sealed class SDLRenderer : IDisposable, ISDLRenderer
{
    private const int MaxCacheEntries  = 4096;
    private const int DefaultFontPtSize = 20;

    private readonly IntPtr _renderer;
    private readonly SdlRendererPerformanceDiagnostics _diagnostics = new();

    private bool   _disposed;
    private IntPtr _font     = IntPtr.Zero;
    private string _fontPath = string.Empty;

    private int _cellWidth  = 12;
    private int _cellHeight = 20;
    private int _underlineHeight = SdlCellMetrics.LogicalUnderlineHeight;
    private int _ascent     = 15;
    private int _descent;
    private int _fontHeight;
    private int _lineSkip;

    private int    _cursorX;
    private int    _cursorY;
    private ushort _cursorType;

    // Cell-sized CP437 B0-DF bitmaps, built once from the cell size using PhasedDither shading.
    private CellGlyphBitmapCache _generatedGlyphs = null!;

    /// <inheritdoc />
    public int CellWidth  => _cellWidth;
    /// <inheritdoc />
    public int CellHeight => _cellHeight;

    // Font configuration kept for re-rasterising on a pixel-density change.
    private int         _fontPtSize = DefaultFontPtSize;
    private FontFitMode _fontFit;
    private int         _logicalCellWidth;
    private int         _logicalCellHeight;
    private SdlCellMetrics _metrics;

    /// <summary>Logical (1×) and drawable cell size.</summary>
    internal SdlCellMetrics Metrics => _metrics;

    /// <summary>Times the font and glyph caches were rebuilt for a new density. Diagnostics and tests.</summary>
    internal int DensityRebuilds { get; private set; }

    // Fallback symbol font (kept open alongside the primary font to provide
    // coverage for emoji / symbol codepoints absent from monospace fonts).
    private IntPtr _symbolFont = IntPtr.Zero;
    private string? _symbolFontPath;

    // Font glyph → cell mask, shared placement with SDLGpuRenderer.
    private SdlCellGlyphRasterizer? _cellGlyphs;

    // ─────────────────────────────────────────────────────────────────────────
    // Font probing
    // ─────────────────────────────────────────────────────────────────────────

    private void TryAddSymbolFallback(float fontPtSize)
    {
        string? symbolPath = SdlFontLocator.ProbeSymbolFontPath();
        if (symbolPath == null) return;

        _symbolFont = SDL3.TTF.OpenFont(symbolPath, fontPtSize);
        if (_symbolFont == IntPtr.Zero) return;

        SDL3.TTF.AddFallbackFont(_font, _symbolFont);
        _symbolFontPath = symbolPath;
        if (SdlRendererPerformanceDiagnostics.Enabled)
            Console.Error.WriteLine($"[SDLRenderer] symbolFallback='{symbolPath}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a text renderer over an existing SDL renderer handle using automatically selected font settings.</summary>
    public SDLRenderer(IntPtr renderer) : this(renderer, null, null) { }

    /// <summary>Creates a text renderer over an SDL renderer handle; null selects an available default font.</summary>
    public SDLRenderer(IntPtr renderer, string? fontName) : this(renderer, fontName, null) { }

    /// <summary>Creates a text renderer over an SDL renderer handle with optional font name and point size; null values use configured defaults.</summary>
    public SDLRenderer(IntPtr renderer, string? fontName, int? fontSize)
        : this(renderer, fontName, fontSize, FontFitMode.Condense) { }

    /// <summary>Creates a text renderer that fits text into the cell by <paramref name="fontFit"/> (<c>[graphics] fontFit</c>).</summary>
    public SDLRenderer(IntPtr renderer, string? fontName, int? fontSize, FontFitMode fontFit)
        : this(renderer, fontName, fontSize, fontFit, 1f) { }

    /// <summary>
    /// Creates a text renderer whose cells and glyphs are sized for a drawable of
    /// <paramref name="pixelDensity"/> pixels per window coordinate (1 on a standard display).
    /// </summary>
    public SDLRenderer(IntPtr renderer, string? fontName, int? fontSize, FontFitMode fontFit, float pixelDensity)
    {
        _renderer = renderer;
        _fontFit  = fontFit;
        _imageTextures = new SdlImageTextureCache(new ImageTextureBackend(renderer), QueryMaxTextureSize(renderer));

        if (!SDL3.TTF.Init())
            throw new Exception("SDL_ttf could not initialize! " + SDL3.SDL.GetError());

        try
        {
            string? fontPath = null;
            if (!string.IsNullOrWhiteSpace(fontName))
            {
                fontPath = SdlFontLocator.ProbeFontPathByName(fontName);
                if (fontPath == null)
                    Console.Error.WriteLine($"[SDLRenderer] Warning: font '{fontName}' not found; falling back to default.");
            }

            fontPath ??= SdlFontLocator.ProbeFontPath();
            if (fontPath == null)
                throw new Exception("[SDLRenderer] No suitable monospace font found on this system.");

            _fontPtSize = fontSize ?? DefaultFontPtSize;
            _fontPath   = fontPath;

            // The logical cell comes from the font at its configured size (1×), measured once.
            IntPtr logicalFont = SDL3.TTF.OpenFont(fontPath, _fontPtSize);
            if (logicalFont == IntPtr.Zero)
                throw new Exception($"[SDLRenderer] Failed to load font '{fontPath}': " + SDL3.SDL.GetError());
            SdlFontMetrics.ComputeMetrics(logicalFont, out _logicalCellWidth, out _logicalCellHeight);
            SDL3.TTF.CloseFont(logicalFont);

            LoadFont(pixelDensity);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the font at <c>pointSize × density</c> and builds the rasteriser and generated-glyph
    /// cache for the drawable-pixel cell of that density.
    /// </summary>
    private void LoadFont(float density)
    {
        _metrics         = SdlCellMetrics.ForDensity(_logicalCellWidth, _logicalCellHeight, density);
        _cellWidth       = _metrics.PixelWidth;
        _cellHeight      = _metrics.PixelHeight;
        _underlineHeight = _metrics.UnderlineHeight;

        float rasterPt = _metrics.RasterPointSize(_fontPtSize);
        _font = SDL3.TTF.OpenFont(_fontPath, rasterPt);
        if (_font == IntPtr.Zero)
            throw new Exception($"[SDLRenderer] Failed to load font '{_fontPath}': " + SDL3.SDL.GetError());

        _ascent     = SDL3.TTF.GetFontAscent(_font);
        _descent    = SDL3.TTF.GetFontDescent(_font);
        _fontHeight = SDL3.TTF.GetFontHeight(_font);
        _lineSkip   = SDL3.TTF.GetFontLineSkip(_font);

        TryAddSymbolFallback(rasterPt);

        _cellGlyphs = new SdlCellGlyphRasterizer(
            _font, _fontPath, rasterPt, _symbolFontPath, _cellWidth, _cellHeight, _fontFit);

        _generatedGlyphs = new CellGlyphBitmapCache(
            new Cp437GlyphGenerator(_cellWidth, _cellHeight,
                Cp437GlyphGeneratorOptions.Default with { ShadingDotSize = _metrics.ShadingDotSize }));

        if (SdlRendererPerformanceDiagnostics.Enabled)
            LogStartupDiagnostics(rasterPt);
    }

    /// <summary>Closes the fonts and rasteriser opened by <see cref="LoadFont"/>; SDL_ttf stays initialised.</summary>
    private void UnloadFont()
    {
        _cellGlyphs?.Dispose();
        _cellGlyphs = null;

        if (_font != IntPtr.Zero)
        {
            SDL3.TTF.ClearFallbackFonts(_font);
            SDL3.TTF.CloseFont(_font);
            _font = IntPtr.Zero;
        }
        if (_symbolFont != IntPtr.Zero)
        {
            SDL3.TTF.CloseFont(_symbolFont);
            _symbolFont = IntPtr.Zero;
        }
        _symbolFontPath = null;
    }

    /// <summary>
    /// Switches to <paramref name="density"/> (the window moved to a display with another pixel
    /// density): drops every glyph texture and re-rasterises the font for the new drawable cell.
    /// A no-op when the density is unchanged, so it never runs per frame.
    /// </summary>
    internal SdlCellMetrics SetPixelDensity(float density)
    {
        float normalized = SdlDisplayScale.NormalizeDensity(density);
        if (normalized == _metrics.Density) return _metrics;

        ClearGlyphCache();
        UnloadFont();
        LoadFont(normalized);
        DensityRebuilds++;
        return _metrics;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Startup / window diagnostics (opt-in; logged to stderr)
    // ─────────────────────────────────────────────────────────────────────────

    private void LogStartupDiagnostics(float fontPtSize)
    {
        Console.Error.WriteLine(
            $"[SDLRenderer] platform={RuntimeInformation.OSDescription} " +
            $"arch={RuntimeInformation.ProcessArchitecture}");

        Console.Error.WriteLine(
            $"[SDLRenderer] font='{_fontPath}' " +
            $"fontSize={_fontPtSize}pt rasterSize={fontPtSize:0.##}pt " +
            $"cell={_cellWidth}x{_cellHeight}px {_metrics.Describe()} " +
            $"ascent={_ascent} descent={_descent} fontH={_fontHeight} lineSkip={_lineSkip} " +
            $"textBaseline={_cellGlyphs?.TextBaseline} " +
            $"fontFit={_cellGlyphs?.FontFit} textSize={_cellGlyphs?.UniformPointSize}pt");

    }

    /// <summary>
    /// Applies <c>TSHARPVISION_SDL_VSYNC</c> and (if diagnostics are enabled) logs
    /// SDL window and display properties. Must be called on the main thread after
    /// the window has been created and sized.
    /// </summary>
    internal void LogWindowDiagnostics(IntPtr window, IntPtr renderer)
    {
        ApplyVSync(renderer);

        if (!SdlRendererPerformanceDiagnostics.Enabled)
            return;

        string? videoDriver  = SDL3.SDL.GetCurrentVideoDriver();
        string? rendererName = SDL3.SDL.GetRendererName(renderer);
        Console.Error.WriteLine($"[SDLRenderer] videoDriver={videoDriver ?? "(unavailable)"}");
        Console.Error.WriteLine($"[SDLRenderer] sdlRenderDriver={rendererName ?? "(unavailable)"}");

        SDL3.SDL.GetWindowSize(window, out int logW, out int logH);
        Console.Error.WriteLine($"[SDLRenderer] window logical={logW}x{logH}px");

        SDL3.SDL.GetWindowSizeInPixels(window, out int pixW, out int pixH);
        Console.Error.WriteLine($"[SDLRenderer] window pixels={pixW}x{pixH}px");

        float density = SDL3.SDL.GetWindowPixelDensity(window);
        float scale   = SDL3.SDL.GetWindowDisplayScale(window);
        bool  hiDpi   = (SDL3.SDL.GetWindowFlags(window) & SDL3.SDL.WindowFlags.HighPixelDensity) != 0;
        Console.Error.WriteLine(
            $"[SDLRenderer] pixelDensity={density:F2} displayScale={scale:F2} highPixelDensity={(hiDpi ? "on" : "off")}");

        SDL3.SDL.GetCurrentRenderOutputSize(renderer, out int outW, out int outH);
        Console.Error.WriteLine($"[SDLRenderer] renderOutput={outW}x{outH}px {_metrics.Describe()}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VSync configuration
    // ─────────────────────────────────────────────────────────────────────────

    private int _vsyncValue = int.MinValue; // int.MinValue = never queried

    // TSHARPVISION_SDL_VSYNC, matched like the SDL GPU switches: trimmed and case-insensitive.
    internal static int? ParseVSyncRequest(string value) => value.Trim().ToLowerInvariant() switch
    {
        "0" or "off" or "false" => SDL3.SDL.RendererVSyncDisabled,
        "1" or "on" or "true"   => 1,
        "adaptive"              => SDL3.SDL.RendererVSyncAdaptive,
        _                       => null,
    };

    private void ApplyVSync(IntPtr renderer)
    {
        if (SDL3.SDL.GetRenderVSync(renderer, out int currentVsync))
        {
            _vsyncValue = currentVsync;
            _diagnostics.SetVSyncValue(currentVsync);
            if (SdlRendererPerformanceDiagnostics.Enabled)
                Console.Error.WriteLine($"[SDLRenderer] vsync.current={currentVsync}");
        }
        else if (SdlRendererPerformanceDiagnostics.Enabled)
        {
            Console.Error.WriteLine("[SDLRenderer] vsync.current=(SDL_GetRenderVSync unavailable)");
        }

        string? envVal = Environment.GetEnvironmentVariable("TSHARPVISION_SDL_VSYNC");
        if (string.IsNullOrWhiteSpace(envVal))
            return;

        int requested;
        if (ParseVSyncRequest(envVal) is int parsed)
            requested = parsed;
        else
        {
            Console.Error.WriteLine(
                $"[SDLRenderer] vsync.request: unrecognised TSHARPVISION_SDL_VSYNC='{envVal}' " +
                "(expected 0/off/false, 1/on/true, or adaptive) — ignoring");
            return;
        }

        bool ok = SDL3.SDL.SetRenderVSync(renderer, requested);

        if (SDL3.SDL.GetRenderVSync(renderer, out int afterVal))
        {
            _vsyncValue = afterVal;
            _diagnostics.SetVSyncValue(afterVal);
        }

        if (SdlRendererPerformanceDiagnostics.Enabled)
        {
            string error    = ok ? string.Empty : (SDL3.SDL.GetError() ?? string.Empty);
            string afterStr = _vsyncValue == int.MinValue ? "(unavailable)" : _vsyncValue.ToString();
            Console.Error.WriteLine(
                $"[SDLRenderer] vsync.request={requested} ok={ok} after={afterStr} error='{error}'");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cursor
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void SetCursor(int x, int y, ushort cursorType)
    {
        _cursorX    = x;
        _cursorY    = y;
        _cursorType = cursorType;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Texture cache
    // ─────────────────────────────────────────────────────────────────────────
    // Classification itself lives in SdlGlyphRenderPolicy, which is pure and testable without
    // an SDL handle.

    private readonly Dictionary<SdlGlyphTextureKey, IntPtr> _glyphCache = new();

    private IntPtr GetOrCreateGlyphTexture(char ch, uint fgArgb, GlyphPhase phase)
    {
        SdlGlyphRenderClass cls = SdlGlyphRenderPolicy.Classify(ch);
        SdlGlyphTextureKey  key = SdlGlyphTextureKey.Create(ch, fgArgb, phase, _generatedGlyphs.Generator);

        if (_glyphCache.TryGetValue(key, out IntPtr cached))
        {
            if (SdlRendererPerformanceDiagnostics.Enabled)
            {
                if (cls == SdlGlyphRenderClass.Generated)
                    _diagnostics.RecordGeneratedCacheHit();
                else
                    _diagnostics.RecordNormalCacheHit();
            }
            return cached;
        }

        if (_glyphCache.Count >= MaxCacheEntries)
            ClearGlyphCache();

        if (SdlRendererPerformanceDiagnostics.Enabled && cls != SdlGlyphRenderClass.Space)
        {
            if (cls == SdlGlyphRenderClass.Generated)
                _diagnostics.RecordGeneratedCacheMiss();
            else
                _diagnostics.RecordNormalCacheMiss();
        }

        IntPtr texture = cls switch
        {
            SdlGlyphRenderClass.Generated => CreateGeneratedTexture(ch, fgArgb, phase),
            SdlGlyphRenderClass.FontCell or SdlGlyphRenderClass.Natural => CreateFontTexture(ch, fgArgb),
            _                             => IntPtr.Zero,
        };

        if (texture != IntPtr.Zero)
            _glyphCache[key] = texture;

        return texture;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generated CP437 B0-DF texture
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the generated <see cref="AlphaBitmap"/> for <paramref name="ch"/> at
    /// <paramref name="phase"/> from the shared <see cref="CellGlyphBitmapCache"/> and uploads it
    /// through <see cref="AlphaBitmapToTexture"/>.
    /// </summary>
    private IntPtr CreateGeneratedTexture(char ch, uint fgArgb, GlyphPhase phase)
    {
        if (!_generatedGlyphs.TryGet(ch, phase, out AlphaBitmap bitmap))
            return IntPtr.Zero;

        GeneratedGlyphValidator.EnsureExpectedSize(bitmap, _cellWidth, _cellHeight, ch);

        IntPtr tex = AlphaBitmapToTexture(bitmap, fgArgb);
        if (tex != IntPtr.Zero)
            _diagnostics.RecordGeneratedGlyphCreated();
        return tex;
    }

    /// <summary>
    /// Converts a cell-sized <see cref="AlphaBitmap"/> to an SDL texture tinted to
    /// <paramref name="fgArgb"/>. Alpha 0 = transparent; 255 = full ink.
    /// </summary>
    private IntPtr AlphaBitmapToTexture(AlphaBitmap bitmap, uint fgArgb)
    {
        byte fgR = (byte)((fgArgb >> 16) & 0xFF);
        byte fgG = (byte)((fgArgb >>  8) & 0xFF);
        byte fgB = (byte)( fgArgb        & 0xFF);

        IntPtr surface = SDL3.SDL.CreateSurface(bitmap.Width, bitmap.Height,
            SDL3.SDL.PixelFormat.ARGB8888);
        if (surface == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            SDL3.SDL.FillSurfaceRect(surface, IntPtr.Zero, 0u);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte a = bitmap[x, y];
                    if (a == 0) continue;
                    SDL3.SDL.WriteSurfacePixel(surface, x, y, fgR, fgG, fgB, a);
                }
            }

            SDL3.SDL.SetSurfaceBlendMode(surface, SDL3.SDL.BlendMode.Blend);

            IntPtr tex = CreateTextureFromSurface(surface);
            if (tex != IntPtr.Zero)
                SDL3.SDL.SetTextureBlendMode(tex, SDL3.SDL.BlendMode.Blend);

            return tex;
        }
        finally
        {
            SDL3.SDL.DestroySurface(surface);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Font glyph texture (text, other box drawing and block elements)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tints the cell-sized mask from the shared <see cref="SdlCellGlyphRasterizer"/> into a
    /// texture, so this back-end and SDLGpuRenderer put every font glyph on the same pixels.
    /// </summary>
    private IntPtr CreateFontTexture(char ch, uint fgArgb)
    {
        byte[]? alpha = _cellGlyphs?.Rasterize(ch);
        if (alpha == null)
            return IntPtr.Zero;

        IntPtr tex = AlphaBitmapToTexture(new AlphaBitmap(_cellWidth, _cellHeight, alpha), fgArgb);
        if (tex != IntPtr.Zero)
            _diagnostics.RecordNormalGlyphCreated();
        return tex;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared texture creation (with optional diagnostics timing)
    // ─────────────────────────────────────────────────────────────────────────

    private IntPtr CreateTextureFromSurface(IntPtr surface)
    {
        if (!SdlRendererPerformanceDiagnostics.Enabled)
            return SDL3.SDL.CreateTextureFromSurface(_renderer, surface);

        var sw = Stopwatch.StartNew();
        IntPtr tex = SDL3.SDL.CreateTextureFromSurface(_renderer, surface);
        sw.Stop();
        _diagnostics.RecordCreateTextureFromSurface(sw.Elapsed);
        return tex;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cache management
    // ─────────────────────────────────────────────────────────────────────────

    private void ClearGlyphCache()
    {
        foreach (IntPtr tex in _glyphCache.Values)
        {
            if (tex != IntPtr.Zero)
                SDL3.SDL.DestroyTexture(tex);
        }
        _glyphCache.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Diagnostics hooks (called by SDLDriver)
    // ─────────────────────────────────────────────────────────────────────────

    internal void RecordIdleWait()
    {
        if (SdlRendererPerformanceDiagnostics.Enabled)
            _diagnostics.RecordIdleWait();
    }

    internal void RecordRenderWithReasons(int reasonMask)
    {
        if (SdlRendererPerformanceDiagnostics.Enabled)
            _diagnostics.RecordRenderWithReasons(reasonMask);
    }

    internal void RecordDrainCycle(int events, int motionReceived, int motionCoalesced, int motionDuringDrag)
    {
        if (SdlRendererPerformanceDiagnostics.Enabled)
            _diagnostics.RecordDrainCycle(events, motionReceived, motionCoalesced, motionDuringDrag);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Renders the screen buffer and caret to the SDL target; the current implementation redraws the full frame.</summary>
    public void Render(
        ScreenBuffer screenBuffer,
        uint regionX, uint regionY, uint regionWidth, uint regionHeight)
    {
        var frameSw = SdlRendererPerformanceDiagnostics.Enabled ? Stopwatch.StartNew() : null;

        Trace?.Record(new SdlRenderOp(SdlRenderOpKind.BeginFrame,
            (int)regionX, (int)regionY, (int)regionWidth, (int)regionHeight, 0xFF000000));

        SDL3.SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL3.SDL.RenderClear(_renderer);

        uint maxY = regionY + regionHeight;
        uint maxX = regionX + regionWidth;

        for (uint y = regionY; y < maxY; y++)
        {
            for (uint x = regionX; x < maxX; x++)
            {
                TScreenChar cell     = screenBuffer.GetChar(x, y);
                byte        attrByte = (byte)(cell.Attr & 0xFF);
                var (fgArgb, bgArgb) = SdlPalette.DecodeAttr(attrByte);

                bool isCursorCell =
                    _cursorType != 0 && (int)x == _cursorX && (int)y == _cursorY;

                if (isCursorCell)
                    (fgArgb, bgArgb) = (bgArgb, fgArgb);

                var cellRect = new SDL3.SDL.FRect
                {
                    X = (int)(x * _cellWidth),
                    Y = (int)(y * _cellHeight),
                    W = _cellWidth,
                    H = _cellHeight,
                };

                // 1. Background — solid rectangle, no gaps.
                DrawCellBackground(cellRect, bgArgb);

                // Underline cursor: restore original colours and draw a 2-logical-px bar.
                if (isCursorCell && _cursorType < 100)
                {
                    var (origFg, origBg) = SdlPalette.DecodeAttr(attrByte);
                    DrawCellBackground(cellRect, origBg);

                    var underline = new SDL3.SDL.FRect
                    {
                        X = cellRect.X,
                        Y = cellRect.Y + cellRect.H - _underlineHeight,
                        W = cellRect.W,
                        H = _underlineHeight,
                    };
                    DrawCellBackground(underline, origFg);
                    fgArgb = origFg;
                }

                char ch = cell.Character;

                // 2. Space / NUL → background only, no glyph texture.
                if (ch is ' ' or '\0')
                    continue;

                // 3. Get (or lazily create and cache) the glyph texture.
                // Phase is the cell's grid position modulo the shading lattice period; it is only
                // consulted for CP437 B0-DF under PhasedDither (see SdlGlyphTextureKey.Create).
                GlyphPhase phase = _generatedGlyphs.Generator.PhaseForCell((int)x, (int)y);
                IntPtr texture = GetOrCreateGlyphTexture(ch, fgArgb, phase);
                if (texture == IntPtr.Zero)
                {
                    Trace?.Record(new SdlRenderOp(SdlRenderOpKind.GlyphMissing,
                        (int)cellRect.X, (int)cellRect.Y, (int)cellRect.W, (int)cellRect.H,
                        fgArgb, ch, SdlGlyphRenderPolicy.Classify(ch)));
                    continue;
                }

                // All glyph textures are rendered into the logical cell rect, 1:1.
                // Generated graphics and shared font masks are exactly CellWidth × CellHeight.
                Trace?.Record(new SdlRenderOp(SdlRenderOpKind.Glyph,
                    (int)cellRect.X, (int)cellRect.Y, (int)cellRect.W, (int)cellRect.H,
                    fgArgb, ch, SdlGlyphRenderPolicy.Classify(ch)));
                SDL3.SDL.RenderTexture(_renderer, texture, IntPtr.Zero, in cellRect);
            }
        }

        // 4. Pixel graphics layers over the finished grid (nothing to do without a layer).
        DrawGraphics();

        // Native-pixel qualification reads the finished frame back here: after SDL_RenderPresent
        // the back buffer contents are undefined.
        BeforePresentForTesting?.Invoke(_renderer);

        if (SdlRendererPerformanceDiagnostics.Enabled)
        {
            var presentSw = Stopwatch.StartNew();
            SDL3.SDL.RenderPresent(_renderer);
            presentSw.Stop();
            _diagnostics.RecordRenderPresent(presentSw.Elapsed);
        }
        else
        {
            SDL3.SDL.RenderPresent(_renderer);
        }
        Trace?.Record(new SdlRenderOp(SdlRenderOpKind.Present, 0, 0, 0, 0, 0));

        if (frameSw != null)
        {
            frameSw.Stop();
            _diagnostics.RecordFrame(frameSw.Elapsed);
        }

        _diagnostics.MaybeReportSummary();
    }

    /// <summary>Records the renderer-facing operations of each frame when set. Null in normal operation.</summary>
    internal SdlRenderTrace? Trace { get; set; }

    /// <summary>
    /// Called with the SDL renderer handle once the whole frame is drawn and before
    /// <c>SDL_RenderPresent</c>, so native-pixel qualification can read the real target back
    /// (<c>SDL_RenderReadPixels</c>). Null in normal operation.
    /// </summary>
    internal Action<IntPtr>? BeforePresentForTesting { get; set; }

    /// <summary>The shared glyph rasteriser (fit mode, common text size, baseline) for qualification.</summary>
    internal SdlCellGlyphRasterizer? GlyphsForTesting => _cellGlyphs;

    /// <summary>IPixelGraphicsDriver layers drawn by the next frame. Set on the UI thread by the driver.</summary>
    internal PixelGraphicsFrame GraphicsFrame { get; set; } = PixelGraphicsFrame.Empty;

    // Pixel graphics: the per-frame call list and one texture per PixelImage.
    private readonly SdlGraphicsPlan _graphicsPlan = new();
    private readonly SdlImageTextureCache _imageTextures;

    /// <summary>The image texture cache. Tests and diagnostics.</summary>
    internal SdlImageTextureCache ImageTexturesForTesting => _imageTextures;

    /// <summary>SDL draw calls (fill batches and textures) of the last frame's graphics pass. Tests.</summary>
    internal int LastGraphicsDrawCalls { get; private set; }

    // Draws the frame's layers over the finished grid (SdlGraphicsPlan: the PixelGraphicsGeometry rectangles,
    // clipped on the CPU and batched into one SDL_RenderFillRects call per primitive and clip rectangle; images as
    // cached textures under SDL_SetRenderClipRect), blended source-over with straight alpha like the CPU reference.
    // The draw blend mode and clip rectangle are restored for the next frame's cells. Without layers this is one
    // reference comparison and a property read.
    private void DrawGraphics()
    {
        PixelGraphicsFrame frame = GraphicsFrame;
        _imageTextures.Retain(frame);
        LastGraphicsDrawCalls = 0;
        if (frame.IsEmpty) return;
        var scale = new PixelScale(
            _metrics.LogicalWidth > 0 ? _metrics.LogicalWidth : _cellWidth,
            _metrics.LogicalHeight > 0 ? _metrics.LogicalHeight : _cellHeight,
            _cellWidth, _cellHeight);
        if (!SDL3.SDL.GetCurrentRenderOutputSize(_renderer, out int outputWidth, out int outputHeight)) return;
        _graphicsPlan.Build(frame, scale, outputWidth, outputHeight);
        if (_graphicsPlan.Commands.Count == 0) return;

        SDL3.SDL.GetRenderDrawBlendMode(_renderer, out SDL3.SDL.BlendMode previousBlend);
        bool previousClipEnabled = SDL3.SDL.RenderClipEnabled(_renderer);
        SDL3.SDL.Rect previousClip = default;
        if (previousClipEnabled) SDL3.SDL.GetRenderClipRect(_renderer, out previousClip);
        bool clipChanged = false;
        SDL3.SDL.SetRenderDrawBlendMode(_renderer, SDL3.SDL.BlendMode.Blend);

        foreach (SdlGraphicsCommand command in _graphicsPlan.Commands)
        {
            if (command.Kind == SdlGraphicsCommandKind.Fill)
            {
                // Fill rectangles are clipped already; an image's clip rectangle must not cut them.
                if (clipChanged) { RestoreClip(previousClipEnabled, previousClip); clipChanged = false; }
                DrawFillBatch(command);
                continue;
            }

            PixelImage image = command.Image!;
            DeviceRect v = command.Visible;
            byte opacity = (byte)command.Rgba;
            uint traceArgb = ((uint)opacity << 24) | 0x00FFFFFFu;
            IntPtr texture = _imageTextures.Get(image);
            if (texture == IntPtr.Zero)
            {
                Trace?.Record(new SdlRenderOp(SdlRenderOpKind.GraphicsImageSkipped, v.X0, v.Y0, v.X1 - v.X0, v.Y1 - v.Y0,
                    traceArgb, ImageId: image.Id, ImageFilter: command.Filter));
                continue;
            }
            Trace?.Record(new SdlRenderOp(SdlRenderOpKind.GraphicsImage, v.X0, v.Y0, v.X1 - v.X0, v.Y1 - v.Y0,
                traceArgb, ImageId: image.Id, ImageFilter: command.Filter));
            var clip = new SDL3.SDL.Rect
            {
                X = command.Clip.X0, Y = command.Clip.Y0,
                W = command.Clip.X1 - command.Clip.X0, H = command.Clip.Y1 - command.Clip.Y0,
            };
            SDL3.SDL.SetRenderClipRect(_renderer, clip);
            clipChanged = true;
            SDL3.SDL.SetTextureScaleMode(texture,
                command.Filter == GraphicsImageFilter.Linear ? SDL3.SDL.ScaleMode.Linear : SDL3.SDL.ScaleMode.Nearest);
            SDL3.SDL.SetTextureAlphaMod(texture, opacity);
            SDL3.SDL.FRect destination = command.Destination;
            if (command.WholeSource) SDL3.SDL.RenderTexture(_renderer, texture, IntPtr.Zero, in destination);
            else
            {
                SDL3.SDL.FRect source = command.Source;
                SDL3.SDL.RenderTexture(_renderer, texture, in source, in destination);
            }
            LastGraphicsDrawCalls++;
        }

        if (clipChanged) RestoreClip(previousClipEnabled, previousClip);
        SDL3.SDL.SetRenderDrawBlendMode(_renderer, previousBlend);
    }

    private void DrawFillBatch(in SdlGraphicsCommand command)
    {
        uint rgba = command.Rgba;
        ReadOnlySpan<SDL3.SDL.FRect> rects = _graphicsPlan.Rects.Slice(command.FirstRect, command.RectCount);
        if (Trace != null)
        {
            uint argb = (rgba >> 8) | (rgba << 24);
            foreach (SDL3.SDL.FRect r in rects)
                Trace.Record(new SdlRenderOp(SdlRenderOpKind.GraphicsFill, (int)r.X, (int)r.Y, (int)r.W, (int)r.H, argb));
        }
        SDL3.SDL.SetRenderDrawColor(_renderer, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        SDL3.SDL.RenderFillRects(_renderer, rects, rects.Length);
        LastGraphicsDrawCalls++;
    }

    private void RestoreClip(bool enabled, SDL3.SDL.Rect clip)
    {
        if (enabled) SDL3.SDL.SetRenderClipRect(_renderer, clip);
        else SDL3.SDL.SetRenderClipRect(_renderer, IntPtr.Zero);
    }

    /// <summary>
    /// The render device was reset or lost (SDL_EVENT_RENDER_DEVICE_RESET / _LOST): texture contents are gone.
    /// Glyph and image textures are dropped and recreated on their next use; the next frame redraws.
    /// </summary>
    internal void HandleDeviceReset()
    {
        ClearGlyphCache();
        _imageTextures.Reset();
    }

    private static int QueryMaxTextureSize(IntPtr renderer)
    {
        if (renderer == IntPtr.Zero) return 0;
        uint props = SDL3.SDL.GetRendererProperties(renderer);
        long max = props == 0 ? 0 : SDL3.SDL.GetNumberProperty(props, "SDL.renderer.max_texture_size", 0);
        return max is > 0 and <= int.MaxValue ? (int)max : 0;
    }

    // Static textures in the byte order of PixelImage (R, G, B, A in memory), straight alpha, blend mode blend.
    private sealed class ImageTextureBackend(IntPtr renderer) : ISdlImageTextureBackend
    {
        private static readonly SDL3.SDL.PixelFormat Rgba32 =
            BitConverter.IsLittleEndian ? SDL3.SDL.PixelFormat.ABGR8888 : SDL3.SDL.PixelFormat.RGBA8888;

        public string LastError { get; private set; } = string.Empty;

        public unsafe IntPtr Create(PixelImage image)
        {
            IntPtr texture = SDL3.SDL.CreateTexture(renderer, Rgba32, SDL3.SDL.TextureAccess.Static, image.Width, image.Height);
            if (texture == IntPtr.Zero) { LastError = SDL3.SDL.GetError() ?? string.Empty; return IntPtr.Zero; }
            bool uploaded;
            fixed (byte* pixels = image.Pixels)
                uploaded = SDL3.SDL.UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixels, image.Stride);
            if (!uploaded)
            {
                LastError = SDL3.SDL.GetError() ?? string.Empty;
                SDL3.SDL.DestroyTexture(texture);
                return IntPtr.Zero;
            }
            SDL3.SDL.SetTextureBlendMode(texture, SDL3.SDL.BlendMode.Blend);
            return texture;
        }

        public void Destroy(IntPtr texture) => SDL3.SDL.DestroyTexture(texture);
    }

    private void DrawCellBackground(SDL3.SDL.FRect rect, uint argb)
    {
        Trace?.Record(new SdlRenderOp(SdlRenderOpKind.Fill,
            (int)rect.X, (int)rect.Y, (int)rect.W, (int)rect.H, argb));
        SDL3.SDL.SetRenderDrawColor(_renderer,
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >>  8) & 0xFF),
            (byte)( argb        & 0xFF),
            255);
        SDL3.SDL.RenderFillRect(_renderer, rect);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disposal
    // ─────────────────────────────────────────────────────────────────────────

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            ClearGlyphCache();
            _imageTextures.Dispose();
            UnloadFont();

            SDL3.TTF.Quit();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
