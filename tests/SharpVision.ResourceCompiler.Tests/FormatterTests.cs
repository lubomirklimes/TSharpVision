// AstFormatter / svrc format tests.
//
// Covers:
//   - Idempotence for dialog, menu, statusbar, and app-shell fixture.
//   - Comment stripping.
//   - String/const/key preservation.
//   - Semantic round-trip (format → compile → inspect).
//   - Structural output shape.
using System.IO;
using System.Linq;
using SharpVision;
using SharpVision.ResourceCompiler;
using SharpVision.ResourceTools;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.ResourceCompiler;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared fixture
// ─────────────────────────────────────────────────────────────────────────────

file static class FormatterFixture
{
    public const string AppShellTrc = @"
const cmAbout = 202;

resource menu ""menu.main"" {
  bounds (0, 0, 80, 1);

  submenu ""~F~ile"" {
    item ""E~x~it"" command=cmQuit key=""Alt+X"";
  }

  submenu ""~H~elp"" {
    item ""~A~bout"" command=cmAbout key=""F1"";
  }
}

resource statusbar ""status.main"" {
  bounds (0, 24, 80, 25);

  range 0 65535 {
    item ""~F1~ About""   command=cmAbout key=""F1"";
    item ""~Alt+X~ Exit"" command=cmQuit  key=""Alt+X"";
  }
}

resource dialog ""dialog.about"" {
  bounds (20, 7, 60, 15);
  title ""About"";
  palette wpGrayDialog;

  static ""SharpVision resource-backed app shell"" bounds=(3, 2, 37, 3);
  button ""~O~K"" bounds=(15, 5, 25, 7) command=cmOK default;
}";

    public const string DialogOnlyTrc = @"
resource dialog ""hello"" {
  bounds (10, 5, 50, 18);
  title ""Hello"";

  static ""Welcome"" bounds=(3, 2, 20, 3);
  button ""~O~K"" bounds=(15, 8, 25, 10) command=cmOK default;
  button ""~C~ancel"" bounds=(26, 8, 36, 10) command=cmCancel;
}";

    public const string MenuOnlyTrc = @"
resource menu ""main"" {
  bounds (0, 0, 80, 1);

  submenu ""~F~ile"" {
    item ""~O~pen"" command=cmOpen key=""F3"";
    separator;
    item ""E~x~it"" command=cmQuit key=""Alt+X"";
  }
}";

    public const string StatusBarOnlyTrc = @"
resource statusbar ""sbar"" {
  bounds (0, 24, 80, 25);

  range 0 65535 {
    item ""~F10~ Menu"" command=cmMenu key=""F10"";
    item ""~Alt+X~ Exit"" command=cmQuit key=""Alt+X"";
  }
}";

    public const string WithCommentsTrc = @"
// This is a line comment
const cmTest = 100; // inline comment

/* Block comment */
resource dialog ""test"" {
  bounds (0, 0, 40, 10); // dialog bounds
  title ""Test""; /* title comment */

  button ""OK"" bounds=(10, 5, 20, 7) command=cmOK default;
}";

    public const string WithValidatorsTrc = @"
resource dialog ""validated"" {
  bounds (5, 5, 55, 20);
  title ""Validated"";

  input ""Name"" bounds=(3, 2, 30, 3) validator=filter(""ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz "");
  input ""Age"" bounds=(3, 4, 15, 5) validator=range(0, 150);
  input ""Mask"" bounds=(3, 6, 20, 7) validator=picture(""###-####"");
}";

    public const string WithCheckboxRadioTrc = @"
resource dialog ""options"" {
  bounds (5, 5, 60, 22);
  title ""Options"";

  checkbox ""Option A"" bounds=(3, 2, 25, 3) command=cmOK items=(""One"", ""Two"", ""Three"");
  radio ""Mode"" bounds=(3, 5, 25, 8) command=cmOK items=(""Fast"", ""Slow"");
}";
}

// ─────────────────────────────────────────────────────────────────────────────
//  Pure formatter unit tests (no registry, no file I/O)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FormatterTests
{
    private static FormatResult Fmt(string src) => Compiler.FormatSource(src);

    private static TrcFile Parse(string src)
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        return new Parser(tokens, diag).ParseFile();
    }

    // ── Idempotence ────────────────────────────────────────────────────────

    [Fact]
    public void Format_Dialog_IsIdempotent()
    {
        var r1 = Fmt(FormatterFixture.DialogOnlyTrc);
        Assert.True(r1.Success);
        var r2 = Fmt(r1.Text);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);
    }

    [Fact]
    public void Format_Menu_IsIdempotent()
    {
        var r1 = Fmt(FormatterFixture.MenuOnlyTrc);
        Assert.True(r1.Success);
        var r2 = Fmt(r1.Text);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);
    }

    [Fact]
    public void Format_StatusBar_IsIdempotent()
    {
        var r1 = Fmt(FormatterFixture.StatusBarOnlyTrc);
        Assert.True(r1.Success);
        var r2 = Fmt(r1.Text);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);
    }

    [Fact]
    public void Format_AppShell_IsIdempotent()
    {
        var r1 = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r1.Success);
        var r2 = Fmt(r1.Text);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);
    }

    // ── Comments ───────────────────────────────────────────────────────────

    [Fact]
    public void Format_StripsLineComments()
    {
        var r = Fmt(FormatterFixture.WithCommentsTrc);
        Assert.True(r.Success);
        Assert.DoesNotContain("//", r.Text);
        Assert.DoesNotContain("This is a line comment", r.Text);
        Assert.DoesNotContain("inline comment", r.Text);
    }

    [Fact]
    public void Format_StripsBlockComments()
    {
        var r = Fmt(FormatterFixture.WithCommentsTrc);
        Assert.True(r.Success);
        Assert.DoesNotContain("/*", r.Text);
        Assert.DoesNotContain("Block comment", r.Text);
        Assert.DoesNotContain("title comment", r.Text);
    }

    [Fact]
    public void Format_CommentedSource_ParsesSuccessfully()
    {
        var r = Fmt(FormatterFixture.WithCommentsTrc);
        Assert.True(r.Success);
        // Formatted output must itself be parseable.
        var r2 = Fmt(r.Text);
        Assert.True(r2.Success);
    }

    // ── Const declarations ─────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesConstDeclarations()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        Assert.Contains("const cmAbout = 202;", r.Text);
    }

    [Fact]
    public void Format_PreservesCommandConstants()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        Assert.Contains("command=cmAbout", r.Text);
        Assert.Contains("command=cmQuit",  r.Text);
    }

    // ── Resource order ─────────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesTopLevelResourceOrder()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        int menuPos   = r.Text.IndexOf("resource menu");
        int statusPos = r.Text.IndexOf("resource statusbar");
        int dialogPos = r.Text.IndexOf("resource dialog");
        Assert.True(menuPos   < statusPos, "menu should come before statusbar");
        Assert.True(statusPos < dialogPos, "statusbar should come before dialog");
    }

    // ── Dialog structure ───────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesDialogControlsOrder()
    {
        var r = Fmt(FormatterFixture.DialogOnlyTrc);
        Assert.True(r.Success);
        int staticPos = r.Text.IndexOf("static ");
        int buttonPos = r.Text.IndexOf("button ");
        Assert.True(staticPos < buttonPos, "static should come before button");
    }

    [Fact]
    public void Format_PreservesPaletteField()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        Assert.Contains("palette wpGrayDialog;", r.Text);
    }

    [Fact]
    public void Format_DialogBoundsBeforeTitle()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        int boundsIdx = r.Text.IndexOf("bounds (20");
        int titleIdx  = r.Text.IndexOf("title \"About\"");
        Assert.True(boundsIdx < titleIdx);
    }

    // ── Menu structure ─────────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesMenuItemsOrder()
    {
        var r = Fmt(FormatterFixture.MenuOnlyTrc);
        Assert.True(r.Success);
        int openPos = r.Text.IndexOf("~O~pen");
        int exitPos = r.Text.IndexOf("E~x~it");
        Assert.True(openPos < exitPos);
    }

    [Fact]
    public void Format_Menu_Separator_IsPreserved()
    {
        var r = Fmt(FormatterFixture.MenuOnlyTrc);
        Assert.True(r.Success);
        Assert.Contains("separator;", r.Text);
    }

    // ── StatusBar structure ────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesStatusItemsOrder()
    {
        var r = Fmt(FormatterFixture.StatusBarOnlyTrc);
        Assert.True(r.Success);
        int menuPos = r.Text.IndexOf("~F10~ Menu");
        int exitPos = r.Text.IndexOf("~Alt+X~ Exit");
        Assert.True(menuPos < exitPos);
    }

    // ── String quoting ─────────────────────────────────────────────────────

    [Fact]
    public void Format_EscapesStrings()
    {
        // Verify QuoteString escapes double quotes and backslashes.
        Assert.Equal("\"hello \\\"world\\\"\"", AstFormatter.QuoteString("hello \"world\""));
        Assert.Equal("\"a\\\\b\"", AstFormatter.QuoteString("a\\b"));
        Assert.Equal("\"\\n\\r\\t\"", AstFormatter.QuoteString("\n\r\t"));
    }

    [Fact]
    public void Format_ProducesFinalNewline()
    {
        var r = Fmt(FormatterFixture.DialogOnlyTrc);
        Assert.True(r.Success);
        Assert.True(r.Text.EndsWith("\n"), "formatted output must end with newline");
    }

    // ── Validators ─────────────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesValidators()
    {
        var r = Fmt(FormatterFixture.WithValidatorsTrc);
        Assert.True(r.Success);
        Assert.Contains("validator=filter(", r.Text);
        Assert.Contains("validator=range(0, 150)", r.Text);
        Assert.Contains("validator=picture(", r.Text);
    }

    [Fact]
    public void Format_Validators_IsIdempotent()
    {
        var r1 = Fmt(FormatterFixture.WithValidatorsTrc);
        Assert.True(r1.Success);
        var r2 = Fmt(r1.Text);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);
    }

    // ── Checkbox / radio items ─────────────────────────────────────────────

    [Fact]
    public void Format_PreservesCheckboxRadioItems()
    {
        var r = Fmt(FormatterFixture.WithCheckboxRadioTrc);
        Assert.True(r.Success);
        Assert.Contains("checkbox ", r.Text);
        Assert.Contains("radio ", r.Text);
        Assert.Contains("\"One\"", r.Text);
        Assert.Contains("\"Fast\"", r.Text);
    }

    // ── Failure cases ──────────────────────────────────────────────────────

    [Fact]
    public void Format_InvalidSource_ReturnsFailure()
    {
        var r = Fmt("this is not valid trc !!!");
        Assert.False(r.Success);
        Assert.Null(r.Text);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void Format_EmptySource_ReturnsEmptyString()
    {
        var r = Fmt("");
        Assert.True(r.Success);
        Assert.NotNull(r.Text);
        Assert.Equal("", r.Text);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Semantic round-trip tests (NonParallel — touches registry + temp files)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class FormatterSemanticTests : IDisposable
{
    private readonly DriverScope   _driver;
    private readonly TempDirectory _tmp;

    public FormatterSemanticTests()
    {
        _driver = new DriverScope();
        _tmp    = new TempDirectory();
        StreamableRegistration.RegisterAll();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static FormatResult Fmt(string src) => Compiler.FormatSource(src);

    // ── Parse → Format → Parse equivalence ────────────────────────────────

    [Fact]
    public void Format_AppShell_ParsesAgain()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success, $"Format failed: {string.Join("; ", r.Diagnostics.Select(d => d.Message))}");

        var r2 = Fmt(r.Text);
        Assert.True(r2.Success, "Re-parse of formatted output failed");
    }

    [Fact]
    public void Format_PreservesResourceKeys()
    {
        var r = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(r.Success);
        Assert.Contains("\"menu.main\"",    r.Text);
        Assert.Contains("\"status.main\"",  r.Text);
        Assert.Contains("\"dialog.about\"", r.Text);
    }

    // ── Format → compile → .tvr inspect ───────────────────────────────────

    [Fact]
    public void Format_AppShell_CompilesToTvr()
    {
        var fmtResult = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(fmtResult.Success);

        string tvrPath = Path.Combine(_tmp.Path, "formatted.tvr");
        var compileResult = Compiler.CompileSource(fmtResult.Text, tvrPath);
        Assert.True(compileResult.Success,
            $"Compile failed: {string.Join("; ", compileResult.Diagnostics.Select(d => d.Message))}");

        var inspector = TvrInspector.Open(tvrPath);
        Assert.Equal(3, inspector.Entries.Count);
        var keys = inspector.Entries.Select(e => e.Key).ToList();
        Assert.Contains("menu.main",    keys);
        Assert.Contains("status.main",  keys);
        Assert.Contains("dialog.about", keys);
    }

    [Fact]
    public void Format_PreservesPaletteField_SemanticRoundTrip()
    {
        var fmtResult = Fmt(FormatterFixture.AppShellTrc);
        Assert.True(fmtResult.Success);
        // Re-parse and check palette survives formatting.
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(fmtResult.Text, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        Assert.Empty(diag);
        var dlg = ast.Resources.First(r => r.Key == "dialog.about");
        Assert.Equal("wpGrayDialog", dlg.Dialog.Palette);
    }

    // ── FormatFile ────────────────────────────────────────────────────────

    [Fact]
    public void FormatFile_ValidPath_ReturnsFormattedText()
    {
        string path = Path.Combine(_tmp.Path, "input.trc");
        File.WriteAllText(path, FormatterFixture.DialogOnlyTrc, System.Text.Encoding.UTF8);
        var r = Compiler.FormatFile(path);
        Assert.True(r.Success);
        Assert.NotEmpty(r.Text);
        Assert.Contains("resource dialog", r.Text);
    }

    [Fact]
    public void FormatFile_MissingPath_ReturnsFailure()
    {
        var r = Compiler.FormatFile(Path.Combine(_tmp.Path, "nonexistent.trc"));
        Assert.False(r.Success);
        Assert.Null(r.Text);
    }

    // ── Inplace simulation ────────────────────────────────────────────────

    [Fact]
    public void Format_Inplace_SimulatedRoundTrip()
    {
        // Simulate --inplace: read, format, write back, re-format, must be equal.
        string path = Path.Combine(_tmp.Path, "inplace.trc");
        File.WriteAllText(path, FormatterFixture.AppShellTrc, System.Text.Encoding.UTF8);

        var r1 = Compiler.FormatFile(path);
        Assert.True(r1.Success);
        File.WriteAllText(path, r1.Text, System.Text.Encoding.UTF8);

        var r2 = Compiler.FormatFile(path);
        Assert.True(r2.Success);
        Assert.Equal(r1.Text, r2.Text);  // idempotent
    }

    [Fact]
    public void Format_InvalidInput_Inplace_DoesNotModifyFile()
    {
        string path = Path.Combine(_tmp.Path, "bad.trc");
        const string badContent = "this is not valid!!!";
        File.WriteAllText(path, badContent, System.Text.Encoding.UTF8);

        var r = Compiler.FormatFile(path);
        Assert.False(r.Success);

        // Simulate --inplace guard: only write if Success.
        if (!r.Success)
        {
            // file should be unchanged
            string still = File.ReadAllText(path, System.Text.Encoding.UTF8);
            Assert.Equal(badContent, still);
        }
    }
}
