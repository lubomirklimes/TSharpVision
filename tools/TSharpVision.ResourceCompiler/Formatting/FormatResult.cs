// TSharpVision Resource Compiler — FormatResult
// Result type returned by AstFormatter and Compiler.FormatSource/FormatFile.
using System.Diagnostics.CodeAnalysis;

namespace TSharpVision.ResourceCompiler;

/// <summary>
/// Returned by <see cref="AstFormatter"/> and the <see cref="Compiler"/> format facade.
/// </summary>
public sealed class FormatResult
{
    /// <summary>Canonical formatted .trc text, or null when <see cref="Success"/> is false.</summary>
    public string? Text { get; }

    /// <summary>Diagnostics from lexing/parsing. Non-empty when Success is false.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>True when there are no error-level diagnostics.</summary>
    [MemberNotNullWhen(true, nameof(Text))]
    public bool Success => Diagnostics.Count == 0;

    public FormatResult(string? text, List<Diagnostic> diag)
    {
        Text        = text;
        Diagnostics = diag.AsReadOnly();
    }
}
