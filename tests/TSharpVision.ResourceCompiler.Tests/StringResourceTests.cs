using System.Collections.Generic;
using System.IO;
using TSharpVision;
using TSharpVision.ResourceCompiler;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceCompiler;

public sealed class StringResourceParserTests
{
    [Fact]
    public void Strings_ParsesResourceKind()
    {
        var diag = new List<Diagnostic>();
        var ast = Parse("""
            resource strings "sharpvision.intl" {
              Menu_File = "~F~ile";
            }
            """, diag);

        Assert.Empty(diag);
        Assert.Single(ast.Resources);
        Assert.Equal(ResourceKind.Strings, ast.Resources[0].Kind);
        Assert.Equal("sharpvision.intl", ast.Resources[0].Key);
        Assert.Single(ast.Resources[0].Strings.Entries);
        Assert.Equal("Menu_File", ast.Resources[0].Strings.Entries[0].Key);
    }

    [Fact]
    public void Strings_AllowsQuotedKeys()
    {
        var diag = new List<Diagnostic>();
        var ast = Parse("""
            resource strings "sharpvision.intl" {
              "menu.file" = "~F~ile";
            }
            """, diag);

        Assert.Empty(diag);
        Assert.Equal("menu.file", ast.Resources[0].Strings.Entries[0].Key);
    }

    private static TrcFile Parse(string src, List<Diagnostic> diag)
    {
        var tokens = new Lexer(src, diag).Tokenize();
        return new Parser(tokens, diag).ParseFile();
    }
}

[Collection("NonParallel")]
public sealed class StringResourceEndToEndTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    [Fact]
    public void Compiler_WritesReadableStringResource()
    {
        string path = Path.Combine(_tmp.Path, "app_cs.tvr");
        var result = Compiler.CompileSource("""
            resource strings "sharpvision.intl" {
              Btn_OK = "~B~udiž";
              Menu_File = "~S~oubor";
            }
            """, path);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.ItemsEmitted);

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(path);
        var rf = new TResourceFile(fp);
        var strings = Assert.IsType<TStringResource>(rf.Get("sharpvision.intl"));
        fp.Close();

        Assert.True(strings.TryGetValue("Btn_OK", out string ok));
        Assert.Equal("~B~udiž", ok);
        Assert.True(strings.TryGetValue("Menu_File", out string menu));
        Assert.Equal("~S~oubor", menu);
    }

    [Fact]
    public void Compiler_StringResource_LoadsThroughTResourceStringProvider_WithUnicode()
    {
        string path = Path.Combine(_tmp.Path, "app_cs.tvr");
        var result = Compiler.CompileSource("""
            resource strings "sharpvision.intl" {
              Czech_Test = "Příliš žluťoučký kůň";
              Same_As_Fallback = "Same";
            }
            """, path);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.ItemsEmitted);

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var provider = TResourceStringProvider.Load(path);

        Assert.True(provider.TryGet("Czech_Test", out string czech));
        Assert.Equal("Příliš žluťoučký kůň", czech);
        Assert.Equal("Příliš žluťoučký kůň", provider.Get("Czech_Test", "fallback"));

        Assert.True(provider.TryGet("Same_As_Fallback", out string same));
        Assert.Equal("Same", same);
        Assert.Equal("Same", provider.Get("Same_As_Fallback", "Same"));
    }

    [Fact]
    public void Formatter_EmitsStringsResource()
    {
        var result = Compiler.FormatSource("""
            resource strings "sharpvision.intl" { "menu.file" = "~F~ile"; Menu_Edit = "~E~dit"; }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            "resource strings \"sharpvision.intl\" {\n" +
            "  \"menu.file\" = \"~F~ile\";\n" +
            "  Menu_Edit = \"~E~dit\";\n" +
            "}\n",
            result.Text);
    }
}
