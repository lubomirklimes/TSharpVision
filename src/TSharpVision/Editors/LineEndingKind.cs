namespace TSharpVision;

/// <summary>Line-break convention observed in a file or requested for saving.</summary>
public enum LineEndingKind
{
    /// <summary>No line-break convention has been determined.</summary>
    Unknown,
    /// <summary>Line feed separates lines.</summary>
    Lf,
    /// <summary>Carriage return followed by line feed separates lines.</summary>
    CrLf,
    /// <summary>Carriage return separates lines.</summary>
    Cr,
    /// <summary>More than one line-break convention occurs in the text.</summary>
    Mixed,
}
