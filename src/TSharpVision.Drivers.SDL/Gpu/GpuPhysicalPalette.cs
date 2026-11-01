using System.Collections.ObjectModel;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// A physical palette of the SDL GPU driver: the 16-entry ARGB table (<c>0xAARRGGBB</c>, index =
/// VGA attribute nibble) that the GPU vertex build (<c>TerminalGpuPipeline.BuildVertices</c>)
/// and the renderer's CPU-compositing fallback map attribute colours through.
/// <para>
/// This is an output-stage substitution only: logical attributes, Borland palettes and
/// blink/intensity semantics are unchanged. Every table is precomputed once; selecting a palette
/// costs nothing per frame.
/// </para>
/// <para>Profiles, in <see cref="All"/> order:</para>
/// <list type="bullet">
/// <item><description><c>Default</c>: the classic VGA 16-colour mapping, byte-identical to the GPU
/// driver's historical table (the VGA text-mode DAC values with brown at index 6). What the driver
/// draws when no palette is configured. There is no separate VGA or CGA profile.</description></item>
/// <item><description><c>Green</c>, <c>Amber</c>, <c>Grayscale</c>: monochrome phosphor ramps.
/// For each VGA entry the Rec.601 luma <c>Y = 0.299 R + 0.587 G + 0.114 B</c> is computed,
/// normalised to 0..1. Index 0 stays exactly black; every other index uses
/// <c>t = 0.12 + 0.88 Y</c> (the 0.12 floor keeps dark blue distinguishable from black), and each
/// channel is <c>round(phosphor * t)</c> (midpoint away from zero) with phosphor colour Green
/// <c>(0x33, 0xFF, 0x33)</c>, Amber <c>(0xFF, 0xB0, 0x00)</c>, Grayscale <c>(0xFF, 0xFF, 0xFF)</c>.
/// Brightness therefore follows VGA luma order, except that Amber and Green draw indices 4, 12, 14 and 15
/// as luminous highlights (cream, pale phosphor green) so accelerators stand out (see <c>WithAmberHighlights</c>,
/// <c>WithGreenHighlights</c>).</description></item>
/// </list>
/// Alpha is always <c>0xFF</c>.
/// </summary>
internal sealed class GpuPhysicalPalette
{
    private readonly uint[] _argb;

    private GpuPhysicalPalette(string id, uint[] argb)
    {
        Id    = id;
        _argb = argb;
        Argb  = Array.AsReadOnly(argb);
    }

    /// <summary>Canonical id (one of the <c>DisplayPresetNames</c> values).</summary>
    public string Id { get; }

    /// <summary>16 entries <c>0xAARRGGBB</c>, index = VGA attribute nibble. Read-only view.</summary>
    public IReadOnlyList<uint> Argb { get; }

    /// <summary>The table as a span, for the per-cell hot paths (no allocation, no interface calls).</summary>
    internal ReadOnlySpan<uint> ArgbSpan => _argb;

    // ─── Tables ──────────────────────────────────────────────────────────────

    /// <summary>The classic VGA 16-colour text-mode table: the <c>Default</c> profile's data and the
    /// luma source of the phosphor ramps. Internal data only, not a selectable profile.</summary>
    private static readonly uint[] VgaTable =
    {
        0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
        0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF,
        0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
    };


    private static readonly uint[] GreenTable     = WithGreenHighlights(PhosphorRamp(0x33, 0xFF, 0x33));
    private static readonly uint[] AmberTable     = WithAmberHighlights(PhosphorRamp(0xFF, 0xB0, 0x00));
    private static readonly uint[] GrayscaleTable = PhosphorRamp(0xFF, 0xFF, 0xFF);

    // Default owns a copy, so the ramp source table is never aliased by a profile.
    private static readonly GpuPhysicalPalette DefaultPalette   = new("Default",   (uint[])VgaTable.Clone());
    private static readonly GpuPhysicalPalette GreenPalette     = new("Green",     GreenTable);
    private static readonly GpuPhysicalPalette AmberPalette     = new("Amber",     AmberTable);
    private static readonly GpuPhysicalPalette GrayscalePalette = new("Grayscale", GrayscaleTable);

    private static readonly ReadOnlyCollection<GpuPhysicalPalette> AllPalettes = Array.AsReadOnly(new[]
    {
        DefaultPalette, GreenPalette, AmberPalette, GrayscalePalette,
    });

    /// <summary>The palette the driver uses when none is configured (the classic VGA 16-colour mapping).</summary>
    public static GpuPhysicalPalette Default => DefaultPalette;

    /// <summary>All profiles: Default, Green, Amber, Grayscale.</summary>
    public static IReadOnlyList<GpuPhysicalPalette> All => AllPalettes;

    /// <summary>
    /// Finds a profile by id, ignoring case and surrounding whitespace. Null, empty, blank or
    /// unknown ids return <see langword="false"/> and <see cref="Default"/>.
    /// </summary>
    public static bool TryFind(string? id, out GpuPhysicalPalette palette)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            string trimmed = id.Trim();
            foreach (GpuPhysicalPalette candidate in AllPalettes)
            {
                if (string.Equals(candidate.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    palette = candidate;
                    return true;
                }
            }
        }
        palette = DefaultPalette;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => Id;

    // ─── Ramp derivation ─────────────────────────────────────────────────────

    /// <summary>Rec.601 luma of an ARGB entry, normalised to 0..1.</summary>
    internal static double Luma(uint argb)
    {
        double r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    private static uint[] PhosphorRamp(byte pr, byte pg, byte pb)
    {
        var table = new uint[16];
        table[0] = 0xFF000000;
        for (int i = 1; i < 16; i++)
        {
            double t = 0.12 + 0.88 * Luma(VgaTable[i]);
            uint r = Scale(pr, t), g = Scale(pg, t), b = Scale(pb, t);
            table[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
        return table;
    }

    /// <summary>
    /// Amber highlight phosphor (TV12g). On a pure luma ramp red (4) lands next to black, so the logical Color
    /// palette's accelerators (red on light gray, beside black text: menu bar, menus, status line) became almost
    /// indistinguishable from the text around them. Red, bright red, yellow (dialog and button mnemonics) and white
    /// are therefore drawn as luminous cream in the same amber hue (about 40°, never pure white), ordered
    /// 4 &lt; 12 &lt; 14 &lt; 15 so white stays the brightest entry. Every other index keeps its ramp value. (Applied in a method, not a
    /// field initializer, because <c>AmberTable</c> is initialised before fields declared after it.)
    /// </summary>
    private static uint[] WithAmberHighlights(uint[] table)
    {
        table[4]  = 0xFFFFCE6E; // red         → (255, 206, 110)
        table[12] = 0xFFFFD478; // bright red  → (255, 212, 120)
        table[14] = 0xFFFFDA84; // yellow      → (255, 218, 132)
        table[15] = 0xFFFFE49C; // white       → (255, 228, 156)
        return table;
    }

    /// <summary>
    /// Green highlight phosphor (TV12i). The same structural issue as Amber: on the pure luma ramp red (4) is
    /// <c>#0F4B0F</c>, only 2.03:1 against the black text beside it on the light gray bar. Red, bright red, yellow
    /// and white are therefore drawn as pale phosphor green with equal red and blue channels, so the hue is exactly
    /// the ramp's 120° (and stays so under the ShadowMask); never pure white, ordered 4 &lt; 12 &lt; 14 &lt; 15 so
    /// white stays the brightest entry. Every other index keeps its ramp value. Green's light bar (7) is brighter
    /// than Amber's, so the accelerator-on-bar ratio tops out lower (2.24:1 for <c>0x74</c>; 4.69:1 for <c>0x24</c>
    /// on the selected bar).
    /// </summary>
    private static uint[] WithGreenHighlights(uint[] table)
    {
        table[4]  = 0xFF98FF98; // red         → (152, 255, 152)
        table[12] = 0xFFA0FFA0; // bright red  → (160, 255, 160)
        table[14] = 0xFFA8FFA8; // yellow      → (168, 255, 168)
        table[15] = 0xFFC8FFC8; // white       → (200, 255, 200)
        return table;
    }

    private static uint Scale(byte channel, double t) =>
        (uint)Math.Clamp(Math.Round(channel * t, MidpointRounding.AwayFromZero), 0, 255);
}
