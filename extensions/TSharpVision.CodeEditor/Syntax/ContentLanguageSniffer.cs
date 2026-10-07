namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// Conservative recognition of JSON, XML and YAML from the beginning of a document whose name says nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Last resort.</b> A syntax service asks this only after the file name, the first-line patterns and a
/// <c>#!</c> line have all failed. A wrong positive is worse than plain text, so every rule needs a signature
/// the format cannot share with prose: JSON and XML have strong ones; YAML is accepted only with a document
/// marker or when every leading line has a YAML shape.
/// </para>
/// <para>
/// Nothing is parsed, executed or resolved: the sample is only inspected, and at most
/// <see cref="MaxInspectedChars"/> characters of it.
/// </para>
/// </remarks>
internal static class ContentLanguageSniffer
{
    /// <summary>How much of a sample is looked at.</summary>
    public const int MaxInspectedChars = 4096;

    /// <summary>The language id reported for JSON.</summary>
    public const string JsonId = "json";

    /// <summary>The language id reported for XML.</summary>
    public const string XmlId = "xml";

    /// <summary>The language id reported for YAML.</summary>
    public const string YamlId = "yaml";

    private const int MaxYamlLines = 20;
    private const int MinYamlLines = 3;
    private const int MaxJsonDepth = 4;

    /// <summary>
    /// Returns <see cref="JsonId"/>, <see cref="XmlId"/> or <see cref="YamlId"/> when the sample carries that
    /// format's signature, otherwise null.
    /// </summary>
    /// <param name="contentSample">The first characters of a document, or null.</param>
    public static string? Detect(string? contentSample)
    {
        if (string.IsNullOrEmpty(contentSample)) return null;

        ReadOnlySpan<char> text = contentSample.AsSpan();
        if (text.Length > MaxInspectedChars) text = text[..MaxInspectedChars];
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        if (text.IndexOf('\0') >= 0) return null;

        ReadOnlySpan<char> body = text.TrimStart();
        if (body.IsEmpty) return null;

        if (body[0] is '{' or '[') return LooksLikeJson(body) ? JsonId : null;
        if (body[0] == '<') return LooksLikeXml(body) ? XmlId : null;
        return LooksLikeYaml(text) ? YamlId : null;
    }

    // ── JSON ────────────────────────────────────────────────────────────────

    private static bool LooksLikeJson(ReadOnlySpan<char> text)
    {
        int position = 0;
        return JsonValueStart(text, ref position, 0, topLevel: true);
    }

    /// <summary>
    /// Whether a JSON container starts at <paramref name="position"/> and its first member is well formed as far as
    /// it can be seen. Running out of sample inside a string or a nested container is accepted; a wrong character
    /// never is.
    /// </summary>
    private static bool JsonValueStart(ReadOnlySpan<char> text, ref int position, int depth, bool topLevel)
    {
        if (depth > MaxJsonDepth) return true;

        char open = text[position++];
        SkipWhitespace(text, ref position);
        if (position >= text.Length) return false;

        if (open == '{')
        {
            if (text[position] == '}') return !topLevel || text[(position + 1)..].Trim().IsEmpty;
            if (text[position] != '"') return false;
            if (!SkipJsonString(text, ref position)) return true;          // the sample ended inside the name
            SkipWhitespace(text, ref position);
            return position >= text.Length || text[position] == ':';
        }

        // An array.
        char first = text[position];
        if (first == ']') return !topLevel || text[(position + 1)..].Trim().IsEmpty;
        if (first is '{' or '[') return JsonValueStart(text, ref position, depth + 1, topLevel: false);

        if (first == '"')
        {
            if (!SkipJsonString(text, ref position)) return true;
        }
        else
        {
            int start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not (',' or ']'))
                position++;
            ReadOnlySpan<char> scalar = text[start..position];
            if (!(scalar.SequenceEqual("true") || scalar.SequenceEqual("false") || scalar.SequenceEqual("null") || IsJsonNumber(scalar)))
                return false;
        }

        SkipWhitespace(text, ref position);
        if (position >= text.Length || text[position] == ',') return true;

        // A one-element array that closes must be the whole document: "[1] First footnote" is prose.
        return text[position] == ']' && (!topLevel || text[(position + 1)..].Trim().IsEmpty);
    }

    /// <summary>Moves past a string that starts at <paramref name="position"/>; false when the sample ends first.</summary>
    private static bool SkipJsonString(ReadOnlySpan<char> text, ref int position)
    {
        position++;
        while (position < text.Length)
        {
            char c = text[position++];
            if (c == '\\') position++;
            else if (c == '"') return true;
            else if (c is '\r' or '\n') return true;                      // not a JSON string: the caller's next check fails
        }

        return false;
    }

    private static bool IsJsonNumber(ReadOnlySpan<char> text)
    {
        int i = 0;
        if (i < text.Length && text[i] == '-') i++;
        int digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
        if (digits == 0) return false;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            int fraction = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; fraction++; }
            if (fraction == 0) return false;
        }

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            if (i < text.Length && text[i] is '+' or '-') i++;
            int exponent = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; exponent++; }
            if (exponent == 0) return false;
        }

        return i == text.Length;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length && text[position] is ' ' or '\t' or '\r' or '\n') position++;
    }

    // ── XML ─────────────────────────────────────────────────────────────────

    private static bool LooksLikeXml(ReadOnlySpan<char> text)
    {
        // Leading comments say nothing either way.
        while (text.StartsWith("<!--"))
        {
            int end = text.IndexOf("-->");
            if (end < 0) return false;
            text = text[(end + 3)..].TrimStart();
            if (text.IsEmpty || text[0] != '<') return false;
        }

        if (text.StartsWith("<?xml", StringComparison.Ordinal) && text.Length > 5 && char.IsWhiteSpace(text[5]))
            return true;

        if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> root = text[9..].TrimStart();
            return !root.StartsWith("html", StringComparison.OrdinalIgnoreCase) && root.Length > 0 && IsNameStart(root[0]);
        }

        if (text.Length < 2 || !IsNameStart(text[1])) return false;

        int i = 1;
        while (i < text.Length && IsNameChar(text[i])) i++;
        ReadOnlySpan<char> name = text[1..i];
        if (i >= text.Length || !(char.IsWhiteSpace(text[i]) || text[i] is '>' or '/')) return false;

        // HTML is its own language, and its tags need not be closed.
        if (name.Equals("html", StringComparison.OrdinalIgnoreCase) || name.Equals("head", StringComparison.OrdinalIgnoreCase)
            || name.Equals("body", StringComparison.OrdinalIgnoreCase))
            return false;

        // An opening tag alone is not evidence ("<stdio.h>"): something must also be closed.
        ReadOnlySpan<char> rest = text[i..];
        return rest.IndexOf("</") >= 0 || rest.IndexOf("/>") >= 0;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or ':';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '-' or '.';

    // ── YAML ────────────────────────────────────────────────────────────────

    private static bool LooksLikeYaml(ReadOnlySpan<char> text)
    {
        bool marker = false;
        bool first = true;
        int lines = 0;
        int keys = 0;

        while (!text.IsEmpty && lines < MaxYamlLines)
        {
            int end = text.IndexOf('\n');
            bool complete = end >= 0;
            ReadOnlySpan<char> line = complete ? text[..end] : text;
            text = complete ? text[(end + 1)..] : ReadOnlySpan<char>.Empty;

            line = line.TrimEnd();
            ReadOnlySpan<char> content = line.TrimStart();
            if (content.IsEmpty || content[0] == '#') continue;

            if (first)
            {
                first = false;
                if (line.SequenceEqual("---") || line.StartsWith("%YAML ", StringComparison.Ordinal))
                {
                    marker = true;
                    continue;
                }
            }

            if (line.SequenceEqual("---") || line.SequenceEqual("...")) continue;

            if (content[0] == '-' && (content.Length == 1 || content[1] == ' '))
            {
                lines++;
                ReadOnlySpan<char> item = content.Length > 1 ? content[2..].TrimStart() : ReadOnlySpan<char>.Empty;
                if (IsYamlKeyLine(item)) keys++;
                continue;
            }

            if (!IsYamlKeyLine(content))
            {
                // The last line of a cut sample may be half a line; it proves nothing either way.
                if (!complete && lines > 0) break;
                return false;
            }

            lines++;
            keys++;
        }

        // With a document marker one well-formed line is enough; without it the whole beginning must agree.
        return marker ? lines >= 1 : lines >= MinYamlLines && keys >= 2;
    }

    /// <summary>Whether a line is <c>key:</c> or <c>key: value</c> with a plain, space-free or quoted key.</summary>
    private static bool IsYamlKeyLine(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty) return false;

        int i = 0;
        if (line[0] is '"' or '\'')
        {
            char quote = line[0];
            i = 1;
            while (i < line.Length && line[i] != quote) i++;
            if (i >= line.Length) return false;
            i++;
        }
        else
        {
            if (!(char.IsLetter(line[0]) || line[0] == '_')) return false;
            while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '-' or '.' or '/')) i++;
        }

        if (i >= line.Length || line[i] != ':') return false;
        return i + 1 == line.Length || line[i + 1] == ' ';
    }
}
