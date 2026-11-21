using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>
/// A grapheme cluster rasterised into its reserved cells: <see cref="Width"/> = cells x cell width, <see cref="Height"/>
/// = cell height, straight-alpha RGBA (R, G, B, A in memory). A monochrome glyph is white with coverage in alpha (the
/// renderer tints it with the foreground); a colour glyph (<see cref="IsColor"/>) keeps its own colours.
/// </summary>
internal sealed record SdlClusterGlyph(
    byte[] Rgba, int Width, int Height, int Cells, bool IsColor, SdlClusterCoverage Coverage,
    SdlFallbackFontRole? Role, string? FontPath, bool Scaled)
{
    /// <summary>Alpha of pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    internal byte AlphaAt(int x, int y) => Rgba[(y * Width + x) * 4 + 3];
}

/// <summary>
/// Shapes and rasterises whole grapheme clusters for both SDL back-ends (TV13m). Each cluster is resolved to ONE
/// primary font (<see cref="SdlClusterFontResolver"/>: base monospace, then the per-OS
/// <see cref="SdlFallbackFontChain"/>) and rendered as UTF-8 text with that font through SDL_ttf (HarfBuzz shaping:
/// combining marks, ZWJ ligatures, skin tones). SDL_ttf's own fallback list is never attached to these font handles,
/// because it splits ZWJ sequences and regional-indicator pairs (U0-A). The raster is then fitted into the cluster's
/// cells: emoji are centred on their ink, other text keeps the shared text baseline and is centred on its advance;
/// anything still too big is scaled down (area filter) and centred, and everything is clipped to the cells, so a
/// cluster never draws over its neighbours. A cluster no font covers becomes a .notdef box inside its cells.
/// <para>Results are cached by (cells, cluster text); the font set and sizes are fixed for the rasteriser's lifetime
/// (a density change builds a new rasteriser). Not thread-safe: one rendering thread per instance, like SDL_ttf.</para>
/// </summary>
internal sealed class SdlClusterRasterizer : IDisposable
{
    internal const int MaxCachedClusters = 2048;

    private static readonly SDL3.SDL.Color White = new() { R = 255, G = 255, B = 255, A = 255 };

    private readonly Slot[] _slots;
    private readonly SdlFallbackFontRole[] _roles;
    private readonly Func<int, int, bool> _hasGlyph;
    private readonly Dictionary<string, SdlClusterGlyph> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SdlClusterGlyph>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private bool _disposed;

    private sealed class Slot(SdlFallbackFont font)
    {
        public readonly SdlFallbackFont Font = font;
        public IntPtr Handle;
        public bool Failed;
        public float PointSize;
        public int Ascent, Height;
    }

    /// <summary>
    /// <paramref name="basePath"/> at <paramref name="textPointSize"/> (the size ordinary text is rasterised at) is the
    /// first font; <paramref name="chain"/> follows. <paramref name="textBaseline"/> is the row text glyphs sit on.
    /// </summary>
    public SdlClusterRasterizer(
        string basePath, float textPointSize, int cellWidth, int cellHeight, int textBaseline,
        IReadOnlyList<SdlFallbackFont> chain)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        TextBaseline = Math.Clamp(textBaseline, 1, cellHeight);
        TextPointSize = textPointSize;
        _lookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();

        var slots = new List<Slot> { new(new SdlFallbackFont(SdlFallbackFontRole.Base, basePath)) };
        foreach (SdlFallbackFont font in chain)
            if (!string.Equals(font.Path, basePath, StringComparison.OrdinalIgnoreCase)) slots.Add(new Slot(font));
        _slots = slots.ToArray();
        _roles = _slots.Select(s => s.Font.Role).ToArray();
        _hasGlyph = HasGlyph;

        if (!Open(_slots[0]))
            throw new InvalidOperationException($"[SDL] Could not open font '{basePath}': {SDL3.SDL.GetError()}");

        // One reference emoji tells whether this machine decodes colour glyphs (COLR/CBDT/sbix through FreeType).
        int emoji = Array.IndexOf(_roles, SdlFallbackFontRole.Emoji);
        if (emoji >= 0 && Open(_slots[emoji]) && SDL3.TTF.FontHasGlyph(_slots[emoji].Handle, 0x1F600))
            ColorGlyphsAvailable = Rasterize("\U0001F600", 2).IsColor;
    }

    public int CellWidth { get; }
    public int CellHeight { get; }
    public int TextBaseline { get; }
    public float TextPointSize { get; }

    /// <summary>True when an emoji font is present and its reference glyph rasterised in colour.</summary>
    public bool ColorGlyphsAvailable { get; }

    /// <summary>The fonts clusters resolve against, in chain order (base first).</summary>
    public IReadOnlyList<SdlFallbackFont> Fonts => _slots.Select(s => s.Font).ToArray();

    /// <summary>Point size a chain font was opened at (fitted for emoji and CJK), or 0 while unopened. Diagnostics.</summary>
    internal float PointSizeOf(int fontIndex) => _slots[fontIndex].PointSize;

    // Diagnostics.
    public int Rasterized { get; private set; }
    public int ScaledClusters { get; private set; }
    public int PartialClusters { get; private set; }
    public int MissingClusters { get; private set; }
    public int CacheResets { get; private set; }
    public int CachedClusters => _cache.Count;

    /// <summary>The font index (into <see cref="Fonts"/>) <paramref name="cluster"/> resolves to, or -1 (missing).</summary>
    public int ResolveFont(ReadOnlySpan<char> cluster, int cells, out SdlClusterCoverage coverage) =>
        SdlClusterFontResolver.Resolve(cluster, cells, _roles, _hasGlyph, out coverage);

    /// <summary>The cached raster of <paramref name="cluster"/> in <paramref name="cells"/> cells; rasterised on first use.</summary>
    public SdlClusterGlyph Get(ReadOnlySpan<char> cluster, int cells)
    {
        cells = Math.Clamp(cells, 1, TScreenChar.MaxCellWidth);
        int length = cluster.Length + 1;
        Span<char> key = length <= 256 ? stackalloc char[length] : new char[length];
        key[0] = (char)('0' + cells);
        cluster.CopyTo(key[1..]);
        if (_lookup.TryGetValue(key, out SdlClusterGlyph? cached)) return cached;

        if (_cache.Count >= MaxCachedClusters)
        {
            _cache.Clear();
            CacheResets++;
        }
        SdlClusterGlyph glyph = Rasterize(cluster, cells);
        _cache[key.ToString()] = glyph;
        return glyph;
    }

    /// <summary>Rasterises <paramref name="cluster"/> into <paramref name="cells"/> cells (no cache).</summary>
    public SdlClusterGlyph Rasterize(ReadOnlySpan<char> cluster, int cells)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Rasterized++;
        int width = cells * CellWidth, height = CellHeight;
        int font = ResolveFont(cluster, cells, out SdlClusterCoverage coverage);
        if (coverage == SdlClusterCoverage.Partial) PartialClusters++;
        if (font < 0 || !Open(_slots[font]))
        {
            MissingClusters++;
            return NotDef(cells, SdlClusterCoverage.Missing);
        }

        Slot slot = _slots[font];
        var canvas = new byte[width * height * 4];
        Surface? s = RenderText(slot.Handle, cluster.ToString());
        if (s == null) return new SdlClusterGlyph(canvas, width, height, cells, false, coverage, slot.Font.Role, slot.Font.Path, false);
        if (s.InkWidth <= 0)
            return new SdlClusterGlyph(canvas, width, height, cells, false, coverage, slot.Font.Role, slot.Font.Path, false);

        bool emoji = slot.Font.Role == SdlFallbackFontRole.Emoji;
        bool scaled = false;
        if (s.InkWidth <= width && s.InkHeight <= height)
        {
            int dx, dy;
            if (emoji)
            {
                dx = (width - s.InkWidth) / 2 - s.InkX0;
                dy = (height - s.InkHeight) / 2 - s.InkY0;
            }
            else
            {
                // SDL_ttf puts the baseline at the font ascent, below any glyph that rises above it.
                int baselineRow = s.Height > slot.Height ? s.Height - (slot.Height - slot.Ascent) : slot.Ascent;
                dx = (width - s.Width) / 2;
                dy = TextBaseline - baselineRow;
                // Keep the ink inside the cells (combining stacks, tall CJK).
                dx = Math.Clamp(dx, -s.InkX0, width - (s.InkX0 + s.InkWidth));
                dy = Math.Clamp(dy, -s.InkY0, height - (s.InkY0 + s.InkHeight));
            }
            Blit(s, canvas, width, height, dx, dy);
        }
        else
        {
            scaled = true;
            ScaledClusters++;
            ScaleInto(s, canvas, width, height);
        }

        bool color = IsColor(canvas);
        if (!color) Whiten(canvas);
        return new SdlClusterGlyph(canvas, width, height, cells, color, coverage, slot.Font.Role, slot.Font.Path, scaled);
    }

    /// <summary>A monochrome .notdef box inset in the cluster's cells (no font covers the cluster).</summary>
    internal SdlClusterGlyph NotDef(int cells, SdlClusterCoverage coverage)
    {
        int width = cells * CellWidth, height = CellHeight;
        var canvas = new byte[width * height * 4];
        int x0 = width >= 6 ? 1 : 0, x1 = width - 1 - x0;
        int y0 = Math.Max(0, height / 6), y1 = Math.Clamp(TextBaseline, y0 + 1, height - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (y == y0 || y == y1 || x == x0 || x == x1)
                {
                    int o = (y * width + x) * 4;
                    canvas[o] = canvas[o + 1] = canvas[o + 2] = canvas[o + 3] = 255;
                }
        return new SdlClusterGlyph(canvas, width, height, cells, false, coverage, null, null, false);
    }

    // --- Fonts ---------------------------------------------------------------

    private bool HasGlyph(int font, int scalar)
    {
        Slot slot = _slots[font];
        return Open(slot) && SDL3.TTF.FontHasGlyph(slot.Handle, (uint)scalar);
    }

    private bool Open(Slot slot)
    {
        if (slot.Handle != IntPtr.Zero) return true;
        if (slot.Failed) return false;
        slot.Handle = SDL3.TTF.OpenFont(slot.Font.Path, TextPointSize);
        if (slot.Handle == IntPtr.Zero)
        {
            slot.Failed = true;
            return false;
        }
        slot.PointSize = TextPointSize;
        switch (slot.Font.Role)
        {
            case SdlFallbackFontRole.Emoji:
                // U0-A: the emoji font at the text size overflows two cells; fit the reference emoji's ink.
                FitSize(slot, "\U0001F600", 2 * CellWidth - (CellWidth >= 4 ? 2 : 0), CellHeight - (CellHeight >= 10 ? 2 : 0));
                break;
            case SdlFallbackFontRole.Cjk:
                FitSize(slot, "\u6F22", 2 * CellWidth, CellHeight);
                break;
        }
        slot.Ascent = SDL3.TTF.GetFontAscent(slot.Handle);
        slot.Height = SDL3.TTF.GetFontHeight(slot.Handle);
        return true;
    }

    // Largest size (0.5 pt steps, down to half the text size) at which the reference's ink fits maxW x maxH.
    private void FitSize(Slot slot, string reference, int maxW, int maxH)
    {
        int scalar = char.ConvertToUtf32(reference, 0);
        if (!SDL3.TTF.FontHasGlyph(slot.Handle, (uint)scalar)) return;
        for (float size = TextPointSize; size >= TextPointSize / 2; size -= 0.5f)
        {
            if (size != slot.PointSize && !SDL3.TTF.SetFontSize(slot.Handle, size)) return;
            slot.PointSize = size;
            Surface? s = RenderText(slot.Handle, reference);
            if (s == null) return;
            if (s.InkWidth <= maxW && s.InkHeight <= maxH) return;
        }
    }

    // --- Raster helpers ------------------------------------------------------

    private sealed class Surface
    {
        public required byte[] Rgba;
        public int Width, Height;
        public int InkX0, InkY0, InkWidth, InkHeight;
    }

    private static Surface? RenderText(IntPtr font, string text)
    {
        IntPtr surface = SDL3.TTF.RenderTextBlended(font, text, 0, White);
        if (surface == IntPtr.Zero) return null;
        IntPtr argb = SDL3.SDL.ConvertSurface(surface, SDL3.SDL.PixelFormat.ARGB8888);
        SDL3.SDL.DestroySurface(surface);
        if (argb == IntPtr.Zero) return null;
        try
        {
            SDL3.SDL.Surface s = Marshal.PtrToStructure<SDL3.SDL.Surface>(argb);
            int w = s.Width, h = s.Height, pitch = s.Pitch;
            var raw = new byte[pitch * h];
            Marshal.Copy(s.Pixels, raw, 0, raw.Length);
            var rgba = new byte[w * h * 4];
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // ARGB8888 on little-endian: memory order B, G, R, A.
                    int si = y * pitch + x * 4, di = (y * w + x) * 4;
                    byte a = raw[si + 3];
                    if (a == 0) continue;
                    rgba[di] = raw[si + 2];
                    rgba[di + 1] = raw[si + 1];
                    rgba[di + 2] = raw[si];
                    rgba[di + 3] = a;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            var result = new Surface { Rgba = rgba, Width = w, Height = h };
            if (x1 >= 0)
            {
                result.InkX0 = x0;
                result.InkY0 = y0;
                result.InkWidth = x1 - x0 + 1;
                result.InkHeight = y1 - y0 + 1;
            }
            return result;
        }
        finally
        {
            SDL3.SDL.DestroySurface(argb);
        }
    }

    // Copies the surface at offset (dx, dy), clipped to the canvas.
    private static void Blit(Surface s, byte[] canvas, int width, int height, int dx, int dy)
    {
        for (int y = 0; y < s.Height; y++)
        {
            int cy = y + dy;
            if ((uint)cy >= (uint)height) continue;
            for (int x = 0; x < s.Width; x++)
            {
                int cx = x + dx;
                if ((uint)cx >= (uint)width) continue;
                int si = (y * s.Width + x) * 4;
                if (s.Rgba[si + 3] == 0) continue;
                s.Rgba.AsSpan(si, 4).CopyTo(canvas.AsSpan((cy * width + cx) * 4));
            }
        }
    }

    // Area-filtered (premultiplied) downscale of the ink box to fit width x height, centred.
    private static void ScaleInto(Surface s, byte[] canvas, int width, int height)
    {
        double scale = Math.Min((double)width / s.InkWidth, (double)height / s.InkHeight);
        int dw = Math.Clamp((int)Math.Floor(s.InkWidth * scale), 1, width);
        int dh = Math.Clamp((int)Math.Floor(s.InkHeight * scale), 1, height);
        int ox = (width - dw) / 2, oy = (height - dh) / 2;
        double sx = (double)s.InkWidth / dw, sy = (double)s.InkHeight / dh;
        for (int y = 0; y < dh; y++)
        {
            double fy0 = y * sy, fy1 = fy0 + sy;
            for (int x = 0; x < dw; x++)
            {
                double fx0 = x * sx, fx1 = fx0 + sx;
                double r = 0, g = 0, b = 0, a = 0, area = 0;
                for (int iy = (int)fy0; iy < Math.Min(s.InkHeight, (int)Math.Ceiling(fy1)); iy++)
                {
                    double wy = Math.Min(fy1, iy + 1) - Math.Max(fy0, iy);
                    for (int ix = (int)fx0; ix < Math.Min(s.InkWidth, (int)Math.Ceiling(fx1)); ix++)
                    {
                        double wgt = wy * (Math.Min(fx1, ix + 1) - Math.Max(fx0, ix));
                        int si = ((s.InkY0 + iy) * s.Width + s.InkX0 + ix) * 4;
                        double pa = s.Rgba[si + 3] / 255.0;
                        r += s.Rgba[si] * pa * wgt;
                        g += s.Rgba[si + 1] * pa * wgt;
                        b += s.Rgba[si + 2] * pa * wgt;
                        a += pa * wgt;
                        area += wgt;
                    }
                }
                if (a <= 0 || area <= 0) continue;
                int di = ((oy + y) * width + ox + x) * 4;
                canvas[di] = (byte)Math.Clamp(Math.Round(r / a), 0, 255);
                canvas[di + 1] = (byte)Math.Clamp(Math.Round(g / a), 0, 255);
                canvas[di + 2] = (byte)Math.Clamp(Math.Round(b / a), 0, 255);
                canvas[di + 3] = (byte)Math.Clamp(Math.Round(a / area * 255), 0, 255);
            }
        }
    }

    /// <summary>
    /// True when any inked pixel is not white: rendered with a white foreground, a monochrome glyph is white
    /// everywhere, so other colours can only come from the font (a colour glyph).
    /// </summary>
    internal static bool IsColor(ReadOnlySpan<byte> rgba)
    {
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 32) continue;
            if (rgba[i] < 240 || rgba[i + 1] < 240 || rgba[i + 2] < 240) return true;
        }
        return false;
    }

    // A monochrome raster is pure coverage: white RGB, so tinting by the foreground is exact.
    private static void Whiten(byte[] rgba)
    {
        for (int i = 0; i + 3 < rgba.Length; i += 4)
            if (rgba[i + 3] != 0) rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
    }

    /// <summary>Font handles this rasteriser holds open (diagnostics: leak checks).</summary>
    public int OpenFonts => _slots.Count(s => s.Handle != IntPtr.Zero);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Slot slot in _slots)
        {
            if (slot.Handle != IntPtr.Zero) SDL3.TTF.CloseFont(slot.Handle);
            slot.Handle = IntPtr.Zero;
        }
        _cache.Clear();
    }
}
