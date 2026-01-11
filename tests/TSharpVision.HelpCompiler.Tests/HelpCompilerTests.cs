// Unit tests for the svhc (TSharpVision Help Compiler) end-to-end pipeline.
// The compiler's Program class and Main method are internal/private, so all
// tests drive it through reflection.  All file I/O uses a temporary directory
// that is cleaned up on disposal.
using System;
using System.IO;
using System.Reflection;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.HelpCompiler.Tests;

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
                .GetType("TSharpVision.HelpCompiler.Program")!
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

    private (int ExitCode, string Out, string Err) RunCaptured(params string[] args)
    {
        TextWriter saveOut = Console.Out;
        TextWriter saveErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = Run(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(saveOut);
            Console.SetError(saveErr);
        }
    }

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
    public void Compile_DefaultFormat_WritesV2Magic()
    {
        string src = WriteSource("v2_magic.txt",
            ".topic Main=2\n" +
            "Unicode ready.\n");
        string hlp = Path.Combine(_tmp.Path, "v2_magic.hlp");
        string sym = Path.Combine(_tmp.Path, "v2_magic.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        Assert.Equal(THelpFile.magicHeaderV2, ReadMagic(hlp));
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        fp.Close();
        Assert.Equal(THelpFile.FormatV2Utf16, hf.formatVersion);
    }

    [Fact]
    public void Compile_FormatV1_WritesV1MagicAndByteHelp()
    {
        string src = WriteSource("v1_magic.txt",
            ".topic Main=2\n" +
            "Latin text.\n");
        string hlp = Path.Combine(_tmp.Path, "v1_magic.hlp");
        string sym = Path.Combine(_tmp.Path, "v1_magic.cs");

        int rc = Run("--format", "v1", src, hlp, sym);

        Assert.Equal(0, rc);
        Assert.Equal(THelpFile.magicHeader, ReadMagic(hlp));
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var topic = hf.GetTopic(2);
        fp.Close();
        Assert.Equal(THelpFile.FormatV1Latin1, hf.formatVersion);
        Assert.Equal("Latin text. ", TopicText(topic));
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

    [Fact]
    public void Diagnostics_TopicExpected_UsesStableCodeAndLocation()
    {
        string src = WriteSource("diag_expected.txt", "Body before topic.\n");
        string hlp = Path.Combine(_tmp.Path, "diag_expected.hlp");
        string sym = Path.Combine(_tmp.Path, "diag_expected.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SVHC0001", result.Err);
        Assert.Contains("diag_expected.txt(1,1): error", result.Err);
    }

    [Fact]
    public void Diagnostics_Redefinition_UsesStableCode()
    {
        string src = WriteSource("diag_redef.txt",
            ".topic Same\n" +
            "First.\n" +
            "\n" +
            ".topic Same\n" +
            "Second.\n");
        string hlp = Path.Combine(_tmp.Path, "diag_redef.hlp");
        string sym = Path.Combine(_tmp.Path, "diag_redef.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SVHC0002", result.Err);
        Assert.Contains("diag_redef.txt(4,1): error", result.Err);
    }

    [Fact]
    public void Diagnostics_UnterminatedCrossRef_UsesStableCode()
    {
        string src = WriteSource("diag_xref.txt",
            ".topic Intro\n" +
            "See {broken\n");
        string hlp = Path.Combine(_tmp.Path, "diag_xref.hlp");
        string sym = Path.Combine(_tmp.Path, "diag_xref.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SVHC0003", result.Err);
        Assert.Contains("diag_xref.txt(2,5): error", result.Err);
    }

    [Fact]
    public void Diagnostics_UnresolvedCrossRef_RemainsWarning()
    {
        string src = WriteSource("diag_warn.txt",
            ".topic Intro\n" +
            "See {Missing}.\n");
        string hlp = Path.Combine(_tmp.Path, "diag_warn.hlp");
        string sym = Path.Combine(_tmp.Path, "diag_warn.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SVHC0004", result.Err);
        Assert.Contains("warning", result.Err);
    }

    [Fact]
    public void Diagnostics_WarnAsError_PromotesWarningAndReturnsNonZero()
    {
        string src = WriteSource("diag_warn_error.txt",
            ".topic Intro\n" +
            "See {Missing}.\n");
        string hlp = Path.Combine(_tmp.Path, "diag_warn_error.hlp");
        string sym = Path.Combine(_tmp.Path, "diag_warn_error.cs");

        var result = RunCaptured("--warn-as-error", src, hlp, sym);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SVHC0004", result.Err);
        Assert.Contains("error", result.Err);
    }

    [Fact]
    public void UnknownDirective_BeforeTopic_WarnsAndDoesNotHideTopic()
    {
        string src = WriteSource("unknown_before.txt",
            ".foo future\n" +
            ".topic Intro\n" +
            "Body.\n");
        string hlp = Path.Combine(_tmp.Path, "unknown_before.hlp");
        string sym = Path.Combine(_tmp.Path, "unknown_before.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SVHC0010", result.Err);
        Assert.Contains("hcIntro", File.ReadAllText(sym));
    }

    [Fact]
    public void UnknownDirective_FormatV1_IsError()
    {
        string src = WriteSource("unknown_v1.txt",
            ".foo future\n" +
            ".topic Intro\n" +
            "Body.\n");
        string hlp = Path.Combine(_tmp.Path, "unknown_v1.hlp");
        string sym = Path.Combine(_tmp.Path, "unknown_v1.cs");

        var result = RunCaptured("--format", "v1", src, hlp, sym);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SVHC0010", result.Err);
        Assert.Contains("error", result.Err);
    }

    [Fact]
    public void UnknownDirective_InsideTopic_IsSkipped()
    {
        string src = WriteSource("unknown_inside.txt",
            ".topic Intro=2\n" +
            "Before.\n" +
            ".future on\n" +
            "After.\n");
        string hlp = Path.Combine(_tmp.Path, "unknown_inside.hlp");
        string sym = Path.Combine(_tmp.Path, "unknown_inside.cs");

        var result = RunCaptured(src, hlp, sym);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SVHC0010", result.Err);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var topic = hf.GetTopic(2);
        fp.Close();
        string text = TopicText(topic);
        Assert.Contains("Before.", text);
        Assert.Contains("After.", text);
    }

    [Fact]
    public void GeneratedIndex_EmptyIndexBody_GeneratesAlphabetizedLinks()
    {
        string src = WriteSource("gen_index.txt",
            ".topic Index=1\n" +
            "\n" +
            ".topic Beta=20\n" +
            "Beta body.\n" +
            "\n" +
            ".topic Alpha=10\n" +
            "Alpha body.\n");
        string hlp = Path.Combine(_tmp.Path, "gen_index.hlp");
        string sym = Path.Combine(_tmp.Path, "gen_index.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var index = hf.GetTopic(1);
        fp.Close();
        Assert.Equal(2, index.GetNumCrossRefs());
        Assert.Equal("Alpha\nBeta\n", TopicText(index));
        Assert.Equal(10, index.crossRefs[0].@ref);
        Assert.Equal(20, index.crossRefs[1].@ref);
    }

    [Fact]
    public void GeneratedIndex_FormatV1_DoesNotGenerateExtendedIndex()
    {
        string src = WriteSource("gen_index_v1.txt",
            ".topic Index=1\n" +
            "\n" +
            ".topic Alpha=10\n" +
            "Alpha body.\n");
        string hlp = Path.Combine(_tmp.Path, "gen_index_v1.hlp");
        string sym = Path.Combine(_tmp.Path, "gen_index_v1.cs");

        int rc = Run("--format", "v1", src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var index = hf.GetTopic(1);
        fp.Close();
        Assert.Equal(THelpFile.FormatV1Latin1, hf.formatVersion);
        Assert.Equal(0, index.GetNumCrossRefs());
        Assert.Equal(string.Empty, TopicText(index));
    }

    [Fact]
    public void GeneratedIndex_NonEmptyIndexBody_IsPreserved()
    {
        string src = WriteSource("manual_index.txt",
            ".topic Index=1\n" +
            "Custom index.\n" +
            "\n" +
            ".topic Alpha=2\n" +
            "Alpha body.\n");
        string hlp = Path.Combine(_tmp.Path, "manual_index.hlp");
        string sym = Path.Combine(_tmp.Path, "manual_index.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var index = hf.GetTopic(1);
        fp.Close();
        Assert.Equal(0, index.GetNumCrossRefs());
        Assert.Equal("Custom index. ", TopicText(index));
    }

    [Fact]
    public void GeneratedIndex_LinksResolveAtRuntime()
    {
        string src = WriteSource("index_runtime.txt",
            ".topic Index=1\n" +
            "\n" +
            ".topic Alpha=10\n" +
            "Alpha body.\n");
        string hlp = Path.Combine(_tmp.Path, "index_runtime.hlp");
        string sym = Path.Combine(_tmp.Path, "index_runtime.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var viewer = new THelpViewer(
            new TRect(0, 0, 40, 8),
            new TScrollBar(new TRect(0, 8, 40, 9)),
            new TScrollBar(new TRect(40, 0, 41, 8)),
            hf,
            1);
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbEnter;
        viewer.HandleEvent(ref ev);

        Assert.Equal(Events.evNothing, ev.What);
        Assert.Equal("Alpha body. ", TopicText(viewer.topic));
        fp.Close();
    }

    [Fact]
    public void Compile_UnicodeText_PreservesUtf16HelpText()
    {
        string src = WriteSource("unicode_text.txt",
            ".topic Unicode=2\n" +
            "Čeština Привет λ.\n");
        string hlp = Path.Combine(_tmp.Path, "unicode_text.hlp");
        string sym = Path.Combine(_tmp.Path, "unicode_text.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var topic = hf.GetTopic(2);
        fp.Close();
        Assert.Equal("Čeština Привет λ. ", TopicText(topic));
    }

    [Fact]
    public void Compile_UnicodeBeforeCrossRef_StoresCharacterOffsets()
    {
        string src = WriteSource("unicode_xref.txt",
            ".topic Main=2\n" +
            "Čau {odkaz:Target}.\n" +
            "\n" +
            ".topic Target=3\n" +
            "Cíl.\n");
        string hlp = Path.Combine(_tmp.Path, "unicode_xref.hlp");
        string sym = Path.Combine(_tmp.Path, "unicode_xref.cs");

        int rc = Run(src, hlp, sym);

        Assert.Equal(0, rc);
        var fp = new Fpstream(hlp);
        var hf = new THelpFile(fp);
        var topic = hf.GetTopic(2);
        fp.Close();
        Assert.Equal("Čau odkaz. ", TopicText(topic));
        Assert.Equal(4, topic.crossRefs[0].offset);
        Assert.Equal(5, topic.crossRefs[0].length);
        Assert.Equal(3, topic.crossRefs[0].@ref);
    }

    private static string TopicText(THelpTopic topic)
    {
        var sb = new StringBuilder();
        for (var p = topic.paragraphs; p != null; p = p.next)
            sb.Append(p.Text);
        return sb.ToString();
    }

    private static uint ReadMagic(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[4];
        Assert.Equal(4, fs.Read(bytes));
        return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
    }
}
