namespace TSharpVision;

/// <summary>
/// An attribute for a range of UTF-16 code units of one editor line, used by
/// <see cref="TEditor.FormatLine(System.Span{TScreenChar}, uint, int, ushort, ushort, System.ReadOnlySpan{EditorTextRun})"/>
/// (for example a syntax colour).
/// </summary>
public readonly struct EditorTextRun
{
    /// <summary>Creates a run.</summary>
    /// <param name="start">Offset of the first code unit, counted from the line start.</param>
    /// <param name="length">Number of code units.</param>
    /// <param name="attribute">Attribute of the text in the run.</param>
    public EditorTextRun(int start, int length, ushort attribute)
    {
        Start = start;
        Length = length;
        Attribute = attribute;
    }

    /// <summary>Offset of the first code unit, counted from the line start.</summary>
    public int Start { get; }

    /// <summary>Number of code units.</summary>
    public int Length { get; }

    /// <summary>Offset just past the run.</summary>
    public int End => Start + Length;

    /// <summary>Attribute of the text in the run.</summary>
    public ushort Attribute { get; }
}
