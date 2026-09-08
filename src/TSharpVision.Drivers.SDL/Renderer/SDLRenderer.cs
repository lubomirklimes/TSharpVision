using System.Diagnostics;
using System.Runtime.InteropServices;
using TSharpVision;
using TSharpVision.Drivers;
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
///   3. Other box drawing: SDL_ttf glyph fitted into the cell.
///   4. Normal text: natural SDL_ttf glyph.
///   5. Space / NUL: background only, no glyph texture.
///
/// Glyph textures carry foreground ink on a transparent background (alpha-only).
/// Background colour is painted separately per cell.
/// Rendering is dirty/on-demand: <see cref="Render"/> is only called when the
/// screen buffer or cursor state has changed.
/// </summary>
public sealed class SDLRenderer : IDisposable, ISDLRenderer
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
    private int _ascent     = 15;
    private int _descent;
    private int _fontHeight;
    private int _lineSkip;

    private int    _cursorX;
    private int    _cursorY;
    private ushort _cursorType;

    // Cell-sized CP437 B0-DF bitmaps, built once from the cell size using PhasedDither shading.
    private CellGlyphBitmapCache _generatedGlyphs = null!;

    public int CellWidth  => _cellWidth;
    public int CellHeight => _cellHeight;

    // Fallback symbol font (kept open alongside the primary font to provide
    // coverage for emoji / symbol codepoints absent from monospace fonts).
    private IntPtr _symbolFont = IntPtr.Zero;

    // ─────────────────────────────────────────────────────────────────────────
    // Font probing
    // ─────────────────────────────────────────────────────────────────────────

    private void TryAddSymbolFallback(int fontPtSize)
    {
        string? symbolPath = SdlFontLocator.ProbeSymbolFontPath();
        if (symbolPath == null) return;

        _symbolFont = SDL3.TTF.OpenFont(symbolPath, fontPtSize);
        if (_symbolFont == IntPtr.Zero) return;

        SDL3.TTF.AddFallbackFont(_font, _symbolFont);
        if (SdlRendererPerformanceDiagnostics.Enabled)
            Console.Error.WriteLine($"[SDLRenderer] symbolFallback='{symbolPath}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    public SDLRenderer(IntPtr renderer) : this(renderer, null, null) { }

    public SDLRenderer(IntPtr renderer, string? fontName) : this(renderer, fontName, null) { }

    public SDLRenderer(IntPtr renderer, string? fontName, int? fontSize)
    {
        _renderer = renderer;

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

            int fontPtSize = fontSize ?? DefaultFontPtSize;
            _font = SDL3.TTF.OpenFont(fontPath, fontPtSize);
            if (_font == IntPtr.Zero)
                throw new Exception($"[SDLRenderer] Failed to load font '{fontPath}': " + SDL3.SDL.GetError());

            _fontPath   = fontPath;
            SdlFontMetrics.ComputeMetrics(_font, out _cellWidth, out _cellHeight);

            _ascent     = SDL3.TTF.GetFontAscent(_font);
            _descent    = SDL3.TTF.GetFontDescent(_font);
            _fontHeight = SDL3.TTF.GetFontHeight(_font);
            _lineSkip   = SDL3.TTF.GetFontLineSkip(_font);

            TryAddSymbolFallback(fontPtSize);

            _generatedGlyphs = new CellGlyphBitmapCache(
                new Cp437GlyphGenerator(_cellWidth, _cellHeight));

            if (SdlRendererPerformanceDiagnostics.Enabled)
                LogStartupDiagnostics(fontPtSize);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Startup / window diagnostics (opt-in; logged to stderr)
    // ─────────────────────────────────────────────────────────────────────────

    private void LogStartupDiagnostics(int fontPtSize)
    {
        Console.Error.WriteLine(
            $"[SDLRenderer] platform={RuntimeInformation.OSDescription} " +
            $"arch={RuntimeInformation.ProcessArchitecture}");

        Console.Error.WriteLine(
            $"[SDLRenderer] font='{_fontPath}' " +
            $"fontSize={fontPtSize}pt " +
            $"cell={_cellWidth}x{_cellHeight}px " +
            $"ascent={_ascent} descent={_descent} fontH={_fontHeight} lineSkip={_lineSkip}");

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
        Console.Error.WriteLine($"[SDLRenderer] pixelDensity={density:F2} displayScale={scale:F2}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VSync configuration
    // ─────────────────────────────────────────────────────────────────────────

    private int _vsyncValue = int.MinValue; // int.MinValue = never queried

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
        if (string.IsNullOrEmpty(envVal))
            return;

        int requested;
        if (envVal is "0" or "off" or "false")
            requested = SDL3.SDL.RendererVSyncDisabled;
        else if (envVal is "1" or "on" or "true")
            requested = 1;
        else if (string.Equals(envVal, "adaptive", StringComparison.OrdinalIgnoreCase))
            requested = SDL3.SDL.RendererVSyncAdaptive;
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

        SDL3.SDL.Color color = ToSdlColor(fgArgb);

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
            SdlGlyphRenderClass.FontCell  => CreateFontCellTexture((uint)ch, color),
            SdlGlyphRenderClass.Natural   => CreateNaturalTexture((uint)ch, color),
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
    // Font-cell texture (shade / block / unsupported box drawing)
    // ─────────────────────────────────────────────────────────────────────────

    private IntPtr CreateFontCellTexture(uint codepoint, SDL3.SDL.Color color)
    {
        IntPtr canvas = CreateFontCellSurface(codepoint, color);
        if (canvas == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            SDL3.SDL.SetSurfaceBlendMode(canvas, SDL3.SDL.BlendMode.Blend);

            IntPtr tex = CreateTextureFromSurface(canvas);
            if (tex != IntPtr.Zero)
            {
                SDL3.SDL.SetTextureBlendMode(tex, SDL3.SDL.BlendMode.Blend);
                _diagnostics.RecordNormalGlyphCreated();
            }
            return tex;
        }
        finally
        {
            SDL3.SDL.DestroySurface(canvas);
        }
    }

    private IntPtr CreateFontCellSurface(uint codepoint, SDL3.SDL.Color color)
    {
        IntPtr glyphSurf = SDL3.TTF.RenderGlyphBlended(_font, (ushort)codepoint, color);
        if (glyphSurf == IntPtr.Zero)
            return IntPtr.Zero;

        IntPtr canvas = SDL3.SDL.CreateSurface(_cellWidth, _cellHeight,
            SDL3.SDL.PixelFormat.ARGB8888);
        if (canvas == IntPtr.Zero)
        {
            SDL3.SDL.DestroySurface(glyphSurf);
            return IntPtr.Zero;
        }

        try
        {
            SDL3.SDL.FillSurfaceRect(canvas, IntPtr.Zero, 0u);
            SDL3.SDL.SetSurfaceBlendMode(glyphSurf, SDL3.SDL.BlendMode.None);
            GetCellBlitPlacement(codepoint, out int srcClipTop, out int dstX);
            BlitGlyphToCanvas(glyphSurf, canvas, srcClipTop, dstX, dstY: 0);
            return canvas;
        }
        catch
        {
            SDL3.SDL.DestroySurface(canvas);
            throw;
        }
        finally
        {
            SDL3.SDL.DestroySurface(glyphSurf);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Natural text texture
    // ─────────────────────────────────────────────────────────────────────────

    private IntPtr CreateNaturalTexture(uint codepoint, SDL3.SDL.Color color)
    {
        IntPtr surface = SDL3.TTF.RenderGlyphBlended(_font, (ushort)codepoint, color);
        if (surface == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            IntPtr tex = CreateTextureFromSurface(surface);
            if (tex != IntPtr.Zero)
            {
                SDL3.SDL.SetTextureBlendMode(tex, SDL3.SDL.BlendMode.Blend);
                _diagnostics.RecordNormalGlyphCreated();
            }
            return tex;
        }
        finally
        {
            SDL3.SDL.DestroySurface(surface);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cell blit helpers
    // ─────────────────────────────────────────────────────────────────────────
    // Placement maths and the clipped blit live in SdlFontCellPlacement, shared with
    // SDLGpuRenderer so both back-ends place font glyphs identically.

    private void GetCellBlitPlacement(uint codepoint, out int srcClipTop, out int dstX) =>
        SdlFontCellPlacement.Compute(_font, codepoint, _ascent, out srcClipTop, out dstX);

    private static void BlitGlyphToCanvas(
        IntPtr glyphSurf, IntPtr canvas, int srcClipTop, int dstX, int dstY) =>
        SdlFontCellPlacement.BlitToCanvas(glyphSurf, canvas, srcClipTop, dstX, dstY);

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
    // Colour helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SDL3.SDL.Color ToSdlColor(uint argb) =>
        new()
        {
            R = (byte)((argb >> 16) & 0xFF),
            G = (byte)((argb >>  8) & 0xFF),
            B = (byte)( argb        & 0xFF),
            A = 255,
        };

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

    public void Render(
        ScreenBuffer screenBuffer,
        uint regionX, uint regionY, uint regionWidth, uint regionHeight)
    {
        var frameSw = SdlRendererPerformanceDiagnostics.Enabled ? Stopwatch.StartNew() : null;

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

                // Underline cursor: restore original colours and draw a 2-px bar.
                if (isCursorCell && _cursorType < 100)
                {
                    var (origFg, origBg) = SdlPalette.DecodeAttr(attrByte);
                    DrawCellBackground(cellRect, origBg);

                    var underline = new SDL3.SDL.FRect
                    {
                        X = cellRect.X,
                        Y = cellRect.Y + cellRect.H - 2,
                        W = cellRect.W,
                        H = 2,
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
                GlyphPhase phase = GlyphPhase.ForCell((int)x, (int)y, _cellWidth, _cellHeight);
                IntPtr texture = GetOrCreateGlyphTexture(ch, fgArgb, phase);
                if (texture == IntPtr.Zero)
                    continue;

                // All glyph textures are rendered into the logical cell rect, 1:1.
                // Generated and composed box textures are exactly CellWidth × CellHeight.
                // Natural text textures are scaled to cell size at render time.
                SDL3.SDL.RenderTexture(_renderer, texture, IntPtr.Zero, in cellRect);
            }
        }

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

        if (frameSw != null)
        {
            frameSw.Stop();
            _diagnostics.RecordFrame(frameSw.Elapsed);
        }

        _diagnostics.MaybeReportSummary();
    }

    private void DrawCellBackground(SDL3.SDL.FRect rect, uint argb)
    {
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

            SDL3.TTF.Quit();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
