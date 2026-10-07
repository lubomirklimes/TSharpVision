using System.Runtime.CompilerServices;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Consumer.Tests;

/// <summary>
/// A language registered by an application is a language of the service like any other: listed, detected in the
/// same order and classified - through the public API alone, on a service of the application's own.
/// </summary>
public sealed class CustomLanguageCatalogTests
{
    [Fact]
    public void LanguageMetadataOwnsReadOnlySnapshots()
    {
        string[] aliases = ["tiny"];
        var extensions = new List<string> { ".tiny" };
        string[] names = ["Tinyfile"];
        var info = new SyntaxLanguageInfo(new SyntaxLanguage("tiny", "Tiny"), aliases, extensions, names);
        aliases[0] = "changed";
        extensions.Clear();
        names[0] = "changed";
        Assert.Equal(new[] { "tiny" }, info.Aliases);
        Assert.Equal(new[] { ".tiny" }, info.FileExtensions);
        Assert.Equal(new[] { "Tinyfile" }, info.FileNames);
        foreach (IReadOnlyList<string> values in new[] { info.Aliases, info.FileExtensions, info.FileNames })
            Assert.Throws<NotSupportedException>(() => ((IList<string>)values)[0] = "changed");
    }

    [Fact]
    public void LanguageMetadataNormalizesNullCollectionsAndRejectsNullElements()
    {
        var language = new SyntaxLanguage("tiny", "Tiny");
        var info = new SyntaxLanguageInfo(language);
        Assert.Empty(info.Aliases);
        Assert.Empty(info.FileExtensions);
        Assert.Empty(info.FileNames);
        Assert.Throws<ArgumentException>(() => new SyntaxLanguageInfo(language, aliases: new[] { (string)null! }));
        Assert.Throws<ArgumentException>(() => new SyntaxLanguageInfo(language, fileExtensions: new[] { (string)null! }));
        Assert.Throws<ArgumentException>(() => new SyntaxLanguageInfo(language, fileNames: new[] { (string)null! }));
    }

    [Fact]
    public void EnumeratedMetadataCannotBeMutatedThroughCollectionCasts()
    {
        TextMateSyntaxService service = WithMos6502();
        SyntaxLanguageInfo info = service.GetLanguages().Single(item => item.Language.Id == "mos6502");
        foreach (IReadOnlyList<string> values in new[] { info.Aliases, info.FileExtensions, info.FileNames })
            Assert.Throws<NotSupportedException>(() => ((IList<string>)values)[0] = "changed");
        Assert.Equal("mos6502", service.DetectLanguage("game.a65", null).Id);
        Assert.Contains(".a65", service.GetLanguages().Single(item => item.Language.Id == "mos6502").FileExtensions);
    }

    private static TextMateSyntaxService WithMos6502(int priority = 0)
    {
        var service = new TextMateSyntaxService();
        service.RegisterLanguage(Mos6502.Definition(priority), () => new Mos6502.Classifier());
        return service;
    }

    [Fact]
    public void ThisAssemblySeesNoInternalsOfThePackage()
    {
        string me = typeof(CustomLanguageCatalogTests).Assembly.GetName().Name!;
        Assert.DoesNotContain(
            typeof(TextMateSyntaxService).Assembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false).Cast<InternalsVisibleToAttribute>(),
            attribute => attribute.AssemblyName.Split(',')[0].Trim() == me);
    }

    [Fact]
    public void TheStandardServiceIsARegistryAndStaysASyntaxService()
    {
        ISyntaxService service = new TextMateSyntaxService();
        ISyntaxLanguageRegistry registry = Assert.IsAssignableFrom<ISyntaxLanguageRegistry>(service);

        registry.RegisterLanguage(Mos6502.Definition(), () => new Mos6502.Classifier());

        Assert.Contains(service.GetLanguages(), info => info.Language.Equals(Mos6502.Language));
    }

    [Fact]
    public void WithoutARegistrationTheBuiltInLanguagesAreWhatTheyWere()
    {
        var service = new TextMateSyntaxService();
        IReadOnlyList<SyntaxLanguageInfo> languages = service.GetLanguages();

        Assert.Equal(61, languages.Count);
        Assert.Equal(
            TextMateSyntaxService.Default.GetLanguages().Select(info => info.Language.Id),
            languages.Select(info => info.Language.Id));
        Assert.DoesNotContain(languages, info => info.Language.Id == Mos6502.Language.Id);
        Assert.Same(languages, service.GetLanguages());

        Assert.Equal("csharp", service.DetectLanguage("Program.cs", null).Id);
        Assert.Equal("x86asm", service.DetectLanguage("boot.asm", null).Id);
        Assert.Equal("properties", service.DetectLanguage(".env", null).Id);
        Assert.True(service.DetectLanguage("game.a65", null).IsPlainText);
        Assert.NotNull(service.CreateClassifier(service.DetectLanguage("data.json", null)));
        Assert.Null(service.CreateClassifier(Mos6502.Language));
    }

    [Fact]
    public void ARegisteredLanguageIsListedOnceInDisplayNameOrderWithItsNames()
    {
        TextMateSyntaxService service = WithMos6502();
        IReadOnlyList<SyntaxLanguageInfo> languages = service.GetLanguages();

        Assert.Equal(62, languages.Count);
        SyntaxLanguageInfo info = Assert.Single(languages, l => l.Language.Id == "mos6502");
        Assert.Equal("6502 Assembly", info.Language.DisplayName);
        Assert.Equal(new[] { "6502", "6502asm", "asm65" }, info.Aliases);
        Assert.Equal(new[] { ".a65", ".asm", ".s65" }, info.FileExtensions);
        Assert.Equal(new[] { "kernel.6502" }, info.FileNames);

        string[] names = languages.Select(l => l.Language.DisplayName).ToArray();
        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal(languages.Count, languages.Select(l => l.Language.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(languages, l => l.Language.IsPlainText);
        Assert.Same(languages, service.GetLanguages());
    }

    [Fact]
    public void AListTakenBeforeARegistrationIsNotChangedByIt()
    {
        var service = new TextMateSyntaxService();
        IReadOnlyList<SyntaxLanguageInfo> before = service.GetLanguages();

        service.RegisterLanguage(Mos6502.Definition(), () => new Mos6502.Classifier());

        Assert.Equal(61, before.Count);
        Assert.Equal(62, service.GetLanguages().Count);
    }

    [Fact]
    public void AliasesThatRepeatTheIdentityAreNotListedTwice()
    {
        var service = new TextMateSyntaxService();
        var language = new SyntaxLanguage("zeta", "Zeta Script");
        service.RegisterLanguage(
            new SyntaxLanguageDefinition(language) { Aliases = new[] { "ZETA", "zeta script", "zs", "ZS" } },
            () => new Mos6502.Classifier());

        Assert.Equal(new[] { "zs" }, service.GetLanguages().Single(l => l.Language.Id == "zeta").Aliases);
    }

    [Fact]
    public void RegistrationsBelongToTheServiceTheyWereMadeOn()
    {
        TextMateSyntaxService first = WithMos6502();
        var second = new TextMateSyntaxService();

        Assert.Equal("mos6502", first.DetectLanguage("game.a65", null).Id);
        Assert.True(second.DetectLanguage("game.a65", null).IsPlainText);
        Assert.Null(second.CreateClassifier(Mos6502.Language));
        Assert.DoesNotContain(second.GetLanguages(), l => l.Language.Id == "mos6502");
        Assert.DoesNotContain(TextMateSyntaxService.Default.GetLanguages(), l => l.Language.Id == "mos6502");

        // The same identifier is free on another service.
        second.RegisterLanguage(new SyntaxLanguageDefinition(Mos6502.Language), () => new Mos6502.Classifier());
        Assert.NotNull(second.CreateClassifier(Mos6502.Language));
    }

    // ── detection ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("game.a65")]
    [InlineData("GAME.A65")]
    [InlineData("C:\\src\\demo/loader.s65")]
    [InlineData("kernel.6502")]
    [InlineData("KERNEL.6502")]
    [InlineData("rom.6502.src")]
    public void ItsExtensionsNamesAndPatternsDetectIt(string fileName)
        => Assert.Equal("mos6502", WithMos6502().DetectLanguage(fileName, null).Id);

    [Theory]
    [InlineData("listing", "; cpu 6502\n lda #$00\n", "mos6502")]
    [InlineData("listing", "  ;CPU=6502 build\n", "mos6502")]
    [InlineData("listing", "; cpu z80\n", SyntaxLanguage.PlainTextId)]
    [InlineData("listing", "#!/bin/sh\n; cpu 6502\n", "shellscript")]
    [InlineData("notes.json", "; cpu 6502\n", "json")]
    public void ItsFirstLinePatternIsTriedWhereAGrammarsWouldBe(string fileName, string sample, string expected)
        => Assert.Equal(expected, WithMos6502().DetectLanguage(fileName, sample).Id);

    [Fact]
    public void DetectionOfEverythingElseIsUnchangedByARegistration()
    {
        TextMateSyntaxService service = WithMos6502();

        Assert.Equal("csharp", service.DetectLanguage("Program.cs", null).Id);
        Assert.Equal("dockerfile", service.DetectLanguage("Dockerfile.dev", null).Id);
        Assert.Equal("python", service.DetectLanguage("tool", "#!/usr/bin/env python3\n").Id);
        Assert.Equal("json", service.DetectLanguage("data", "{\n  \"a\": 1,\n  \"b\": [1, 2]\n}\n").Id);
        Assert.Same(SyntaxLanguage.PlainText, service.DetectLanguage("notes.txt", "lda #$00\n"));
    }

    // ── the classifier ─────────────────────────────────────────────────────

    [Fact]
    public void ItsClassifierClassifies()
    {
        TextMateSyntaxService service = WithMos6502();
        ISyntaxClassifier classifier = service.CreateClassifier(service.DetectLanguage("game.a65", null))!;

        Assert.Equal(Mos6502.Language, classifier.Language);
        SyntaxLineResult line = classifier.ClassifyLine("start: LDA #$FF ; load", classifier.InitialState);

        Assert.Equal(
            new[]
            {
                new SyntaxSpan(0, 6, SyntaxClass.Function),
                new SyntaxSpan(7, 3, SyntaxClass.Keyword),
                new SyntaxSpan(12, 3, SyntaxClass.Number),
                new SyntaxSpan(16, 6, SyntaxClass.Comment),
            },
            line.Spans);
        Assert.Equal(classifier.InitialState, line.EndState);
    }

    [Fact]
    public void TheFactoryRunsOnceAtFirstUseAndItsClassifierIsShared()
    {
        var service = new TextMateSyntaxService();
        int created = 0;
        service.RegisterLanguage(Mos6502.Definition(), () =>
        {
            created++;
            return new Mos6502.Classifier();
        });

        Assert.Equal(0, created);
        _ = service.GetLanguages();
        _ = service.DetectLanguage("game.a65", null);
        Assert.Equal(0, created);

        ISyntaxClassifier? first = service.CreateClassifier(Mos6502.Language);
        ISyntaxClassifier? again = service.CreateClassifier(new SyntaxLanguage("mos6502", "any name"));

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal(1, created);
    }

    [Fact]
    public void AnUnknownLanguageHasNoClassifier()
    {
        TextMateSyntaxService service = WithMos6502();

        Assert.Null(service.CreateClassifier(new SyntaxLanguage("z80", "Z80 Assembly")));
        Assert.Null(service.CreateClassifier(new SyntaxLanguage("MOS6502", "wrong case")));
        Assert.Null(service.CreateClassifier(SyntaxLanguage.PlainText));
    }
}

/// <summary>Several languages claiming the same name: one deterministic answer, never a question.</summary>
public sealed class CustomLanguageCollisionTests
{
    private static SyntaxLanguageDefinition Claim(string id, string[] extensions, int priority = 0, string[]? names = null, string[]? patterns = null)
        => new(new SyntaxLanguage(id, id.ToUpperInvariant() + " Assembly"))
        {
            FileExtensions = extensions,
            FileNames = names ?? Array.Empty<string>(),
            FileNamePatterns = patterns ?? Array.Empty<string>(),
            Priority = priority,
        };

    private static string Detect(TextMateSyntaxService service, string fileName) => service.DetectLanguage(fileName, null).Id;

    private static TextMateSyntaxService Service(params SyntaxLanguageDefinition[] definitions)
    {
        var service = new TextMateSyntaxService();
        foreach (SyntaxLanguageDefinition definition in definitions)
            service.RegisterLanguage(definition, () => new Mos6502.Classifier());
        return service;
    }

    [Fact]
    public void ASingleCandidateIsSimplyChosen()
        => Assert.Equal("mos6502", Detect(Service(Mos6502.Definition()), "game.a65"));

    /// <summary>The realistic case: <c>.asm</c> belongs to the bundled x86 assembly and to 6502 alike.</summary>
    [Fact]
    public void AtEqualPriorityTheBuiltInLanguageKeepsAnExtensionBothClaim()
    {
        TextMateSyntaxService service = Service(Mos6502.Definition());

        Assert.Equal("x86asm", Detect(service, "boot.asm"));
        Assert.Equal("mos6502", Detect(service, "boot.a65"));
    }

    [Fact]
    public void AHigherPriorityTakesTheSharedExtension()
    {
        Assert.Equal("mos6502", Detect(Service(Mos6502.Definition(priority: 1)), "boot.asm"));
        Assert.Equal("x86asm", Detect(Service(Mos6502.Definition(priority: -1)), "boot.asm"));
    }

    [Fact]
    public void AmongEqualPrioritiesTheFirstRegisteredWinsAndReversingTheOrderReversesTheAnswer()
    {
        SyntaxLanguageDefinition z80 = Claim("z80", new[] { ".zzasm" });
        SyntaxLanguageDefinition m68k = Claim("m68k", new[] { ".zzasm" });

        Assert.Equal("z80", Detect(Service(z80, m68k), "prog.zzasm"));
        Assert.Equal("m68k", Detect(Service(m68k, z80), "prog.zzasm"));
    }

    [Fact]
    public void PriorityDecidesAmongRegisteredLanguagesWhateverTheOrder()
    {
        SyntaxLanguageDefinition low = Claim("z80", new[] { ".asm" }, priority: 1);
        SyntaxLanguageDefinition high = Claim("m68k", new[] { ".asm" }, priority: 2);

        Assert.Equal("m68k", Detect(Service(low, high), "prog.asm"));
        Assert.Equal("m68k", Detect(Service(high, low), "prog.asm"));
    }

    [Fact]
    public void SpecificityIsDecidedBeforePriority()
    {
        TextMateSyntaxService service = Service(
            Claim("z80", new[] { ".asm" }, priority: 100),
            Claim("m68k", new[] { ".x68.asm" }),
            Claim("arm", Array.Empty<string>(), names: new[] { "vectors.x68.asm" }),
            Claim("mips", Array.Empty<string>(), priority: 50, patterns: new[] { "vectors.*" }));

        // exact name > pattern > longest extension > extension, each regardless of the priorities below it
        Assert.Equal("arm", Detect(service, "vectors.x68.asm"));
        Assert.Equal("mips", Detect(service, "vectors.asm"));
        Assert.Equal("m68k", Detect(service, "main.x68.asm"));
        Assert.Equal("z80", Detect(service, "main.asm"));
    }

    [Fact]
    public void ABuiltInExtensionIsNotTakenOverWithoutAPriority()
    {
        Assert.Equal("json", Detect(Service(Claim("myjson", new[] { ".json" })), "data.json"));
        Assert.Equal("myjson", Detect(Service(Claim("myjson", new[] { ".json" }, priority: 1)), "data.json"));
    }

    [Fact]
    public void TheSameInputGivesTheSameAnswerEveryTime()
    {
        TextMateSyntaxService service = Service(Mos6502.Definition(), Claim("z80", new[] { ".asm" }), Claim("m68k", new[] { ".asm" }));

        string[] answers = Enumerable.Range(0, 50).Select(_ => Detect(service, "boot.asm")).Distinct().ToArray();
        Assert.Equal(new[] { "x86asm" }, answers);
    }

    [Fact]
    public void EveryClaimantStaysAvailableForAnExplicitChoice()
    {
        TextMateSyntaxService service = Service(Mos6502.Definition(), Claim("z80", new[] { ".asm" }));

        foreach (string id in new[] { "x86asm", "mos6502", "z80" })
        {
            SyntaxLanguageInfo info = Assert.Single(service.GetLanguages(), l => l.Language.Id == id);
            Assert.Contains(".asm", info.FileExtensions, StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(service.CreateClassifier(info.Language));
        }
    }
}

/// <summary>A registration that is wrong fails where it is made, and breaks nothing else.</summary>
public sealed class CustomLanguageFailureTests
{
    private static readonly Func<ISyntaxClassifier> Factory = () => new Mos6502.Classifier();

    private static SyntaxLanguageDefinition Named(string id) => new(new SyntaxLanguage(id, id + " language"));

    private static void AssertIntact(TextMateSyntaxService service, int count = 61)
    {
        Assert.Equal(count, service.GetLanguages().Count);
        Assert.Equal("json", service.DetectLanguage("data.json", null).Id);
        ISyntaxClassifier json = service.CreateClassifier(service.DetectLanguage("data.json", null))!;
        Assert.NotEmpty(json.ClassifyLine("{\"a\": 1}", json.InitialState).Spans);
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var service = new TextMateSyntaxService();

        Assert.Throws<ArgumentNullException>(() => service.RegisterLanguage(null!, Factory));
        Assert.Throws<ArgumentNullException>(() => service.RegisterLanguage(Named("a"), null!));
        Assert.Throws<ArgumentNullException>(() => service.RegisterTextMateLanguage(null!, "{}"));
        Assert.Throws<ArgumentNullException>(() => service.RegisterTextMateLanguage(Named("a"), (string)null!));
        Assert.Throws<ArgumentNullException>(() => service.RegisterTextMateLanguage(Named("a"), (Stream)null!));
        Assert.Throws<ArgumentNullException>(() => new SyntaxLanguageDefinition(null!));
        Assert.Throws<ArgumentNullException>(() => new SyntaxLanguageDefinition(new SyntaxLanguage("a", "A")) { Aliases = null! });
        Assert.Throws<ArgumentException>(() => new SyntaxLanguage(" ", "Blank"));
        AssertIntact(service);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("JSON")]
    [InlineData("properties")]
    [InlineData("toml")]
    public void AnIdentifierAlreadyRegisteredIsRefused(string id)
    {
        var service = new TextMateSyntaxService();

        var error = Assert.Throws<InvalidOperationException>(() => service.RegisterLanguage(Named(id), Factory));
        Assert.Contains(id, error.Message, StringComparison.OrdinalIgnoreCase);
        AssertIntact(service);
        Assert.Equal("JSON", service.GetLanguages().Single(l => l.Language.Id == "json").Language.DisplayName);
    }

    [Fact]
    public void ASecondRegistrationOfTheSameLanguageIsRefusedAndTheFirstStays()
    {
        var service = new TextMateSyntaxService();
        service.RegisterLanguage(Mos6502.Definition(), Factory);
        ISyntaxClassifier first = service.CreateClassifier(Mos6502.Language)!;

        Assert.Throws<InvalidOperationException>(() => service.RegisterLanguage(Named("mos6502"), () => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => service.RegisterTextMateLanguage(Named("Mos6502"), Tiny.GrammarText));

        Assert.Same(first, service.CreateClassifier(Mos6502.Language));
        Assert.Equal("6502 Assembly", service.GetLanguages().Single(l => l.Language.Id == "mos6502").Language.DisplayName);
        AssertIntact(service, 62);
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("PlainText")]
    public void PlainTextCannotBeRegistered(string id)
    {
        var service = new TextMateSyntaxService();

        Assert.Throws<InvalidOperationException>(() => service.RegisterLanguage(Named(id), Factory));
        Assert.Throws<InvalidOperationException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(SyntaxLanguage.PlainText), Factory));
        Assert.Throws<InvalidOperationException>(() => service.RegisterTextMateLanguage(Named(id), Tiny.GrammarText));

        Assert.Null(service.CreateClassifier(SyntaxLanguage.PlainText));
        Assert.Same(SyntaxLanguage.PlainText, service.DetectLanguage("notes.txt", null));
        Assert.DoesNotContain(service.GetLanguages(), l => l.Language.IsPlainText);
        AssertIntact(service);
    }

    [Fact]
    public void MalformedMetadataIsRefused()
    {
        var service = new TextMateSyntaxService();
        var language = new SyntaxLanguage("bad", "Bad");

        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(Named("has space"), Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { Aliases = new[] { " " } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileExtensions = new[] { "" } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileExtensions = new[] { "*.bad" } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileExtensions = new[] { "." } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileNames = new[] { "dir/file" } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileNamePatterns = new[] { "" } }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FirstLinePattern = "(" }, Factory));
        Assert.Throws<ArgumentException>(() => service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FirstLinePattern = " " }, Factory));

        Assert.True(service.DetectLanguage("x.bad", null).IsPlainText);
        AssertIntact(service);

        // Nothing was half-registered: the identifier is still free.
        service.RegisterLanguage(new SyntaxLanguageDefinition(language) { FileExtensions = new[] { "bad" } }, Factory);
        Assert.Equal("bad", service.DetectLanguage("x.bad", null).Id);
    }

    [Fact]
    public void AFactoryThatThrowsLeavesItsLanguagePlainAndIsNotAskedAgain()
    {
        var service = new TextMateSyntaxService();
        int calls = 0;
        service.RegisterLanguage(Mos6502.Definition(), () =>
        {
            calls++;
            throw new InvalidOperationException("broken");
        });

        Assert.Null(service.CreateClassifier(Mos6502.Language));
        Assert.Null(service.CreateClassifier(Mos6502.Language));
        Assert.Equal(1, calls);

        // Still listed and detected; only the colour is missing.
        Assert.Equal("mos6502", service.DetectLanguage("game.a65", null).Id);
        AssertIntact(service, 62);
    }

    [Fact]
    public void AFactoryThatReturnsNothingLeavesItsLanguagePlain()
    {
        var service = new TextMateSyntaxService();
        service.RegisterLanguage(Named("nothing"), () => null!);
        service.RegisterLanguage(Named("stateless"), () => new Broken(BrokenKind.NoInitialState));

        Assert.Null(service.CreateClassifier(new SyntaxLanguage("nothing", "x")));
        Assert.Null(service.CreateClassifier(new SyntaxLanguage("stateless", "x")));
        AssertIntact(service, 63);
    }

    [Theory]
    [InlineData((int)BrokenKind.Throws)]
    [InlineData((int)BrokenKind.NoResult)]
    [InlineData((int)BrokenKind.SpanPastTheLine)]
    [InlineData((int)BrokenKind.OverlappingSpans)]
    [InlineData((int)BrokenKind.NegativeSpan)]
    public void AClassifierThatFailsOnALineYieldsAPlainLineAndKeepsTheState(int kind)
    {
        var service = new TextMateSyntaxService();
        service.RegisterLanguage(Named("broken"), () => new Broken((BrokenKind)kind));
        ISyntaxClassifier classifier = service.CreateClassifier(new SyntaxLanguage("broken", "x"))!;

        SyntaxLineResult line = classifier.ClassifyLine("lda #$00", classifier.InitialState);

        Assert.Empty(line.Spans);
        Assert.Same(classifier.InitialState, line.EndState);
        Assert.Equal("broken", classifier.Language.Id);
        AssertIntact(service, 62);
    }

    [Fact]
    public void ASharedClassifierIsNeverEnteredByTwoThreadsAtOnce()
    {
        var service = new TextMateSyntaxService();
        var probe = new ReentryProbe();
        service.RegisterLanguage(Named("probe"), () => probe);
        ISyntaxClassifier classifier = service.CreateClassifier(new SyntaxLanguage("probe", "x"))!;

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (int i = 0; i < 20; i++) classifier.ClassifyLine("nop", classifier.InitialState);
        });

        Assert.Equal(64 * 20, probe.Calls);
        Assert.False(probe.Overlapped);
    }

    [Fact]
    public async Task RegisteringWhileOtherThreadsDetectDisturbsNoOne()
    {
        var service = new TextMateSyntaxService();
        using var stop = new CancellationTokenSource();
        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                Assert.Equal("csharp", service.DetectLanguage("Program.cs", null).Id);
                Assert.InRange(service.GetLanguages().Count, 61, 61 + 40);
            }
        })).ToArray();

        for (int i = 0; i < 40; i++)
            service.RegisterLanguage(new SyntaxLanguageDefinition(new SyntaxLanguage("lang" + i, "Language " + i)) { FileExtensions = new[] { ".l" + i } }, Factory);

        stop.Cancel();
        await Task.WhenAll(readers);
        Assert.Equal(101, service.GetLanguages().Count);
        Assert.Equal("lang39", service.DetectLanguage("x.l39", null).Id);
    }

    private enum BrokenKind
    {
        Throws,
        NoResult,
        SpanPastTheLine,
        OverlappingSpans,
        NegativeSpan,
        NoInitialState,
    }

    private sealed class Broken : ISyntaxClassifier
    {
        private readonly BrokenKind _kind;

        public Broken(BrokenKind kind) => _kind = kind;

        public SyntaxLanguage Language { get; } = new("someone-else", "Someone Else");

        public SyntaxLineState InitialState => _kind == BrokenKind.NoInitialState ? null! : State.Instance;

        public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState) => _kind switch
        {
            BrokenKind.Throws => throw new InvalidOperationException("broken"),
            BrokenKind.NoResult => default,
            BrokenKind.SpanPastTheLine => new SyntaxLineResult(new[] { new SyntaxSpan(2, lineText.Length, SyntaxClass.Keyword) }, new State()),
            BrokenKind.OverlappingSpans => new SyntaxLineResult(
                new[] { new SyntaxSpan(0, 4, SyntaxClass.Keyword), new SyntaxSpan(2, 2, SyntaxClass.Number) }, new State()),
            _ => new SyntaxLineResult(new[] { new SyntaxSpan(3, -1, SyntaxClass.Keyword) }, new State()),
        };
    }

    private sealed class State : SyntaxLineState
    {
        public static readonly State Instance = new();

        public override bool Equals(object? obj) => obj is State;

        public override int GetHashCode() => 1;
    }

    private sealed class ReentryProbe : ISyntaxClassifier
    {
        private int _inside;
        private int _calls;

        public bool Overlapped { get; private set; }

        public int Calls => _calls;

        public SyntaxLanguage Language { get; } = new("probe", "Probe");

        public SyntaxLineState InitialState => State.Instance;

        public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
        {
            if (Interlocked.Increment(ref _inside) != 1) Overlapped = true;
            Thread.SpinWait(200);
            _calls++;
            Interlocked.Decrement(ref _inside);
            return new SyntaxLineResult(Array.Empty<SyntaxSpan>(), startState);
        }
    }
}
