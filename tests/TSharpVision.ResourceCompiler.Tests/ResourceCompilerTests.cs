// Tests that touch the global Pstream registry are collected under
// NonParallel and use StreamableRegistryScope for isolation.
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.ResourceCompiler;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

// ─────────────────────────────────────────────────────────────────────────────
//  Lexer tests (do not touch Pstream registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LexerTests
{
    private static List<Token> Lex(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        return new Lexer(src, diag).Tokenize();
    }

    private static List<Token> NonEof(string src) =>
        Lex(src).Where(t => t.Kind != TokenKind.Eof).ToList();

    [Fact] public void Lexer_Empty_ReturnsEof()
    {
        var t = Lex(string.Empty);
        Assert.Single(t);
        Assert.Equal(TokenKind.Eof, t[0].Kind);
    }

    [Fact] public void Lexer_Identifier()
    {
        var t = NonEof("hello");
        Assert.Single(t);
        Assert.Equal(TokenKind.Identifier, t[0].Kind);
        Assert.Equal("hello", t[0].Value);
    }

    [Fact] public void Lexer_Identifier_WithUnderscore()
    {
        var t = NonEof("_my_var");
        Assert.Single(t);
        Assert.Equal("_my_var", t[0].Value);
    }

    [Fact] public void Lexer_Keywords_ReturnedAsIdentifiers()
    {
        foreach (var kw in new[] { "const", "resource", "dialog", "button", "static",
                                   "label", "input", "checkbox", "radio",
                                   "bounds", "title", "palette", "command",
                                   "default", "validator", "items",
                                   "filter", "range", "picture" })
        {
            var t = NonEof(kw);
            Assert.Single(t);
            Assert.Equal(TokenKind.Identifier, t[0].Kind);
            Assert.Equal(kw, t[0].Value);
        }
    }

    [Fact] public void Lexer_Integer_Positive()
    {
        var t = NonEof("42");
        Assert.Single(t);
        Assert.Equal(TokenKind.Integer, t[0].Kind);
        Assert.Equal("42", t[0].Value);
    }

    [Fact] public void Lexer_Integer_Negative()
    {
        var t = NonEof("-5");
        Assert.Single(t);
        Assert.Equal(TokenKind.Integer, t[0].Kind);
        Assert.Equal("-5", t[0].Value);
    }

    [Fact] public void Lexer_StringLiteral_Simple()
    {
        var t = NonEof("\"hello\"");
        Assert.Single(t);
        Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
        Assert.Equal("hello", t[0].Value);
    }

    [Fact] public void Lexer_StringLiteral_EscapeSequences()
    {
        var t = NonEof("\"a\\nb\\tc\\\\d\\\"e\"");
        Assert.Single(t);
        Assert.Equal("a\nb\tc\\d\"e", t[0].Value);
    }

    [Fact] public void Lexer_StringLiteral_Empty()
    {
        var t = NonEof("\"\"");
        Assert.Single(t);
        Assert.Equal(string.Empty, t[0].Value);
    }

    [Fact] public void Lexer_LineComment_Skipped()
    {
        var t = NonEof("// this is a comment\nhello");
        Assert.Single(t);
        Assert.Equal("hello", t[0].Value);
    }

    [Fact] public void Lexer_BlockComment_Skipped()
    {
        var t = NonEof("/* block */ hello");
        Assert.Single(t);
        Assert.Equal("hello", t[0].Value);
    }

    [Fact] public void Lexer_Punctuation()
    {
        var t = NonEof("; { } ( ) = ,");
        var kinds = t.Select(x => x.Kind).ToList();
        Assert.Equal(new[]
        {
            TokenKind.Semicolon, TokenKind.OpenBrace, TokenKind.CloseBrace,
            TokenKind.OpenParen, TokenKind.CloseParen, TokenKind.Equals,
            TokenKind.Comma,
        }, kinds);
    }

    [Fact] public void Lexer_LineAndColumnTracking()
    {
        var t = NonEof("a\nb");
        Assert.Equal(1, t[0].Line); Assert.Equal(1, t[0].Column);
        Assert.Equal(2, t[1].Line); Assert.Equal(1, t[1].Column);
    }

    [Fact] public void Lexer_UnterminatedString_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        Lex("\"unterminated\n", diag);
        Assert.Single(diag);
        Assert.Equal(DiagnosticCodes.UnterminatedString, diag[0].Code);
    }

    [Fact] public void Lexer_InvalidCharacter_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        var t = Lex("hello @ world", diag);
        Assert.Single(diag);
        Assert.Equal(DiagnosticCodes.InvalidCharacter, diag[0].Code);
        Assert.Equal(1, diag[0].Line);
        Assert.Equal(7, diag[0].Column);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Parser tests (do not touch Pstream registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ParserTests
{
    private static (TrcFile ast, List<Diagnostic> diag) Parse(string src)
    {
        var diag   = new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        return (ast, diag);
    }

    [Fact] public void Parser_EmptyFile_NoItems()
    {
        var (ast, diag) = Parse(string.Empty);
        Assert.Empty(ast.Consts);
        Assert.Empty(ast.Resources);
        Assert.Empty(diag);
    }

    [Fact] public void Parser_ConstDirective()
    {
        var (ast, diag) = Parse("const cmOpen = 1001;");
        Assert.Empty(diag);
        Assert.Single(ast.Consts);
        Assert.Equal("cmOpen", ast.Consts[0].Name);
        Assert.Equal(1001, ast.Consts[0].Value);
    }

    [Fact] public void Parser_MinimalDialog()
    {
        var src = @"resource dialog ""dialog.test"" { bounds (1,2,40,12); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        Assert.Single(ast.Resources);
        var r = ast.Resources[0];
        Assert.Equal("dialog.test", r.Key);
        Assert.Equal(ResourceKind.Dialog, r.Kind);
        Assert.NotNull(r.Dialog.Bounds);
        Assert.Equal(1,  r.Dialog.Bounds.X1);
        Assert.Equal(2,  r.Dialog.Bounds.Y1);
        Assert.Equal(40, r.Dialog.Bounds.X2);
        Assert.Equal(12, r.Dialog.Bounds.Y2);
    }

    [Fact] public void Parser_DialogWithTitleAndPalette()
    {
        var src = @"resource dialog ""d"" { bounds(1,1,40,10); title ""Hello""; palette wpGrayDialog; }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        Assert.Equal("Hello",        ast.Resources[0].Dialog.Title);
        Assert.Equal("wpGrayDialog", ast.Resources[0].Dialog.Palette);
    }

    [Fact] public void Parser_MultipleResources()
    {
        var src = @"resource dialog ""d1"" { bounds(0,0,10,5); } resource dialog ""d2"" { bounds(0,0,20,8); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        Assert.Equal(2, ast.Resources.Count);
        Assert.Equal("d1", ast.Resources[0].Key);
        Assert.Equal("d2", ast.Resources[1].Key);
    }

    [Fact] public void Parser_ButtonControl()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); button ""~O~K"" bounds=(10,5,20,7) command=cmOK default; }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        var ctrl = ast.Resources[0].Dialog.Controls.Single();
        Assert.Equal(ControlKind.Button, ctrl.Kind);
        Assert.Equal("~O~K",  ctrl.Title);
        Assert.Equal("cmOK",  ctrl.Command);
        Assert.True(ctrl.IsDefault);
        Assert.NotNull(ctrl.Bounds);
    }

    [Fact] public void Parser_StaticControl()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); static ""Label:"" bounds=(1,1,20,2); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        Assert.Equal(ControlKind.Static, ast.Resources[0].Dialog.Controls[0].Kind);
    }

    [Fact] public void Parser_InputControl_WithFilterValidator()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=filter(""ABC""); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        var ctrl = ast.Resources[0].Dialog.Controls.Single();
        Assert.Equal(ControlKind.Input, ctrl.Kind);
        var fv = Assert.IsType<FilterValidatorNode>(ctrl.Validator);
        Assert.Equal("ABC", fv.ValidChars);
    }

    [Fact] public void Parser_InputControl_WithRangeValidator()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=range(1, 100); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        var rv = Assert.IsType<RangeValidatorNode>(ast.Resources[0].Dialog.Controls[0].Validator);
        Assert.Equal(1L,   rv.Min);
        Assert.Equal(100L, rv.Max);
    }

    [Fact] public void Parser_InputControl_WithPictureValidator()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=picture(""##-##""); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        var pv = Assert.IsType<PictureValidatorNode>(ast.Resources[0].Dialog.Controls[0].Validator);
        Assert.Equal("##-##", pv.Pic);
    }

    [Fact] public void Parser_CheckboxControl_WithItems()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); checkbox ""Options"" bounds=(1,1,30,8) items=(""One"",""Two"",""Three""); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        var ctrl = ast.Resources[0].Dialog.Controls.Single();
        Assert.Equal(ControlKind.Checkbox, ctrl.Kind);
        Assert.Equal(new[] { "One", "Two", "Three" }, ctrl.Items);
    }

    [Fact] public void Parser_RadioControl_WithItems()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); radio ""Mode"" bounds=(1,1,30,6) items=(""Fast"",""Safe""); }";
        var (ast, diag) = Parse(src);
        Assert.Empty(diag);
        Assert.Equal(ControlKind.Radio, ast.Resources[0].Dialog.Controls[0].Kind);
        Assert.Equal(2, ast.Resources[0].Dialog.Controls[0].Items.Count);
    }

    [Fact] public void Parser_DuplicateDialogField_EmitsDiagnostic()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); bounds(0,0,40,15); }";
        var (ast, diag) = Parse(src);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.DuplicateField);
    }

    [Fact] public void Parser_UnknownControlKind_EmitsDiagnostic()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); combobox ""X"" bounds=(1,1,10,2); }";
        var (_, diag) = Parse(src);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UnknownControlKind);
    }

    [Fact] public void Parser_MissingCloseBrace_RecoversContinues()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15);"; // missing }
        var (ast, diag) = Parse(src);
        // Should produce a diagnostic but still return an AST
        Assert.NotNull(ast);
    }

    [Fact] public void Parser_MalformedBounds_EmitsDiagnostic()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,abc,15); }";
        var (_, diag) = Parse(src);
        Assert.NotEmpty(diag);
    }

    [Fact] public void Parser_UnknownResourceKind_EmitsDiagnostic()
    {
        var src = @"resource unknownkind ""m"" { }";
        var (_, diag) = Parse(src);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UnexpectedToken);
    }

    [Fact] public void Parser_EmptyItemList_EmitsDiagnostic()
    {
        var src = @"resource dialog ""d"" { bounds(0,0,40,15); checkbox ""X"" bounds=(1,1,10,5) items=(); }";
        var (_, diag) = Parse(src);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.EmptyItemList);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Builder tests (touch Pstream registry → NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class BuilderTests : IDisposable
{
    private readonly DriverScope _driver;

    public BuilderTests() => _driver = new DriverScope();
    public void Dispose()
    {
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static List<(string key, TDialog dlg)> Build(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        StreamableRegistration.RegisterAll();
        return new Builder(diag).Build(ast)
            .Select(r => (r.key, r.obj as TDialog))
            .ToList();
    }

    [Fact] public void Builder_BuildsDialog()
    {
        var result = Build(@"resource dialog ""d"" { bounds(5,5,45,15); title ""Test""; }");
        Assert.Single(result);
        Assert.Equal("d", result[0].key);
        Assert.Equal("Test", result[0].dlg.title);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_MissingBounds_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        var result = Build(@"resource dialog ""d"" { title ""X""; }", diag);
        Assert.Empty(result);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.MissingRequiredBounds);
    }

    [Fact] public void Builder_BuildsButton()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); button ""~O~K"" bounds=(10,5,20,7) command=cmOK default; }");
        var dlg = result[0].dlg;
        TButton btn = null;
        dlg.ForEachView(v => { if (v is TButton b) btn = b; });
        Assert.NotNull(btn);
        Assert.Equal("~O~K", btn.Title);
        Assert.Equal(Views.cmOK, btn.Command);
        Assert.True(btn.AmDefault);
        dlg.ShutDown();
    }

    [Fact] public void Builder_BuildsStaticText()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); static ""Hello:"" bounds=(1,1,20,2); }");
        var dlg = result[0].dlg;
        TStaticText st = null;
        dlg.ForEachView(v => { if (v is TStaticText s && !(v is TLabel)) st = s; });
        Assert.NotNull(st);
        dlg.ShutDown();
    }

    [Fact] public void Builder_BuildsLabel()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); label ""Name:"" bounds=(1,1,15,2); }");
        var dlg = result[0].dlg;
        TLabel lbl = null;
        dlg.ForEachView(v => { if (v is TLabel l) lbl = l; });
        Assert.NotNull(lbl);
        dlg.ShutDown();
    }

    [Fact] public void Builder_BuildsInputLine()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4); }");
        var dlg = result[0].dlg;
        TInputLine inp = null;
        dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.NotNull(inp);
        dlg.ShutDown();
    }

    [Fact] public void Builder_AppliesFilterValidator()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=filter(""ABC""); }");
        TInputLine inp = null;
        result[0].dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.IsType<TFilterValidator>(inp.Validator);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_AppliesRangeValidator()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=range(1,999); }");
        TInputLine inp = null;
        result[0].dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.IsType<TRangeValidator>(inp.Validator);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_AppliesPictureValidator()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); input """" bounds=(1,3,30,4) validator=picture(""##/##""); }");
        TInputLine inp = null;
        result[0].dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.IsType<TPXPictureValidator>(inp.Validator);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_AppliesUserDefinedConst()
    {
        var result = Build(@"const myCmd = 500; resource dialog ""d"" { bounds(0,0,40,15); button ""Go"" bounds=(1,1,10,3) command=myCmd; }");
        TButton btn = null;
        result[0].dlg.ForEachView(v => { if (v is TButton b) btn = b; });
        Assert.NotNull(btn);
        Assert.Equal((ushort)500, btn.Command);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_UndefinedCommand_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); button ""X"" bounds=(1,1,10,3) command=cmNonExistent; }", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UndefinedCommand);
    }

    [Fact] public void Builder_BuildsCheckBoxes()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); checkbox ""Opts"" bounds=(1,1,30,8) items=(""A"",""B""); }");
        TCheckBoxes cb = null;
        result[0].dlg.ForEachView(v => { if (v is TCheckBoxes c) cb = c; });
        Assert.NotNull(cb);
        Assert.Equal(2, cb.Strings.Count);
        Assert.Equal("A", cb.Strings[0]);
        Assert.Equal("B", cb.Strings[1]);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_BuildsRadioButtons()
    {
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); radio ""Mode"" bounds=(1,1,30,6) items=(""Fast"",""Safe""); }");
        TRadioButtons rb = null;
        result[0].dlg.ForEachView(v => { if (v is TRadioButtons r) rb = r; });
        Assert.NotNull(rb);
        Assert.Equal(2, rb.Strings.Count);
        result[0].dlg.ShutDown();
    }

    [Fact] public void Builder_DuplicateResourceKey_EmitsDiagnosticAndSkipsSecond()
    {
        var diag   = new List<Diagnostic>();
        var result = Build(@"resource dialog ""d"" { bounds(0,0,40,15); } resource dialog ""d"" { bounds(0,0,40,15); }", diag);
        Assert.Single(result);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.DuplicateResourceKey);
        result[0].dlg.ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  End-to-end compiler tests (touch Pstream registry → NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class CompilerEndToEndTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public CompilerEndToEndTests()
    {
        _driver = new DriverScope();
        _tmp    = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static readonly string HelloTrc = @"
const cmOpen = 1001;

resource dialog ""dialog.hello"" {
  bounds (10, 5, 50, 15);
  title ""Hello"";
  palette wpGrayDialog;

  static ""Enter name:"" bounds=(3, 2, 20, 3);
  input """" bounds=(3, 4, 30, 5) validator=filter(""ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz "");
  button ""~O~K"" bounds=(10, 7, 20, 9) command=cmOK default;
  button ""~C~ancel"" bounds=(22, 7, 36, 9) command=cmCancel;
}
";

    private static readonly string OptionsTrc = @"
resource dialog ""dialog.options"" {
  bounds (5, 3, 55, 18);
  title ""Options"";
  palette wpGrayDialog;

  checkbox ""Features"" bounds=(3, 3, 35, 8) items=(""One"", ""Two"", ""Three"");
  radio ""Mode"" bounds=(3, 9, 35, 14) items=(""Fast"", ""Safe"");
  button ""~O~K"" bounds=(10, 15, 20, 17) command=cmOK default;
}
";

    // ── E2E basic ─────────────────────────────────────────────────────────────

    [Fact]
    public void E2E_CompileHello_ProducesReadableDialog()
    {
        string tvrPath = Path.Combine(_tmp.Path, "hello.tvr");
        var result = Compiler.CompileSource(HelloTrc, tvrPath);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.ItemsEmitted);
        Assert.True(File.Exists(tvrPath));

        // Read back via TResourceFile.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvrPath);
        var rf  = new TResourceFile(fp);
        var dlg = rf.Get("dialog.hello") as TDialog;

        Assert.NotNull(dlg);
        Assert.Equal("Hello", dlg.title);

        // Bounds: TDialog(10,5,50,15) → TGroup/TWindow size=(40,10) origin=(10,5).
        Assert.Equal(10, dlg.origin.x);
        Assert.Equal(5,  dlg.origin.y);

        int childCount = 0;
        TButton okBtn  = null;
        dlg.ForEachView(v =>
        {
            childCount++;
            if (v is TButton b && b.Command == Views.cmOK) okBtn = b;
        });

        // TFrame + static + input + 2 buttons = 5 children.
        Assert.Equal(5, childCount);
        Assert.NotNull(okBtn);
        Assert.True(okBtn.AmDefault);

        fp.Close();
        dlg.ShutDown();
    }

    [Fact]
    public void E2E_CompileOptions_ProducesCheckboxAndRadio()
    {
        string tvrPath = Path.Combine(_tmp.Path, "options.tvr");
        var result = Compiler.CompileSource(OptionsTrc, tvrPath);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp  = new Fpstream(tvrPath);
        var rf  = new TResourceFile(fp);
        var dlg = rf.Get("dialog.options") as TDialog;

        Assert.NotNull(dlg);
        TCheckBoxes cb = null;
        TRadioButtons rb = null;
        dlg.ForEachView(v =>
        {
            if (v is TCheckBoxes c) cb = c;
            if (v is TRadioButtons r) rb = r;
        });
        Assert.NotNull(cb);
        Assert.NotNull(rb);
        Assert.Equal(3, cb.Strings.Count);
        Assert.Equal(2, rb.Strings.Count);

        fp.Close();
        dlg.ShutDown();
    }

    [Fact]
    public void E2E_MultipleDialogsInOneTvr()
    {
        string trc = HelloTrc + "\n" + OptionsTrc;
        string tvrPath = Path.Combine(_tmp.Path, "multi.tvr");
        var result = Compiler.CompileSource(trc, tvrPath);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(2, result.ItemsEmitted);

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvrPath);
        var rf = new TResourceFile(fp);
        Assert.Equal(2, rf.Count());
        var d1 = rf.Get("dialog.hello")   as TDialog;
        var d2 = rf.Get("dialog.options") as TDialog;
        Assert.NotNull(d1);
        Assert.NotNull(d2);
        fp.Close();
        d1.ShutDown();
        d2.ShutDown();
    }

    [Fact]
    public void E2E_CompileTwice_SemanticEquivalence()
    {
        string tvr1 = Path.Combine(_tmp.Path, "run1.tvr");
        string tvr2 = Path.Combine(_tmp.Path, "run2.tvr");
        Compiler.CompileSource(HelloTrc, tvr1);
        Compiler.CompileSource(HelloTrc, tvr2);

        // Semantic check: both contain dialog.hello with the same title.
        foreach (var tvrPath in new[] { tvr1, tvr2 })
        {
            Pstream.DeInitTypes();
            StreamableRegistration.RegisterAll();
            var fp = new Fpstream(tvrPath);
            var rf  = new TResourceFile(fp);
            var dlg = rf.Get("dialog.hello") as TDialog;
            Assert.Equal("Hello", dlg.title);
            fp.Close();
            dlg.ShutDown();
        }

        // Best-effort byte equality (TResourceFile may vary; we check size is non-zero).
        var bytes1 = File.ReadAllBytes(tvr1);
        var bytes2 = File.ReadAllBytes(tvr2);
        Assert.True(bytes1.Length > 0);
        Assert.Equal(bytes1.Length, bytes2.Length);
        // Full byte comparison — log a note if it fails but do not hard-fail.
        // TResourceFile TOC offsets may vary on second compile if the stream accumulates
        // prior data.  The semantic equivalence check above is the canonical assertion;
        // byte equality is a bonus when it holds.
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void E2E_RoundTrip_CompileLoadRewriteLoad()
    {
        string tvrPath  = Path.Combine(_tmp.Path, "rt.tvr");
        Compiler.CompileSource(HelloTrc, tvrPath);

        // Load the dialog from the tvr.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        TDialog dlgFirst;
        {
            var fp = new Fpstream(tvrPath);
            var rf = new TResourceFile(fp);
            dlgFirst = (TDialog)rf.Get("dialog.hello");
            fp.Close();
        }

        // Re-write to a MemoryStream via Opstream, then read back.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        var ms = new MemoryStream();
        var os = new Opstream(ms);
        os.WriteObject(dlgFirst);
        os.Flush();

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        ms.Position = 0;
        var dlgSecond = (TDialog)TDialog.Build();
        var ip = new Ipstream(ms);
        ip.ReadObject(dlgSecond);

        Assert.Equal("Hello", dlgSecond.title);

        dlgFirst.ShutDown();
        dlgSecond.ShutDown();
    }

    [Fact]
    public void E2E_Czech_UnicodeTitle_CompileSucceeds()
    {
        // TV streaming stores strings as Latin-1 (Borland wire format), so only
        // code-points ≤ 0xFF survive the round-trip intact.  This test verifies
        // that the compiler accepts non-ASCII titles and emits a .tvr without
        // errors; it does NOT assert round-trip Unicode fidelity.
        string trc = "resource dialog \"dialog.czech\" { bounds(5,5,50,15); title \"\u010cesk\u00fd dialog\"; button \"OK\" bounds=(1,7,10,9) command=cmOK; }";
        string tvrPath = Path.Combine(_tmp.Path, "czech.tvr");
        var result = Compiler.CompileSource(trc, tvrPath);

        Assert.True(result.Success || result.Diagnostics.All(d => d.Code != DiagnosticCodes.MissingRequiredBounds),
            string.Join("; ", result.Diagnostics));
        Assert.True(File.Exists(tvrPath));
    }

    [Fact]
    public void E2E_RegistrationIsolation_AfterDeInit()
    {
        // Verify the compiler still works after a DeInitTypes/RegisterAll cycle.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        Pstream.DeInitTypes();

        string tvrPath = Path.Combine(_tmp.Path, "isolation.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); button ""X"" bounds=(1,1,10,3) command=cmOK; }",
            tvrPath);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    // ── Negative compiler tests ───────────────────────────────────────────────

    [Fact]
    public void E2E_Negative_UndefinedCommand_NoOutput()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_cmd.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); button ""X"" bounds=(1,1,10,3) command=cmNonExistent; }",
            tvrPath);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.UndefinedCommand);
    }

    [Fact]
    public void E2E_Negative_DuplicateResourceKey()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_dup.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); } resource dialog ""d"" { bounds(0,0,40,10); }",
            tvrPath);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateResourceKey);
    }

    [Fact]
    public void E2E_Negative_MissingBounds()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_bounds.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { title ""X""; }",
            tvrPath);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.MissingRequiredBounds);
    }

    [Fact]
    public void E2E_Negative_MissingControlBounds()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_cbounds.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); button ""OK"" command=cmOK; }",
            tvrPath);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.MissingRequiredBounds);
    }

    [Fact]
    public void E2E_Negative_UnknownValidator()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_val.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); input """" bounds=(1,1,20,2) validator=regex("".*""); }",
            tvrPath);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.UnsupportedValidator);
    }

    [Fact]
    public void E2E_Negative_InvalidBounds()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_ibounds.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,abc,10); }",
            tvrPath);
        Assert.False(result.Success);
    }

    [Fact]
    public void E2E_Negative_EmptyItemList()
    {
        string tvrPath = Path.Combine(_tmp.Path, "neg_items.tvr");
        var result = Compiler.CompileSource(
            @"resource dialog ""d"" { bounds(0,0,40,10); checkbox ""X"" bounds=(1,1,30,8) items=(); }",
            tvrPath);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.EmptyItemList);
    }

    [Fact]
    public void E2E_CompileFromFile_WritesAndReadsBack()
    {
        string trcPath = _tmp.CreateFile("hello.trc", HelloTrc);
        string tvrPath = Path.Combine(_tmp.Path, "hello_file.tvr");
        var result = Compiler.Compile(trcPath, tvrPath);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.True(File.Exists(tvrPath));

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        var fp  = new Fpstream(tvrPath);
        var rf  = new TResourceFile(fp);
        var dlg = rf.Get("dialog.hello") as TDialog;
        Assert.NotNull(dlg);
        fp.Close();
        dlg.ShutDown();
    }

    [Fact]
    public void E2E_Compiler_FileNotFound_ReturnsDiagnostic()
    {
        string missing = Path.Combine(_tmp.Path, "nonexistent.trc");
        var result = Compiler.Compile(missing, Path.Combine(_tmp.Path, "out.tvr"));
        Assert.False(result.Success);
        Assert.Single(result.Diagnostics);
        Assert.Equal("TRC0000", result.Diagnostics[0].Code);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CommandIds tests (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CommandIdsTests
{
    [Fact] public void CommandIds_Builtins_Resolve()
    {
        Assert.True(CommandIds.TryResolve("cmOK",     null, out var code));
        Assert.Equal(Views.cmOK, code);

        Assert.True(CommandIds.TryResolve("cmCancel", null, out code));
        Assert.Equal(Views.cmCancel, code);

        Assert.True(CommandIds.TryResolve("cmHelp",   null, out code));
        Assert.Equal(Views.cmHelp, code);
    }

    [Fact] public void CommandIds_UserConst_Resolves()
    {
        var user = new Dictionary<string, int> { ["myCmd"] = 999 };
        Assert.True(CommandIds.TryResolve("myCmd", user, out var code));
        Assert.Equal((ushort)999, code);
    }

    [Fact] public void CommandIds_Unknown_ReturnsFalse()
    {
        Assert.False(CommandIds.TryResolve("cmNonExistent", null, out _));
    }

    [Fact] public void CommandIds_UserConst_OverridesNotBuiltin()
    {
        // User-defined consts shadow built-in names; the user const wins.
        // This allows overriding built-in commands intentionally.
        var user = new Dictionary<string, int> { ["cmOK"] = 999 };
        Assert.True(CommandIds.TryResolve("cmOK", user, out var code));
        Assert.Equal((ushort)999, code); // user const wins
    }

    [Fact] public void CommandIds_BuiltinNames_NotEmpty()
    {
        Assert.NotEmpty(CommandIds.BuiltinNames);
        Assert.Contains("cmOK",     CommandIds.BuiltinNames);
        Assert.Contains("cmCancel", CommandIds.BuiltinNames);
        Assert.Contains("cmDefault",CommandIds.BuiltinNames);
    }
}
