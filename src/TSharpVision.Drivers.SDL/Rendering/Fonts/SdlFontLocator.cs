namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>
/// Shared font-path lookup used by both SDL back-ends (<c>SDLRenderer</c>, <c>SDLGpuRenderer</c>)
/// and by the headless glyph-diagnostic tool.
/// </summary>
internal static class SdlFontLocator
{
    // ─────────────────────────────────────────────────────────────────────────
    // Default monospace font
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the first font path that exists on the current platform,
    /// or <c>null</c> if none is found.
    /// </summary>
    public static string? ProbeFontPath()
    {
        IEnumerable<string> candidates;

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "Fonts");
            candidates = new[]
            {
                Path.Combine(winFonts, "JetBrainsMono-Regular.ttf"),
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

    // ─────────────────────────────────────────────────────────────────────────
    // Named font lookup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches system font directories for a font whose file name contains
    /// a normalized form of <paramref name="fontName"/> (spaces, hyphens, underscores and
    /// dots stripped, case-insensitive). Returns the first matching path, or <c>null</c>.
    /// An existing file path is returned as-is (made absolute).
    /// </summary>
    public static string? ProbeFontPathByName(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return null;

        if (File.Exists(fontName))
            return Path.GetFullPath(fontName);

        string nameWithoutExt = Path.GetFileNameWithoutExtension(fontName);
        string normalized     = NormalizeFontName(nameWithoutExt);

        foreach (string dir in GetFontSearchDirectories())
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in SafeEnumerateFontFiles(dir))
            {
                string stem           = Path.GetFileNameWithoutExtension(file);
                string normalizedStem = NormalizeFontName(stem);

                if (normalizedStem.Equals(normalized, StringComparison.OrdinalIgnoreCase)   ||
                    normalizedStem.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(normalizedStem, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Directories searched by <see cref="ProbeFontPathByName"/>, in order, de-duplicated.
    /// </summary>
    public static IEnumerable<string> GetFontSearchDirectories()
    {
        IEnumerable<string> fontDirs;

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "Fonts");
            string userFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            fontDirs = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), winFonts, userFonts };
        }
        else if (OperatingSystem.IsMacOS())
        {
            fontDirs = new[]
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                "/System/Library/Fonts",
                "/Library/Fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts"),
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
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts"),
            };
        }

        return fontDirs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Symbol / emoji fallback font
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the first symbol/emoji fallback font path on the current platform,
    /// or <c>null</c> if none is found.
    /// Used to provide glyph coverage for characters absent from the primary monospace font.
    /// </summary>
    public static string? ProbeSymbolFontPath()
    {
        IEnumerable<string> candidates;

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "Fonts");
            candidates = new[]
            {
                Path.Combine(winFonts, "seguisym.ttf"),   // Segoe UI Symbol
                Path.Combine(winFonts, "seguiemj.ttf"),   // Segoe UI Emoji
                Path.Combine(winFonts, "symbol.ttf"),
            };
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates = new[]
            {
                "/System/Library/Fonts/Apple Symbols.ttf",
                "/System/Library/Fonts/Supplemental/Symbol.ttf",
                "/System/Library/Fonts/Symbol.ttf",
            };
        }
        else
        {
            candidates = new[]
            {
                "/usr/share/fonts/truetype/unifont/unifont.ttf",
                "/usr/share/fonts/unifont/unifont.ttf",
                "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
                "/usr/share/fonts/noto/NotoSans-Regular.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            };
        }

        foreach (string path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Strips spaces, hyphens, underscores and dots, then lower-cases.</summary>
    public static string NormalizeFontName(string value) =>
        value.Replace(" ", "").Replace("-", "").Replace("_", "").Replace(".", "").ToLowerInvariant();

    /// <summary>
    /// Recursively enumerates <c>.ttf</c> / <c>.ttc</c> / <c>.otf</c> files under
    /// <paramref name="root"/>, silently skipping directories that cannot be read.
    /// </summary>
    public static IEnumerable<string> SafeEnumerateFontFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            IEnumerable<string> files;
            try   { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (string file in files)
            {
                if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            IEnumerable<string> subDirs;
            try   { subDirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (string subDir in subDirs)
                pending.Push(subDir);
        }
    }
}
