// Palette/color-scheme runtime audit and palette name validation tests.
//
// Key audit finding: TPalette, TColorItem, and TColorGroup are NOT TStreamable.
// No standalone palette resource kind is implemented because there is no safe
// streamable runtime object to emit.
//
// What this file covers:
//   - TRC0209 UnknownPaletteName — dialog `palette` field now validates the name.
//   - Known names: wpBlueDialog, wpCyanDialog, wpGrayDialog, wpGreenDialog.
//   - Unknown names emit TRC0209; no runtime effect in either case.
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.ResourceCompiler;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

// ─────────────────────────────────────────────────────────────────────────────
//  Palette runtime streamability audit  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PaletteStreamabilityAuditTests
{
    [Fact]
    public void TPalette_IsNotTStreamable()
    {
        // TPalette is a plain byte-array wrapper, not a TStreamable.
        // This is the primary blocker for a standalone .tvr palette resource.
        Assert.False(typeof(TPalette).IsAssignableTo(typeof(TStreamable)));
    }

    [Fact]
    public void TColorItem_IsNotTStreamable()
    {
        // TColorItem is a plain linked-list node, not registered with Pstream.
        Assert.False(typeof(TColorItem).IsAssignableTo(typeof(TStreamable)));
    }

    [Fact]
    public void TColorGroup_IsNotTStreamable()
    {
        // TColorGroup is a plain linked-list node, not registered with Pstream.
        Assert.False(typeof(TColorGroup).IsAssignableTo(typeof(TStreamable)));
    }

    [Fact]
    public void TColorGroupList_IsTStreamable()
    {
        // TColorGroupList IS a TStreamable (registered), but it is a UI list
        // widget (TListViewer subclass) for the TColorDialog editor — not a
        // standalone palette data resource.
        Assert.True(typeof(TColorGroupList).IsAssignableTo(typeof(TStreamable)));
    }

    [Fact]
    public void TColorDialog_IsTStreamable()
    {
        // TColorDialog IS a TStreamable, but it is an interactive color editor
        // dialog — not a palette data store.
        Assert.True(typeof(TColorDialog).IsAssignableTo(typeof(TStreamable)));
    }

    [Fact]
    public void TDialog_GetPalette_ReturnsFixedClassStaticPalette()
    {
        // TDialog.GetPalette() returns the same class-static TPalette for all
        // instances. There is no per-instance palette override mechanism.
        // This confirms that `palette wpGrayDialog;` cannot be applied at runtime.
        var dlg1 = new TDialog(new TRect(0, 0, 40, 15), "A");
        var dlg2 = new TDialog(new TRect(0, 0, 40, 15), "B");
        Assert.Same(dlg1.GetPalette(), dlg2.GetPalette()); // identical reference
        dlg1.ShutDown();
        dlg2.ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Palette name validation  (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PaletteNameValidationTests
{
    private static List<Diagnostic> BuildDiag(string src)
    {
        var diag   = new List<Diagnostic>();
        var tokens = new Lexer(src, diag).Tokenize();
        var ast    = new Parser(tokens, diag).ParseFile();
        new Builder(diag).Build(ast);
        return diag;
    }

    // ── CommandIds.IsKnownPaletteName ──────────────────────────────────────

    [Theory]
    [InlineData("wpBlueDialog")]
    [InlineData("wpCyanDialog")]
    [InlineData("wpGrayDialog")]
    [InlineData("wpGreenDialog")]
    public void KnownPaletteNames_RecognizedByCommandIds(string name)
    {
        Assert.True(CommandIds.IsKnownPaletteName(name));
    }

    [Theory]
    [InlineData("wpUnknown")]
    [InlineData("wpRedDialog")]
    [InlineData("something")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownPaletteNames_NotRecognizedByCommandIds(string name)
    {
        Assert.False(CommandIds.IsKnownPaletteName(name));
    }

    [Fact]
    public void PaletteNames_NotEmpty()
    {
        Assert.NotEmpty(CommandIds.PaletteNames);
    }

    // ── Builder validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("wpBlueDialog")]
    [InlineData("wpCyanDialog")]
    [InlineData("wpGrayDialog")]
    [InlineData("wpGreenDialog")]
    public void Builder_KnownPaletteName_NoDiagnosticTRC0209(string name)
    {
        var src = $@"resource dialog ""d"" {{
  bounds (0, 0, 40, 15);
  palette {name};
}}";
        var diag = BuildDiag(src);
        Assert.DoesNotContain(diag, d => d.Code == DiagnosticCodes.UnknownPaletteName);
    }

    [Fact]
    public void Builder_UnknownPaletteName_EmitsTRC0209()
    {
        var diag = BuildDiag(@"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  palette wpUnknownColor;
}");
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UnknownPaletteName);
    }

    [Fact]
    public void Builder_DialogWithoutPalette_NoDiagnosticTRC0209()
    {
        var diag = BuildDiag(@"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
}");
        Assert.DoesNotContain(diag, d => d.Code == DiagnosticCodes.UnknownPaletteName);
    }

    [Fact]
    public void Builder_UnknownPaletteName_DiagnosticMessageContainsPaletteName()
    {
        var diag = BuildDiag(@"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  palette wpFake;
}");
        var d209 = diag.FirstOrDefault(d => d.Code == DiagnosticCodes.UnknownPaletteName);
        Assert.NotNull(d209);
        Assert.Contains("wpFake", d209.Message);
    }

    [Fact]
    public void Builder_UnknownPaletteName_DiagnosticBlocksEmit()
    {
        // TRC0209 is a compile error — the resource is not emitted.
        // The user must correct the palette name to a known value.
        var src = @"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  palette wpFake;
  button ""~O~K"" bounds=(10,10,30,12) command=cmOK default;
}";
        var diag = BuildDiag(src);
        Assert.Contains(diag, d => d.Code == DiagnosticCodes.UnknownPaletteName);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  End-to-end: palette field with compiler  (NonParallel — touches registry)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class PaletteEndToEndTests : IDisposable
{
    private readonly DriverScope    _driver;
    private readonly TempDirectory  _tmp;

    public PaletteEndToEndTests()
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

    private CompileResult Compile(string src)
    {
        string tvrPath = Path.Combine(_tmp.Path, "out.tvr");
        return Compiler.CompileSource(src, tvrPath);
    }

    [Fact]
    public void E2E_KnownPalette_CompileSucceeds()
    {
        var r = Compile(@"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  palette wpGrayDialog;
  button ""~O~K"" bounds=(10,10,30,12) command=cmOK default;
}");
        Assert.True(r.Success);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.UnknownPaletteName);
    }

    [Fact]
    public void E2E_UnknownPalette_EmitsTRC0209_AndBlocksEmit()
    {
        // TRC0209 is a compile error — dialog is not emitted.
        // Unknown palette names should be corrected to one of the known names.
        var r = Compile(@"resource dialog ""d"" {
  bounds (0, 0, 40, 15);
  palette wpFakePal;
}");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.UnknownPaletteName);
        Assert.False(r.Success);
        Assert.Equal(0, r.ItemsEmitted);
    }

    [Fact]
    public void E2E_KnownPalette_DialogRoundTrips()
    {
        string tvrPath = Path.Combine(_tmp.Path, "gray.tvr");
        var r = Compiler.CompileSource(@"resource dialog ""d"" {
  bounds (5, 5, 45, 20);
  title ""Gray"";
  palette wpGrayDialog;
  button ""~O~K"" bounds=(10,10,30,12) command=cmOK default;
}", tvrPath);
        Assert.True(r.Success);

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp  = new Fpstream(tvrPath);
        var rf  = new TResourceFile(fp);
        var dlg = rf.Get("d") as TDialog;
        fp.Close();

        Assert.NotNull(dlg);
        Assert.Equal("Gray", dlg.title);
        dlg.ShutDown();
    }
}
