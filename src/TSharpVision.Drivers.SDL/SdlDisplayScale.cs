namespace TSharpVision.Drivers.SDL;

/// <summary>
/// The two coordinate spaces of one SDL window: <b>window</b> coordinates (what
/// <c>SDL_GetWindowSize</c> reports and what SDL mouse events carry) and <b>drawable</b> pixels
/// (what <c>SDL_GetWindowSizeInPixels</c>, the renderer output and the GPU swapchain have).
/// <para>
/// With <c>SDL_WINDOW_HIGH_PIXEL_DENSITY</c> the two differ on a Retina / HiDPI display (macOS 2×,
/// Wayland scaled outputs); everywhere else they are equal. Both SDL back-ends render and hit-test
/// in drawable pixels, so window coordinates are converted exactly once, here. Pure and
/// SDL-independent apart from <see cref="Query"/>, so the conversion is unit-testable.
/// </para>
/// </summary>
/// <param name="WindowWidth">Window width in window coordinates.</param>
/// <param name="WindowHeight">Window height in window coordinates.</param>
/// <param name="PixelWidth">Drawable width in pixels.</param>
/// <param name="PixelHeight">Drawable height in pixels.</param>
/// <param name="Density">
/// Pixels per window coordinate as SDL reports it (<c>SDL_GetWindowPixelDensity</c>), normalised by
/// <see cref="NormalizeDensity"/>. It is what glyphs are rasterised at.
/// </param>
internal readonly record struct SdlDisplayScale(
    int   WindowWidth,
    int   WindowHeight,
    int   PixelWidth,
    int   PixelHeight,
    float Density)
{
    /// <summary>A 1× window of unknown size: window coordinates are drawable pixels.</summary>
    public static readonly SdlDisplayScale Identity = new(0, 0, 0, 0, 1f);

    /// <summary>
    /// Builds a scale from raw SDL values, normalising the density. A density SDL could not report
    /// (0, negative, NaN) falls back to the measured pixel/window ratio, then to 1.
    /// </summary>
    public static SdlDisplayScale Create(
        int windowWidth, int windowHeight, int pixelWidth, int pixelHeight, float reportedDensity)
    {
        float density = reportedDensity;
        if (!(density > 0f) && windowWidth > 0 && pixelWidth > 0)
            density = pixelWidth / (float)windowWidth;
        return new SdlDisplayScale(
            Math.Max(0, windowWidth), Math.Max(0, windowHeight),
            Math.Max(0, pixelWidth),  Math.Max(0, pixelHeight),
            NormalizeDensity(density));
    }

    /// <summary>Reads the current window and drawable size and pixel density of <paramref name="window"/>.</summary>
    public static SdlDisplayScale Query(IntPtr window)
    {
        SDL3.SDL.GetWindowSize(window, out int windowWidth, out int windowHeight);
        SDL3.SDL.GetWindowSizeInPixels(window, out int pixelWidth, out int pixelHeight);
        return Create(windowWidth, windowHeight, pixelWidth, pixelHeight, SDL3.SDL.GetWindowPixelDensity(window));
    }

    /// <summary>
    /// Clamps a density into a usable range and snaps values within 1 % of an integer to that
    /// integer, so a reported 1.9999 does not rasterise glyphs one pixel short of a 2× cell.
    /// Non-integer densities (1.25, 1.5, 1.75…) are kept as they are.
    /// </summary>
    public static float NormalizeDensity(float density)
    {
        if (!(density > 0f) || float.IsInfinity(density)) return 1f;
        density = Math.Clamp(density, 0.25f, 8f);
        float nearest = MathF.Round(density);
        return MathF.Abs(density - nearest) < 0.01f ? nearest : density;
    }

    /// <summary>Drawable pixels per window coordinate horizontally (the measured ratio, else <see cref="Density"/>).</summary>
    public float ScaleX => WindowWidth > 0 && PixelWidth > 0 ? PixelWidth / (float)WindowWidth : Density;

    /// <summary>Drawable pixels per window coordinate vertically (the measured ratio, else <see cref="Density"/>).</summary>
    public float ScaleY => WindowHeight > 0 && PixelHeight > 0 ? PixelHeight / (float)WindowHeight : Density;

    /// <summary>
    /// Converts an SDL mouse position (window coordinates) to the drawable pixel it falls on.
    /// Rounds down, so a pointer inside a cell never reports the next cell.
    /// </summary>
    public (int X, int Y) WindowToPixel(float x, float y) =>
        ((int)MathF.Floor(x * ScaleX), (int)MathF.Floor(y * ScaleY));

    /// <summary>Diagnostic line. Format is stable enough to grep.</summary>
    public string Describe() =>
        $"window={WindowWidth}x{WindowHeight} drawable={PixelWidth}x{PixelHeight} " +
        $"density={Density.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} " +
        $"scale={ScaleX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}x" +
        $"{ScaleY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
}
