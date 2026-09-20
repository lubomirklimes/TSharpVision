using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// Representative constructs through the real TextMate backend, asserted as semantic roles only.
/// </summary>
public sealed class TextMateClassificationTests
{
    private static readonly TextMateSyntaxService Service = TextMateSyntaxService.Default;

    /// <summary>Classifies consecutive lines and returns, per line, each span's text and role.</summary>
    private static List<(string Text, SyntaxClass Class)>[] Classify(string fileName, params string[] lines)
    {
        SyntaxLanguage language = Service.DetectLanguage(fileName, null);
        ISyntaxClassifier classifier = Service.CreateClassifier(language)
            ?? throw new InvalidOperationException($"no classifier for {fileName}");

        var result = new List<(string, SyntaxClass)>[lines.Length];
        SyntaxLineState state = classifier.InitialState;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            SyntaxLineResult classified = classifier.ClassifyLine(line, state);
            result[i] = classified.Spans.Select(s => (line.Substring(s.Start, s.Length), s.Class)).ToList();
            state = classified.EndState;
        }

        return result;
    }

    private static SyntaxClass RoleOf(List<(string Text, SyntaxClass Class)> line, string text)
    {
        foreach ((string spanText, SyntaxClass role) in line)
            if (spanText.Contains(text, StringComparison.Ordinal))
                return role;

        return SyntaxClass.Plain;
    }

    [Fact]
    public void CSharpKeywordsStringsNumbersAndCommentsGetTheirRoles()
    {
        var lines = Classify("Program.cs",
            "using System; // note",
            "var s = \"text\"; int n = 42;",
            "#if DEBUG",
            "void Run() => Call(1);");

        Assert.Equal(SyntaxClass.Keyword, RoleOf(lines[0], "using"));
        Assert.Contains(("// note", SyntaxClass.Comment), lines[0]);
        Assert.Contains(("\"text\"", SyntaxClass.String), lines[1]);
        Assert.Equal(SyntaxClass.Keyword, RoleOf(lines[1], "var"));
        Assert.Equal(SyntaxClass.Keyword, RoleOf(lines[1], "int"));
        Assert.Contains(("42", SyntaxClass.Number), lines[1]);
        Assert.All(lines[2], span => Assert.Equal(SyntaxClass.Preprocessor, span.Class));
        Assert.Contains("DEBUG", string.Concat(lines[2].Select(s => s.Text)));
        Assert.Equal(SyntaxClass.Function, RoleOf(lines[3], "Call"));
    }

    [Fact]
    public void ACSharpBlockCommentCarriesItsStateAcrossLines()
    {
        var lines = Classify("a.cs",
            "int a = 1; /* start",
            "still inside",
            "end */ int b = 2;");

        Assert.Equal(new[] { ("still inside", SyntaxClass.Comment) }, lines[1]);
        Assert.Contains(("end */", SyntaxClass.Comment), lines[2]);
        Assert.Equal(SyntaxClass.Keyword, RoleOf(lines[2], "int"));

        // The same text from the initial state is not a comment: the role came from the carried state.
        var alone = Classify("a.cs", "still inside");
        Assert.DoesNotContain(alone[0], span => span.Class == SyntaxClass.Comment);
    }

    [Fact]
    public void JsonPropertiesStringsNumbersAndConstantsAreDistinguished()
    {
        var line = Classify("settings.json", "{ \"name\": \"x\", \"count\": 12.5, \"ok\": true }")[0];

        Assert.Contains(("\"name\"", SyntaxClass.Attribute), line);
        Assert.Contains(("\"x\"", SyntaxClass.String), line);
        Assert.Contains(("12.5", SyntaxClass.Number), line);
        Assert.Contains(("true", SyntaxClass.Constant), line);
    }

    [Fact]
    public void XmlElementsAndAttributesAreTagsAndAttributes()
    {
        var line = Classify("app.config.xml", "<root attr=\"v\"><child/></root>")[0];

        Assert.Equal(SyntaxClass.Tag, RoleOf(line, "root"));
        Assert.Equal(SyntaxClass.Attribute, RoleOf(line, "attr"));
        Assert.Contains(("\"v\"", SyntaxClass.String), line);
        Assert.Equal(SyntaxClass.Tag, RoleOf(line, "child"));
    }

    [Fact]
    public void MarkdownHeadingsAndCodeAreHeadingAndCode()
    {
        var lines = Classify("README.md",
            "# Title",
            "Use `code` here",
            "```",
            "plain block",
            "```");

        Assert.Contains(("# Title", SyntaxClass.Heading), lines[0]);
        Assert.Contains(("`code`", SyntaxClass.Code), lines[1]);
        Assert.Contains(("plain block", SyntaxClass.Code), lines[3]);
    }

    [Fact]
    public void ShellScriptsHaveKeywordsBuiltinsStringsAndComments()
    {
        var lines = Classify("build.sh",
            "#!/bin/bash",
            "echo \"hi\" # note",
            "if [ -f x ]; then exit 1; fi");

        Assert.Contains(("#!/bin/bash", SyntaxClass.Comment), lines[0]);
        Assert.Equal(SyntaxClass.Function, RoleOf(lines[1], "echo"));
        Assert.Contains(("\"hi\"", SyntaxClass.String), lines[1]);
        Assert.Contains(("# note", SyntaxClass.Comment), lines[1]);
        Assert.Contains(("if", SyntaxClass.Keyword), lines[2]);
        Assert.Contains(("then", SyntaxClass.Keyword), lines[2]);
        Assert.Contains(("fi", SyntaxClass.Keyword), lines[2]);
    }

    [Fact]
    public void ClassificationNeverChangesTheTextAndStaysInsideTheLine()
    {
        ISyntaxClassifier classifier = Service.CreateClassifier(Service.DetectLanguage("x.cs", null))!;
        string line = "var été = \"šč\"; // 😀 emoji";
        string copy = new(line.ToCharArray());

        SyntaxLineResult result = classifier.ClassifyLine(line, classifier.InitialState);

        Assert.Equal(copy, line);
        Assert.All(result.Spans, span =>
        {
            Assert.True(span.Start >= 0 && span.Length > 0 && span.End <= line.Length);
        });
        Assert.Equal(result.Spans.OrderBy(s => s.Start), result.Spans);
    }

    [Fact]
    public void AnOverlongLineIsPlainAndPassesItsStateThrough()
    {
        ISyntaxClassifier classifier = Service.CreateClassifier(Service.DetectLanguage("x.cs", null))!;
        SyntaxLineState inComment = classifier.ClassifyLine("/* open", classifier.InitialState).EndState;

        SyntaxLineResult result = classifier.ClassifyLine(new string('x', TextMateSyntaxClassifier.MaxLineLength + 1), inComment);

        Assert.Empty(result.Spans);
        Assert.Equal(inComment, result.EndState);
    }

    [Theory]
    [InlineData("comment.line.double-slash.cs", SyntaxClass.Comment)]
    [InlineData("keyword.operator.assignment.cs", SyntaxClass.Operator)]
    [InlineData("keyword.control.shell", SyntaxClass.Keyword)]
    [InlineData("storage.type.class.cs", SyntaxClass.Keyword)]
    [InlineData("support.type.property-name.json", SyntaxClass.Attribute)]
    [InlineData("support.type.builtin.ts", SyntaxClass.Type)]
    [InlineData("constant.numeric.decimal.cs", SyntaxClass.Number)]
    [InlineData("constant.language.json", SyntaxClass.Constant)]
    [InlineData("entity.name.function.cs", SyntaxClass.Function)]
    [InlineData("entity.name.tag.localname.xml", SyntaxClass.Tag)]
    [InlineData("markup.heading.markdown", SyntaxClass.Heading)]
    [InlineData("markup.inline.raw.string.markdown", SyntaxClass.Code)]
    [InlineData("variable.other.readwrite.cs", SyntaxClass.Identifier)]
    [InlineData("invalid.illegal.cs", SyntaxClass.Invalid)]
    [InlineData("meta.paragraph.markdown", SyntaxClass.Plain)]
    [InlineData("keywordish.fake", SyntaxClass.Plain)]
    public void ScopeNamesMapToABoundedSetOfRoles(string scope, SyntaxClass expected)
        => Assert.Equal(expected, TextMateScopeMapper.ClassifyScope(scope));
}
