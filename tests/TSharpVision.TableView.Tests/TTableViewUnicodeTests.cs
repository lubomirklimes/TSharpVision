using System.Reflection;
using System.Text;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.TableView.Tests;

/// <summary>
/// TV13p: column widths, headers, alignment and truncation measured in cells with the core text layout \u2014 wide
/// characters, emoji and combining sequences in cells and headers, clusters never split.
/// </summary>
public sealed class TTableViewUnicodeTests
{
    private const string Emoji = "\U0001F600";
    private const string ThumbsUpMedium = "\U0001F44D\U0001F3FD";
    private const string Kan = "\u6F22";
    private const string Ji = "\u5B57";
    private const string EAcute = "e\u0301";

    private static TScreenChar At(TableHarness h, int x, int y) => h.Host.buffer!.Data[(y * h.Width) + x];

    // Each cell's text in screen columns: a lead's whole cluster, a trail as nothing (so the string is shorter than
    // the row when it holds wide clusters); trailing blanks removed.
    private static string Clusters(TableHarness h, int y)
    {
        var text = new StringBuilder();
        for (int x = 0; x < h.Width; x++)
        {
            TScreenChar cell = At(h, x, y);
            if (cell.Kind != TScreenCellKind.Trail) text.Append(cell.Character == '\0' ? " " : cell.GetText());
        }

        return text.ToString().TrimEnd();
    }

    // No trail without its lead, no lead without all its trails.
    private static void AssertWellFormed(TableHarness h, int y)
    {
        for (int x = 0; x < h.Width; x++)
        {
            TScreenChar cell = At(h, x, y);
            if (cell.Kind == TScreenCellKind.Lead)
            {
                for (int i = 1; i < cell.CellWidth; i++)
                    Assert.Equal(TScreenCellKind.Trail, At(h, x + i, y).Kind);
            }
            else if (cell.Kind == TScreenCellKind.Trail)
            {
                int lead = x - 1;
                while (lead >= 0 && At(h, lead, y).Kind == TScreenCellKind.Trail) lead--;
                Assert.True(lead >= 0 && At(h, lead, y).Kind == TScreenCellKind.Lead, $"orphan trail at {x},{y}");
                Assert.True(x - lead < At(h, lead, y).CellWidth, $"trail at {x},{y} beyond its lead");
            }
        }
    }

    private static void WithProfile(TextWidthProfile profile, Action action)
    {
        PropertyInfo seam = typeof(TextLayout).GetProperty("ProfileOverride", BindingFlags.NonPublic | BindingFlags.Static)
                            ?? throw new InvalidOperationException("TextLayout.ProfileOverride test seam not found");
        seam.SetValue(null, profile);
        try { action(); }
        finally { seam.SetValue(null, TextWidthProfile.Default); }
    }

    [Fact]
    public void WideHeadersAndCellsAreMeasuredInCells()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, c) => new TableCell(c == 0 ? Kan + Ji : Emoji + Emoji + Emoji),
            new[] { new TableColumn(Kan + Ji + Kan), new TableColumn("E") }));

        Assert.Equal(6, h.View.GetColumnWidth(0));   // the header: three wide characters
        Assert.Equal(6, h.View.GetColumnWidth(1));   // sampled from the cells: three emoji
        Assert.Equal('\u2502', At(h, 6, 0).Character);
        Assert.Equal('\u2502', At(h, 6, 2).Character);
        Assert.Equal('\u2502', At(h, 13, 2).Character);
        Assert.Equal(Kan + Ji + Kan + "\u2502E     \u2502", Clusters(h, 0));
        Assert.Equal(Kan + Ji + "  \u2502" + Emoji + Emoji + Emoji + "\u2502", Clusters(h, 2));
        AssertWellFormed(h, 0);
        AssertWellFormed(h, 2);
    }

    [Fact]
    public void TruncationKeepsWholeClustersAndEndsWithTheEllipsis()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell("A" + Emoji + "B" + ThumbsUpMedium + "C"),
            new[] { new TableColumn("T", 6) }));

        // A, emoji, B fill four cells; the thumbs-up would straddle the ellipsis, so a blank stands in.
        Assert.Equal("A" + Emoji + "B \u2026\u2502", Clusters(h, 2));
        Assert.Equal('\u2026', At(h, 5, 2).Character);
        AssertWellFormed(h, 2);
    }

    [Fact]
    public void AnOverLongWideHeaderIsClippedWithoutSplittingACharacter()
    {
        using var h = new TableHarness(width: 60, height: 5);
        h.Show(new SyntheticTable(0, 1, columnList: new[] { new TableColumn(string.Concat(Enumerable.Repeat(Kan, 100))) }));

        Assert.Equal(TTableView.MaxColumnWidth, h.View.GetColumnWidth(0));
        // 39 cells before the ellipsis hold 19 wide characters and one blank.
        Assert.Equal(string.Concat(Enumerable.Repeat(Kan, 19)) + " \u2026\u2502", Clusters(h, 0));
        AssertWellFormed(h, 0);
    }

    [Fact]
    public void CombiningSequencesTakeOneCellEach()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell(EAcute + EAcute + EAcute + EAcute + EAcute),
            new[] { new TableColumn("C") }));

        Assert.Equal(5, h.View.GetColumnWidth(0));
        Assert.Equal(EAcute + EAcute + EAcute + EAcute + EAcute + "\u2502", Clusters(h, 2));
        Assert.Equal('\u2502', At(h, 5, 2).Character);
    }

    [Theory]
    [InlineData(TableAlignment.Left, 0)]
    [InlineData(TableAlignment.Center, 1)]
    [InlineData(TableAlignment.Right, 3)]
    public void AlignmentCountsCells(TableAlignment alignment, int expectedX)
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell(Kan + EAcute),
            new[] { new TableColumn("T", 6, alignment) }));

        Assert.Equal((TScreenCellKind.Lead, Kan), (At(h, expectedX, 2).Kind, At(h, expectedX, 2).GetText()));
        Assert.Equal(TScreenCellKind.Trail, At(h, expectedX + 1, 2).Kind);
        Assert.Equal(EAcute, At(h, expectedX + 2, 2).GetText());
        Assert.Equal('\u2502', At(h, 6, 2).Character);
        AssertWellFormed(h, 2);
    }

    [Fact]
    public void CenteredAsciiPutsTheOddCellOnTheRight()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell("ab"),
            new[] { new TableColumn("T", 5, TableAlignment.Center) }));

        Assert.Equal(" ab  \u2502", h.Rows[2]);
    }

    [Fact]
    public void ControlCharactersNextToEmojiStayVisibleDots()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(1, 1, (_, _) => new TableCell(Emoji + "\u0001x\ty")));

        Assert.StartsWith(Emoji + "\u00B7x\u00B7y", Clusters(h, 2));
        Assert.Equal(6, h.View.GetColumnWidth(0));   // emoji 2, then one cell for each dot and letter
    }

    [Fact]
    public void TheLegacyProfileKeepsOneCellPerUtf16Unit()
    {
        WithProfile(TextWidthProfile.Legacy, () =>
        {
            using var h = new TableHarness(width: 30, height: 5);
            h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell(Emoji + Kan),
                new[] { new TableColumn(Kan + Ji) }));

            Assert.Equal(TTableView.MinColumnWidth, h.View.GetColumnWidth(0));   // 3 units, at least the minimum
            Assert.Equal(Kan + Ji + "  \u2502", h.Rows[0]);
            for (int x = 0; x < 6; x++) Assert.Equal(TScreenCellKind.Single, At(h, x, 2).Kind);
        });
    }
}
