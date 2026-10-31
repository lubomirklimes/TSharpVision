using System.Text;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class CharacterMapTests
{
    [Theory]
    [InlineData("0000", 0)]
    [InlineData("U+D7FF", 0xD7FF)]
    [InlineData("0xe000", 0xE000)]
    [InlineData("10ffff", 0x10FFFF)]
    [InlineData("  U+251C  ", 0x251C)]
    [InlineData("0x1F600", 0x1F600)]
    public void ParsesScalars(string input, int expected)
    {
        Assert.True(CharacterMapModel.TryParse(input, out int code, out string error), error);
        Assert.Equal(expected, code);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("xyz")]
    [InlineData("U+")]
    [InlineData("-1")]
    [InlineData("110000")]
    [InlineData("FFFFFFFF")]
    [InlineData("D800")]
    [InlineData("DFFF")]
    public void RejectsInvalidGoTo(string input)
    {
        Assert.False(CharacterMapModel.TryParse(input, out _, out string error));
        Assert.NotEmpty(error);
    }
    [Theory]
    [InlineData(0, "U+0000", "00", @"\u0000")]
    [InlineData(0xD7FF, "U+D7FF", "ED 9F BF", @"\uD7FF")]
    [InlineData(0xE000, "U+E000", "EE 80 80", @"\uE000")]
    [InlineData(0x10FFFF, "U+10FFFF", "F4 8F BF BF", @"\U0010FFFF")]
    [InlineData(0x1F600, "U+1F600", "F0 9F 98 80", @"\U0001F600")]
    public void MetadataAndClipboardContainExactScalar(int code, string notation, string utf8, string source)
    {
        var model = new CharacterMapModel(); model.Select(code);
        Assert.Equal(notation, model.Notation); Assert.Equal(utf8, model.Utf8); Assert.Equal(source, model.Source);
        var clipboard = new InMemoryClipboardService();
        foreach (int kind in new[] { 0, 1, 2 })
        {
            Assert.True(model.Copy(kind, clipboard, out _));
            Assert.Equal(kind == 0 ? new Rune(code).ToString() : kind == 1 ? notation : source, clipboard.GetText());
        }
        Assert.False(model.Copy(0, new NullClipboardService(), out string error));
        Assert.Contains("unavailable", error);
    }
    [Theory]
    [InlineData(0)] [InlineData(0x7F)] [InlineData(0x85)] [InlineData(0x301)]
    [InlineData(0xFE0F)] [InlineData(0x200B)] [InlineData(0x4E00)] [InlineData(0x1F600)]
    [InlineData(0xD800)] [InlineData(0xDFFF)] [InlineData(0x231A)]
    [InlineData(0x26BD)] [InlineData(0x2E80)] [InlineData(0x3164)] [InlineData(0xFFA0)]
    public void UnsafeCellsUsePlaceholderWithoutCopyingIt(int code)
    {
        var model = new CharacterMapModel(); model.Select(code);
        Assert.Equal('.', CharacterMapModel.Glyph(code));
        var clipboard = new InMemoryClipboardService(); clipboard.SetText("preserve");
        if (Rune.IsValid(code))
        {
            Assert.True(model.Copy(0, clipboard, out _));
            Assert.Equal(new Rune(code).ToString(), clipboard.GetText());
        }
        else
        {
            Assert.False(model.Copy(0, clipboard, out _));
            Assert.Equal("preserve", clipboard.GetText());
            Assert.Null(model.Character); Assert.Contains("surrogate", model.Utf8);
        }
    }
    [Theory]
    [InlineData(0x251C)] [InlineData(0x2801)] [InlineData(0xA641)] [InlineData(0x1F00)]
    public void SafeBmpCharactersUseActualGlyph(int code) => Assert.Equal((char)code, CharacterMapModel.Glyph(code));

    [Fact]
    public void VirtualGridNavigationVisibilityAndBoundaries()
    {
        var model = new CharacterMapModel(); model.Resize(8); model.Select(0);
        model.Move(-16); Assert.Equal(0, model.Selected);
        model.Move(16); Assert.Equal(16, model.Selected);
        model.Move(15); Assert.Equal(31, model.Selected);
        model.Move(16 * model.Rows); Assert.Equal(159, model.Selected);
        Visible(); model.Resize(1); Visible(); model.Resize(100); Visible();
        model.Select(0xD7FF); model.Move(1); Assert.Equal(0xE000, model.Selected);
        model.Move(-1); Assert.Equal(0xD7FF, model.Selected);
        model.Select(0x10FFFF); model.Move(16); Assert.Equal(0x10FFFF, model.Selected); Visible();
        foreach (var range in CharacterMapModel.Ranges)
        {
            model.Select(range.First); Visible(); Assert.InRange(model.Selected, range.First, range.Last);
        }
        void Visible() => Assert.InRange(model.Selected / 16, model.TopRow, model.TopRow + model.Rows - 1);
    }
    [Fact]
    public void ActualDiagnosticsMnemonicDispatchCreatesModelessWindowAndClosesCleanly()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var diagnostics = TVDemoMenuNavigationTests.Items(app.MenuBar!.Menu!).Single(i => i.Name == "D~i~agnostics").SubMenu;
        var items = TVDemoMenuNavigationTests.Items(diagnostics).ToArray();
        Assert.Equal(items.Length, items.Select(i => i.HotChar()).Distinct().Count());
        Assert.Equal('C', Assert.Single(items, i => i.Command == ShowcaseCmd.CharacterMap).HotChar());
        app.Step(() => ComboInput.Send(ComboInput.Typed("c")));
        var altI = ComboInput.Key(Keys.kbAltI); app.HandleEvent(ref altI);
        TEvent command = default; app.GetEvent(ref command);
        Assert.Equal(ShowcaseCmd.CharacterMap, command.message.command);
        app.HandleEvent(ref command); Assert.Equal(Events.evNothing, command.What);
        var window = Assert.Single(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<CharacterMapWindow>());
        Assert.False(window.GetState(Views.sfModal));
        Assert.True(window.size.x <= 80 && window.size.y <= 23);
        app.Redraw();
        var view = window.View;
        view.Model.Select(0x251C); view.DrawView();
        var origin = view.MakeGlobal(new TPoint(0, 0));
        var screen = RootScreenCopy.CopyRoot();
        int row = view.Model.Selected / 16 - view.Model.TopRow + 1;
        int col = 8 + (view.Model.Selected % 16) * view.CellWidth;
        Assert.Equal('\u251C', screen[origin.x + col, origin.y + row].Character);
        Assert.NotEqual(screen[origin.x + col, origin.y + row].Attr, screen[origin.x + col - view.CellWidth, origin.y + row].Attr);
        Assert.Equal("002510", new string(Enumerable.Range(0, 6).Select(x => screen[origin.x + x, origin.y + row].Character).ToArray()));
        for (int x = 0; x < 16; x++) Assert.Equal(x.ToString("X")[0], screen[origin.x + 8 + x * view.CellWidth, origin.y].Character);
        var previous = ComboInput.Typed("["); app.HandleEvent(ref previous);
        var next = ComboInput.Typed("]"); app.HandleEvent(ref next); Assert.Equal(0x251C, view.Model.Selected);
        var end = ComboInput.Key(Keys.kbEnd); app.HandleEvent(ref end); Assert.Equal(0x10FFFF, view.Model.Selected);
        var home = ComboInput.Key(Keys.kbHome); app.HandleEvent(ref home); Assert.Equal(0, view.Model.Selected);
        var down = ComboInput.Key(Keys.kbDown); app.HandleEvent(ref down); Assert.Equal(16, view.Model.Selected);
        int before = view.Model.Selected;
        var page = ComboInput.Key(Keys.kbPgDn); app.HandleEvent(ref page); Assert.Equal(before + 16 * view.Model.Rows, view.Model.Selected);
        window.ChangeBounds(new TRect(0, 0, 60, 16)); app.Redraw();
        Assert.Equal(window.size.x - 2, view.size.x); Assert.Equal(window.size.y - 2, view.size.y);
        Assert.InRange(view.Model.Selected / 16, view.Model.TopRow, view.Model.TopRow + view.GridRows - 1);
        window.ChangeBounds(new TRect(0, 0, 78, 23)); app.Redraw();
        app.RunCommand(TVDemoCmd.cmAsciiTable);
        Assert.Single(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<AsciiTableDialog>());
        window.Select(); var close = ScriptedDemoApp.CommandEvent(Views.cmClose); app.HandleEvent(ref close);
        Assert.Null(window.owner); Assert.Null(view.owner); Assert.Null(window.frame);
    }
    [Fact]
    public void ActualGoToRangeAndCopyActions()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        app.RunCommand(ShowcaseCmd.CharacterMap);
        var view = Assert.Single(ScriptedDemoApp.Descendants(app.DeskTop!).OfType<CharacterMapView>());
        Go("U+1F600"); Assert.Equal(0x1F600, view.Model.Selected);
        Go("D800"); Assert.Equal(0x1F600, view.Model.Selected); Assert.Contains("surrogate", view.Feedback);
        app.Step(() =>
        {
            var dialog = app.Modal<TDialog>();
            Assert.Same(app.DeskTop, dialog.owner);
            Assert.NotEqual((ushort)0, dialog.GetColor(6));
            var combo = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TComboBox>());
            combo.Text = CharacterMapModel.Ranges[3].ToString();
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        var rangeKey = ComboInput.Typed("r"); app.HandleEvent(ref rangeKey);
        Assert.Equal(0x2580, view.Model.Selected);
        var original = ClipboardService.Current;
        try
        {
            var clipboard = new InMemoryClipboardService(); ClipboardService.Current = clipboard;
            view.Model.Select(0x301);
            var copy = ComboInput.Typed("c"); app.HandleEvent(ref copy); Assert.Equal("\u0301", clipboard.GetText());
            copy = ComboInput.Typed("s"); app.HandleEvent(ref copy); Assert.Equal(@"\u0301", clipboard.GetText());
            ClipboardService.Current = new NullClipboardService();
            copy = ComboInput.Typed("c"); app.HandleEvent(ref copy); Assert.Contains("unavailable", view.Feedback);
        }
        finally { ClipboardService.Current = original; }
        void Go(string value)
        {
            app.Step(() =>
            {
                var dialog = app.Modal<TDialog>();
                Assert.Same(app.DeskTop, dialog.owner);
                Assert.NotEqual((ushort)0, dialog.GetColor(6));
                Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).SetData(value);
                ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
            });
            var key = ComboInput.Typed("g"); app.HandleEvent(ref key);
        }
    }
}
