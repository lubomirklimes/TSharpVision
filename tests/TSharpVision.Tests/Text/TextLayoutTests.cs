using System.Globalization;
using System.Text;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

public sealed class TextLayoutTests
{
    private const string Sample1 = "A\U0001F600B\U0001F44D\U0001F3FDC\U0001F1E8\U0001F1FFD\U0001F468\u200D\U0001F469\u200D\U0001F467E";

    private static readonly TextLayoutOptions Unicode = TextLayoutOptions.Default with { Profile = TextWidthProfile.Unicode };
    private static readonly TextLayoutOptions CodePoint = TextLayoutOptions.Default with { Profile = TextWidthProfile.CodePoint };
    private static readonly TextLayoutOptions Legacy = TextLayoutOptions.Default with { Profile = TextWidthProfile.Legacy };

    private static List<TextCluster> Clusters(string text, in TextLayoutOptions o, int startColumn = 0)
    {
        var list = new List<TextCluster>();
        foreach (TextCluster c in TextLayout.EnumerateClusters(text, o, startColumn)) list.Add(c);
        return list;
    }

    private static string Describe(string text, in TextLayoutOptions o) =>
        string.Join(" ", Clusters(text, o).Select(c => $"{c.Start}+{c.Length}@{c.Column}w{c.Width}"));

    // ---- TV13h final §5.1 samples ----------------------------------------------------------------------

    [Fact]
    public void Sample1MixedEmojiRow()
    {
        Assert.Equal(23, Sample1.Length);
        Assert.Equal("0+1@0w1 1+2@1w2 3+1@3w1 4+4@4w2 8+1@6w1 9+4@7w2 13+1@9w1 14+8@10w2 22+1@12w1",
            Describe(Sample1, Unicode));
        Assert.Equal(13, TextLayout.Measure(Sample1, Unicode));
        Assert.Equal(23, TextLayout.Measure(Sample1, Legacy));
        // Per code point: 😀 2, 👍+🏽 4, two regional indicators 4, family 6 (ZWJ = 0).
        Assert.Equal(1 + 2 + 1 + 4 + 1 + 4 + 1 + 6 + 1, TextLayout.Measure(Sample1, CodePoint));
    }

    [Fact]
    public void Sample1CaretStopsAndTrailColumns()
    {
        int[] offsets = [0, 1, 3, 4, 8, 9, 13, 14, 22, 23];
        int[] columns = [0, 1, 3, 4, 6, 7, 9, 10, 12, 13];
        for (int i = 0; i < offsets.Length; i++)
        {
            Assert.Equal(columns[i], TextLayout.ColumnAtOffset(Sample1, offsets[i], Unicode));
            Assert.Equal(offsets[i], TextLayout.OffsetAtColumn(Sample1, columns[i], Unicode, ColumnBias.Floor, out int col));
            Assert.Equal(columns[i], col);
        }
        // Trail columns of the wide clusters: floor to the cluster start, ceiling to its end.
        foreach (var (column, start, end) in new[] { (2, 1, 3), (5, 4, 8), (8, 9, 13), (11, 14, 22) })
        {
            Assert.Equal(start, TextLayout.OffsetAtColumn(Sample1, column, Unicode, ColumnBias.Floor, out _));
            Assert.Equal(end, TextLayout.OffsetAtColumn(Sample1, column, Unicode, ColumnBias.Ceiling, out int c));
            Assert.Equal(column + 1, c);
            Assert.Equal(start, TextLayout.OffsetAtColumn(Sample1, column, Unicode, ColumnBias.Nearest, out _));
        }
        // An offset inside a cluster reports the cluster's column.
        Assert.Equal(10, TextLayout.ColumnAtOffset(Sample1, 17, Unicode));
        Assert.Equal(23, TextLayout.OffsetAtColumn(Sample1, 40, Unicode, ColumnBias.Floor, out int past));
        Assert.Equal(13, past);
    }

    [Fact]
    public void Sample2CombiningAccent()
    {
        Assert.Equal("0+2@0w1", Describe("e\u0301", Unicode));
        Assert.Equal(1, TextLayout.Measure("e\u0301", CodePoint));
        Assert.Equal(2, TextLayout.Measure("e\u0301", Legacy));
    }

    [Fact]
    public void Sample3Cjk()
    {
        Assert.Equal("0+1@0w2 1+1@2w2", Describe("漢字", Unicode));
        Assert.Equal(4, TextLayout.Measure("漢字", CodePoint));
        Assert.Equal(2, TextLayout.Measure("漢字", Legacy));
    }

    [Fact]
    public void Sample4TabThenWideCluster()
    {
        Assert.Equal("0+1@0w8 1+1@8w2 2+1@10w1", Describe("\t漢x", Unicode));
        Assert.Equal("0+1@0w5 1+1@5w2 2+1@7w1", Describe("\t漢x", Unicode with { TabSize = 5 }));
        Assert.Equal("0+1@3w5 1+1@8w2 2+1@10w1", string.Join(" ", Clusters("\t漢x", Unicode, 3).Select(c => $"{c.Start}+{c.Length}@{c.Column}w{c.Width}")));
        // Tab size below 1 behaves as 1, as in TEditor.
        Assert.Equal(1, TextLayout.Measure("\t", Unicode with { TabSize = 0 }));
        // Fit never splits the wide cluster: 9 columns hold the tab only.
        Assert.Equal(1, TextLayout.Fit("\t漢x", 9, Unicode, out int used));
        Assert.Equal(8, used);
        Assert.Equal(2, TextLayout.Fit("\t漢x", 10, Unicode, out used));
        Assert.Equal(10, used);
    }

    // ---- U0-C corpus and policies ----------------------------------------------------------------------

    [Theory]
    [InlineData("❤", 1, 1)]                          // ❤ text default
    [InlineData("❤\uFE0F", 2, 1)]                    // ❤\uFE0F VS16
    [InlineData("❤\uFE0E", 1, 1)]                    // ❤\uFE0E VS15
    [InlineData("\U0001F600\uFE0E", 2, 2)]                // 😀 has no text variation: stays 2
    [InlineData("☺\uFE0F", 2, 1)]                    // ☺\uFE0F
    [InlineData("1\uFE0F\u20E3", 2, 1)]                   // keycap
    [InlineData("\U0001F469\U0001F3FD\u200D\U0001F4BB", 2, 6)] // 👩🏽\u200D💻
    [InlineData("\U0001F1E8", 2, 2)]                      // lone regional indicator: deliberately 2
    [InlineData("각", 2, 2)]              // Hangul L V T
    [InlineData("कि", 1, 2)]                    // Devanagari ki (SpacingMark): 1 by cluster
    [InlineData("±§Ω→", 4, 4)]        // East Asian Ambiguous: narrow by default
    [InlineData("☰", 2, 2)]                          // ☰ wide since Unicode 16.0
    public void UnicodeAndCodePointWidths(string text, int unicode, int codePoint)
    {
        Assert.Equal(unicode, TextLayout.Measure(text, Unicode));
        Assert.Equal(codePoint, TextLayout.Measure(text, CodePoint));
        Assert.Equal(text.Length, TextLayout.Measure(text, Legacy));
    }

    [Fact]
    public void AmbiguousWideOption()
    {
        Assert.Equal(8, TextLayout.Measure("±§Ω→", Unicode with { AmbiguousIsWide = true }));
        Assert.Equal(1, TextLayout.Measure("a", Unicode with { AmbiguousIsWide = true }));
    }

    [Fact]
    public void DevanagariConjunctFollowsTheRuntimeWithoutGB9c()
    {
        // UAX #29 16.0 makes क\u094Dष one cluster (GB9c); .NET 10 StringInfo does not implement GB9c.
        Assert.Equal("0+2@0w1 2+1@1w1", Describe("क\u094Dष", Unicode));
    }

    [Fact]
    public void ControlsLineBreaksMalformedAndZeroWidth()
    {
        Assert.Equal(TextClusterKind.LineBreak, Clusters("\n", Unicode)[0].Kind);
        Assert.Equal("0+2@0w0", Describe("\r\n", Unicode));
        Assert.Equal(TextClusterKind.LineBreak, Clusters("\r\n", Unicode)[0].Kind);
        var cr = Clusters("a\rb", Unicode);
        Assert.Equal((TextClusterKind.Control, 1), (cr[1].Kind, cr[1].Width));
        var bell = Clusters("\u0007\u0085", Unicode);
        Assert.All(bell, c => Assert.Equal((TextClusterKind.Control, 1), (c.Kind, c.Width)));

        var bad = Clusters("a\uD800b\uDC00", Unicode);
        Assert.Equal(4, bad.Count);
        Assert.Equal((TextClusterKind.Malformed, 1), (bad[1].Kind, bad[1].Width));
        Assert.Equal((TextClusterKind.Malformed, 1), (bad[3].Kind, bad[3].Width));

        var lone = Clusters("\u0301x", Unicode);
        Assert.Equal((TextClusterKind.ZeroWidth, 0), (lone[0].Kind, lone[0].Width));
        Assert.Equal(0, lone[1].Column);   // the lone mark takes no column
        Assert.Equal(1, Clusters("\u0301", Unicode with { ZeroWidthAsOne = true })[0].Width);
        Assert.Equal(0, TextLayout.Measure("\u200B", Unicode));
        Assert.Equal(1, TextLayout.Measure("\u00AD", Unicode));   // soft hyphen stays visible
    }

    [Fact]
    public void GiantClustersAreNotCapped()
    {
        string marks = "a" + new string('\u0301', 300);
        var c = Clusters(marks, Unicode);
        Assert.Single(c);
        Assert.Equal((301, 1), (c[0].Length, c[0].Width));

        var flags = new StringBuilder();
        for (int i = 0; i < 301; i++) flags.Append("\U0001F1E8");
        var ri = Clusters(flags.ToString(), Unicode);
        Assert.Equal(151, ri.Count);                // 150 pairs and a lone indicator
        Assert.All(ri, x => Assert.Equal(2, x.Width));
        Assert.Equal(2, ri[^1].Length);
    }

    [Fact]
    public void DefaultProfileFollowsOverrideSeam()
    {
        try
        {
            Assert.Equal(TextWidthProfile.Unicode, TextLayout.DefaultProfile);
            Assert.Equal(13, TextLayout.Measure(Sample1));
            TextLayout.ProfileOverride = TextWidthProfile.Legacy;
            Assert.Equal(TextWidthProfile.Legacy, TextLayout.DefaultProfile);
            Assert.Equal(23, TextLayout.Measure(Sample1));
        }
        finally
        {
            TextLayout.ProfileOverride = TextWidthProfile.Default;
        }
    }

    // ---- Conformance ---------------------------------------------------------------------------------

    [Fact]
    public void GraphemeBreakTest16FailsOnlyTheSevenGB9cLines()
    {
        string root = RepositoryLayout.Root(typeof(TextLayoutTests).Assembly);
        string text = UnicodeWidthTableGenerator.ReadVerified(UnicodeWidthTableGenerator.DataDirectory(root), "GraphemeBreakTest.txt");
        int pass = 0;
        var failures = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            int hash = raw.IndexOf('#');
            string line = (hash < 0 ? raw : raw[..hash]).Trim();
            if (line.Length == 0) continue;

            var sb = new StringBuilder();
            var expected = new List<int>();
            foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "÷") { if (sb.Length > 0) expected.Add(sb.Length); }
                else if (token != "×") sb.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber)));
            }
            string s = sb.ToString();

            var runtime = new List<int>();
            for (int i = 0; i < s.Length;) runtime.Add(i += StringInfo.GetNextTextElementLength(s.AsSpan(i)));
            var layout = Clusters(s, Unicode).Select(c => c.End).ToList();
            Assert.Equal(runtime, layout);   // TextLayout segments exactly like the runtime, fast path included

            if (runtime.SequenceEqual(expected)) pass++;
            else failures.Add(raw);
        }
        Assert.Equal(1086, pass);
        Assert.Equal(7, failures.Count);
        Assert.All(failures, f => Assert.Contains("[9.3]", f));   // GB9c, InCB conjunct linking
    }

    // ---- Properties ----------------------------------------------------------------------------------

    private static readonly string[] Alphabet =
    [
        "a", "Z", " ", "~", "č", "Ř", "±", "§", "ß", "\u00AD", "\u0301", "\u0308", "\uFE0E", "\uFE0F", "\u200D",
        "\U0001F1E8", "\U0001F1FF", "\U0001F600", "\U0001F44D", "\U0001F3FD", "\U0001F468", "❤", "漢", "字",
        "ᄀ", "ᅡ", "ᆨ", "क", "\u094D", "ि", "\t", "\r", "\n", "\u0007", "\u0085", "\uD800",
        "\uDC00", "\u20E3", "1", "\u0600",
    ];

    private static string RandomText(Random random, int maxLength)
    {
        var sb = new StringBuilder();
        int n = random.Next(maxLength + 1);
        for (int i = 0; i < n; i++) sb.Append(Alphabet[random.Next(Alphabet.Length)]);
        return sb.ToString();
    }

    [Fact]
    public void FastPathEqualsRuntimeSegmentationAndSlowWidth()
    {
        var random = new Random(13);
        for (int round = 0; round < 20_000; round++)
        {
            string s = RandomText(random, 24);
            int i = 0, column = 0;
            foreach (TextCluster c in TextLayout.EnumerateClusters(s, Unicode, 0))
            {
                int expectedLength = StringInfo.GetNextTextElementLength(s.AsSpan(i));
                Assert.Equal(i, c.Start);
                Assert.Equal(expectedLength, c.Length);
                Assert.Equal(column, c.Column);
                // Width is a function of the cluster text and column only.
                Assert.Equal(c.Width, TextLayout.GetCluster(s.Substring(i, c.Length), 0, column, Unicode).Width);
                i += c.Length;
                column += c.Width;
            }
            Assert.Equal(s.Length, i);
        }
    }

    [Theory]
    [InlineData(TextWidthProfile.Unicode)]
    [InlineData(TextWidthProfile.CodePoint)]
    [InlineData(TextWidthProfile.Legacy)]
    public void RestartAtAnyBoundaryMatchesFullLayout(TextWidthProfile profile)
    {
        var o = TextLayoutOptions.Default with { Profile = profile, TabSize = 4 };
        var random = new Random(7);
        for (int round = 0; round < 2_000; round++)
        {
            string s = RandomText(random, 20);
            var full = Clusters(s, o);
            foreach (TextCluster b in full)
            {
                var resumed = Clusters(s[b.Start..], o, b.Column)
                    .Select(c => new TextCluster(c.Start + b.Start, c.Length, c.Column, c.Width, c.Kind));
                Assert.Equal(full.SkipWhile(c => c.Start < b.Start), resumed);
            }
        }
    }

    [Fact]
    public void GapEnumeratorMatchesContiguousLayoutAtEverySplit()
    {
        var random = new Random(11);
        for (int round = 0; round < 3_000; round++)
        {
            string s = RandomText(random, 16);
            var whole = Clusters(s, Unicode);
            for (int split = 0; split <= s.Length; split++)
            {
                var gap = new List<TextCluster>();
                foreach (TextCluster c in TextLayout.EnumerateClusters(s.AsSpan(0, split), s.AsSpan(split), Unicode, 0))
                    gap.Add(c);
                Assert.Equal(whole, gap);
            }
        }
    }

    [Fact]
    public void GapEnumeratorHandlesGiantClustersAcrossTheGap()
    {
        string s = "x" + new string('\u0301', 1000) + "y" + "\U0001F468\u200D\U0001F469";
        var whole = Clusters(s, Unicode);
        foreach (int split in new[] { 1, 2, 17, 500, 1000, 1001, 1002, 1003, 1004 })
        {
            var gap = new List<TextCluster>();
            foreach (TextCluster c in TextLayout.EnumerateClusters(s.AsSpan(0, split), s.AsSpan(split), Unicode, 0)) gap.Add(c);
            Assert.Equal(whole, gap);
        }
    }

    [Fact]
    public void OffsetColumnRoundTripAtEveryBoundary()
    {
        var random = new Random(5);
        for (int round = 0; round < 3_000; round++)
        {
            string s = RandomText(random, 20);
            foreach (TextCluster c in Clusters(s, Unicode))
            {
                Assert.Equal(c.Column, TextLayout.ColumnAtOffset(s, c.Start, Unicode));
                if (c.Width == 0) continue;
                // Zero-width clusters share a column with their successor: the first cluster at that column wins.
                int first = Clusters(s, Unicode).First(x => x.Column == c.Column).Start;
                Assert.Equal(first, TextLayout.OffsetAtColumn(s, c.Column, Unicode, ColumnBias.Floor, out int col));
                Assert.Equal(c.Column, col);
            }
            Assert.Equal(TextLayout.Measure(s, Unicode), TextLayout.ColumnAtOffset(s, s.Length, Unicode));
        }
    }

    [Fact]
    public void HotPathsDoNotAllocate()
    {
        string ascii = new('x', 100_000);
        string czech = string.Concat(Enumerable.Repeat("Příliš žluťoučký kůň ", 5_000));
        string cjk = string.Concat(Enumerable.Repeat("漢字", 50_000));
        string emoji = string.Concat(Enumerable.Repeat(Sample1, 5_000));
        string marks = "a" + new string('\u0301', 100_000);
        string[] inputs = [ascii, czech, cjk, emoji, marks];

        void Run()
        {
            foreach (string s in inputs)
            {
                TextLayout.Measure(s, Unicode);
                TextLayout.ColumnAtOffset(s, s.Length / 2, Unicode);
                TextLayout.OffsetAtColumn(s, 1000, Unicode, ColumnBias.Nearest, out _);
                TextLayout.Fit(s, 5000, Unicode, out _);
                foreach (TextCluster _ in TextLayout.EnumerateClusters(s.AsSpan(0, s.Length / 2 + 1), s.AsSpan(s.Length / 2 + 1), Unicode, 0)) { }
            }
        }

        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- Hotkey markup -------------------------------------------------------------------------------

    [Theory]
    [InlineData("~O~K", 2, 'O')]
    [InlineData("~C~ancel", 6, 'C')]
    [InlineData("Ulož~i~t", 6, 'I')]
    [InlineData("~漢~字", 4, '漢')]
    [InlineData("no hotkey", 9, '\0')]
    [InlineData("trailing~", 8, '\0')]
    [InlineData("", 0, '\0')]
    public void HotkeyMarkup(string text, int width, char hotkey)
    {
        Assert.Equal(width, HotkeyText.Measure(text));
        Assert.Equal(hotkey, HotkeyText.GetHotkey(text));
    }
}
