using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Text;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>TV13t: the Character Map's two-panel browser, find (F), show all (A) and resizing.</summary>
[Collection("NonParallel")]
public sealed class CharacterBrowserTests
{
    private static (TScreenChar[] Row, TPoint Origin) Row(CharacterMapView view, int y)
    {
        var screen = TScreen.ScreenBuffer!.Data;
        TPoint origin = view.MakeGlobal(new TPoint(0, 0));
        var row = new TScreenChar[view.size.x];
        for (int x = 0; x < view.size.x; x++) row[x] = screen[(origin.y + y) * TScreen.ScreenWidth + origin.x + x];
        return (row, origin);
    }

    private static string Text(ReadOnlySpan<TScreenChar> cells) => WideCellTests.Text(cells);

    // Every shortcut is listed: in full where there is room, otherwise in the compact two-per-line form.
    internal static void AssertAllShortcuts(CharacterMapView view, string right)
    {
        string[] expected = view.RightWidth >= 40 || view.size.y - 11 >= 9
            ? ["Find", "Show all", "Go to code", "Ranges", "Samples page", "Copy char", "Copy U+", "Copy C#", "Select", "Page", "Back/close"]
            : ["F Find   A All", "G Go to  R Range", "U Sample C Copy", "N Copy U+ S C#", "Arrows/PgUp/PgDn", "Esc Back/close"];
        Assert.Contains("Esc Back/close", right);
        foreach (string text in expected) Assert.Contains(text, right);
    }

    private static (DemoTestScope Scope, CharacterMapWindow Window) Open(int width = 78, int height = 23)
    {
        // A 132x45 screen holds the largest window (120x37) with its frame, desktop and status line.
        var scope = new DemoTestScope(132, 45);
        var window = new CharacterMapWindow(new TRect(0, 0, width, height));
        scope.App.DeskTop!.Insert(window);
        scope.App.Redraw();
        return (scope, window);
    }

    private static void Key(DemoTestScope scope, string text)
    {
        var ev = ComboInput.Typed(text); scope.App.HandleEvent(ref ev);
    }

    private static void KeyCode(DemoTestScope scope, ushort code)
    {
        var ev = ComboInput.Key(code); scope.App.HandleEvent(ref ev);
    }

    [Theory]
    [InlineData(60, 16)] [InlineData(78, 23)] [InlineData(100, 30)] [InlineData(120, 37)]
    public void GridFillsEveryRowAndTheRightPanelKeepsAllShortcuts(int width, int height)
    {
        var (scope, window) = Open(width, height);
        using var _ = scope;
        CharacterMapView view = window.View;
        Assert.Equal(view.size.y - 1, view.GridRows);
        Assert.Equal(view.GridRows, view.Model.Rows);
        for (int y = 1; y < view.size.y; y++)
            Assert.Equal($"{(view.Model.TopRow + y - 1) * 16:X6}", Text(Row(view, y).Row.AsSpan(0, 6)));
        string right = string.Join("\n", Enumerable.Range(0, view.size.y).Select(y => Text(Row(view, y).Row.AsSpan(view.RightColumn))));
        AssertAllShortcuts(view, right);
        // Metadata survives the minimum size too (native-run finding D1).
        Assert.Contains("Block: Box", right);
        Assert.Contains("UTF-8 E2 94 80", right);
        Assert.Contains("UTF-16 2500", right);
        Assert.Contains("Category: So", right);
        Assert.DoesNotContain("Preview", right);
        for (int y = 0; y < view.size.y; y++) WideCellTests.AssertWellFormed(Row(view, y).Row, $"{width}x{height} row {y}");
    }

    [Fact]
    public void TickListsTheCheckMarksInFixedColumnsAndAllRestoresTheGrid()
    {
        var (scope, window) = Open();
        using var _ = scope;
        CharacterMapView view = window.View;
        view.Model.Select(0x2500);
        int top = view.Model.TopRow;

        view.ApplySearch("tick"); view.DrawView();
        Assert.NotNull(view.Filter);
        Assert.Equal([0x2713, 0x2714, 0x2611, 0x2705], view.Filter!.Codes[..4]);
        Assert.StartsWith($"{view.Filter.Count} matches: tick", Text(Row(view, 0).Row));
        for (int i = 0; i < 4; i++)
        {
            TScreenChar[] row = Row(view, 1 + i).Row;
            int code = view.Filter.Codes[i];
            Assert.Equal($"U+{code:X4}", Text(row.AsSpan(0, 6)));
            Assert.Equal(char.ConvertFromUtf32(code), row[CharacterMapView.ListGlyphColumn].GetText());
            string name = UnicodeCharacterNames.Shared.Label(code);
            Assert.StartsWith(name[..5], Text(row.AsSpan(CharacterMapView.ListNameColumn)));   // same column for 1- and 2-cell glyphs
            WideCellTests.AssertWellFormed(row);
        }
        Assert.Equal(0x2713, view.Model.Selected);
        Assert.Contains("CHECK MARK", Text(Row(view, 1).Row.AsSpan(view.RightColumn)) + Text(Row(view, 2).Row.AsSpan(view.RightColumn)));

        Key(scope, "a");
        Assert.Null(view.Filter);
        Assert.Equal(0x2500, view.Model.Selected);
        Assert.Equal(top, view.Model.TopRow);
    }

    [Theory]
    [InlineData("grinning")] [InlineData("CJK")] [InlineData("combining acute")] [InlineData("U+0041")]
    [InlineData("family")] [InlineData("arrow")]
    public void EveryListRowKeepsItsColumns(string query)
    {
        var (scope, window) = Open(60, 16);
        using var _ = scope;
        CharacterMapView view = window.View;
        view.ApplySearch(query); view.DrawView();
        Assert.NotNull(view.Filter);
        for (int y = 1; y < view.size.y && y - 1 < view.Filter!.Count; y++)
        {
            TScreenChar[] row = Row(view, y).Row;
            Assert.StartsWith("U+", Text(row.AsSpan(0, 2)));
            Assert.NotEqual(TScreenCellKind.Trail, row[CharacterMapView.ListNameColumn].Kind);
            Assert.Equal('\u2502', row[view.LeftWidth].Character);
            WideCellTests.AssertWellFormed(row, $"{query} row {y}");
        }
    }

    [Fact]
    public void ZeroMatchesAndCancelKeepTheCurrentStateAndEmptyQueryShowsAll()
    {
        var (scope, window) = Open();
        using var _ = scope;
        CharacterMapView view = window.View;
        view.ApplySearch("arrow");
        int count = view.Filter!.Count;
        view.ApplySearch("zzqq nothing");
        Assert.Equal(count, view.Filter!.Count);
        Assert.Contains("No matches", view.Feedback);
        Assert.Equal("zzqq nothing", view.LastQuery);

        // F, then Cancel: nothing changes.
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            Assert.Equal("zzqq nothing", Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).Data);
            ScriptedDemoApp.Button(dialog, Views.cmCancel).Press();
        });
        Key(scope, "f");
        Assert.Equal(count, view.Filter!.Count);

        view.ApplySearch("   ");
        Assert.Null(view.Filter);
    }

    [Fact]
    public void FindDialogTakesTypedLettersAndConfirmShowsResults()
    {
        var (scope, window) = Open();
        using var _ = scope;
        CharacterMapView view = window.View;
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            // Letters typed into the dialog reach its input line, not the browser's A/F/G shortcuts.
            ComboInput.Send(ComboInput.Typed("t"), ComboInput.Typed("i"), ComboInput.Typed("c"), ComboInput.Typed("k"));
        });
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            Assert.EndsWith("tick", Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).Data);
            Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).SetData("tick");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        Key(scope, "f");
        Assert.NotNull(view.Filter);
        Assert.Equal(0x2713, view.Model.Selected);
    }

    [Fact]
    public void ListNavigationMouseCopyAndEsc()
    {
        var (scope, window) = Open();
        using var _ = scope;
        CharacterMapView view = window.View;
        view.ApplySearch("arrow"); view.DrawView();
        KeyCode(scope, Keys.kbDown);
        Assert.Equal(1, view.ResultIndex);
        KeyCode(scope, Keys.kbPgDn);
        Assert.Equal(1 + view.GridRows, view.ResultIndex);
        Assert.InRange(view.ResultIndex - view.ResultTop, 0, view.GridRows - 1);
        KeyCode(scope, Keys.kbEnd);
        Assert.Equal(view.Filter!.Count - 1, view.ResultIndex);
        KeyCode(scope, Keys.kbHome);
        Assert.Equal((0, 0), (view.ResultIndex, view.ResultTop));

        TPoint origin = view.MakeGlobal(new TPoint(0, 0));
        var click = new TEvent { What = Events.evMouseDown };
        click.mouse.where = new TPoint(origin.x + 20, origin.y + 3);
        click.mouse.buttons = 1;
        view.HandleEvent(ref click);
        Assert.Equal(2, view.ResultIndex);
        Assert.Equal(view.Filter.Codes[2], view.Model.Selected);

        var original = ClipboardService.Current;
        try
        {
            var clipboard = new InMemoryClipboardService(); ClipboardService.Current = clipboard;
            Key(scope, "c");
            Assert.Equal(char.ConvertFromUtf32(view.Filter.Codes[2]), clipboard.GetText());
            Key(scope, "n");
            Assert.Equal($"U+{view.Filter.Codes[2]:X4}", clipboard.GetText());
        }
        finally { ClipboardService.Current = original; }

        KeyCode(scope, Keys.kbEsc);
        Assert.Null(view.Filter);
        KeyCode(scope, Keys.kbEsc);
        Assert.Null(window.owner);    // Esc in the grid closes the window
    }

    [Fact]
    public void SamplesPageRoundTripKeepsTheFilterAndGoToEndsIt()
    {
        var (scope, window) = Open();
        using var _ = scope;
        CharacterMapView view = window.View;
        view.ApplySearch("arrow");
        KeyCode(scope, Keys.kbDown); KeyCode(scope, Keys.kbDown);
        Key(scope, "u");
        Assert.True(view.ShowSamples);
        Assert.Contains("Unicode samples", Text(Row(view, 0).Row));
        Key(scope, "u");
        Assert.False(view.ShowSamples);
        Assert.NotNull(view.Filter);
        Assert.Equal(2, view.ResultIndex);

        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).SetData("1F600");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        Key(scope, "g");
        Assert.Null(view.Filter);
        Assert.Equal(0x1F600, view.Model.Selected);
    }

    [Fact]
    public void ResizingDuringASearchKeepsTheSelectionVisibleAndRowsWellFormed()
    {
        var (scope, window) = Open(120, 37);
        using var _ = scope;
        CharacterMapView view = window.View;
        view.ApplySearch("CJK");
        for (int i = 0; i < 40; i++) KeyCode(scope, Keys.kbDown);
        int selected = view.Model.Selected;
        foreach (var (w, h) in new[] { (60, 16), (100, 30), (78, 23), (120, 37), (60, 16) })
        {
            window.ChangeBounds(new TRect(0, 0, w, h)); scope.App.Redraw();
            Assert.Equal(40, view.ResultIndex);
            Assert.Equal(selected, view.Model.Selected);
            Assert.InRange(view.ResultIndex - view.ResultTop, 0, view.GridRows - 1);
            int row = view.ResultIndex - view.ResultTop + 1;
            Assert.StartsWith($"U+{selected:X4}", Text(Row(view, row).Row));
            for (int y = 0; y < view.size.y; y++) WideCellTests.AssertWellFormed(Row(view, y).Row, $"{w}x{h} row {y}");
        }
    }
}
