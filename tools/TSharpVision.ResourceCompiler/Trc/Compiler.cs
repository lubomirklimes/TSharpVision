// TSharpVision Resource Compiler
// Public facade: Compile(trcPath, tvrPath) → CompileResult.
using System.IO;
using System.Text;

namespace TSharpVision.ResourceCompiler;

/// <summary>Result returned by <see cref="Compiler.Compile"/>.</summary>
public sealed class CompileResult
{
    /// <summary>All diagnostics emitted during compilation.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Number of resource items written to the .tvr file.
    /// 0 when the compilation failed before any items were emitted.
    /// </summary>
    public int ItemsEmitted { get; }

    /// <summary>True when there are no error-level diagnostics.</summary>
    public bool Success => Diagnostics.Count == 0;

    public CompileResult(List<Diagnostic> diag, int itemsEmitted)
    {
        Diagnostics   = diag.AsReadOnly();
        ItemsEmitted  = itemsEmitted;
    }
}

/// <summary>
/// Single-entry-point compiler facade.
/// Pipeline: .trc text → Lexer → Parser → Builder → Emitter → .tvr
/// </summary>
public static class Compiler
{
    /// <summary>
    /// Compiles <paramref name="trcPath"/> to <paramref name="tvrPath"/>.
    /// </summary>
    /// <param name="trcPath">Input .trc text resource script (UTF-8).</param>
    /// <param name="tvrPath">Output .tvr binary resource file path.</param>
    /// <returns>A <see cref="CompileResult"/> with diagnostics and item count.</returns>
    public static CompileResult Compile(string trcPath, string tvrPath)
    {
        string source;
        try
        {
            source = File.ReadAllText(trcPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return new CompileResult(
                new List<Diagnostic>
                {
                    new Diagnostic("TRC0000", $"Cannot read source file: {ex.Message}", 1, 1),
                },
                0);
        }
        return CompileSource(source, tvrPath);
    }

    /// <summary>
    /// Compiles a .trc source string to <paramref name="tvrPath"/>.
    /// Useful for testing without touching the filesystem for the source.
    /// </summary>
    public static CompileResult CompileSource(string source, string tvrPath)
    {
        // StreamableRegistration must be called before any TStreamable is
        // constructed or written.  Safe to call multiple times (idempotent).
        StreamableRegistration.RegisterAll();

        var diag = new List<Diagnostic>();

        // 1. Lex
        var tokens = new Lexer(source, diag).Tokenize();

        // 2. Parse
        var ast = new Parser(tokens, diag).ParseFile();

        // 3. Build
        var resources = new Builder(diag).Build(ast);

        // Stop early if any errors occurred.
        if (diag.Count > 0 || resources.Count == 0)
            return new CompileResult(diag, 0);

        // 4. Emit
        try
        {
            Emitter.Emit(tvrPath, resources);
        }
        catch (Exception ex)
        {
            diag.Add(new Diagnostic("TRC0000",
                $"Emitter error: {ex.Message}", 0, 0));
        }

        return new CompileResult(diag, resources.Count);
    }

    // ── Format facade ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="source"/> and returns canonical formatted .trc text.
    /// <para>Comments are stripped — see <see cref="AstFormatter"/> for details.</para>
    /// </summary>
    public static FormatResult FormatSource(string source)
    {
        var diag   = new List<Diagnostic>();
        var tokens = new Lexer(source, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();

        if (diag.Count > 0)
            return new FormatResult(null, diag);

        string text = new AstFormatter().Format(ast);
        return new FormatResult(text, diag);
    }

    /// <summary>
    /// Reads <paramref name="trcPath"/> and returns canonical formatted .trc text.
    /// </summary>
    public static FormatResult FormatFile(string trcPath)
    {
        string source;
        try
        {
            source = System.IO.File.ReadAllText(trcPath, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return new FormatResult(null, new List<Diagnostic>
            {
                new Diagnostic("TRC0000", $"Cannot read source file: {ex.Message}", 1, 1),
            });
        }
        return FormatSource(source);
    }
}
