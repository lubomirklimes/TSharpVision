namespace TSharpVision.Drivers;

/// <summary>Optional physical (output-stage) palette capability for screen drivers.</summary>
/// <remarks>Absent on drivers that do not support it; discover it with
/// <c>TDisplay.driver is IPhysicalPaletteDriver</c>. A driver decorator hides the capability
/// unless it forwards it. The physical palette recolours only the 16 output colours the driver
/// presents; it never changes logical attributes (<see cref="TColorAttr"/>, the Color, BlackWhite
/// and Monochrome application palettes, blink or intensity). Ids are driver-defined; the common ones
/// are in <see cref="DisplayPresetNames"/>. <c>[graphics] palette</c> selects the initial palette.</remarks>
public interface IPhysicalPaletteDriver
{
    /// <summary>Canonical ids of the supported palettes. <see cref="DisplayPresetNames.Default"/>
    /// (the driver's unmodified output) is always listed first.</summary>
    IReadOnlyList<string> PhysicalPalettes { get; }

    /// <summary>Canonical id of the active palette, as listed in <see cref="PhysicalPalettes"/>.</summary>
    string PhysicalPalette { get; }

    /// <summary>Selects a palette. Matching is case-insensitive with surrounding whitespace ignored.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="palette"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="palette"/> names no supported palette;
    /// the active selection is left unchanged.</exception>
    void SetPhysicalPalette(string palette);
}
