using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Dialogs.ComboInput;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ComboBoxAuditClosureTests : IDisposable
{
    private readonly DriverScope _driver = new(80, 25);
    private readonly ComboApp _app;

    public ComboBoxAuditClosureTests()
    {
        ResetQueue();
        _app = new ComboApp();
    }

    public void Dispose()
    {
        _app.ShutDown();
        ResetQueue();
        _driver.Dispose();
    }

    private static TEvent Command(ushort command)
    {
        var e = new TEvent { What = Events.evCommand };
        e.message.command = command;
        return e;
    }

    private void Run()
    {
        _app.Run();
        Assert.False(_app.TimedOut);
        Assert.Equal(0, _app.StepsLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resize_cancels_without_commit_and_reaches_program_exactly_once(bool tracking)
    {
        var d = new ComboDialog();
        _app.DeskTop!.Insert(d.Dialog);
        d.Encoding.SelectedIndex = 0;
        d.Encoding.Select();
        var events = new ComboEvents(d.Encoding);
        TComboBoxDropDown? popup = null;
        TPoint point = default;
        int children = ComboDialog.ChildCount(_app.DeskTop);
        for (int round = 1; round <= 3; round++)
        {
            int expected = round;
            _app.Step(() => Send(AltDown));
            _app.Step(() =>
            {
                popup = d.Popup(d.Encoding);
                point = At(popup.List, 1, 2);
                if (tracking) Send(Mouse(Events.evMouseDown, point));
                else Send(Key(Keys.kbDown));
            });
            _app.Step(() => Send(Command(Views.cmScreenResized)));
            _app.Step(() => { if (tracking) Send(Mouse(Events.evMouseUp, point)); });
            _app.Step(() =>
            {
                Assert.Equal(expected, _app.ResizeCommands);
                Assert.False(d.Encoding.IsDropDownOpen);
                Assert.Null(popup!.owner);
                Assert.Equal(children, ComboDialog.ChildCount(_app.DeskTop));
                Assert.Equal(0, d.Encoding.SelectedIndex);
                Assert.Equal("UTF-8", d.Encoding.Text);
                Assert.Empty(events.Log);
                Assert.Same(d.Encoding, d.Dialog.current);
            });
        }
        // Reopen and release normally: cancellation must not leave mouse tracking stuck.
        _app.Step(() => Send(AltDown));
        _app.Step(() => Click(At(d.Popup(d.Encoding).List, 1, 2)));
        _app.Step(() =>
        {
            Assert.False(d.Encoding.IsDropDownOpen);
            Assert.Equal(2, d.Encoding.SelectedIndex);
            Assert.Equal("Windows-1250", d.Encoding.Text);
            Assert.Equal(1, events.Selection);
            Assert.Equal(1, events.Text);
            Send(Command(Views.cmQuit));
        });
        Run();
    }

    [Theory]
    [InlineData(false, "disable")]
    [InlineData(true, "disable")]
    [InlineData(true, "direct")]
    [InlineData(true, "hide")]
    [InlineData(true, "detach")]
    public void Invalid_ancestor_cancels_and_reenabled_tree_can_commit(bool nested, string change)
    {
        var dialog = new TDialog(new TRect(5, 2, 65, 21), "Ancestor");
        var box = new TGroupBox(new TRect(1, 1, 50, 14), "Outer");
        var inner = new TGroupBox(new TRect(1, 1, 45, 11), "Inner");
        var combo = new TComboBox(new TRect(2, 2, 24, 3), new[] { "One", "Two", "Three" }, editable: true);
        if (nested) { box.Insert(inner); inner.Insert(combo); }
        else { box.Insert(combo); inner.ShutDown(); }
        dialog.Insert(box);
        _app.DeskTop!.Insert(dialog);
        combo.SelectedIndex = 0;
        combo.Focus();
        var events = new ComboEvents(combo);
        TComboBoxDropDown? popup = null;
        int children = ComboDialog.ChildCount(_app.DeskTop);
        _app.Step(() => Send(AltDown, Key(Keys.kbDown)));
        _app.Step(() =>
        {
            popup = Assert.IsType<TComboBoxDropDown>(combo.DropDown);
            Assert.Equal(1, popup.List.focused);
            switch (change)
            {
                case "disable": box.SetState(Views.sfDisabled, true); break;
                case "direct": combo.SetState(Views.sfDisabled, true); break;
                case "hide": box.Hide(); break;
                case "detach": dialog.Remove(box); break;
            }
            Send(Key(Keys.kbEnter));
        });
        _app.Step(() =>
        {
            Assert.False(combo.IsDropDownOpen);
            Assert.Null(popup!.owner);
            Assert.Equal(children, ComboDialog.ChildCount(_app.DeskTop));
            Assert.Equal(0, combo.SelectedIndex);
            Assert.Equal("One", combo.Text);
            Assert.Empty(events.Log);
            box.SetState(Views.sfDisabled, false);
            combo.SetState(Views.sfDisabled, false);
            box.Show();
            if (box.owner == null) dialog.Insert(box);
            combo.Focus();
            Send(AltDown, Key(Keys.kbDown), Key(Keys.kbEnter));
        });
        _app.Step(() =>
        {
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(1, combo.SelectedIndex);
            Assert.Equal("Two", combo.Text);
            Assert.Equal(1, events.Selection);
            Assert.Equal(1, events.Text);
            Send(Command(Views.cmQuit));
        });
        Run();
    }
}
