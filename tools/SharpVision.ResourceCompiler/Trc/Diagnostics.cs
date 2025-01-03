// SharpVision Resource Compiler
// Diagnostic codes and Diagnostic record.
// Typed diagnostics for lexer / parser / builder errors.
namespace SharpVision.ResourceCompiler;

/// <summary>
/// Represents one compiler diagnostic (error or warning) with a stable
/// error code, source location, and human-readable message.
/// </summary>
public sealed class Diagnostic
{
    public string Code    { get; }
    public string Message { get; }
    public int    Line    { get; }
    public int    Column  { get; }

    public Diagnostic(string code, string message, int line, int column)
    {
        Code    = code;
        Message = message;
        Line    = line;
        Column  = column;
    }

    public override string ToString() =>
        $"({Line},{Column}): error {Code}: {Message}";
}

/// <summary>Stable error codes used by svrc.</summary>
public static class DiagnosticCodes
{
    // Lexer errors — TRC00xx
    public const string UnterminatedString    = "TRC0001";
    public const string InvalidCharacter      = "TRC0002";

    // Parser errors — TRC01xx
    public const string UnexpectedToken       = "TRC0101";
    public const string MissingSemicolon      = "TRC0102";
    public const string MissingOpenBrace      = "TRC0103";
    public const string MissingCloseBrace     = "TRC0104";
    public const string MissingOpenParen      = "TRC0105";
    public const string MissingCloseParen     = "TRC0106";
    public const string MissingEquals         = "TRC0107";
    public const string DuplicateField        = "TRC0108";
    public const string UnknownControlKind    = "TRC0109";
    public const string MalformedBounds       = "TRC0110";
    public const string DuplicateResourceKey  = "TRC0111";
    public const string MissingResourceKey    = "TRC0112";
    public const string EmptyItemList         = "TRC0113";
    public const string UnexpectedEof         = "TRC0114";

    // Builder errors — TRC02xx
    public const string MissingRequiredBounds = "TRC0201";
    public const string UndefinedCommand      = "TRC0202";
    public const string UnsupportedValidator  = "TRC0203";
    public const string InvalidBoundsValue    = "TRC0204";
    public const string EmptySubmenu          = "TRC0205";
    public const string EmptyStatusRange      = "TRC0206";
    public const string InvalidStatusRange    = "TRC0207";
    public const string UnknownKeyName        = "TRC0208";
    public const string UnknownPaletteName    = "TRC0209";
}
