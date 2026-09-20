using TSharpVision.Constants;

namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// Turns a <see cref="SyntaxClass"/> into a screen attribute, starting from the attribute the view's
/// own palette gives ordinary text.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where colours are decided.</b> Classification never produces colours, and document text never
/// holds them: a view asks its palette for the normal text attribute and passes it through a scheme
/// while drawing. A scheme only replaces the foreground; the background — and therefore the window,
/// dialog or theme the view sits in — stays the palette's.
/// </para>
/// <para>
/// <b>Always legible.</b> A role whose foreground would equal the background is drawn with the normal
/// attribute instead, so no palette can make classified text disappear. <see cref="Monochrome"/>
/// assigns nothing and draws every role as plain text.
/// </para>
/// <para>
/// This is deliberately not a theme system and has no relation to TextMate themes: named themes,
/// contrast validation and user customisation build on top of it later.
/// </para>
/// </remarks>
public sealed class SyntaxColorScheme
{
    private readonly byte?[] _foregrounds;

    /// <summary>Creates a scheme from per-role foreground colours (the low nibble of an attribute).</summary>
    /// <param name="foregrounds">Roles and their foreground colour 0–15; a role that is absent is drawn plain.</param>
    public SyntaxColorScheme(IReadOnlyDictionary<SyntaxClass, byte> foregrounds)
    {
        ArgumentNullException.ThrowIfNull(foregrounds);
        _foregrounds = new byte?[byte.MaxValue + 1];
        foreach (KeyValuePair<SyntaxClass, byte> entry in foregrounds)
            _foregrounds[(byte)entry.Key] = (byte)(entry.Value & Colors.fgMask);
    }

    /// <summary>
    /// Gets the default scheme for the 16-colour palette: distinguishable roles over whatever background
    /// the view's palette uses.
    /// </summary>
    public static SyntaxColorScheme Default { get; } = new(new Dictionary<SyntaxClass, byte>
    {
        [SyntaxClass.Comment] = Colors.fgLightGray,
        [SyntaxClass.String] = Colors.fgLightGreen,
        [SyntaxClass.Number] = Colors.fgLightMagenta,
        [SyntaxClass.Constant] = Colors.fgLightMagenta,
        [SyntaxClass.Keyword] = Colors.fgWhite,
        [SyntaxClass.Type] = Colors.fgLightCyan,
        [SyntaxClass.Function] = Colors.fgYellow,
        [SyntaxClass.Preprocessor] = Colors.fgLightRed,
        [SyntaxClass.Tag] = Colors.fgLightCyan,
        [SyntaxClass.Attribute] = Colors.fgYellow,
        [SyntaxClass.Heading] = Colors.fgWhite,
        [SyntaxClass.Code] = Colors.fgLightGreen,
        [SyntaxClass.Invalid] = Colors.fgLightRed,
    });

    /// <summary>Gets a scheme that draws every role as plain text.</summary>
    public static SyntaxColorScheme Monochrome { get; } = new(new Dictionary<SyntaxClass, byte>());

    /// <summary>Gets the foreground assigned to <paramref name="syntaxClass"/>, or null when it is drawn plain.</summary>
    public byte? GetForeground(SyntaxClass syntaxClass) => _foregrounds[(byte)syntaxClass];

    /// <summary>Gets the attribute to draw <paramref name="syntaxClass"/> with, given the view's normal text attribute.</summary>
    public ushort Apply(SyntaxClass syntaxClass, ushort normalAttribute)
    {
        byte? foreground = _foregrounds[(byte)syntaxClass];
        if (foreground is not byte fg) return normalAttribute;

        byte background = (byte)((normalAttribute & Colors.bgMask) >> 4);
        if (fg == background) return normalAttribute;

        return (ushort)((normalAttribute & ~Colors.fgMask & 0xFF) | fg);
    }
}
