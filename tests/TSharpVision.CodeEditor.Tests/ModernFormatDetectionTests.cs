using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// The modern text and configuration formats: names, the TOML grammar this assembly adds, and the conservative
/// content sniff for JSON, XML and YAML when the name says nothing.
/// </summary>
public sealed class ModernFormatDetectionTests
{
    private static readonly TextMateSyntaxService Service = TextMateSyntaxService.Default;

    [Theory]
    [InlineData("a.json", "json")]
    [InlineData("settings.jsonc", "jsonc")]
    [InlineData("a.xml", "xml")]
    [InlineData("web.config", "xml")]
    [InlineData("App.csproj", "xml")]
    [InlineData("App.fsproj", "xml")]
    [InlineData("App.vbproj", "xml")]
    [InlineData("Directory.Build.props", "xml")]
    [InlineData("Directory.Build.targets", "xml")]
    [InlineData("Window.xaml", "xml")]
    [InlineData("Strings.resx", "xml")]
    [InlineData("a.yaml", "yaml")]
    [InlineData("a.yml", "yaml")]
    [InlineData("Cargo.toml", "toml")]
    [InlineData("Cargo.lock", "toml")]
    [InlineData("setup.ini", "ini")]
    [InlineData("README.md", "markdown")]
    [InlineData("notes.markdown", "markdown")]
    [InlineData(".env", "properties")]
    [InlineData(".env.local", "properties")]
    [InlineData("prod.env", "properties")]
    [InlineData("schema.sql", "sql")]
    public void TheNameDecides(string fileName, string expected)
        => Assert.Equal(expected, Service.DetectLanguage(fileName, null).Id);

    [Theory]
    [InlineData("a.json")]
    [InlineData("a.xml")]
    [InlineData("a.yaml")]
    [InlineData("a.toml")]
    [InlineData("a.ini")]
    [InlineData("a.md")]
    [InlineData(".env")]
    [InlineData("a.sql")]
    public void EveryFormatHasAClassifier(string fileName)
        => Assert.NotNull(Service.CreateClassifier(Service.DetectLanguage(fileName, null)));

    [Fact]
    public void TomlIsClassifiedByItsOwnGrammar()
    {
        ISyntaxClassifier classifier = Service.CreateClassifier(Service.DetectLanguage("a.toml", null))!;
        SyntaxLineState state = classifier.InitialState;

        SyntaxLineResult table = classifier.ClassifyLine("[package]", state);
        Assert.Contains(table.Spans, s => s.Class == SyntaxClass.Heading);

        string line = "name = \"demo\" # comment";
        SyntaxLineResult entry = classifier.ClassifyLine(line, table.EndState);
        Assert.Contains(entry.Spans, s => s.Class == SyntaxClass.Attribute && line.Substring(s.Start, s.Length) == "name");
        Assert.Contains(entry.Spans, s => s.Class == SyntaxClass.String);
        Assert.Contains(entry.Spans, s => s.Class == SyntaxClass.Comment);

        SyntaxLineResult number = classifier.ClassifyLine("port = 8080", entry.EndState);
        Assert.Contains(number.Spans, s => s.Class == SyntaxClass.Number);
        SyntaxLineResult flag = classifier.ClassifyLine("debug = true", number.EndState);
        Assert.Contains(flag.Spans, s => s.Class == SyntaxClass.Constant);
    }

    [Theory]
    [InlineData("{\"a\": 1}", "json")]
    [InlineData("  {\n  \"name\": \"x\"\n}", "json")]
    [InlineData("{}", "json")]
    [InlineData("[]", "json")]
    [InlineData("[1, 2, 3]", "json")]
    [InlineData("[{\"a\": true}]", "json")]
    [InlineData("[\"a\", \"b\"]", "json")]
    [InlineData("﻿{\"a\": 1}", "json")]
    [InlineData("<?xml version=\"1.0\"?>\n<a/>", "xml")]
    [InlineData("<root><child a=\"1\"/></root>", "xml")]
    [InlineData("<!-- note -->\n<root>\n  <x>1</x>\n</root>", "xml")]
    [InlineData("<!DOCTYPE note SYSTEM \"n.dtd\">\n<note/>", "xml")]
    [InlineData("---\nname: x\n", "yaml")]
    [InlineData("%YAML 1.2\n---\na: 1\n", "yaml")]
    [InlineData("name: demo\nversion: 1\nitems:\n  - a\n  - b\n", "yaml")]
    [InlineData("- name: a\n- name: b\n- name: c\n", "yaml")]
    public void ContentIsRecognisedWhenTheNameSaysNothing(string sample, string expected)
    {
        Assert.Equal(expected, Service.DetectLanguage("response", sample).Id);
        Assert.Equal(expected, Service.DetectLanguage("notes.txt", sample).Id);
        Assert.Equal(expected, Service.DetectLanguage(null, sample).Id);
    }

    [Theory]
    [InlineData("Dear John: I hope this finds you well.\nRegards: me\nPS: nothing\n")]
    [InlineData("Note: this is prose.\nIt goes on for a while.\nAnd on.\n")]
    [InlineData("key: value\n")]
    [InlineData("a: 1\nb: 2\n")]
    [InlineData("[section]\nkey=value\n")]
    [InlineData("[[servers]]\nname = \"a\"\n")]
    [InlineData("[link](http://example.com) and text")]
    [InlineData("{ foo: 1 }")]
    [InlineData("{not json}")]
    [InlineData("[1] First footnote")]
    [InlineData("<3 you")]
    [InlineData("<stdio.h>")]
    [InlineData("<html><body></body></html>")]
    [InlineData("<!DOCTYPE html>\n<p>x</p>")]
    [InlineData("a < b and c > d")]
    [InlineData("just some words\nmore words\n")]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData("- one\n- two\n- three\n")]
    public void AmbiguousContentStaysPlain(string sample)
        => Assert.True(Service.DetectLanguage("notes.txt", sample).IsPlainText, sample);

    [Fact]
    public void AKnownNameIsNeverOverriddenByContent()
    {
        Assert.Equal("ini", Service.DetectLanguage("a.ini", "{\"a\": 1}").Id);
        Assert.Equal("markdown", Service.DetectLanguage("a.md", "---\ntitle: x\n---\n").Id);
        Assert.Equal("csharp", Service.DetectLanguage("a.cs", "<root><a/></root>").Id);
    }

    [Fact]
    public void BinaryLookingContentIsNeverSniffed()
        => Assert.True(Service.DetectLanguage("blob", "{\"a\"\0: 1}").IsPlainText);
}
