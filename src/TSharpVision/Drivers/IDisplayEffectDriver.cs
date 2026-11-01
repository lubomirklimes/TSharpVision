namespace TSharpVision.Drivers;

/// <summary>Optional display (post-processing) effect capability for screen drivers.</summary>
/// <remarks>Absent on drivers that do not support it; discover it with
/// <c>TDisplay.driver is IDisplayEffectDriver</c>. A driver decorator hides the capability
/// unless it forwards it. An effect changes only how the driver presents its output; it never
/// changes logical attributes, application palettes or the screen buffer. Ids are driver-defined;
/// the common ones are in <see cref="DisplayPresetNames"/>. <c>[graphics] effect</c> selects the
/// initial effect.</remarks>
public interface IDisplayEffectDriver
{
    /// <summary>Canonical ids of the supported effects. <see cref="DisplayPresetNames.Default"/>
    /// (the driver's unmodified output) is always listed first.</summary>
    IReadOnlyList<string> DisplayEffects { get; }

    /// <summary>Canonical id of the active effect, as listed in <see cref="DisplayEffects"/>.</summary>
    string DisplayEffect { get; }

    /// <summary>Selects an effect. Matching is case-insensitive with surrounding whitespace ignored.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="effect"/> names no supported effect;
    /// the active selection is left unchanged.</exception>
    void SetDisplayEffect(string effect);
}
