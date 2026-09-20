using TextMateSharp.Grammars;

namespace TSharpVision.CodeEditor.Syntax.TextMate;

/// <summary>
/// Maps TextMate scope stacks onto the bounded <see cref="SyntaxClass"/> set.
/// </summary>
/// <remarks>
/// <para>
/// A token carries a scope stack from the outermost scope (<c>source.cs</c>) to the innermost
/// (<c>punctuation.definition.string.begin.cs</c>). The innermost scope that matches a rule decides the
/// role, so an embedded construct (a C# keyword inside a Markdown code fence) keeps its own role.
/// Rules match whole dotted segments by prefix and are tried in the order below, most specific first:
/// <c>keyword.operator</c> before <c>keyword</c>, <c>support.type.property-name</c> before
/// <c>support.type</c>.
/// </para>
/// <para>
/// Two containers then colour what the innermost pass left plain: anything inside
/// <c>meta.preprocessor</c> is <see cref="SyntaxClass.Preprocessor"/>, and anything inside Markdown code
/// (<c>markup.fenced_code</c>, <c>markup.inline.raw</c>, <c>markup.raw</c>) is <see cref="SyntaxClass.Code"/>.
/// </para>
/// <para>
/// Unmatched scopes — <c>meta.*</c>, most <c>punctuation.*</c>, <c>markup.bold</c> — are plain. The full
/// scope names never leave this class: consumers see only the role.
/// </para>
/// </remarks>
internal static class TextMateScopeMapper
{
    private static readonly (string Prefix, SyntaxClass Class)[] Rules =
    {
        ("comment", SyntaxClass.Comment),
        ("punctuation.definition.comment", SyntaxClass.Comment),
        ("invalid", SyntaxClass.Invalid),
        ("keyword.preprocessor", SyntaxClass.Preprocessor),
        ("keyword.operator", SyntaxClass.Operator),
        ("keyword", SyntaxClass.Keyword),
        ("storage", SyntaxClass.Keyword),
        ("markup.inline.raw", SyntaxClass.Code),
        ("markup.fenced_code", SyntaxClass.Code),
        ("markup.raw", SyntaxClass.Code),
        ("string", SyntaxClass.String),
        ("support.type.property-name", SyntaxClass.Attribute),
        ("entity.other.attribute-name", SyntaxClass.Attribute),
        ("constant.numeric", SyntaxClass.Number),
        ("constant", SyntaxClass.Constant),
        ("entity.name.tag", SyntaxClass.Tag),
        ("entity.name.section", SyntaxClass.Heading),
        ("markup.heading", SyntaxClass.Heading),
        ("entity.name.function", SyntaxClass.Function),
        ("support.function", SyntaxClass.Function),
        ("entity.name.type", SyntaxClass.Type),
        ("entity.name.class", SyntaxClass.Type),
        ("entity.name.namespace", SyntaxClass.Type),
        ("entity.other.inherited-class", SyntaxClass.Type),
        ("support.type", SyntaxClass.Type),
        ("support.class", SyntaxClass.Type),
        ("entity.name.variable", SyntaxClass.Identifier),
        ("variable", SyntaxClass.Identifier),
    };

    /// <summary>Turns tokens into merged, non-plain spans clipped to the line.</summary>
    public static IReadOnlyList<SyntaxSpan> ToSpans(IToken[]? tokens, int lineLength)
    {
        if (tokens is null || tokens.Length == 0 || lineLength == 0) return Array.Empty<SyntaxSpan>();

        var spans = new List<SyntaxSpan>(tokens.Length);
        foreach (IToken token in tokens)
        {
            int start = Math.Clamp(token.StartIndex, 0, lineLength);
            int end = Math.Clamp(token.EndIndex, start, lineLength);
            if (end <= start) continue;

            SyntaxClass role = Classify(token.Scopes);
            if (role == SyntaxClass.Plain) continue;

            if (spans.Count > 0 && spans[^1].End == start && spans[^1].Class == role)
                spans[^1] = spans[^1] with { Length = end - spans[^1].Start };
            else
                spans.Add(new SyntaxSpan(start, end - start, role));
        }

        return spans.Count == 0 ? Array.Empty<SyntaxSpan>() : spans.ToArray();
    }

    /// <summary>Decides the role of one token from its scope stack, outermost first.</summary>
    public static SyntaxClass Classify(IReadOnlyList<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0) return SyntaxClass.Plain;

        SyntaxClass role = SyntaxClass.Plain;
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            role = ClassifyScope(scopes[i]);
            if (role != SyntaxClass.Plain) break;
        }

        if (role is SyntaxClass.Plain or SyntaxClass.Identifier or SyntaxClass.Operator)
        {
            foreach (string scope in scopes)
            {
                if (HasPrefix(scope, "meta.preprocessor")) return SyntaxClass.Preprocessor;
                if (role == SyntaxClass.Plain
                    && (HasPrefix(scope, "markup.fenced_code") || HasPrefix(scope, "markup.inline.raw") || HasPrefix(scope, "markup.raw")))
                    return SyntaxClass.Code;
            }
        }

        return role;
    }

    /// <summary>Decides the role of a single scope name.</summary>
    public static SyntaxClass ClassifyScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope)) return SyntaxClass.Plain;

        foreach ((string prefix, SyntaxClass role) in Rules)
            if (HasPrefix(scope, prefix))
                return role;

        return SyntaxClass.Plain;
    }

    private static bool HasPrefix(string scope, string prefix)
        => scope.StartsWith(prefix, StringComparison.Ordinal)
           && (scope.Length == prefix.Length || scope[prefix.Length] == '.');
}
