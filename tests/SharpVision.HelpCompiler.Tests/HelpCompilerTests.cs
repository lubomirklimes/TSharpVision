// Unit tests for the svhc (SharpVision Help Compiler) end-to-end pipeline.
// The compiler's Program class and Main method are internal/private, so all
// tests drive it through reflection.  All file I/O uses a temporary directory
// that is cleaned up on disposal.
using System;
using System.IO;
using System.Reflection;
using System.Text;
using SharpVision;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.HelpCompiler.Tests;

// Touches the global Pstream registry via THelpFile.RegisterStreamableTypes().
[Collection("NonParallel")]
public sealed class HelpCompilerTests : IDisposable
{
    // ── setup ─────────────────────────────────────────────────────────────────

    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;

    // Reflection handle to the private static int Main(string[]).
    private static readonly MethodInfo s_main =
        Assembly.Load("svhc")
                .GetType("SharpVision.HelpCompiler.Program")!
                .GetMethod("Main",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(string[]) },
                    null)!;

    public HelpCompilerTests()
    {
        _streams = new StreamableRegistryScope();
        _tmp = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
    }

    // Invokes the compiler and returns its exit code.
    private int Run(params string[] args) =>
        (int)s_main.Invoke(null, new object[] { args })!;

    // Writes a UTF-8 source file in the temp directory and returns its path.
    private string WriteSource(string name, string content) =>
        _tmp.CreateFile(name, content);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compile_MinimalSingleTopic_ReturnsZero()
    {
        string src = WriteSource("test.txt",
            ".topic Intro\n" +
            "Welcome to the help system.\n");
        string hlp = Path.Combine(_tmp.Path, "test.hlp");
        string sym = Path.Combine(_tmp.Path, "test.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_MinimalSingleTopic_CreatesHelpFile()
    {
        string src = WriteSource("test.txt",
            ".topic Intro\n" +
            "Welcome to the help system.\n");
        string hlp = Path.Combine(_tmp.Path, "test.hlp");
        string sym = Path.Combine(_tmp.Path, "test.cs");

        Run(src, hlp, sym);

        Assert.True(File.Exists(hlp), "compiled .hlp file should be created");
    }

    [Fact]
    public void Compile_MinimalSingleTopic_CreatesSymbolFile()
    {
        string src = WriteSource("test.txt",
            ".topic Intro\n" +
            "Welcome to the help system.\n");
        string hlp = Path.Combine(_tmp.Path, "test.hlp");
        string sym = Path.Combine(_tmp.Path, "test.cs");

        Run(src, hlp, sym);

        Assert.True(File.Exists(sym), "symbol .cs file should be created");
    }

    [Fact]
    public void Compile_MinimalSingleTopic_SymbolFileContainsConstant()
    {
        string src = WriteSource("test.txt",
            ".topic Intro\n" +
            "Welcome to the help system.\n");
        string hlp = Path.Combine(_tmp.Path, "test.hlp");
        string sym = Path.Combine(_tmp.Path, "test.cs");

        Run(src, hlp, sym);

        string csContent = File.ReadAllText(sym);
        Assert.Contains("hcIntro", csContent);
    }

    [Fact]
    public void Compile_MultipleTopics_ReturnsZero()
    {
        string src = WriteSource("multi.txt",
            ".topic Alpha\n" +
            "First topic body.\n" +
            "\n" +
            ".topic Beta\n" +
            "Second topic body.\n");
        string hlp = Path.Combine(_tmp.Path, "multi.hlp");
        string sym = Path.Combine(_tmp.Path, "multi.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_MultipleTopics_AllSymbolsEmitted()
    {
        string src = WriteSource("multi.txt",
            ".topic Alpha\n" +
            "First topic body.\n" +
            "\n" +
            ".topic Beta\n" +
            "Second topic body.\n");
        string hlp = Path.Combine(_tmp.Path, "multi.hlp");
        string sym = Path.Combine(_tmp.Path, "multi.cs");

        Run(src, hlp, sym);

        string csContent = File.ReadAllText(sym);
        Assert.Contains("hcAlpha", csContent);
        Assert.Contains("hcBeta", csContent);
    }

    [Fact]
    public void Compile_HelpFileIsNonEmpty()
    {
        string src = WriteSource("nonempty.txt",
            ".topic Main\n" +
            "Some body text.\n");
        string hlp = Path.Combine(_tmp.Path, "nonempty.hlp");
        string sym = Path.Combine(_tmp.Path, "nonempty.cs");

        Run(src, hlp, sym);

        var info = new FileInfo(hlp);
        Assert.True(info.Length > 0, ".hlp file should not be empty");
    }

    [Fact]
    public void Compile_NoArguments_ReturnsNonZero()
    {
        int rc = Run();

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_NonExistentInputFile_ReturnsNonZero()
    {
        string missing = Path.Combine(_tmp.Path, "does_not_exist.txt");
        string hlp = Path.Combine(_tmp.Path, "out.hlp");
        string sym = Path.Combine(_tmp.Path, "out.cs");

        int rc = Run(missing, hlp, sym);

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_EmptyInputFile_ReturnsZero()
    {
        // An empty file has no topics; the compiler should succeed with 0 definitions.
        string src = WriteSource("empty.txt", "");
        string hlp = Path.Combine(_tmp.Path, "empty.hlp");
        string sym = Path.Combine(_tmp.Path, "empty.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_EmptyInputFile_EmitsEmptySymbolClass()
    {
        string src = WriteSource("empty.txt", "");
        string hlp = Path.Combine(_tmp.Path, "empty.hlp");
        string sym = Path.Combine(_tmp.Path, "empty.cs");

        Run(src, hlp, sym);

        string csContent = File.ReadAllText(sym);
        Assert.Contains("public static class HelpCtx", csContent);
    }

    [Fact]
    public void Compile_MalformedTopicHeader_MissingSymbol_ReturnsNonZero()
    {
        // ".topic" followed by nothing — no symbol name.
        string src = WriteSource("bad.txt", ".topic\n");
        string hlp = Path.Combine(_tmp.Path, "bad.hlp");
        string sym = Path.Combine(_tmp.Path, "bad.cs");

        int rc = Run(src, hlp, sym);

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_MalformedTopicHeader_NonNumericExplicitId_ReturnsNonZero()
    {
        string src = WriteSource("bad2.txt", ".topic Foo=abc\n");
        string hlp = Path.Combine(_tmp.Path, "bad2.hlp");
        string sym = Path.Combine(_tmp.Path, "bad2.cs");

        int rc = Run(src, hlp, sym);

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_UnterminatedCrossRef_ReturnsNonZero()
    {
        string src = WriteSource("xref_bad.txt",
            ".topic Intro\n" +
            "See {this broken cross ref\n");
        string hlp = Path.Combine(_tmp.Path, "xref_bad.hlp");
        string sym = Path.Combine(_tmp.Path, "xref_bad.cs");

        int rc = Run(src, hlp, sym);

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_UnresolvedCrossRef_ReturnsZeroWithWarning()
    {
        // An unresolved {link} emits a warning but is not a fatal error.
        string src = WriteSource("xref_warn.txt",
            ".topic Intro\n" +
            "See {NoSuchTopic} for details.\n");
        string hlp = Path.Combine(_tmp.Path, "xref_warn.hlp");
        string sym = Path.Combine(_tmp.Path, "xref_warn.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_ResolvableCrossRef_ReturnsZero()
    {
        string src = WriteSource("xref_ok.txt",
            ".topic TopicA\n" +
            "Go to {TopicB} for more.\n" +
            "\n" +
            ".topic TopicB\n" +
            "You arrived.\n");
        string hlp = Path.Combine(_tmp.Path, "xref_ok.hlp");
        string sym = Path.Combine(_tmp.Path, "xref_ok.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_LiteralBrace_DoesNotThrow()
    {
        // '{{' should emit a literal '{' without being treated as a cross-ref.
        string src = WriteSource("brace.txt",
            ".topic Syntax\n" +
            "Use {{ to escape a brace.\n");
        string hlp = Path.Combine(_tmp.Path, "brace.hlp");
        string sym = Path.Combine(_tmp.Path, "brace.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_DuplicateTopicSymbol_ReturnsNonZero()
    {
        string src = WriteSource("dup.txt",
            ".topic Same\n" +
            "First.\n" +
            "\n" +
            ".topic Same\n" +
            "Second.\n");
        string hlp = Path.Combine(_tmp.Path, "dup.hlp");
        string sym = Path.Combine(_tmp.Path, "dup.cs");

        int rc = Run(src, hlp, sym);

        Assert.NotEqual(0, rc);
    }

    [Fact]
    public void Compile_ExplicitNumericId_AppearsInSymbolFile()
    {
        string src = WriteSource("numid.txt",
            ".topic Entry=100\n" +
            "Fixed-id topic.\n");
        string hlp = Path.Combine(_tmp.Path, "numid.hlp");
        string sym = Path.Combine(_tmp.Path, "numid.cs");

        Run(src, hlp, sym);

        string csContent = File.ReadAllText(sym);
        Assert.Contains("hcEntry", csContent);
        Assert.Contains("100", csContent);
    }

    [Fact]
    public void Compile_CrossRefWithAlias_ReturnsZero()
    {
        // {visible:alias} syntax — alias resolves the target, visible text shown.
        string src = WriteSource("alias.txt",
            ".topic Alpha\n" +
            "See {the page:Beta} for info.\n" +
            "\n" +
            ".topic Beta\n" +
            "Target page.\n");
        string hlp = Path.Combine(_tmp.Path, "alias.hlp");
        string sym = Path.Combine(_tmp.Path, "alias.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }

    [Fact]
    public void Compile_MultipleSymbolsOnOneTopic_AllInSymbolFile()
    {
        string src = WriteSource("multiid.txt",
            ".topic First, Second\n" +
            "Shared topic body.\n");
        string hlp = Path.Combine(_tmp.Path, "multiid.hlp");
        string sym = Path.Combine(_tmp.Path, "multiid.cs");

        Run(src, hlp, sym);

        string csContent = File.ReadAllText(sym);
        Assert.Contains("hcFirst", csContent);
        Assert.Contains("hcSecond", csContent);
    }

    [Fact]
    public void Compile_PreformattedParagraph_ReturnsZero()
    {
        // A paragraph that begins with a leading space is treated as preformatted.
        string src = WriteSource("preformat.txt",
            ".topic Code\n" +
            "    first preformatted line\n" +
            "    second preformatted line\n");
        string hlp = Path.Combine(_tmp.Path, "preformat.hlp");
        string sym = Path.Combine(_tmp.Path, "preformat.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
    }
}
