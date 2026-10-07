using System.Text;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Consumer.Tests;

/// <summary>"Tiny": a language whose grammar lives in this test assembly, not in TSharpVision.</summary>
internal static class Tiny
{
    public static readonly SyntaxLanguage Language = new("tiny", "Tiny");

    public static SyntaxLanguageDefinition Definition => new(Language)
    {
        Aliases = new[] { "tn" },
        FileExtensions = new[] { ".tiny" },
        FileNames = new[] { "Tinyfile" },
    };

    public static Stream OpenGrammar()
        => typeof(Tiny).Assembly.GetManifestResourceStream("tiny.tmLanguage.json")
           ?? throw new InvalidOperationException("The test grammar is not embedded.");

    public static string GrammarText
    {
        get
        {
            using var reader = new StreamReader(OpenGrammar(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}

/// <summary>
/// The convenient path for the common case: an application adds a TextMate grammar of its own - from a resource,
/// a stream or text - and writes no tokenizer.
/// </summary>
public sealed class TextMateLanguageTests
{
    private static TextMateSyntaxService WithTiny()
    {
        var service = new TextMateSyntaxService();
        using Stream grammar = Tiny.OpenGrammar();
        service.RegisterTextMateLanguage(Tiny.Definition, grammar);
        return service;
    }

    private static SyntaxClass[] Classes(SyntaxLineResult line) => line.Spans.Select(span => span.Class).ToArray();

    [Fact]
    public void AGrammarFromAnAssemblyResourceRegistersItsLanguage()
    {
        TextMateSyntaxService service = WithTiny();

        SyntaxLanguageInfo info = Assert.Single(service.GetLanguages(), l => l.Language.Id == "tiny");
        Assert.Equal("Tiny", info.Language.DisplayName);
        Assert.Equal(new[] { "tn" }, info.Aliases);
        Assert.Equal(62, service.GetLanguages().Count);

        Assert.Equal("tiny", service.DetectLanguage("program.tiny", null).Id);
        Assert.Equal("tiny", service.DetectLanguage("/work/Tinyfile", null).Id);
        Assert.True(TextMateSyntaxService.Default.DetectLanguage("program.tiny", null).IsPlainText);
    }

    [Fact]
    public void ItsGrammarClassifies()
    {
        TextMateSyntaxService service = WithTiny();
        ISyntaxClassifier classifier = service.CreateClassifier(Tiny.Language)!;

        Assert.Equal(Tiny.Language, classifier.Language);
        Assert.Same(classifier, service.CreateClassifier(service.DetectLanguage("a.tiny", null)));

        SyntaxLineResult line = classifier.ClassifyLine("let x = 42 \"text\" # note", classifier.InitialState);

        Assert.Equal(new[] { SyntaxClass.Keyword, SyntaxClass.Number, SyntaxClass.String, SyntaxClass.Comment }, Classes(line));
        Assert.Equal(new SyntaxSpan(0, 3, SyntaxClass.Keyword), line.Spans[0]);
        Assert.Equal(new SyntaxSpan(8, 2, SyntaxClass.Number), line.Spans[1]);
    }

    [Fact]
    public void AConstructThatSpansLinesCarriesItsStateAcrossThem()
    {
        ISyntaxClassifier classifier = WithTiny().CreateClassifier(Tiny.Language)!;

        SyntaxLineResult open = classifier.ClassifyLine("let /* begin", classifier.InitialState);
        SyntaxLineResult inside = classifier.ClassifyLine("let 1", open.EndState);
        SyntaxLineResult close = classifier.ClassifyLine("end */ let", inside.EndState);

        // The state after an ordinary line, which is what a closed comment must come back to.
        SyntaxLineState outside = classifier.ClassifyLine("let", classifier.InitialState).EndState;
        Assert.NotEqual(outside, open.EndState);
        Assert.Equal(new[] { SyntaxClass.Comment }, Classes(inside));
        Assert.Equal(new[] { SyntaxClass.Comment, SyntaxClass.Keyword }, Classes(close));
        Assert.Equal(outside, close.EndState);
    }

    [Fact]
    public void TheGrammarStreamIsReadDuringTheCallAndLeftOpen()
    {
        var service = new TextMateSyntaxService();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(Tiny.GrammarText));

        service.RegisterTextMateLanguage(Tiny.Definition, stream);

        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
        stream.Dispose();
        Assert.NotEmpty(service.CreateClassifier(Tiny.Language)!.ClassifyLine("let", service.CreateClassifier(Tiny.Language)!.InitialState).Spans);
    }

    [Fact]
    public void AGrammarGivenAsTextRegistersTheSameLanguage()
    {
        var service = new TextMateSyntaxService();
        service.RegisterTextMateLanguage(Tiny.Definition, Tiny.GrammarText);

        ISyntaxClassifier classifier = service.CreateClassifier(Tiny.Language)!;
        Assert.Equal(new[] { SyntaxClass.Comment }, Classes(classifier.ClassifyLine("# only a comment", classifier.InitialState)));
    }

    [Fact]
    public void AGrammarWithAByteOrderMarkIsRead()
    {
        var service = new TextMateSyntaxService();
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Tiny.GrammarText)).ToArray();
        using var stream = new MemoryStream(bytes);

        service.RegisterTextMateLanguage(Tiny.Definition, stream);

        Assert.NotNull(service.CreateClassifier(Tiny.Language));
    }

    [Fact]
    public void AGenericAndATextMateLanguageLiveInOneCatalog()
    {
        TextMateSyntaxService service = WithTiny();
        service.RegisterLanguage(Mos6502.Definition(), () => new Mos6502.Classifier());

        Assert.Equal(63, service.GetLanguages().Count);
        Assert.Equal("tiny", service.DetectLanguage("a.tiny", null).Id);
        Assert.Equal("mos6502", service.DetectLanguage("a.a65", null).Id);
        Assert.Equal("csharp", service.DetectLanguage("a.cs", null).Id);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{ \"name\": \"No scope\", \"patterns\": [] }")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    public void AGrammarThatCannotBeUsedIsRefusedAndNothingIsRegistered(string grammar)
    {
        var service = new TextMateSyntaxService();

        Assert.Throws<FormatException>(() => service.RegisterTextMateLanguage(Tiny.Definition, grammar));

        Assert.Equal(61, service.GetLanguages().Count);
        Assert.True(service.DetectLanguage("a.tiny", null).IsPlainText);
        Assert.Null(service.CreateClassifier(Tiny.Language));
        Assert.Equal("json", service.DetectLanguage("a.json", null).Id);
        Assert.NotNull(service.CreateClassifier(service.DetectLanguage("a.json", null)));

        // The identifier is still free for the grammar done right.
        service.RegisterTextMateLanguage(Tiny.Definition, Tiny.GrammarText);
        Assert.NotNull(service.CreateClassifier(Tiny.Language));
    }

    [Fact]
    public void AGrammarCannotTakeTheScopeOfABundledOne()
    {
        var service = new TextMateSyntaxService();
        string impostor = Tiny.GrammarText.Replace("source.tiny", "source.json", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => service.RegisterTextMateLanguage(Tiny.Definition, impostor));

        Assert.Null(service.CreateClassifier(Tiny.Language));
        ISyntaxClassifier json = service.CreateClassifier(service.DetectLanguage("a.json", null))!;
        Assert.Contains(SyntaxClass.Attribute, Classes(json.ClassifyLine("{\"let\": 1}", json.InitialState)));
    }

    [Fact]
    public void TwoLanguagesCannotShareOneRegisteredScope()
    {
        TextMateSyntaxService service = WithTiny();
        var other = new SyntaxLanguageDefinition(new SyntaxLanguage("tiny2", "Tiny Two"));

        Assert.Throws<InvalidOperationException>(() => service.RegisterTextMateLanguage(other, Tiny.GrammarText));

        Assert.Equal(62, service.GetLanguages().Count);
        Assert.NotNull(service.CreateClassifier(Tiny.Language));
    }

    [Fact]
    public void ABadDefinitionIsRefusedBeforeItsGrammarIsTaken()
    {
        var service = new TextMateSyntaxService();
        var bad = new SyntaxLanguageDefinition(new SyntaxLanguage("json", "Mine"));

        Assert.Throws<InvalidOperationException>(() => service.RegisterTextMateLanguage(bad, Tiny.GrammarText));

        // The grammar's scope was not consumed by the failed attempt.
        service.RegisterTextMateLanguage(Tiny.Definition, Tiny.GrammarText);
        Assert.NotNull(service.CreateClassifier(Tiny.Language));
    }
}
