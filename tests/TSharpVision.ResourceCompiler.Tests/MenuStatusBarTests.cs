// Menu and StatusBar resource compiler tests.
// Tests that touch the global Pstream registry use [Collection("NonParallel")].
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.ResourceCompiler;
using TSharpVision.ResourceTools;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

// ─────────────────────────────────────────────────────────────────────────────
//  Parser — menu syntax  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MenuParserTests
{
    private static TrcFile Parse(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        return new Parser(tokens, diag).ParseFile();
    }

    [Fact]
    public void Menu_ParsesResourceKind()
    {
        var ast = Parse(@"resource menu ""m"" { }");
        Assert.Single(ast.Resources);
        Assert.Equal(ResourceKind.Menu, ast.Resources[0].Kind);
        Assert.Equal("m", ast.Resources[0].Key);
    }

    [Fact]
    public void Menu_ParsesSubmenu()
    {
        var ast = Parse(@"resource menu ""m"" {
  submenu ""~F~ile"" {
    item ""~O~pen"" command=cmOpen key=""F3"";
    separator;
  }
}");
        var body = ast.Resources[0].Menu;
        Assert.NotNull(body);
        Assert.Single(body.Items);
        var sm = body.Items[0];
        Assert.Equal(MenuItemKind.Submenu, sm.Kind);
        Assert.Equal("~F~ile", sm.Title);
        Assert.Equal(2, sm.Children.Count);
        Assert.Equal(MenuItemKind.Item,      sm.Children[0].Kind);
        Assert.Equal(MenuItemKind.Separator, sm.Children[1].Kind);
    }

    [Fact]
    public void Menu_ParsesNestedSubmenus()
    {
        var ast = Parse(@"resource menu ""m"" {
  submenu ""Top"" {
    submenu ""Nested"" {
      item ""Leaf"" command=cmOK;
    }
  }
}");
        var top    = ast.Resources[0].Menu.Items[0];
        var nested = top.Children[0];
        Assert.Equal(MenuItemKind.Submenu, nested.Kind);
        Assert.Equal("Nested", nested.Title);
        Assert.Equal(MenuItemKind.Item, nested.Children[0].Kind);
    }

    [Fact]
    public void Menu_ParsesItemWithNoKey()
    {
        var ast = Parse(@"resource menu ""m"" {
  submenu ""A"" {
    item ""About"" command=cmAbout;
  }
}");
        var item = ast.Resources[0].Menu.Items[0].Children[0];
        Assert.Equal(MenuItemKind.Item, item.Kind);
        Assert.Equal("cmAbout", item.Command);
        Assert.Null(item.Key);
    }

    [Fact]
    public void Menu_ParsesOptionalBounds()
    {
        var ast = Parse(@"resource menu ""m"" {
  bounds (0, 0, 80, 1);
}");
        var body = ast.Resources[0].Menu;
        Assert.NotNull(body.Bounds);
        Assert.Equal(0, body.Bounds.X1);
        Assert.Equal(0, body.Bounds.Y1);
        Assert.Equal(80, body.Bounds.X2);
        Assert.Equal(1, body.Bounds.Y2);
    }

    [Fact]
    public void Menu_EmptyBody_IsValid()
    {
        var diag = new List<Diagnostic>();
        var ast = Parse(@"resource menu ""m"" { }", diag);
        Assert.Empty(diag);
        Assert.Empty(ast.Resources[0].Menu.Items);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Parser — statusbar syntax  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StatusBarParserTests
{
    private static TrcFile Parse(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        return new Parser(tokens, diag).ParseFile();
    }

    [Fact]
    public void StatusBar_ParsesResourceKind()
    {
        var ast = Parse(@"resource statusbar ""s"" { }");
        Assert.Single(ast.Resources);
        Assert.Equal(ResourceKind.StatusBar, ast.Resources[0].Kind);
    }

    [Fact]
    public void StatusBar_ParsesRange()
    {
        var ast = Parse(@"resource statusbar ""s"" {
  range 0 65535 {
    item ""~F1~ Help"" command=cmHelp key=""F1"";
  }
}");
        var body = ast.Resources[0].StatusBar;
        Assert.NotNull(body);
        Assert.Single(body.Ranges);
        var rng = body.Ranges[0];
        Assert.Equal(0, rng.Min);
        Assert.Equal(65535, rng.Max);
        Assert.Single(rng.Items);
        Assert.Equal("~F1~ Help", rng.Items[0].Text);
        Assert.Equal("cmHelp", rng.Items[0].Command);
        Assert.Equal("F1", rng.Items[0].Key);
    }

    [Fact]
    public void StatusBar_ParsesMultipleRanges()
    {
        var ast = Parse(@"resource statusbar ""s"" {
  range 0 100 {
    item ""A"" command=cmOK key=""F1"";
  }
  range 101 200 {
    item ""B"" command=cmCancel key=""F2"";
  }
}");
        var body = ast.Resources[0].StatusBar;
        Assert.Equal(2, body.Ranges.Count);
        Assert.Equal(0,   body.Ranges[0].Min);
        Assert.Equal(100, body.Ranges[0].Max);
        Assert.Equal(101, body.Ranges[1].Min);
        Assert.Equal(200, body.Ranges[1].Max);
    }

    [Fact]
    public void StatusBar_ParsesOptionalBounds()
    {
        var ast = Parse(@"resource statusbar ""s"" {
  bounds (0, 24, 80, 25);
}");
        var body = ast.Resources[0].StatusBar;
        Assert.NotNull(body.Bounds);
        Assert.Equal(24, body.Bounds.Y1);
        Assert.Equal(25, body.Bounds.Y2);
    }

    [Fact]
    public void StatusBar_EmptyBody_IsValid()
    {
        var diag = new List<Diagnostic>();
        var ast = Parse(@"resource statusbar ""s"" { }", diag);
        Assert.Empty(diag);
        Assert.Empty(ast.Resources[0].StatusBar.Ranges);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CommandIds extension tests  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CommandIdsExtensionTests
{
    [Theory]
    [InlineData("cmQuit",    Views.cmQuit)]
    [InlineData("cmClose",   Views.cmClose)]
    [InlineData("cmHelp",    Views.cmHelp)]
    [InlineData("cmOK",      Views.cmOK)]
    [InlineData("cmCancel",  Views.cmCancel)]
    [InlineData("cmCut",     Views.cmCut)]
    [InlineData("cmCopy",    Views.cmCopy)]
    [InlineData("cmPaste",   Views.cmPaste)]
    [InlineData("cmSave",    80)]
    [InlineData("cmSaveAs",  81)]
    [InlineData("cmOpen",    100)]
    [InlineData("cmNew",     101)]
    public void BuiltinCommand_Resolves(string name, int expectedCode)
    {
        bool ok = CommandIds.TryResolve(name, new Dictionary<string, int>(), out ushort code);
        Assert.True(ok);
        Assert.Equal((ushort)expectedCode, code);
    }

    [Fact]
    public void UserConst_OverridesBuiltin()
    {
        var consts = new Dictionary<string, int> { ["cmSave"] = 999 };
        CommandIds.TryResolve("cmSave", consts, out ushort code);
        Assert.Equal((ushort)999, code);
    }

    [Theory]
    [InlineData("F1",  Keys.kbF1)]
    [InlineData("F2",  Keys.kbF2)]
    [InlineData("F3",  Keys.kbF3)]
    [InlineData("F10", Keys.kbF10)]
    [InlineData("Alt+X", Keys.kbAltX)]
    [InlineData("Ctrl+Ins", Keys.kbCtrlIns)]
    [InlineData("Shift+Ins", Keys.kbShiftIns)]
    [InlineData("Alt+Back",  Keys.kbAltBack)]
    public void KeyResolver_KnownKey_Resolves(string name, ushort expectedCode)
    {
        bool ok = CommandIds.TryResolveKey(name, out ushort code);
        Assert.True(ok);
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void KeyResolver_UnknownKey_ReturnsFalse()
    {
        bool ok = CommandIds.TryResolveKey("F99", out ushort code);
        Assert.False(ok);
        Assert.Equal(Keys.kbNoKey, code);
    }

    [Fact]
    public void KeyResolver_CaseInsensitive()
    {
        CommandIds.TryResolveKey("f1", out ushort lo);
        CommandIds.TryResolveKey("F1", out ushort up);
        Assert.Equal(lo, up);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Builder — menu  (touch registry → NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class MenuBuilderTests : IDisposable
{
    private readonly DriverScope _driver;

    public MenuBuilderTests() => _driver = new DriverScope();

    public void Dispose()
    {
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static List<(string key, TStreamable obj)> Build(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        StreamableRegistration.RegisterAll();
        return new Builder(diag).Build(ast);
    }

    [Fact]
    public void Builder_BuildsMenuBar()
    {
        var result = Build(@"resource menu ""m"" {
  submenu ""~F~ile"" {
    item ""~O~pen"" command=cmOpen key=""F3"";
  }
}");
        Assert.Single(result);
        var mb = result[0].obj as TMenuBar;
        Assert.NotNull(mb);
        Assert.NotNull(mb.Menu);
        // Top-level: one submenu named ~F~ile
        Assert.NotNull(mb.Menu.Items);
        Assert.Equal("~F~ile", mb.Menu.Items.Name);
        mb.ShutDown();
    }

    [Fact]
    public void Builder_MenuBar_DefaultBounds()
    {
        var result = Build(@"resource menu ""m"" { }");
        var mb = result[0].obj as TMenuBar;
        Assert.Equal(0, mb.origin.x);
        Assert.Equal(0, mb.origin.y);
        Assert.Equal(80, mb.size.x);
        Assert.Equal(1, mb.size.y);
        mb.ShutDown();
    }

    [Fact]
    public void Builder_MenuBar_ExplicitBounds()
    {
        var result = Build(@"resource menu ""m"" { bounds(10, 2, 70, 3); }");
        var mb = result[0].obj as TMenuBar;
        Assert.Equal(10, mb.origin.x);
        Assert.Equal(2, mb.origin.y);
        Assert.Equal(60, mb.size.x);
        Assert.Equal(1, mb.size.y);
        mb.ShutDown();
    }

    [Fact]
    public void Builder_MenuBar_SeparatorInSubmenu()
    {
        var result = Build(@"resource menu ""m"" {
  submenu ""~F~ile"" {
    item ""Open"" command=cmOpen;
    separator;
    item ""Quit"" command=cmQuit;
  }
}");
        var mb  = (TMenuBar)result[0].obj;
        var sm  = mb.Menu.Items;              // first top-level = submenu
        var sub = sm.SubMenu.Items;           // Open
        Assert.Null(sub.Next.Name);           // separator has Name=null
        Assert.Equal(0, sub.Next.Command);    // separator Command=0
        Assert.NotNull(sub.Next.Next);        // Quit
        mb.ShutDown();
    }

    [Fact]
    public void Builder_MenuBar_NestedSubmenus()
    {
        var result = Build(@"resource menu ""m"" {
  submenu ""Top"" {
    submenu ""Sub"" {
      item ""Leaf"" command=cmOK;
    }
  }
}");
        var mb     = (TMenuBar)result[0].obj;
        var top    = mb.Menu.Items;
        var sub    = top.SubMenu.Items;
        Assert.Equal(0, sub.Command);         // sub is a submenu (Command=0)
        Assert.NotNull(sub.SubMenu);
        var leaf   = sub.SubMenu.Items;
        Assert.Equal(Views.cmOK, leaf.Command);
        mb.ShutDown();
    }

    [Fact]
    public void Builder_EmptySubmenu_EmitsDiagnostic()
    {
        var diag   = new List<Diagnostic>();
        var result = Build(@"resource menu ""m"" {
  submenu ""Empty"" { }
}", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.EmptySubmenu);
        // Should still build — empty submenu allowed.
        Assert.NotEmpty(result);
        ((TMenuBar)result[0].obj).ShutDown();
    }

    [Fact]
    public void Builder_MenuBar_UnknownKey_EmitsTRC0208()
    {
        var diag   = new List<Diagnostic>();
        var result = Build(@"resource menu ""m"" {
  submenu ""A"" {
    item ""X"" command=cmOK key=""F99"";
  }
}", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UnknownKeyName);
        Assert.NotEmpty(result);
        ((TMenuBar)result[0].obj).ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Builder — statusbar  (touch registry → NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class StatusBarBuilderTests : IDisposable
{
    private readonly DriverScope _driver;

    public StatusBarBuilderTests() => _driver = new DriverScope();

    public void Dispose()
    {
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static List<(string key, TStreamable obj)> Build(string src, List<Diagnostic> diag = null)
    {
        diag ??= new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        StreamableRegistration.RegisterAll();
        return new Builder(diag).Build(ast);
    }

    [Fact]
    public void Builder_BuildsStatusLine()
    {
        var result = Build(@"resource statusbar ""s"" {
  range 0 65535 {
    item ""~F1~ Help"" command=cmHelp key=""F1"";
  }
}");
        Assert.Single(result);
        var sl = result[0].obj as TStatusLine;
        Assert.NotNull(sl);
        Assert.NotNull(sl.Defs);
        Assert.Equal(0,     sl.Defs.Min);
        Assert.Equal(65535, sl.Defs.Max);
        Assert.NotNull(sl.Defs.Items);
        sl.ShutDown();
    }

    [Fact]
    public void Builder_StatusLine_DefaultBounds()
    {
        var result = Build(@"resource statusbar ""s"" {
  range 0 65535 { }
}");
        var sl = (TStatusLine)result[0].obj;
        Assert.Equal(0,  sl.origin.x);
        Assert.Equal(24, sl.origin.y);
        Assert.Equal(80, sl.size.x);
        Assert.Equal(1,  sl.size.y);
        sl.ShutDown();
    }

    [Fact]
    public void Builder_StatusLine_ExplicitBounds()
    {
        var result = Build(@"resource statusbar ""s"" {
  bounds (0, 23, 80, 24);
  range 0 65535 { }
}");
        var sl = (TStatusLine)result[0].obj;
        Assert.Equal(23, sl.origin.y);
        Assert.Equal(24, sl.origin.y + sl.size.y);
        sl.ShutDown();
    }

    [Fact]
    public void Builder_StatusLine_MultipleRanges()
    {
        var result = Build(@"resource statusbar ""s"" {
  range 0 100 {
    item ""~F1~ Help"" command=cmHelp key=""F1"";
  }
  range 101 200 {
    item ""~F2~ Save"" command=cmSave key=""F2"";
  }
}");
        var sl = (TStatusLine)result[0].obj;
        Assert.NotNull(sl.Defs);
        Assert.NotNull(sl.Defs.Next);
        Assert.Equal(0,   sl.Defs.Min);
        Assert.Equal(100, sl.Defs.Max);
        Assert.Equal(101, sl.Defs.Next.Min);
        Assert.Equal(200, sl.Defs.Next.Max);
        sl.ShutDown();
    }

    [Fact]
    public void Builder_StatusLine_InvalidRange_EmitsDiagnostic()
    {
        var diag   = new List<Diagnostic>();
        var result = Build(@"resource statusbar ""s"" {
  range 200 100 {
    item ""X"" command=cmOK key=""F1"";
  }
}", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.InvalidStatusRange);
    }

    [Fact]
    public void Builder_EmptyStatusBar_EmitsDiagnostic()
    {
        var diag   = new List<Diagnostic>();
        var result = Build(@"resource statusbar ""s"" { }", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.EmptyStatusRange);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Inline fixture sources
// ─────────────────────────────────────────────────────────────────────────────

file static class MenuStatusBarFixtures
{
    public const string MenuTrc = @"
const cmAbout = 1000;

resource menu ""menu.main"" {
  submenu ""~F~ile"" {
    item ""~O~pen"" command=cmOpen key=""F3"";
    item ""~S~ave"" command=cmSave key=""F2"";
    separator;
    item ""E~x~it"" command=cmQuit key=""Alt+X"";
  }

  submenu ""~E~dit"" {
    item ""~C~ut""   command=cmCut   key=""Shift+Del"";
    item ""~C~opy""  command=cmCopy  key=""Ctrl+Ins"";
    item ""~P~aste"" command=cmPaste key=""Shift+Ins"";
    separator;
    item ""~U~ndo""  command=cmUndo  key=""Alt+Back"";
  }

  submenu ""~H~elp"" {
    item ""~A~bout"" command=cmAbout;
  }
}
";

    public const string StatusBarTrc = @"
resource statusbar ""status.main"" {
  range 0 65535 {
    item ""~F1~ Help""    command=cmHelp  key=""F1"";
    item ""~F2~ Save""    command=cmSave  key=""F2"";
    item ""~F3~ Open""    command=cmOpen  key=""F3"";
    item ""~Alt+X~ Exit"" command=cmQuit  key=""Alt+X"";
  }
}
";

    public const string AppShellTrc = @"
const cmAbout = 1000;

resource dialog ""dialog.about"" {
  bounds (20, 8, 60, 16);
  title ""About"";
  static ""TSharpVision v1"" bounds=(2, 2, 38, 3);
  button ""~O~K"" bounds=(14, 5, 26, 7) command=cmOK default;
}

resource menu ""menu.main"" {
  submenu ""~F~ile"" {
    item ""~O~pen"" command=cmOpen key=""F3"";
    item ""~S~ave"" command=cmSave key=""F2"";
    separator;
    item ""E~x~it"" command=cmQuit key=""Alt+X"";
  }

  submenu ""~H~elp"" {
    item ""~A~bout..."" command=cmAbout;
  }
}

resource statusbar ""status.main"" {
  range 0 65535 {
    item ""~F1~ Help""    command=cmHelp  key=""F1"";
    item ""~F3~ Open""    command=cmOpen  key=""F3"";
    item ""~Alt+X~ Exit"" command=cmQuit  key=""Alt+X"";
  }
}
";
}

// ─────────────────────────────────────────────────────────────────────────────
//  End-to-end compile menu and statusbar from inline source
//  (touch registry + filesystem → NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class MenuStatusBarEndToEndTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public MenuStatusBarEndToEndTests()
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

    private string Compile(string src, string outFile)
    {
        string tvrPath = Path.Combine(_tmp.Path, outFile);
        var result = Compiler.CompileSource(src, tvrPath);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return tvrPath;
    }

    // ── menu ─────────────────────────────────────────────────────────────

    [Fact]
    public void E2E_MenuFixture_ProducesReadableMenuBar()
    {
        string tvr = Compile(MenuStatusBarFixtures.MenuTrc, "menu.tvr");

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvr);
        var rf = new TResourceFile(fp);
        var mb = rf.Get("menu.main") as TMenuBar;
        fp.Close();

        Assert.NotNull(mb);
        // Three top-level submenus: File, Edit, Help.
        int topCount = 0;
        for (var m = mb.Menu.Items; m != null; m = m.Next) topCount++;
        Assert.Equal(3, topCount);
        mb.ShutDown();
    }

    [Fact]
    public void E2E_MenuFixture_FileSubmenuHasFourItems()
    {
        string tvr = Compile(MenuStatusBarFixtures.MenuTrc, "menu2.tvr");

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvr);
        var mb = (TMenuBar)new TResourceFile(fp).Get("menu.main");
        fp.Close();

        // File submenu = first top-level item
        var fileSubMenu = mb.Menu.Items.SubMenu;
        Assert.NotNull(fileSubMenu);
        int count = 0;
        for (var m = fileSubMenu.Items; m != null; m = m.Next) count++;
        Assert.Equal(4, count); // Open, Save, separator, Exit
        mb.ShutDown();
    }

    // ── statusbar ─────────────────────────────────────────────────────────

    [Fact]
    public void E2E_StatusBarFixture_ProducesReadableStatusLine()
    {
        string tvr = Compile(MenuStatusBarFixtures.StatusBarTrc, "statusbar.tvr");

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvr);
        var sl = (TStatusLine)new TResourceFile(fp).Get("status.main");
        fp.Close();

        Assert.NotNull(sl);
        Assert.NotNull(sl.Defs);
        // Single range 0..65535 with four items.
        int itemCount = 0;
        for (var i = sl.Defs.Items; i != null; i = i.Next) itemCount++;
        Assert.Equal(4, itemCount);
        sl.ShutDown();
    }

    [Fact]
    public void E2E_StatusBarFixture_ItemsHaveCorrectCommands()
    {
        string tvr = Compile(MenuStatusBarFixtures.StatusBarTrc, "statusbar2.tvr");

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvr);
        var sl = (TStatusLine)new TResourceFile(fp).Get("status.main");
        fp.Close();

        var items = sl.Defs.Items;
        Assert.Equal(Views.cmHelp, items.Command);    // F1 Help
        items = items.Next;
        Assert.Equal(80, items.Command);               // F2 Save = cmSave=80
        sl.ShutDown();
    }

    // ── app-shell (mixed) ────────────────────────────────────────────────

    [Fact]
    public void E2E_AppShellFixture_ThreeResources()
    {
        string tvrPath = Path.Combine(_tmp.Path, "app-shell.tvr");
        var result = Compiler.CompileSource(MenuStatusBarFixtures.AppShellTrc, tvrPath);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(3, result.ItemsEmitted);
    }

    [Fact]
    public void E2E_AppShellFixture_AllTypesReadBack()
    {
        string tvr = Compile(MenuStatusBarFixtures.AppShellTrc, "app-shell2.tvr");

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp  = new Fpstream(tvr);
        var rf  = new TResourceFile(fp);
        var dlg = rf.Get("dialog.about")  as TDialog;
        var mb  = rf.Get("menu.main")     as TMenuBar;
        var sl  = rf.Get("status.main")   as TStatusLine;
        fp.Close();

        Assert.NotNull(dlg);
        Assert.NotNull(mb);
        Assert.NotNull(sl);
        dlg.ShutDown();
        mb.ShutDown();
        sl.ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Inspector Show/Validate for menu and statusbar  (NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class MenuStatusBarInspectorTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public MenuStatusBarInspectorTests()
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

    private string Compile(string src, string outFile)
    {
        string tvrPath = Path.Combine(_tmp.Path, outFile);
        var result = Compiler.CompileSource(src, tvrPath);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return tvrPath;
    }

    [Fact]
    public void Inspector_List_ShowsMenuAndStatusBarTypes()
    {
        string tvr = Compile(MenuStatusBarFixtures.AppShellTrc, "insp1.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("TMenuBar",    r.Output);
        Assert.Contains("TStatusLine", r.Output);
    }

    [Fact]
    public void Inspector_Show_MenuBar_ReturnsTopLevelCount()
    {
        string tvr = Compile(MenuStatusBarFixtures.MenuTrc, "insp2.tvr");
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var r = InspectorCommands.Show(tvr, "menu.main");
        Assert.True(r.Success, r.Error);
        Assert.Contains("TopLevelItems:", r.Output);
    }

    [Fact]
    public void Inspector_Show_StatusLine_ReturnsRangeAndItemCount()
    {
        string tvr = Compile(MenuStatusBarFixtures.StatusBarTrc, "insp3.tvr");
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var r = InspectorCommands.Show(tvr, "status.main");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Ranges:", r.Output);
        Assert.Contains("Items:",  r.Output);
    }

    [Fact]
    public void Inspector_Validate_AppShell_Succeeds()
    {
        string tvr = Compile(MenuStatusBarFixtures.AppShellTrc, "insp4.tvr");
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Errors:", r.Output);
        Assert.Contains("0", r.Output);
    }

    [Fact]
    public void Inspector_Validate_AppShell_CountsMenusAndStatusLines()
    {
        string tvr = Compile(MenuStatusBarFixtures.AppShellTrc, "insp5.tvr");
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Menus:", r.Output);
        Assert.Contains("StatusLines:", r.Output);
        Assert.Contains("Dialogs:", r.Output);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Semantic regression tests
//  Covers: undefined-command in menu/statusbar, user-const-overrides-builtin
//  in compilation context, duplicate key across resource kinds.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class SemanticRegressionTests : IDisposable
{
    private readonly DriverScope _driver;

    public SemanticRegressionTests() => _driver = new DriverScope();

    public void Dispose()
    {
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private static List<(string key, TStreamable obj)> BuildAll(string src, List<Diagnostic> diag)
    {
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        StreamableRegistration.RegisterAll();
        return new Builder(diag).Build(ast);
    }

    // ── undefined command ─────────────────────────────────────────────────

    [Fact]
    public void MenuItem_UndefinedCommand_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        BuildAll(@"resource menu ""m"" {
  submenu ""A"" {
    item ""X"" command=cmNonExistent;
  }
}", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UndefinedCommand);
    }

    [Fact]
    public void StatusBarItem_UndefinedCommand_EmitsDiagnostic()
    {
        var diag = new List<Diagnostic>();
        BuildAll(@"resource statusbar ""s"" {
  range 0 65535 {
    item ""X"" command=cmNonExistent key=""F1"";
  }
}", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UndefinedCommand);
    }

    // ── user-const overrides built-in in compilation context ──────────────

    [Fact]
    public void DialogCommand_UserConstOverridesBuiltin()
    {
        // Redefine cmOK as 999; button should get command 999, not Views.cmOK (10).
        var diag   = new List<Diagnostic>();
        var result = BuildAll(@"
const cmOK = 999;
resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  button ""~O~K"" bounds=(10, 10, 30, 12) command=cmOK default;
}", diag);
        Assert.Empty(diag);
        var dlg = (TDialog)result[0].obj;
        TButton btn = null;
        dlg.ForEachView(v => { if (v is TButton b) btn = b; });
        Assert.NotNull(btn);
        Assert.Equal((ushort)999, btn.Command);
        dlg.ShutDown();
    }

    [Fact]
    public void MenuCommand_UserConstOverridesBuiltin()
    {
        // Redefine cmSave as 999; menu item should get command 999, not 80.
        var diag   = new List<Diagnostic>();
        var result = BuildAll(@"
const cmSave = 999;
resource menu ""m"" {
  submenu ""~F~ile"" {
    item ""~S~ave"" command=cmSave key=""F2"";
  }
}", diag);
        Assert.Empty(diag);
        var mb   = (TMenuBar)result[0].obj;
        var item = mb.Menu.Items.SubMenu.Items;   // first item in File submenu
        Assert.Equal((ushort)999, item.Command);
        mb.ShutDown();
    }

    [Fact]
    public void StatusCommand_UserConstOverridesBuiltin()
    {
        // Redefine cmHelp as 999; statusbar item should get command 999, not Views.cmHelp (9).
        var diag   = new List<Diagnostic>();
        var result = BuildAll(@"
const cmHelp = 999;
resource statusbar ""s"" {
  range 0 65535 {
    item ""~F1~ Help"" command=cmHelp key=""F1"";
  }
}", diag);
        Assert.Empty(diag);
        var sl = (TStatusLine)result[0].obj;
        Assert.Equal((ushort)999, sl.Defs.Items.Command);
        sl.ShutDown();
    }

    // ── duplicate key across resource kinds ───────────────────────────────

    [Fact]
    public void DuplicateKeyAcrossDialogAndMenu_IsRejected()
    {
        var diag   = new List<Diagnostic>();
        var result = BuildAll(@"
resource dialog ""shared.key"" { bounds (0, 0, 40, 15); }
resource menu   ""shared.key"" { }
", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.DuplicateResourceKey);
        Assert.Single(result); // second resource skipped
        ((TDialog)result[0].obj).ShutDown();
    }

    [Fact]
    public void DuplicateKeyAcrossMenuAndStatusBar_IsRejected()
    {
        var diag   = new List<Diagnostic>();
        var result = BuildAll(@"
resource menu      ""shared.key"" { }
resource statusbar ""shared.key"" { }
", diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.DuplicateResourceKey);
        Assert.Single(result);
        ((TMenuBar)result[0].obj).ShutDown();
    }
}
