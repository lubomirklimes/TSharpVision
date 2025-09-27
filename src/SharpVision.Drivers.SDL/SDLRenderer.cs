using SharpVision;
using SharpVision.Drivers;
using System.Runtime.InteropServices;

namespace SharpVision.Drivers.SDL;

public class SDLRenderer : IDisposable, IRenderer
{
    // ---- Glyph texture cache -------------------------------------------
    // Key: (codepoint, ARGB foreground color). Value: SDL texture handle.
    // Bounded at MaxCacheEntries; evicted all-at-once when the cap is hit.
    private const int MaxCacheEntries = 4096;

    private struct GlyphKey : IEquatable<GlyphKey>
    {
        public uint Codepoint;
        public uint FgColor;   // 0xAARRGGBB
        public bool Equals(GlyphKey o) => Codepoint == o.Codepoint && FgColor == o.FgColor;
        public override bool Equals(object? obj) => obj is GlyphKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(Codepoint, FgColor);
    }

    private readonly Dictionary<GlyphKey, IntPtr> _glyphCache = new();

    // ---- Font point size (configurable; metrics derived at runtime) ----
    private const int FontPtSize = 20;

    // ---- State ---------------------------------------------------------
    private bool disposedValue;
    private readonly IntPtr _renderer;
    private int _cellWidth  = 12;   // overwritten in constructor from font metrics
    private int _cellHeight = 26;   // overwritten in constructor from font metrics
    private IntPtr _font = IntPtr.Zero;

    // ---- Cursor state --------------------------------------------------
    // Cursor position in character-grid coordinates.
    // cursorType: 0 = hidden; >= 100 = block; other non-zero = underline.
    private int _cursorX;
    private int _cursorY;
    private ushort _cursorType;   // 0 means hidden

    /// <summary>
    /// Updates the cursor position and visibility for the next Render call.
    /// Called by <see cref="SDLDriver"/> from SetCaretPosition / SetCursorType.
    /// </summary>
    public void SetCursor(int x, int y, ushort cursorType)
    {
        _cursorX    = x;
        _cursorY    = y;
        _cursorType = cursorType;
    }

    /// <summary>Cell width in pixels, derived from the loaded font's glyph advance.</summary>
    public int CellWidth  => _cellWidth;
    /// <summary>Cell height in pixels, derived from the loaded font's line skip.</summary>
    public int CellHeight => _cellHeight;

    // ---- Font probing --------------------------------------------------
    /// <summary>
    /// Returns the first font path that exists on the current platform,
    /// or <c>null</c> if none is found. Does not open or load the font.
    /// </summary>
    public static string? ProbeFontPath()
    {
        IEnumerable<string> candidates;

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "Fonts");
            // Cascadia Mono before Consolas — better box-drawing glyph coverage.
            candidates = new[]
            {
                Path.Combine(winFonts, "CascadiaMono-Regular.ttf"),
                Path.Combine(winFonts, "consola.ttf"),
                Path.Combine(winFonts, "cour.ttf"),
                Path.Combine(winFonts, "lucon.ttf"),
            };
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates = new[]
            {
                "/System/Library/Fonts/Menlo.ttc",
                "/Library/Fonts/Menlo.ttc",
                "/System/Library/Fonts/Monaco.ttf",
                "/System/Library/Fonts/Supplemental/Menlo.ttc",
            };
        }
        else
        {
            // Linux / other Unix
            candidates = new[]
            {
                "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                "/usr/share/fonts/truetype/liberation2/LiberationMono-Regular.ttf",
                "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
                "/usr/share/fonts/dejavu/DejaVuSansMono.ttf",
            };
        }

        foreach (string path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public SDLRenderer(IntPtr renderer) : this(renderer, null) { }

    /// <summary>
    /// Searches system font directories for a font whose file name contains
    /// a normalized form of <paramref name="fontName"/> (spaces, hyphens, and
    /// underscores stripped, case-insensitive).
    /// Returns the first matching font file path, or <c>null</c> if none is found.
    /// </summary>
    public static string? ProbeFontPathByName(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return null;

        // If the user entered a direct path, use it first.
        if (File.Exists(fontName))
            return Path.GetFullPath(fontName);

        // If the user entered "Perfect DOS VGA 437.ttf", compare as "Perfect DOS VGA 437".
        string fontNameWithoutExtension = Path.GetFileNameWithoutExtension(fontName);

        string normalized = NormalizeFontName(fontNameWithoutExtension);

        IEnumerable<string> fontDirs;

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "Fonts");

            string userFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "Fonts");

            fontDirs = new[]
            {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            winFonts,
            userFonts
        };
        }
        else if (OperatingSystem.IsMacOS())
        {
            fontDirs = new[]
            {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            "/System/Library/Fonts",
            "/Library/Fonts",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Fonts")
        };
        }
        else
        {
            fontDirs = new[]
            {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fonts"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "fonts")
        };
        }

        foreach (string dir in fontDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in SafeEnumerateFontFiles(dir))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                string normalizedStem = NormalizeFontName(stem);

                if (normalizedStem.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalizedStem.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(normalizedStem, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }

    private static string NormalizeFontName(string value)
    {
        return value
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace(".", "")
            .ToLowerInvariant();
    }

    private static IEnumerable<string> SafeEnumerateFontFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (string subDir in subDirs)
                pending.Push(subDir);
        }
    }

    public SDLRenderer(IntPtr renderer, string? fontName)
    {
        _renderer = renderer;

        if (!SDL3.TTF.Init())
            throw new Exception("SDL_ttf could not initialize! " + SDL3.SDL.GetError());

        string? fontPath = null;
        if (!string.IsNullOrWhiteSpace(fontName))
        {
            fontPath = ProbeFontPathByName(fontName);
            if (fontPath == null)
                Console.Error.WriteLine($"Warning: font '{fontName}' not found; falling back to default.");
        }

        fontPath ??= ProbeFontPath();
        if (fontPath == null)
            throw new Exception("No suitable monospace font found on this system.");

        _font = SDL3.TTF.OpenFont(fontPath, FontPtSize);
        if (_font == IntPtr.Zero)
            throw new Exception($"Failed to load font '{fontPath}': " + SDL3.SDL.GetError());

        // Derive cell metrics from the loaded font.
        ComputeMetrics(_font, out _cellWidth, out _cellHeight);

        Console.WriteLine($"Cell metrics: {_cellWidth}x{_cellHeight} pixels (derived from font '{fontPath}')");
        _cellWidth = 14;
        _cellHeight = 26;
    }

    // ---- Cell metrics --------------------------------------------------

    /// <summary>
    /// Computes cell width and height from SDL_ttf font metrics.
    /// CellHeight = font line skip (recommended line spacing).
    /// CellWidth  = maximum advance of a representative monospace sample.
    /// Safe to call without an SDL window; only requires SDL_ttf.
    /// </summary>
    internal static void ComputeMetrics(IntPtr font, out int cellWidth, out int cellHeight)
    {
        // CellHeight: font line skip is the recommended number of pixels
        // between successive lines of text — the correct cell height.
        int lineSkip = SDL3.TTF.GetFontLineSkip(font);
        int fontH    = SDL3.TTF.GetFontHeight(font);
        cellHeight   = lineSkip > 0 ? lineSkip : (fontH > 0 ? fontH : 26);

        // CellWidth: advance of representative characters.  For a well-formed
        // monospace font all advances are identical, but we take the maximum
        // over a sample set as a safety measure (avoids a zero result if one
        // glyph happens to be missing).
        int maxAdv = 0;
        foreach (ushort cp in (ushort[])['M', 'W', '0', 'X'])
        {
            if (SDL3.TTF.GetGlyphMetrics(font, cp,
                    out _, out _, out _, out _, out int adv) && adv > maxAdv)
                maxAdv = adv;
        }
        // Fallback: 60 % of cellHeight (typical monospace aspect ratio).
        cellWidth = maxAdv > 0 ? maxAdv : Math.Max(8, (cellHeight * 6 + 9) / 10);
    }

    /// <summary>
    /// Headless helper: probe font, init TTF, compute and return cell metrics,
    /// then tear down TTF.  Returns <c>null</c> if no font is found or TTF
    /// initialisation fails — safe to call from smoke tests.
    /// </summary>
    public static (int CellWidth, int CellHeight)? ProbeMetrics()
    {
        string? fontPath = ProbeFontPath();
        if (fontPath == null) return null;

        try
        {
            if (!SDL3.TTF.Init()) return null;
            IntPtr f = SDL3.TTF.OpenFont(fontPath, FontPtSize);
            if (f == IntPtr.Zero) { SDL3.TTF.Quit(); return null; }

            ComputeMetrics(f, out int cw, out int ch);

            SDL3.TTF.CloseFont(f);
            SDL3.TTF.Quit();
            return (cw, ch);
        }
        catch
        {
            try { SDL3.TTF.Quit(); } catch { }
            return null;
        }
    }

    // ---- Glyph cache helpers -------------------------------------------

    private IntPtr GetOrCreateGlyphTexture(uint codepoint, uint fgArgb)
    {
        var key = new GlyphKey { Codepoint = codepoint, FgColor = fgArgb };
        if (_glyphCache.TryGetValue(key, out IntPtr cached))
            return cached;

        // Cache cap: evict all entries (simple clear-on-overflow for v1).
        if (_glyphCache.Count >= MaxCacheEntries)
            ClearGlyphCache();

        byte r = (byte)((fgArgb >> 16) & 0xFF);
        byte g = (byte)((fgArgb >>  8) & 0xFF);
        byte b = (byte)( fgArgb        & 0xFF);

        var color = new SDL3.SDL.Color { R = r, G = g, B = b, A = 255 };
        IntPtr surface = SDL3.TTF.RenderGlyphBlended(_font, (ushort)codepoint, color);
        if (surface == IntPtr.Zero) return IntPtr.Zero;

        IntPtr texture = SDL3.SDL.CreateTextureFromSurface(_renderer, surface);
        SDL3.SDL.DestroySurface(surface);

        if (texture != IntPtr.Zero)
            _glyphCache[key] = texture;

        return texture;
    }

    private void ClearGlyphCache()
    {
        foreach (var tex in _glyphCache.Values)
            if (tex != IntPtr.Zero) SDL3.SDL.DestroyTexture(tex);
        _glyphCache.Clear();
    }

    // ---- Rendering -----------------------------------------------------

    public void Render(ScreenBuffer screenBuffer, uint regionX, uint regionY, uint regionWidth, uint regionHeight)
    {
        // Full clear with black.
        SDL3.SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL3.SDL.RenderClear(_renderer);

        for (uint y = regionY; y < regionY + regionHeight; y++)
        {
            for (uint x = regionX; x < regionX + regionWidth; x++)
            {
                TScreenChar cell = screenBuffer.GetChar(x, y);

                // Decode 16-color VGA attribute from the raw attribute byte.
                byte attrByte = (byte)(cell.Attr & 0xFF);
                var (fgArgb, bgArgb) = SdlPalette.DecodeAttr(attrByte);

                // Cursor: render the cursor cell with fg/bg swapped so the
                // cursor is always visible against any background color.
                bool isCursorCell = _cursorType != 0
                    && (int)x == _cursorX && (int)y == _cursorY;
                if (isCursorCell)
                    (fgArgb, bgArgb) = (bgArgb, fgArgb);

                // --- Background fill (per-cell rectangle) ---------------
                SDL3.SDL.SetRenderDrawColor(_renderer,
                    (byte)((bgArgb >> 16) & 0xFF),
                    (byte)((bgArgb >>  8) & 0xFF),
                    (byte)( bgArgb        & 0xFF),
                    255);

                var rect = new SDL3.SDL.FRect
                {
                    X = (int)(x * _cellWidth),
                    Y = (int)(y * _cellHeight),
                    W = _cellWidth,
                    H = _cellHeight
                };

                // For underline-style cursors render only the bottom strip;
                // for block cursors or normal cells fill the entire cell.
                if (isCursorCell && _cursorType < 100)
                {
                    // Restore normal bg for the top portion of the cursor cell.
                    var (origFg, origBg) = SdlPalette.DecodeAttr(attrByte);
                    SDL3.SDL.SetRenderDrawColor(_renderer,
                        (byte)((origBg >> 16) & 0xFF),
                        (byte)((origBg >>  8) & 0xFF),
                        (byte)( origBg        & 0xFF),
                        255);
                    SDL3.SDL.RenderFillRect(_renderer, rect);

                    // Draw the underline bar (bottom 2 pixels).
                    var underline = new SDL3.SDL.FRect
                    {
                        X = rect.X,
                        Y = rect.Y + rect.H - 2,
                        W = rect.W,
                        H = 2,
                    };
                    // Inverted fg colour for the underline.
                    SDL3.SDL.SetRenderDrawColor(_renderer,
                        (byte)((origFg >> 16) & 0xFF),
                        (byte)((origFg >>  8) & 0xFF),
                        (byte)( origFg        & 0xFF),
                        255);
                    SDL3.SDL.RenderFillRect(_renderer, underline);

                    // Re-use original colours for the glyph on top.
                    (fgArgb, bgArgb) = (origFg, origBg);
                }
                else
                {
                    SDL3.SDL.RenderFillRect(_renderer, rect);
                }

                // --- Glyph (cached texture) ----------------------------
                char ch = cell.Character;
                if (ch != ' ' && ch != '\0')
                {
                    IntPtr texture = GetOrCreateGlyphTexture((uint)ch, fgArgb);
                    if (texture != IntPtr.Zero)
                    {
                        var dstRect = new SDL3.SDL.FRect
                        {
                            X = rect.X,
                            Y = rect.Y,
                            W = _cellWidth,
                            H = _cellHeight
                        };
                        SDL3.SDL.RenderTexture(_renderer, texture, IntPtr.Zero, ref dstRect);
                    }
                }
            }
        }

        SDL3.SDL.RenderPresent(_renderer);
    }

    // ---- Disposal ------------------------------------------------------

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                ClearGlyphCache();
                if (_font != IntPtr.Zero)
                {
                    SDL3.TTF.CloseFont(_font);
                    _font = IntPtr.Zero;
                }
                SDL3.TTF.Quit();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
