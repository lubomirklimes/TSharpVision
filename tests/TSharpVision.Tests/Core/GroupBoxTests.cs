using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class GroupBoxTests
{
    private static string Row(TView view, int y)
    {
        var at = view.MakeGlobal(new TPoint(0, y));
        var screen = RootScreenCopy.CopyRoot();
        return new string(Enumerable.Range(0, view.size.x).Select(x => screen[at.x + x, at.y].Character).ToArray());
    }

    private static TGroupBox Show(TApplication app, int width = 20, int height = 5, string title = "Disk")
    {
        var box = new TGroupBox(new TRect(2, 2, 2 + width, 2 + height), title);
        app.DeskTop!.Insert(box);
        app.Redraw();
        return box;
    }

    private static void Key(TView receiver, ushort key)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = key;
        receiver.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Theory]
    [InlineData(TGroupBoxTitleAlignment.Left, 1)]
    [InlineData(TGroupBoxTitleAlignment.Center, 7)]
    [InlineData(TGroupBoxTitleAlignment.Right, 13)]
    public void TitleAlignmentCornersAndBackground(TGroupBoxTitleAlignment alignment, int start)
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var box = Show(app);
        box.TitleAlignment = alignment;
        var top = Row(box, 0);
        Assert.Equal('┌', top[0]);
        Assert.Equal('┐', top[^1]);
        Assert.Equal(" Disk ", top.Substring(start, 6));
        Assert.Equal("│                  │", Row(box, 1));
        Assert.Equal("└──────────────────┘", Row(box, 4));
        var at = box.MakeGlobal(new TPoint(0, 0));
        var screen = RootScreenCopy.CopyRoot();
        Assert.Equal((byte)box.GetColor(1), (byte)screen[at.x, at.y].Attr);
        Assert.Equal((byte)box.GetColor(6), (byte)screen[at.x + 2, at.y + 1].Attr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Příliš žluťoučký kůň")]
    [InlineData("A😀BC😀DEFG")]
    public void EverySmallWidthAndAlignmentIsSafe(string title)
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var box = Show(app);
        foreach (var alignment in Enum.GetValues<TGroupBoxTitleAlignment>())
        foreach (int height in new[] { 0, 1, 2, 5 })
        for (int width = 0; width <= 24; width++)
        {
            box.Title = title;
            box.TitleAlignment = alignment;
            box.ChangeBounds(new TRect(2, 2, 2 + width, 2 + height));
            app.Redraw();
            Assert.True(box.ContentRect.b.x >= box.ContentRect.a.x);
            Assert.True(box.ContentRect.b.y >= box.ContentRect.a.y);
            if (width == 0 || height == 0) continue;
            string row = Row(box, 0);
            Assert.Equal('┌', row[0]);
            if (width > 1) Assert.Equal('┐', row[^1]);
            for (int i = 0; i < row.Length; i++)
            {
                if (char.IsHighSurrogate(row[i])) Assert.True(i + 1 < row.Length && char.IsLowSurrogate(row[i + 1]));
                if (char.IsLowSurrogate(row[i])) Assert.True(i > 0 && char.IsHighSurrogate(row[i - 1]));
            }
            if (title.Length == 0 && width > 1) Assert.Equal(new string('─', width - 2), row[1..^1]);
        }
    }

    private sealed class Fill(TRect bounds) : TView(bounds)
    {
        public override void Draw()
        {
            Span<TScreenChar> cells = stackalloc TScreenChar[size.x];
            cells.Fill(new TScreenChar('X', (TColorAttr)GetColor(6)));
            WriteLine(0, 0, size.x, size.y, cells);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChildClipProtectsBorderEvenOnIncrementalDrawAndRemoval(bool buffered)
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var box = Show(app, 12, 5, "");
        box.FreeBuffer();
        if (!buffered) box.options &= unchecked((ushort)~Views.ofBuffered);
        var child = new Fill(new TRect(-2, -2, 16, 8));
        box.Insert(child);
        app.Redraw();
        child.DrawView();
        Assert.Equal(new TPoint(-2, -2), child.origin);
        Assert.Equal("┌──────────┐", Row(box, 0));
        Assert.Equal("│XXXXXXXXXX│", Row(box, 2));
        Assert.Equal("└──────────┘", Row(box, 4));
        Assert.Equal(new TRect(1, 1, 11, 4), box.ContentRect);
        box.Remove(child);
        Assert.Null(child.owner);
        Assert.Equal("│          │", Row(box, 2));
        box.ShowBorder = false;
        Assert.Equal(box.GetExtent(), box.ContentRect);
        box.Insert(child);
        Assert.Equal(new string('X', 12), Row(box, 0));
        box.ShowBorder = true;
        Assert.Equal("┌──────────┐", Row(box, 0));
    }

    [Fact]
    public void NestedClipParentClipAndRepeatedResizeLeaveNoOldCells()
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var outer = Show(app, 24, 10, "Outer");
        var inner = new TGroupBox(new TRect(2, 2, 20, 8), "Inner");
        inner.Insert(new Fill(new TRect(-3, -3, 30, 15)));
        outer.Insert(inner);
        foreach (int width in new[] { 18, 6, 22, 3, 18 })
        {
            inner.Locate(new TRect(2, 2, 2 + width, 8));
            Assert.Equal('┌', Row(inner, 0)[0]);
            if (width < 22) Assert.Equal('┐', Row(inner, 0)[^1]);
            else Assert.Equal('│', Row(outer, 2)[^1]); // the parent clips the oversized nested frame
            Assert.Equal('│', Row(outer, 4)[0]);
            Assert.Equal('│', Row(outer, 4)[^1]);
            if (width < 20) Assert.Equal(' ', Row(outer, 4)[2 + width]);
        }
        inner.Locate(new TRect(-3, -2, 10, 5));
        app.Redraw();
        Assert.Equal('┌', Row(outer, 0)[0]);
        inner.Locate(new TRect(50, 50, 60, 55));
        app.Redraw();
        Assert.Equal(' ', Row(outer, 4)[4]);
    }

    private static TInputLine Input(int y) => new(new TRect(2, y, 15, y + 1), 30);

    [Fact]
    public void TabVisitsLeavesReentersAndReversesNestedGroupsSkippingIneligibleViews()
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var dialog = new TDialog(new TRect(0, 0, 60, 20), "Focus");
        var before = Input(1);
        var box = new TGroupBox(new TRect(2, 3, 40, 16), "Group");
        var first = Input(1);
        var disabled = Input(2); disabled.SetState(Views.sfDisabled, true);
        var hidden = Input(3); hidden.Hide();
        var nested = new TGroupBox(new TRect(2, 4, 30, 9), "Nested");
        var n1 = Input(1); var n2 = Input(2);
        nested.Insert(n1); nested.Insert(n2);
        var last = Input(10);
        box.Insert(first); box.Insert(disabled); box.Insert(hidden);
        box.Insert(new TStaticText(new TRect(17, 1, 25, 2), "Skip"));
        box.Insert(nested); box.Insert(last);
        var after = Input(17);
        dialog.Insert(before); dialog.Insert(box); dialog.Insert(after);
        app.DeskTop!.Insert(dialog);
        before.Focus();
        Key(dialog, Keys.kbTab); Assert.Same(first, box.current); Assert.Same(box, dialog.current);
        Key(dialog, Keys.kbTab); Assert.Same(nested, box.current); Assert.Same(n1, nested.current);
        Key(dialog, Keys.kbTab); Assert.Same(n2, nested.current);
        app.Redraw(); Assert.True(n2.GetState(Views.sfFocused));
        Key(dialog, Keys.kbTab); Assert.Same(last, box.current);
        Key(dialog, Keys.kbTab); Assert.Same(after, dialog.current);
        Key(dialog, Keys.kbShiftTab); Assert.Same(last, box.current);
        Key(dialog, Keys.kbShiftTab); Assert.Same(n2, nested.current);
        Key(dialog, Keys.kbShiftTab); Assert.Same(n1, nested.current);
        Key(dialog, Keys.kbShiftTab); Assert.Same(first, box.current);
        Key(dialog, Keys.kbShiftTab); Assert.Same(before, dialog.current);
        box.Hide(); before.Focus(); Key(dialog, Keys.kbTab); Assert.Same(after, dialog.current);
        box.Show(); box.SetState(Views.sfDisabled, true);
        before.Focus(); Key(dialog, Keys.kbTab); Assert.Same(after, dialog.current);
        dialog.ShutDown();
        Assert.Null(first.owner); Assert.Null(n1.owner); Assert.Null(nested.owner); Assert.Null(box.owner);
        Assert.Null(box.last); Assert.Null(nested.last);
    }

    private sealed class MouseProbe(TRect bounds) : TView(bounds)
    {
        public int Clicks;
        public override void HandleEvent(ref TEvent ev)
        {
            base.HandleEvent(ref ev);
            if (ev.What == Events.evMouseDown) { Clicks++; ClearEvent(ref ev); }
        }
    }

    [Fact]
    public void MouseSelectsChildThroughOwnerChainAndBorderDoesNotDispatchToChild()
    {
        using var scope = new DriverScope();
        using var app = new TApplication();
        var box = Show(app);
        var probe = new MouseProbe(new TRect(0, 0, 18, 4));
        probe.options |= Views.ofSelectable | Views.ofFirstClick;
        box.Insert(probe);
        var other = new TGroupBox(new TRect(30, 2, 50, 8), "Other");
        other.Insert(Input(1)); app.DeskTop!.Insert(other); other.Focus();
        var click = new TEvent { What = Events.evMouseDown };
        click.mouse.where = box.MakeGlobal(new TPoint(3, 2));
        app.DeskTop.HandleEvent(ref click);
        Assert.Same(box, app.DeskTop.current);
        Assert.True(probe.GetState(Views.sfFocused)); Assert.Equal(1, probe.Clicks);
        click.What = Events.evMouseDown; click.mouse.where = box.MakeGlobal(new TPoint(0, 0));
        app.DeskTop.HandleEvent(ref click); Assert.Equal(1, probe.Clicks);
    }

    [Theory]
    [InlineData(80, 25)]
    [InlineData(140, 40)]
    public void DemoFitsScreenAndInheritsDialogAndBlueWindowPalettes(int width, int height)
    {
        using var scope = new DriverScope((ushort)width, (ushort)height);
        using var app = new TApplication();
        int left = (app.DeskTop!.size.x - 64) / 2;
        int top = (app.DeskTop.size.y - 19) / 2;
        var dialog = TVDemoApp.CreateGroupBoxDemoDialog(new TRect(left, top, left + 64, top + 19));
        app.DeskTop!.Insert(dialog); app.Redraw();
        var box = (TGroupBox)dialog.FirstThat((v, _) => v is TGroupBox b && b.Title == "Disk to Compress:", (object?)null)!;
        Assert.Contains("Disk to Compress:", Row(box, 0));
        Assert.Equal(dialog.MapColor(6), box.MapColor(6));
        Key(dialog, Keys.kbTab); Assert.IsType<TCheckBoxes>(box.current);
        var blue = new TWindow(new TRect(2, 1, 66, 20), "Blue", Views.wnNoNumber);
        dialog.Remove(box); blue.Insert(box); app.DeskTop.Insert(blue); app.Redraw();
        Assert.Equal(blue.MapColor(6), box.MapColor(6));
        Assert.Contains("Disk to Compress:", Row(box, 0));
    }
}
