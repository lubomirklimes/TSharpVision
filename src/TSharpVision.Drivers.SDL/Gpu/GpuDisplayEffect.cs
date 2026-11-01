namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Display effect of the SDL GPU driver, applied after the terminal grid is drawn. One effect is
/// active at a time.
/// </summary>
internal enum GpuDisplayEffect
{
    /// <summary>No effect: the frame is drawn exactly as without effect support.</summary>
    Default,

    /// <summary>
    /// Translucent black horizontal strips over the grid viewport (see
    /// <see cref="GpuScanlineGeometry"/> for the geometry and the strip alpha).
    /// </summary>
    Scanlines,

    /// <summary>Phosphor glow: a bounded halo around bright glyph pixels (offscreen post-process).</summary>
    Glow,

    /// <summary>CRT curvature: a bounded barrel warp of the grid image (offscreen post-process).</summary>
    Curvature,

    /// <summary>CRT shadow mask: a fine phosphor mask anchored to device pixels (offscreen post-process).</summary>
    ShadowMask,
}

/// <summary>Canonical ids and parsing for <see cref="GpuDisplayEffect"/>.</summary>
internal static class GpuDisplayEffects
{
    /// <summary>Canonical id of <see cref="GpuDisplayEffect.Default"/>.</summary>
    public const string DefaultId = "Default";

    /// <summary>Canonical id of <see cref="GpuDisplayEffect.Scanlines"/>.</summary>
    public const string ScanlinesId = "Scanlines";

    /// <summary>Canonical id of <see cref="GpuDisplayEffect.Glow"/>.</summary>
    public const string GlowId = "Glow";

    /// <summary>Canonical id of <see cref="GpuDisplayEffect.Curvature"/>.</summary>
    public const string CurvatureId = "Curvature";

    /// <summary>Canonical id of <see cref="GpuDisplayEffect.ShadowMask"/>.</summary>
    public const string ShadowMaskId = "ShadowMask";

    private static readonly (GpuDisplayEffect Effect, string Id)[] Table =
    {
        (GpuDisplayEffect.Default,    DefaultId),
        (GpuDisplayEffect.Scanlines,  ScanlinesId),
        (GpuDisplayEffect.Glow,       GlowId),
        (GpuDisplayEffect.Curvature,  CurvatureId),
        (GpuDisplayEffect.ShadowMask, ShadowMaskId),
    };

    private static readonly string[] AllIds = Table.Select(e => e.Id).ToArray();

    /// <summary>All canonical ids, <c>Default</c> first.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(AllIds);

    /// <summary>True for effects drawn by the offscreen post-process pass (the grid is first drawn to a texture).</summary>
    public static bool IsPostProcess(GpuDisplayEffect effect) =>
        effect is GpuDisplayEffect.Glow or GpuDisplayEffect.Curvature or GpuDisplayEffect.ShadowMask;

    /// <summary>Canonical id of <paramref name="effect"/>.</summary>
    public static string Id(GpuDisplayEffect effect)
    {
        foreach (var (e, id) in Table)
            if (e == effect) return id;
        throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
    }

    /// <summary>
    /// Parses a canonical id, ignoring case and surrounding whitespace. Null, blank or unknown ids
    /// (including numeric strings) return <see langword="false"/> and <see cref="GpuDisplayEffect.Default"/>.
    /// </summary>
    public static bool TryParse(string? id, out GpuDisplayEffect effect)
    {
        effect = GpuDisplayEffect.Default;
        if (string.IsNullOrWhiteSpace(id)) return false;
        string trimmed = id.Trim();
        foreach (var (e, canonical) in Table)
        {
            if (string.Equals(trimmed, canonical, StringComparison.OrdinalIgnoreCase))
            {
                effect = e;
                return true;
            }
        }
        return false;
    }
}
