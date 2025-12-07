// Tests prove the .trc → .tvr → runtime load pipeline end-to-end using the
// app-shell.trc fixture content. All registry-touching tests use [NonParallel].
//
// The inline fixture mirrors samples/resources/app-shell.trc exactly.
using System.IO;
using System.Linq;
using SharpVision;
using SharpVision.Constants;
using SharpVision.ResourceCompiler;
using SharpVision.ResourceTools;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.ResourceCompiler;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared fixture — mirrors samples/resources/app-shell.trc exactly
// ─────────────────────────────────────────────────────────────────────────────

file static class AppShellFixture
{
    public const string Trc = @"
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
}

// ─────────────────────────────────────────────────────────────────────────────
//  Compile tests  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AppShellCompileTests
{
    private static List<Diagnostic> CompileDiag(string src)
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        new Parser(tokens, diag).ParseFile();
        return diag;
    }

    [Fact]
    public void AppShellTrc_ParsesWithNoDiagnostics()
    {
        var diag = CompileDiag(AppShellFixture.Trc);
        Assert.Empty(diag);
    }

    [Fact]
    public void AppShellTrc_ContainsThreeResources()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        Assert.Empty(diag);
        Assert.Equal(3, ast.Resources.Count);
    }

    [Fact]
    public void AppShellTrc_ResourceKeys_AreCorrect()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        var keys = ast.Resources.Select(r => r.Key).ToList();
        Assert.Contains("menu.main",    keys);
        Assert.Contains("status.main",  keys);
        Assert.Contains("dialog.about", keys);
    }

    [Fact]
    public void AppShellTrc_ResourceKinds_AreCorrect()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        Assert.Equal(ResourceKind.Menu,      ast.Resources[0].Kind);
        Assert.Equal(ResourceKind.StatusBar, ast.Resources[1].Kind);
        Assert.Equal(ResourceKind.Dialog,    ast.Resources[2].Kind);
    }

    [Fact]
    public void AppShellTrc_ConstCmAbout_IsResolved()
    {
        // The TRC defines `const cmAbout = 202`. Verify it parses as a const.
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        Assert.Empty(diag);
        Assert.Single(ast.Consts);
        Assert.Equal("cmAbout", ast.Consts[0].Name);
        Assert.Equal(202, ast.Consts[0].Value);
    }

    [Fact]
    public void AppShellTrc_DialogPalette_IsKnownName()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        var dlg = ast.Resources.First(r => r.Key == "dialog.about");
        Assert.Equal("wpGrayDialog", dlg.Dialog.Palette);
        Assert.True(CommandIds.IsKnownPaletteName(dlg.Dialog.Palette));
    }

    [Fact]
    public void AppShellTrc_Menu_HasTwoSubmenus()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        var menu = ast.Resources.First(r => r.Key == "menu.main").Menu;
        Assert.Equal(2, menu.Items.Count);
        Assert.Equal("~F~ile", menu.Items[0].Title);
        Assert.Equal("~H~elp", menu.Items[1].Title);
    }

    [Fact]
    public void AppShellTrc_StatusBar_HasOneRange()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        var sb = ast.Resources.First(r => r.Key == "status.main").StatusBar;
        Assert.Single(sb.Ranges);
        Assert.Equal(0,     sb.Ranges[0].Min);
        Assert.Equal(65535, sb.Ranges[0].Max);
        Assert.Equal(2, sb.Ranges[0].Items.Count);
    }

    [Fact]
    public void AppShellTrc_Dialog_HasTwoControls()
    {
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(AppShellFixture.Trc, diag).Tokenize();
        var ast = new Parser(tokens, diag).ParseFile();
        var dlg = ast.Resources.First(r => r.Key == "dialog.about").Dialog;
        Assert.Equal(2, dlg.Controls.Count);   // static + button
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Builder + runtime load tests  (NonParallel — touches registry)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class AppShellRuntimeTests : IDisposable
{
    private readonly DriverScope   _driver;
    private readonly TempDirectory _tmp;

    public AppShellRuntimeTests()
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

    private CompileResult Compile()
    {
        string path = Path.Combine(_tmp.Path, "app-shell.tvr");
        return Compiler.CompileSource(AppShellFixture.Trc, path);
    }

    private string CompilePath()
    {
        var r = Compile();
        Assert.True(r.Success, string.Join("; ", r.Diagnostics.Select(d => d.ToString())));
        return Path.Combine(_tmp.Path, "app-shell.tvr");
    }

    // ── Compile ───────────────────────────────────────────────────────────────

    [Fact]
    public void AppShell_CompileSucceeds()
    {
        var r = Compile();
        Assert.True(r.Success);
        Assert.Empty(r.Diagnostics);
        Assert.Equal(3, r.ItemsEmitted);
    }

    [Fact]
    public void AppShell_Tvr_FileExists()
    {
        var r = Compile();
        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(_tmp.Path, "app-shell.tvr")));
    }

    // ── Runtime load: menu.main ───────────────────────────────────────────────

    [Fact]
    public void AppShell_MenuMain_LoadsAsMenuBar()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var obj = rf.Get("menu.main");
        fp.Close();

        Assert.NotNull(obj);
        Assert.IsType<TMenuBar>(obj);
        ((TMenuBar)obj).ShutDown();
    }

    [Fact]
    public void AppShell_MenuMain_HasTwoTopLevelSubmenus()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var mb = (TMenuBar)rf.Get("menu.main");
        fp.Close();

        Assert.NotNull(mb?.Menu?.Items);
        // Count top-level menu items
        int count = 0;
        for (var it = mb.Menu.Items; it != null; it = it.Next) count++;
        Assert.Equal(2, count);
        Assert.Equal("~F~ile", mb.Menu.Items.Name);
        mb.ShutDown();
    }

    // ── Runtime load: status.main ─────────────────────────────────────────────

    [Fact]
    public void AppShell_StatusMain_LoadsAsStatusLine()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var obj = rf.Get("status.main");
        fp.Close();

        Assert.NotNull(obj);
        Assert.IsType<TStatusLine>(obj);
        ((TStatusLine)obj).ShutDown();
    }

    [Fact]
    public void AppShell_StatusMain_HasCorrectRange()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var sl = (TStatusLine)rf.Get("status.main");
        fp.Close();

        Assert.NotNull(sl.Defs);
        Assert.Equal(0,     sl.Defs.Min);
        Assert.Equal(65535, sl.Defs.Max);
        sl.ShutDown();
    }

    // ── Runtime load: dialog.about ────────────────────────────────────────────

    [Fact]
    public void AppShell_DialogAbout_LoadsAsDialog()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var obj = rf.Get("dialog.about");
        fp.Close();

        Assert.NotNull(obj);
        Assert.IsType<TDialog>(obj);
        ((TDialog)obj).ShutDown();
    }

    [Fact]
    public void AppShell_DialogAbout_TitleMatchesSource()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var dlg = (TDialog)rf.Get("dialog.about");
        fp.Close();

        Assert.Equal("About", dlg.title);
        dlg.ShutDown();
    }

    [Fact]
    public void AppShell_DialogAbout_BoundsMatchSource()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var dlg = (TDialog)rf.Get("dialog.about");
        fp.Close();

        // source: bounds (20, 7, 60, 15) → size 40×8
        Assert.Equal(40, dlg.size.x);
        Assert.Equal(8,  dlg.size.y);
        dlg.ShutDown();
    }

    [Fact]
    public void AppShell_DialogAbout_HasChildControls()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var dlg = (TDialog)rf.Get("dialog.about");
        fp.Close();

        // Dialog should have at least two children: TStaticText + TButton.
        int children = 0;
        dlg.ForEachView(_ => children++);
        Assert.True(children >= 2, $"Expected ≥ 2 child controls, got {children}");
        dlg.ShutDown();
    }

    // ── TResourceFile contains exactly expected keys ───────────────────────────

    [Fact]
    public void AppShell_Tvr_ContainsExactlyThreeKeys()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        int count = rf.Count();
        fp.Close();

        Assert.Equal(3, count);
    }

    [Fact]
    public void AppShell_Tvr_AllExpectedKeysPresent()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var keys = Enumerable.Range(0, rf.Count()).Select(i => rf.KeyAt((short)i)).ToList();
        fp.Close();

        Assert.Contains("menu.main",    keys);
        Assert.Contains("status.main",  keys);
        Assert.Contains("dialog.about", keys);
    }

    // ── svres validate compatibility ──────────────────────────────────────────

    [Fact]
    public void AppShell_SvresValidate_Succeeds()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.Validate(path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Errors:      0", result.Output);
        Assert.Contains("Dialogs:     1", result.Output);
        Assert.Contains("Menus:       1", result.Output);
        Assert.Contains("StatusLines: 1", result.Output);
    }

    [Fact]
    public void AppShell_SvresList_ShowsAllThreeKeys()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.List(path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("menu.main",    result.Output);
        Assert.Contains("status.main",  result.Output);
        Assert.Contains("dialog.about", result.Output);
    }

    [Fact]
    public void AppShell_SvresShow_MenuMain_ShowsMenuBar()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.Show(path, "menu.main");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("menu.main", result.Output);
        Assert.Contains("TMenuBar",  result.Output);
    }

    [Fact]
    public void AppShell_SvresShow_StatusMain_ShowsStatusLine()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.Show(path, "status.main");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status.main",  result.Output);
        Assert.Contains("TStatusLine",  result.Output);
    }

    [Fact]
    public void AppShell_SvresShow_DialogAbout_ShowsDialog()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.Show(path, "dialog.about");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dialog.about", result.Output);
        Assert.Contains("TDialog",      result.Output);
    }

    // ── Missing resource error cases ──────────────────────────────────────────

    [Fact]
    public void AppShell_MissingKey_ReturnsNull()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var obj = rf.Get("nonexistent.key");
        fp.Close();

        Assert.Null(obj);
    }

    [Fact]
    public void AppShell_SvresShow_MissingKey_ReturnsError()
    {
        string path = CompilePath();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var result = InspectorCommands.Show(path, "nonexistent.key");
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void AppShell_SvresList_MissingFile_ReturnsError()
    {
        var result = InspectorCommands.List(Path.Combine(_tmp.Path, "does-not-exist.tvr"));
        Assert.NotEqual(0, result.ExitCode);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Source file presence test  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AppShellSourceFileTests
{
    // The test binary output is bin/Debug/net8.0; the repo root is three levels up.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string TrcPath =>
        Path.Combine(RepoRoot, "Fixtures", "app-shell.trc");

    [Fact]
    public void AppShellTrcFile_ExistsInRepository()
    {
        Assert.True(File.Exists(TrcPath), $"Expected app-shell.trc at: {TrcPath}");
    }

    [Fact]
    public void AppShellTrcFile_CompilesWithNoDiagnostics()
    {
        if (!File.Exists(TrcPath))
            return; // skip if path resolution fails in unusual build layouts

        string src = File.ReadAllText(TrcPath, System.Text.Encoding.UTF8);
        var diag = new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        new Parser(tokens, diag).ParseFile();
        Assert.Empty(diag);
    }
}
