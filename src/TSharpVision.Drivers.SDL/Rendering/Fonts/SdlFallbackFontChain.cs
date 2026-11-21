namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>What a font in the cluster fallback chain is for; steers order (emoji fonts move with presentation) and fitting.</summary>
internal enum SdlFallbackFontRole
{
    /// <summary>The configured monospace text font.</summary>
    Base,
    /// <summary>A colour emoji font (fitted size, glyphs centred on their ink).</summary>
    Emoji,
    /// <summary>An outline CJK font (fitted so a 2-cell ideograph fits).</summary>
    Cjk,
    /// <summary>A monochrome symbol font.</summary>
    Symbol,
}

/// <summary>One fallback font: its role and file path (a .ttc opens its first face).</summary>
internal readonly record struct SdlFallbackFont(SdlFallbackFontRole Role, string Path);

/// <summary>
/// The per-OS chain of fallback fonts a grapheme cluster is resolved against after the base font (TV13m). Nothing is
/// bundled: every entry is a system font path, used only when the file exists. Windows order (verified on this host,
/// U0-A): <c>seguiemj.ttf</c> (colour emoji, COLR) -> <c>YuGothM.ttc</c> / <c>msyh.ttc</c> (outline CJK; <c>msgothic.ttc</c>
/// is deliberately absent: its small bitmap strikes render corrupted) -> <c>seguisym.ttf</c>. macOS and Linux lists are
/// candidates, not verified on those systems.
/// <para>
/// Override with the environment variable <see cref="EnvironmentVariable"/>: a <c>;</c>-separated list of
/// <c>role=path</c> entries (roles <c>emoji</c>, <c>cjk</c>, <c>symbol</c>), in chain order, replacing the defaults;
/// <c>none</c> disables every fallback (the base font alone).
/// </para>
/// </summary>
internal static class SdlFallbackFontChain
{
    /// <summary>Overrides the default chain (see the class summary).</summary>
    internal const string EnvironmentVariable = "TSHARPVISION_SDL_FALLBACK_FONTS";

    /// <summary>The chain for this machine: the override when set, otherwise the existing default candidates.</summary>
    internal static IReadOnlyList<SdlFallbackFont> Resolve() =>
        Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable), DefaultCandidates(), File.Exists);

    /// <summary>
    /// The chain from an override string (null or blank: <paramref name="defaults"/>) keeping only files that exist,
    /// and at most one entry per path.
    /// </summary>
    internal static IReadOnlyList<SdlFallbackFont> Resolve(
        string? overrideValue, IReadOnlyList<SdlFallbackFont> defaults, Func<string, bool> exists)
    {
        IReadOnlyList<SdlFallbackFont> source = string.IsNullOrWhiteSpace(overrideValue) ? defaults : Parse(overrideValue);
        var chain = new List<SdlFallbackFont>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool cjkFound = false;
        foreach (SdlFallbackFont font in source)
        {
            // Defaults list several CJK candidates; the first one present is enough.
            if (ReferenceEquals(source, defaults) && font.Role == SdlFallbackFontRole.Cjk && cjkFound) continue;
            if (!exists(font.Path) || !seen.Add(font.Path)) continue;
            if (font.Role == SdlFallbackFontRole.Cjk) cjkFound = true;
            chain.Add(font);
        }
        return chain;
    }

    /// <summary>Parses an override value; unknown roles and empty entries are ignored, <c>none</c> yields an empty chain.</summary>
    internal static IReadOnlyList<SdlFallbackFont> Parse(string value)
    {
        var list = new List<SdlFallbackFont>();
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return list;
        foreach (string raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            string path = raw[(eq + 1)..].Trim().Trim('"');
            if (path.Length == 0) continue;
            SdlFallbackFontRole? role = raw[..eq].Trim().ToLowerInvariant() switch
            {
                "emoji" => SdlFallbackFontRole.Emoji,
                "cjk" => SdlFallbackFontRole.Cjk,
                "symbol" => SdlFallbackFontRole.Symbol,
                _ => null,
            };
            if (role is { } r) list.Add(new SdlFallbackFont(r, path));
        }
        return list;
    }

    /// <summary>Default candidates for the current OS, in chain order (existence is checked by <see cref="Resolve()"/>).</summary>
    internal static IReadOnlyList<SdlFallbackFont> DefaultCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            string fonts = System.IO.Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts");
            return new SdlFallbackFont[]
            {
                new(SdlFallbackFontRole.Emoji, System.IO.Path.Combine(fonts, "seguiemj.ttf")),
                new(SdlFallbackFontRole.Cjk, System.IO.Path.Combine(fonts, "YuGothM.ttc")),
                new(SdlFallbackFontRole.Cjk, System.IO.Path.Combine(fonts, "msyh.ttc")),
                new(SdlFallbackFontRole.Symbol, System.IO.Path.Combine(fonts, "seguisym.ttf")),
            };
        }
        if (OperatingSystem.IsMacOS())
        {
            // PENDING on macOS: Apple Color Emoji is sbix (fixed bitmap strikes); scaling unverified (U0-A K1g).
            return new SdlFallbackFont[]
            {
                new(SdlFallbackFontRole.Emoji, "/System/Library/Fonts/Apple Color Emoji.ttc"),
                new(SdlFallbackFontRole.Cjk, "/System/Library/Fonts/Hiragino Sans GB.ttc"),
                new(SdlFallbackFontRole.Cjk, "/System/Library/Fonts/PingFang.ttc"),
                new(SdlFallbackFontRole.Symbol, "/System/Library/Fonts/Apple Symbols.ttf"),
            };
        }
        // PENDING on Linux: Noto Color Emoji is CBDT (one 109 px strike); scaling unverified (U0-A K1g).
        return new SdlFallbackFont[]
        {
            new(SdlFallbackFontRole.Emoji, "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf"),
            new(SdlFallbackFontRole.Emoji, "/usr/share/fonts/noto/NotoColorEmoji.ttf"),
            new(SdlFallbackFontRole.Emoji, "/usr/share/fonts/google-noto-emoji/NotoColorEmoji.ttf"),
            new(SdlFallbackFontRole.Cjk, "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
            new(SdlFallbackFontRole.Cjk, "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc"),
            new(SdlFallbackFontRole.Cjk, "/usr/share/fonts/google-noto-cjk/NotoSansCJK-Regular.ttc"),
            new(SdlFallbackFontRole.Symbol, "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
            new(SdlFallbackFontRole.Symbol, "/usr/share/fonts/TTF/DejaVuSans.ttf"),
        };
    }
}

/// <summary>How well the font picked for a cluster covers it.</summary>
internal enum SdlClusterCoverage
{
    /// <summary>The font has every scalar that is not ignorable.</summary>
    Full,
    /// <summary>No font has them all; the font has the cluster's first scalar and draws its own .notdef for the rest, inside the cluster's cells.</summary>
    Partial,
    /// <summary>No font has even the first scalar: a .notdef box is drawn over the cluster's cells.</summary>
    Missing,
}

/// <summary>
/// Picks ONE primary font for a whole cluster (TV13m, U0-A: SDL_ttf's own fallback splits ZWJ/flag/VS clusters, so the
/// cluster must be shaped by a single font that covers it). Pure: font coverage comes through a delegate.
/// </summary>
internal static class SdlClusterFontResolver
{
    /// <summary>
    /// Index into <paramref name="roles"/> of the font for <paramref name="cluster"/>, or -1 when coverage is
    /// <see cref="SdlClusterCoverage.Missing"/>. Order: the chain order, with emoji fonts moved first for emoji
    /// presentation and last for text presentation (VS15); a font covers the cluster when
    /// <paramref name="hasGlyph"/>(font, scalar) holds for every scalar that is not
    /// <see cref="SdlClusterClassifier.IsIgnorableForCoverage"/>.
    /// </summary>
    internal static int Resolve(
        ReadOnlySpan<char> cluster, int cells, IReadOnlyList<SdlFallbackFontRole> roles,
        Func<int, int, bool> hasGlyph, out SdlClusterCoverage coverage)
    {
        SdlClusterPresentation presentation = SdlClusterClassifier.Classify(cluster, cells);
        int count = roles.Count;
        Span<int> order = count <= 32 ? stackalloc int[count] : new int[count];
        int n = 0;
        if (presentation == SdlClusterPresentation.Emoji)
            for (int i = 0; i < count; i++) if (roles[i] == SdlFallbackFontRole.Emoji) order[n++] = i;
        for (int i = 0; i < count; i++)
            if (roles[i] != SdlFallbackFontRole.Emoji || presentation == SdlClusterPresentation.Default) order[n++] = i;
        if (presentation == SdlClusterPresentation.Text)
            for (int i = 0; i < count; i++) if (roles[i] == SdlFallbackFontRole.Emoji) order[n++] = i;

        int firstScalar = -1;
        for (int i = 0; i < cluster.Length;)
        {
            int s = SdlClusterClassifier.NextScalar(cluster, ref i);
            if (!SdlClusterClassifier.IsIgnorableForCoverage(s)) { firstScalar = s; break; }
        }
        if (firstScalar < 0)
        {
            // Only ignorables (a lone ZWJ, a stray selector): nothing to draw; the first font shapes it to nothing.
            coverage = SdlClusterCoverage.Full;
            return count > 0 ? order[0] : -1;
        }

        foreach (int font in order)
        {
            bool all = true;
            for (int i = 0; i < cluster.Length && all;)
            {
                int s = SdlClusterClassifier.NextScalar(cluster, ref i);
                if (!SdlClusterClassifier.IsIgnorableForCoverage(s) && !hasGlyph(font, s)) all = false;
            }
            if (all) { coverage = SdlClusterCoverage.Full; return font; }
        }
        foreach (int font in order)
        {
            if (hasGlyph(font, firstScalar)) { coverage = SdlClusterCoverage.Partial; return font; }
        }
        coverage = SdlClusterCoverage.Missing;
        return -1;
    }
}
