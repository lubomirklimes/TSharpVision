using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

public sealed class LanguageDetectionTests
{
    private static readonly TextMateSyntaxService Service = TextMateSyntaxService.Default;

    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("data.JSON", "json")]
    [InlineData("TSharpCommander.csproj", "xml")]
    [InlineData("README.md", "markdown")]
    [InlineData("build.sh", "shellscript")]
    [InlineData("C:\\work\\src/app.ts", "typescript")]
    [InlineData("/home/user/module.py", "python")]
    [InlineData("script.ps1", "powershell")]
    public void TheExtensionDecides(string fileName, string expected)
        => Assert.Equal(expected, Service.DetectLanguage(fileName, null).Id);

    [Theory]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("Containerfile", "dockerfile")]
    [InlineData("Dockerfile.dev", "dockerfile")]
    [InlineData("Makefile", "makefile")]
    [InlineData("GNUmakefile", "makefile")]
    public void SpecialFileNamesDeclaredByTheGrammarPackageAreRecognised(string fileName, string expected)
        => Assert.Equal(expected, Service.DetectLanguage(fileName, null).Id);

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("no-extension")]
    [InlineData("archive.unknownext")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownNameIsPlainTextAndNotAnError(string? fileName)
    {
        SyntaxLanguage language = Service.DetectLanguage(fileName, null);

        Assert.Same(SyntaxLanguage.PlainText, language);
        Assert.True(language.IsPlainText);
        Assert.Null(Service.CreateClassifier(language));
    }

    [Theory]
    [InlineData("configure", "#!/bin/sh\necho hi\n", "shellscript")]
    [InlineData("tool", "#!/usr/bin/env python3\nprint(1)\n", "python")]
    [InlineData("run", "\uFEFF#!/usr/bin/env -S node --flag\n", "javascript")]
    public void AScriptWithoutATellingNameIsRecognisedFromItsFirstLine(string fileName, string sample, string expected)
        => Assert.Equal(expected, Service.DetectLanguage(fileName, sample).Id);

    [Fact]
    public void TheNameWinsOverContentHints()
        => Assert.Equal("json", Service.DetectLanguage("x.json", "#!/bin/bash\n").Id);

    [Fact]
    public void ContentThatIsNotAScriptLineStaysPlain()
        => Assert.True(Service.DetectLanguage("data", "rm -rf /\n$(whoami)\n").IsPlainText);

    [Fact]
    public void ClassifiersAreSharedPerLanguage()
    {
        SyntaxLanguage csharp = Service.DetectLanguage("a.cs", null);

        ISyntaxClassifier? first = Service.CreateClassifier(csharp);
        ISyntaxClassifier? second = Service.CreateClassifier(Service.DetectLanguage("b.cs", null));

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(csharp, first!.Language);
        Assert.Equal("C#", csharp.DisplayName);
    }

    [Fact]
    public void LanguagesCompareByIdentifier()
    {
        Assert.Equal(new SyntaxLanguage("csharp", "C#"), new SyntaxLanguage("csharp", "Another name"));
        Assert.NotEqual(new SyntaxLanguage("csharp", "C#"), SyntaxLanguage.PlainText);
        Assert.Throws<ArgumentException>(() => new SyntaxLanguage(" ", "x"));
    }

    [Theory]
    [InlineData("Dockerfile.*", "Dockerfile.prod", true)]
    [InlineData("Dockerfile.*", "dockerfile.PROD", true)]
    [InlineData("Dockerfile.*", "Dockerfile", false)]
    [InlineData("*.log.?", "app.log.1", true)]
    [InlineData("*.log.?", "app.log.12", false)]
    public void FileNamePatternsMatchWholeNames(string pattern, string name, bool expected)
        => Assert.Equal(expected, TextMateLanguageCatalog.WildcardMatch(pattern, name));
}
