using System.Text;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Text;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>Diagnostics, Character Map as a Unicode acceptance view (TV13r): cells, markers and edges.</summary>
[Collection("NonParallel")]
public sealed class CharacterMapUnicodeTests
{
    private static (TScreenChar[] Row, int Width) ViewRow(CharacterMapView view, int y)
    {
        var screen = TScreen.ScreenBuffer!.Data;
        TPoint origin = view.MakeGlobal(new TPoint(0, 0));
        var row = new TScreenChar[view.size.x];
        for (int x = 0; x < view.size.x; x++) row[x] = screen[(origin.y + y) * TScreen.ScreenWidth + origin.x + x];
        return (row, view.size.x);
    }

    private static string Text(ReadOnlySpan<TScreenChar> cells) => WideCellTests.Text(cells);

    private static CharacterMapView Open(DemoTestScope scope, int width = 78, int height = 23)
    {
        var app = scope.App;
        var window = new CharacterMapWindow(new TRect(0, 0, width, height));
        app.DeskTop!.Insert(window);
        app.Redraw();
        return window.View;
    }

    [Theory]
    [InlineData(0x1F600)] [InlineData(0x4E00)] [InlineData(0x1D11E)] [InlineData(0x0301)]
    public void GridDrawsTheScalarAsClusterCells(int code)
    {
        using var scope = new DemoTestScope();
        CharacterMapView view = Open(scope);
        view.Model.Select(code); view.DrawView();
        int y = view.Model.Selected / 16 - view.Model.TopRow + 1;
        int x = 8 + (code % 16) * view.CellWidth;
        var (row, _) = ViewRow(view, y);
        string expected = CharacterMapModel.Preview(code);
        Assert.Equal(expected, row[x].GetText());
        int columns = CharacterMapModel.PreviewColumns(code);
        Assert.Equal(columns, row[x].CellWidth);
        if (columns == 2) Assert.Equal(TScreenCellKind.Trail, row[x + 1].Kind);
        WideCellTests.AssertWellFormed(row);
    }

    [Theory]
    [InlineData(0x1F600)] [InlineData(0x2500)] [InlineData(0x10FFF0)]
    public void EveryGridRowIsWellFormedInBothWindowSizes(int code)
    {
        foreach (var (w, h) in new[] { (78, 23), (60, 16) })
        {
            using var scope = new DemoTestScope();
            CharacterMapView view = Open(scope, w, h);
            view.Model.Select(code); view.DrawView();
            for (int y = 0; y < view.size.y; y++) WideCellTests.AssertWellFormed(ViewRow(view, y).Row, $"{w}x{h} row {y}");
        }
    }

    [Fact]
    public void SamplePageMarksTheExpectedEndColumnOfEverySample()
    {
        using var scope = new DemoTestScope();
        CharacterMapView view = Open(scope);
        var key = ComboInput.Typed("u"); scope.App.HandleEvent(ref key);
        Assert.True(view.ShowSamples);

        for (int i = 0; i < CharacterMapModel.Samples.Length; i++)
        {
            CharacterMapSample sample = CharacterMapModel.Samples[i];
            var (row, _) = ViewRow(view, 2 + i);
            int width = TextLayout.Measure(sample.Text, TextLayoutOptions.Default);
            int start = CharacterMapView.SampleColumn;
            Assert.Equal(sample.Text, Text(row.AsSpan(start, width)));
            Assert.Equal('|', row[start + width].Character);
            Assert.Contains(width == 1 ? "| 1 cell " : $"| {width} cells", Text(row));
            WideCellTests.AssertWellFormed(row, sample.Label);
        }
        // The documented widths (TV13j/U1 policy): CJK and emoji 2, supplementary musical symbol 1, accent 1.
        Assert.Equal(new[] { 20, 3, 1, 4, 1, 11, 2, 6, 13 },
            CharacterMapModel.Samples.Select(s => TextLayout.Measure(s.Text, TextLayoutOptions.Default)).ToArray());
    }

    [Fact]
    public void SamplePageEdgesKeepWideCharactersWholeOrBlank()
    {
        using var scope = new DemoTestScope();
        CharacterMapView view = Open(scope);
        view.ShowSamples = true; view.DrawView();
        int edgeRow = 2 + CharacterMapModel.Samples.Length;
        int last = view.size.x - 1;

        var (fit, _) = ViewRow(view, edgeRow);
        Assert.Equal((TScreenCellKind.Lead, "\u6F22"), (fit[last - 1].Kind, fit[last - 1].GetText()));
        Assert.Equal(TScreenCellKind.Trail, fit[last].Kind);

        var (cut, _) = ViewRow(view, edgeRow + 1);
        Assert.Equal((TScreenCellKind.Single, ' '), (cut[last].Kind, cut[last].Character));

        var (left, _) = ViewRow(view, edgeRow + 2);
        Assert.Equal((TScreenCellKind.Single, ' '), (left[0].Kind, left[0].Character));
        Assert.Equal("\u5B57", left[1].GetText());
        Assert.Equal(TScreenCellKind.Trail, left[2].Kind);

        for (int y = 0; y < view.size.y; y++) WideCellTests.AssertWellFormed(ViewRow(view, y).Row, $"row {y}");
    }

    [Fact]
    public void SamplePageSurvivesTheMinimumWindowSizeAndToggleBack()
    {
        using var scope = new DemoTestScope();
        CharacterMapView view = Open(scope, 60, 16);
        var key = ComboInput.Typed("u"); scope.App.HandleEvent(ref key);
        for (int y = 0; y < view.size.y; y++) WideCellTests.AssertWellFormed(ViewRow(view, y).Row, $"row {y}");
        key = ComboInput.Typed("u"); scope.App.HandleEvent(ref key);
        Assert.False(view.ShowSamples);
        Assert.Contains(Enumerable.Range(0, view.size.y).Select(y => Text(ViewRow(view, y).Row)), line => line.Contains("G Go to"));
    }

    [Fact]
    public void LegacyProfileSamplePageUsesOneCellPerCodeUnit()
    {
        try
        {
            TextLayout.ProfileOverride = TextWidthProfile.Legacy;
            using var scope = new DemoTestScope();
            CharacterMapView view = Open(scope);
            view.ShowSamples = true; view.DrawView();
            int cjk = Array.FindIndex(CharacterMapModel.Samples, s => s.Label == "CJK");
            var (row, _) = ViewRow(view, 2 + cjk);
            Assert.Equal('|', row[CharacterMapView.SampleColumn + 2].Character);
            Assert.All(row, c => Assert.Equal(TScreenCellKind.Single, c.Kind));
        }
        finally
        {
            TextLayout.ProfileOverride = TextWidthProfile.Default;
        }
    }
}
