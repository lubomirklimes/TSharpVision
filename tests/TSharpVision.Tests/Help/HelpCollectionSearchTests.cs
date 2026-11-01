using System.Diagnostics;
using System.IO;
using TSharpVision.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Help;

// TV12l: Help v2 — THelpCollection (several .hlp files, first registration wins for a context) and full-text search
// over topic bodies, plus the help viewer's search UI. Fixtures are written with the existing THelpFile writer in V1
// (single-byte legacy encodings) and V2 (UTF-16) formats; the on-disk formats are not changed.
[Collection("NonParallel")]
public sealed class HelpCollectionSearchTests : IDisposable
{
    private readonly StreamableRegistryScope _streams = new();
    private readonly TempDirectory _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
    }

    private sealed record Topic(int Context, string Text, params (int Ref, int Offset, byte Length)[] Links);

    private string Build(string name, int format, ILegacyTextEncoding? encoding, params Topic[] topics)
    {
        THelpFile.RegisterStreamableTypes();
        string path = Path.Combine(_tmp.Path, name);
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp, format, encoding ?? LegacyTextEncodings.Latin1);
        foreach (var t in topics)
        {
            var topic = new THelpTopic();
            foreach (string paragraph in t.Text.Split('\n'))
                topic.AddParagraph(new TParagraph { wrap = false, Text = paragraph + "\n" });
            foreach (var (r, o, l) in t.Links)
                topic.AddCrossRef(new TCrossRef { @ref = r, offset = o, length = l });
            hf.RecordPositionInIndex(t.Context);
            hf.PutTopic(topic);
        }
        hf.Flush();
        fp.Close();
        return path;
    }

    private string BuildV2(string name, params Topic[] topics) => Build(name, THelpFile.FormatV2Utf16, null, topics);

    private string Czech() => BuildV2("cs.hlp",
        new Topic(1, "Rejstřík\nObsah nápovědy"),
        new Topic(2, "Úvod\nŽluťoučký kůň úpěl ďábelské ódy."),
        new Topic(3, "Soubory\nOtevření a ČTENÍ souborů ve formátu C# (verze 2)."));

    // ── search semantics ──────────────────────────────────────────────────

    [Theory]
    [InlineData("ŽLUŤOUČKÝ", 2)]            // case folding of Czech letters
    [InlineData("žluťoučký kůň", 2)]         // several words, all required
    [InlineData("kůň žluťoučký", 2)]         // word order does not matter
    [InlineData("čtení", 3)]                 // upper-case body text found by a lower-case query
    [InlineData("soub", 3)]                  // a word may be part of a longer word
    [InlineData("c#", 3)]                    // characters have no special meaning
    [InlineData("(verze 2).", 3)]
    [InlineData("ábel", 2)]                  // body text, not only titles
    public void SearchFindsBodyWordsIgnoringCase(string query, int context)
    {
        using var help = new THelpCollection();
        help.Open(Czech());
        var result = Assert.Single(help.Search(query));
        Assert.Equal(context, result.Context);
        Assert.Equal("cs.hlp", result.Source);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("zlutoucky")] [InlineData("kůň neexistuje")] [InlineData("*")]
    [InlineData(".*")] [InlineData("\\d+")]
    public void QueriesWithoutAMatchFindNothingAndDiacriticsAreNotFolded(string query)
    {
        using var help = new THelpCollection();
        help.Open(Czech());
        Assert.Empty(help.Search(query));
    }

    [Fact]
    public void TitlesComeFromTheFirstLineAndRankingIsDeterministic()
    {
        string path = BuildV2("rank.hlp",
            new Topic(5, "Printing\nThe printer prints pages."),             // body: printer, prints
            new Topic(4, "Overview\nprinting printing printing"),            // body: 3x printing
            new Topic(6, "Printer setup\nChoose the printer."),               // title + body
            new Topic(7, "Other\nNothing relevant here."));
        using var help = new THelpCollection();
        help.Open(path);

        // Title occurrences weigh 8 and body occurrences 1 (the first line is part of the body too):
        // 5 = 8 + 3, 6 = 8 + 2, 4 = 0 + 3.
        var results = help.Search("print");
        Assert.Equal(new[] { 5, 6, 4 }, results.Select(r => r.Context));
        Assert.Equal(new[] { "Printing", "Printer setup", "Overview" }, results.Select(r => r.Title));
        Assert.Equal(new[] { 11, 10, 3 }, results.Select(r => r.Score));
        // Repeated searches return the same order (and reuse the cached text).
        for (int i = 0; i < 3; i++)
            Assert.Equal(results.Select(r => (r.Context, r.Score)), help.Search("PRINT").Select(r => (r.Context, r.Score)));
        Assert.Single(help.Search("print", maxResults: 1));
    }

    [Fact]
    public void EqualScoresAreOrderedByRegistrationThenContext()
    {
        string a = BuildV2("a.hlp", new Topic(9, "Same\nword"), new Topic(8, "Same\nword"));
        string b = BuildV2("b.hlp", new Topic(1, "Same\nword"));
        using var help = new THelpCollection();
        help.Open(b, "B");
        help.Open(a, "A");
        Assert.Equal(new[] { ("B", 1), ("A", 8), ("A", 9) }, help.Search("word").Select(r => (r.Source, r.Context)));
    }

    [Fact]
    public void PhraseMatchRanksAboveScatteredWords()
    {
        string path = BuildV2("phrase.hlp",
            new Topic(2, "A\nopen file here, and the file opens"),
            new Topic(3, "B\nfile open"),
            new Topic(4, "C\nopen the file"));
        using var help = new THelpCollection();
        help.Open(path);
        Assert.Equal(4, help.Search("open the file")[0].Context);
    }

    [Fact]
    public void LinkSpacesAreSearchedAsSpaces()
    {
        string path = BuildV2("links.hlp", new Topic(2, "Topic\nSee File\u00FFDialog\u00A0Basics.", (3, 13, 11)), new Topic(3, "Target\nx"));
        using var help = new THelpCollection();
        help.Open(path);
        Assert.Equal(2, Assert.Single(help.Search("file dialog basics")).Context);
    }

    [Fact]
    public void VeryLargeTopicIsSearchedToTheEnd()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => $"Line {i} of filler text about nothing in particular.").ToList();
        lines.Add("The needle is here: Šťastný konec.");
        string path = BuildV2("large.hlp", new Topic(2, "Large\n" + string.Join('\n', lines)), new Topic(3, "Small\nšťastný začátek"));
        using var help = new THelpCollection();
        help.Open(path);
        var watch = Stopwatch.StartNew();
        Assert.Equal(new[] { 2 }, help.Search("needle ŠŤASTNÝ").Select(r => r.Context));
        Assert.Equal(new[] { 2, 3 }, help.Search("šťastný").Select(r => r.Context).Order());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"search took {watch.Elapsed}");
    }

    // ── collections of files ──────────────────────────────────────────────

    [Fact]
    public void FirstRegisteredFileWinsForADuplicateContextAndSearchShowsBothSources()
    {
        string first = BuildV2("first.hlp", new Topic(1, "Index\nfirst index"), new Topic(10, "Editing\nfirst editor body"));
        string second = BuildV2("second.hlp", new Topic(1, "Index\nsecond index"), new Topic(10, "Editing\nsecond editor body"),
            new Topic(20, "Only second\nunique"));
        using var help = new THelpCollection();
        var f1 = help.Open(first);
        var f2 = help.Open(second, "Second pack");

        Assert.Same(f1, help.FindContext(10));
        Assert.Same(f2, help.FindContext(20));
        Assert.Null(help.FindContext(99));
        Assert.Contains("first editor", THelpCollection.PlainText(help.GetTopic(10)));
        Assert.Contains("No help available", THelpCollection.PlainText(help.GetTopic(99)));

        var results = help.Search("editing");
        Assert.Equal(new[] { ("first.hlp", 10), ("Second pack", 10) }, results.Select(r => (r.Source, r.Context)));
        Assert.All(results, r => Assert.Equal("Editing", r.Title));

        Assert.True(help.Remove(f1));
        Assert.False(help.Remove(f1));
        Assert.Same(f2, help.FindContext(10)); // the next registration takes over
        Assert.Contains("second editor", THelpCollection.PlainText(help.GetTopic(10)));
        Assert.Equal(new[] { "Second pack" }, help.Search("editing").Select(r => r.Source));
    }

    [Fact]
    public void MixedV1AndV2FilesAreSearchedTogether()
    {
        string v1 = Build("legacy.hlp", THelpFile.FormatV1Latin1, null, new Topic(2, "Größe\nDie GRÖSSE ändern."));
        string v1cs = Build("legacy-cs.hlp", THelpFile.FormatV1Latin1, LegacyTextEncodings.Cp852, new Topic(3, "Čeština\nPŘÍLIŠ žluťoučký."));
        string v2 = BuildV2("modern.hlp", new Topic(4, "Größe\nNeue größe."));
        using var help = new THelpCollection();
        help.Open(v1);
        help.Open(v2);
        // A V1 file in another legacy encoding is opened by the caller with that encoding and registered.
        var fs = new Fpstream(new FileStream(v1cs, FileMode.Open, FileAccess.Read, FileShare.Read), ownsStream: true);
        try
        {
            help.Add(new THelpFile(fs, THelpFile.FormatV2Utf16, LegacyTextEncodings.Cp852), "Čeština (V1)");
            Assert.Equal(THelpFile.FormatV1Latin1, help.Files[0].formatVersion);
            Assert.Equal(THelpFile.FormatV2Utf16, help.Files[1].formatVersion);

            // "GRÖSSE" is not folded to "größe" (simple case mapping), so the V1 topic matches on its title only.
            Assert.Equal(new[] { ("modern.hlp", 4), ("legacy.hlp", 2) }, help.Search("größe").Select(r => (r.Source, r.Context)));
            Assert.Equal(("Čeština (V1)", 3), help.Search("příliš ŽLUŤOUČKÝ").Select(r => (r.Source, r.Context)).Single());
            Assert.Throws<ArgumentException>(() => help.Add(help.Files[2], "again"));
        }
        finally { help.Clear(); fs.Close(); }
    }

    [Fact]
    public void MissingAndCorruptFilesAreRejectedWithoutKeepingAHandle()
    {
        using var help = new THelpCollection();
        Assert.Throws<FileNotFoundException>(() => help.Open(Path.Combine(_tmp.Path, "missing.hlp")));
        Assert.False(File.Exists(Path.Combine(_tmp.Path, "missing.hlp"))); // nothing was created

        string text = Path.Combine(_tmp.Path, "notes.hlp");
        File.WriteAllText(text, "This is not a help file at all.");
        Assert.Throws<InvalidDataException>(() => help.Open(text));
        File.Delete(text); // the handle was released

        // A help header with a damaged index either is rejected or registers with nothing to find; never a crash.
        string damaged = Path.Combine(_tmp.Path, "damaged.hlp");
        File.WriteAllBytes(damaged, new byte[] { 0x46, 0x42, 0x48, 0x32, 0, 0, 0, 0, 0x10, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        THelpFile? opened = null;
        try { opened = help.Open(damaged); }
        catch (InvalidDataException) { }
        if (opened != null)
        {
            Assert.Empty(help.Search("help"));
            Assert.True(help.Remove(opened));
        }
        File.Delete(damaged);
        Assert.Empty(help.Files);
    }

    [Fact]
    public void DamagedTopicIsSkippedAndTheRestIsStillSearched()
    {
        string path = BuildV2("partly.hlp", new Topic(2, "Good\nreadable topic"), new Topic(3, "Bad\nreadable but damaged"));
        long position;
        using (var probe = new THelpCollection())
            position = probe.Open(path).index.Position(3);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            fs.Position = position;
            fs.Write(new byte[] { 0xFE, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF });
        }
        using var help = new THelpCollection();
        help.Open(path);
        Assert.Equal(new[] { 2 }, help.Search("readable").Select(r => r.Context));
    }

    [Fact]
    public void RemoveAndDisposeReleaseTheFiles()
    {
        string a = BuildV2("a.hlp", new Topic(2, "A\nalpha"));
        string b = BuildV2("b.hlp", new Topic(2, "B\nbeta"));
        var help = new THelpCollection();
        var fa = help.Open(a);
        help.Open(b);
        Assert.Single(help.Search("alpha"));
        help.Remove(fa);
        Assert.Empty(help.Search("alpha"));
        Assert.False(help.Contains(fa));
        Assert.Null(help.GetName(fa));
        File.Delete(a);
        help.Dispose();
        File.Delete(b);
        Assert.Empty(help.Files);
    }

    [Fact]
    public void SingleFileUseIsUnchanged()
    {
        string path = BuildV2("single.hlp", new Topic(1, "Index\nhome"), new Topic(2, "Two\nsecond", (1, 4, 6)));
        using var driver = new DriverScope();
        var fp = new Fpstream(path);
        try
        {
            var hf = new THelpFile(fp);
            var viewer = new THelpViewer(new TRect(0, 0, 50, 10), null, null, hf, 2);
            Assert.Contains("second", THelpCollection.PlainText(viewer.topic));
            viewer.NavigateTo(1);
            Assert.Contains("home", THelpCollection.PlainText(viewer.topic));
            viewer.GoBack();
            Assert.Contains("second", THelpCollection.PlainText(viewer.topic));
            Assert.Same(hf, viewer.hFile);
        }
        finally { fp.Close(); }
    }

    // ── viewer navigation over a collection ───────────────────────────────

    [Fact]
    public void ViewerNavigatesAcrossFilesAndBackSkipsRemovedFiles()
    {
        string first = BuildV2("first.hlp", new Topic(1, "Index one\nstart"), new Topic(2, "One\nfirst two"));
        string second = BuildV2("second.hlp", new Topic(1, "Index two\nother start"), new Topic(5, "Five\nsecond five", (1, 7, 4)));
        using var driver = new DriverScope();
        using var help = new THelpCollection();
        var f1 = help.Open(first); var f2 = help.Open(second);
        var viewer = new THelpViewer(new TRect(0, 0, 50, 10), null, null, help, 5);
        Assert.Same(f2, viewer.hFile); // context 5 exists only in the second file

        Assert.True(viewer.NavigateTo(f1, 2));
        Assert.Contains("first two", THelpCollection.PlainText(viewer.topic));
        viewer.NavigateTo(1); // a link stays in the current file
        Assert.Same(f1, viewer.hFile);
        Assert.Contains("Index one", THelpCollection.PlainText(viewer.topic));
        viewer.GoBack();
        viewer.GoBack();
        Assert.Same(f2, viewer.hFile);
        Assert.Contains("second five", THelpCollection.PlainText(viewer.topic));

        var stale = help.Search("first two").Single();
        help.Remove(f1);
        Assert.False(viewer.NavigateTo(stale.File, stale.Context)); // a stale result does not open a removed file
        Assert.Same(f2, viewer.hFile);

        Assert.True(viewer.NavigateTo(f2, 1));
        help.Remove(f2);
        viewer.SwitchToTopic(1); // the closed file is not read
        Assert.Contains("No help available", THelpCollection.PlainText(viewer.topic));
    }

    // ── search UI inside a running program ────────────────────────────────

    [Fact]
    public void AltSSearchListsSourcesOpensTheChosenTopicAndBackspaceReturns()
    {
        string first = BuildV2("guide.hlp", new Topic(1, "Index\nguide start"), new Topic(7, "Files\nOpening files in the guide."));
        string second = BuildV2("extras.hlp", new Topic(3, "Files\nOpening files with extras."));
        using var help = new THelpCollection();
        var f1 = help.Open(first);
        var f2 = help.Open(second);
        using var scope = new DemoTestScope(); var app = scope.App;
        var window = new THelpWindow(help, 1);
        app.DeskTop!.Insert(window);
        var viewer = Assert.IsType<THelpViewer>(window.Viewer);

        List<string> rows = new();
        app.Step(() =>
        {
            var input = app.Modal<TDialog>();
            Assert.Contains("Search Help", input.title);
            ComboInput.Type("OPENING files");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
            app.Step(() =>
            {
                var results = Assert.IsType<THelpSearchResultsDialog>(TView.ModalTopView);
                rows = Enumerable.Range(0, results.List.range).Select(i => results.List.GetText(i, 80)).ToList();
                ComboInput.Send(ComboInput.Key(Keys.kbDown), ComboInput.Key(Keys.kbEnter));
            });
        });
        var altS = ComboInput.Key(Keys.kbAltS);
        viewer.HandleEvent(ref altS);

        Assert.Equal(Events.evNothing, altS.What);
        Assert.Equal(new[] { "Files  [guide.hlp]", "Files  [extras.hlp]" }, rows);
        Assert.Same(f2, viewer.hFile);
        Assert.Contains("with extras", THelpCollection.PlainText(viewer.topic));
        Assert.Same(window, app.DeskTop.current);
        Assert.True(viewer.GetState(Views.sfFocused));

        var back = ComboInput.Key(Keys.kbBack);
        viewer.HandleEvent(ref back);
        Assert.Same(f1, viewer.hFile);
        Assert.Contains("guide start", THelpCollection.PlainText(viewer.topic));

        // A repeated search offers the previous words; no match shows a message and keeps the topic.
        app.Step(() =>
        {
            var input = app.Modal<TDialog>();
            var line = ScriptedDemoApp.Descendants(input).OfType<TInputLine>().Single();
            Assert.Equal("OPENING files", line.Data);
            line.SetData("nowhere");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
            app.Step(() =>
            {
                Assert.Contains("No help topics contain", ScriptedDemoApp.Text(app.Modal<TDialog>()));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
        });
        var command = ScriptedDemoApp.CommandEvent(Views.cmHelpSearch);
        viewer.HandleEvent(ref command);
        Assert.Same(f1, viewer.hFile);
        Assert.Contains("guide start", THelpCollection.PlainText(viewer.topic));

        // Escape in the word prompt changes nothing.
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        viewer.Search();
        Assert.Same(f1, viewer.hFile);
        window.Close();
    }

    [Fact]
    public void SingleFileViewerSearchesItsOwnFile()
    {
        string path = BuildV2("own.hlp", new Topic(1, "Index\nstart"), new Topic(4, "Colors\nPalette families."));
        var fp = new Fpstream(path);
        try
        {
            using var scope = new DemoTestScope(); var app = scope.App;
            var hf = new THelpFile(fp);
            var window = new THelpWindow(hf, 1);
            app.DeskTop!.Insert(window);
            var viewer = window.Viewer!;
            app.Step(() =>
            {
                ComboInput.Type("palette");
                ComboInput.Send(ComboInput.Key(Keys.kbEnter));
                app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEnter)));
            });
            viewer.Search();
            Assert.Contains("Palette families", THelpCollection.PlainText(viewer.topic));
            window.Close();
        }
        finally { fp.Close(); }
    }

    // ── F1 and Help > Search in a program ─────────────────────────────────

    [Fact]
    public void F1AndHelpSearchCommandUseTheProgramsCollection()
    {
        string first = BuildV2("first.hlp", new Topic(1, "Index\none"), new Topic(42, "Focused\nfrom the first file"));
        string second = BuildV2("second.hlp", new Topic(42, "Focused\nfrom the second file"), new Topic(43, "Other\nsecond only"));
        using var help = new THelpCollection();
        var f1 = help.Open(first); help.Open(second);
        using var driver = new DriverScope();
        var app = new CollectionProgram(help);
        try
        {
            var probe = new TView(new TRect(1, 1, 2, 2)) { helpCtx = 42 };
            probe.options |= Views.ofSelectable;
            var dialog = new TWindow(new TRect(0, 0, 20, 6), "Probe", 1);
            dialog.Insert(probe);
            app.DeskTop!.Insert(dialog);
            probe.Select();
            Assert.Equal(42, app.GetHelpCtx());
            var ev = ScriptedDemoApp.CommandEvent(Views.cmHelp);
            app.HandleEvent(ref ev);
            var window = Assert.IsType<THelpWindow>(app.LastHelpWindow);
            Assert.Same(f1, window.Viewer!.hFile);
            Assert.Contains("from the first file", THelpCollection.PlainText(window.Viewer.topic));
        }
        finally { app.ShutDown(); }
    }

    private sealed class CollectionProgram : TProgram
    {
        private readonly THelpCollection _help;
        public CollectionProgram(THelpCollection help) => _help = help;
        public THelpWindow? LastHelpWindow { get; private set; }
        public override THelpCollection? GetHelpCollection() => _help;
        public override THelpFile? GetHelpFile() => throw new InvalidOperationException("the collection must be used");
        protected override void ExecuteHelp(THelpWindow window) { LastHelpWindow = window; base.ExecuteHelp(window); }
    }
}
