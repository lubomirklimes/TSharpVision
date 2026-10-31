// TComboBox inside a modal TDialog run by a real program loop. Input goes through the shared queue and
// TProgram.GetEvent, as the drivers deliver it; scripted steps run whenever the queue is empty, so each step sees the
// state the input before it left — including while the dropdown's own modal loop is running.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Dialogs.ComboInput;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ComboBoxModalTests : IDisposable
{
    private readonly DriverScope _driver = new(80, 25);
    private readonly ComboApp _app;
    private readonly ComboDialog _d = new();

    public ComboBoxModalTests()
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

    private void Step(Action step) => _app.Step(step);

    // Runs the dialog to completion and checks the script ran out cleanly.
    private ushort Run(TDialog? dialog = null)
    {
        dialog ??= _d.Dialog;
        int before = ComboDialog.ChildCount(_app.DeskTop!);
        ushort result = _app.DeskTop!.ExecView(dialog);
        Assert.False(_app.TimedOut, "the script did not close every modal loop");
        Assert.Equal(0, _app.StepsLeft);
        Assert.Equal(before, ComboDialog.ChildCount(_app.DeskTop!));
        dialog.ShutDown();
        return result;
    }

    private TComboBoxDropDown Popup(TComboBox combo) => _d.Popup(combo);

    // ── keyboard ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbDown, Keys.kbLeftAlt)]
    [InlineData(Keys.kbF4, 0u)]
    public void Alt_Down_or_F4_opens_the_dropdown_below_the_control(ushort key, uint modifiers)
    {
        Step(() => Send(Key(key, modifiers)));
        Step(() =>
        {
            Assert.True(_d.Case.IsDropDownOpen);
            TComboBoxDropDown popup = Popup(_d.Case);
            Assert.Same(_app.DeskTop, popup.owner);
            TPoint combo = At(_d.Case, 0, 0), top = At(popup, 0, 0);
            Assert.Equal(combo.x, top.x);
            Assert.Equal(combo.y + 1, top.y);
            Assert.Equal(_d.Case.size.x, popup.size.x);
            Assert.Equal(3 + 2, popup.size.y);
            Assert.Equal(0, popup.List.focused);
            Assert.Null(popup.ScrollBar);
            Send(Key(Keys.kbEsc));
        });
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal(-1, _d.Case.SelectedIndex);
    }

    [Fact]
    public void Enter_commits_the_highlight_without_pressing_the_default_button()
    {
        var events = new ComboEvents(_d.Case);
        Step(() => Send(AltDown, Key(Keys.kbDown), Key(Keys.kbDown)));
        Step(() =>
        {
            Assert.Equal(2, Popup(_d.Case).List.focused);
            Assert.Empty(events.Log);   // moving the highlight is not a selection change
            Send(Key(Keys.kbEnter));
        });
        Step(() =>
        {
            // Still in the dialog: the Enter went to the dropdown, not to the OK button.
            Assert.False(_d.Case.IsDropDownOpen);
            Assert.Equal("Upper", _d.Case.Text);
            Assert.Same(_d.Case, _d.Dialog.current);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal(new[] { "sel:2" }, events.Log);
    }

    [Theory]
    [InlineData(' ', Keys.kbNoKey, 0u)]
    [InlineData('\0', Keys.kbF4, 0u)]
    [InlineData('\0', Keys.kbUp, Keys.kbLeftAlt)]
    public void Space_F4_and_Alt_Up_also_commit(char typed, ushort key, uint modifiers)
    {
        Step(() => Send(AltDown, Key(Keys.kbDown), typed != '\0' ? Typed(typed.ToString()) : Key(key, modifiers)));
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal(1, _d.Case.SelectedIndex);
    }

    [Fact]
    public void The_first_Esc_closes_only_the_dropdown_and_the_second_cancels_the_dialog()
    {
        _d.Case.SelectedIndex = 0;
        var events = new ComboEvents(_d.Case);
        Step(() => Send(AltDown, Key(Keys.kbDown), Key(Keys.kbEsc)));
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Assert.Equal(0, _d.Case.SelectedIndex);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Empty(events.Log);
    }

    [Fact]
    public void Enter_with_the_dropdown_closed_presses_the_default_button()
    {
        Step(() => Send(Key(Keys.kbDown), Key(Keys.kbEnter)));

        Assert.Equal(Views.cmOK, Run());
        Assert.Equal("Keep", _d.Case.Text);
    }

    [Fact]
    public void Home_End_and_paging_move_the_highlight_through_a_long_list()
    {
        var d = new ComboDialog(caseItems: Enumerable.Range(0, 50).Select(i => $"Item {i:00}"));
        Step(() => Send(AltDown));
        Step(() =>
        {
            TComboBoxDropDown popup = d.Popup(d.Case);
            Assert.Equal(8 + 2, popup.size.y);
            Assert.NotNull(popup.ScrollBar);
            Send(Key(Keys.kbEnd));
        });
        Step(() =>
        {
            TComboBoxList list = d.Popup(d.Case).List;
            Assert.Equal(49, list.focused);
            Assert.Equal(42, list.topItem);
            Assert.Equal(49, d.Popup(d.Case).ScrollBar!.value);
            Send(Key(Keys.kbHome), Key(Keys.kbPgDn));
        });
        Step(() =>
        {
            Assert.Equal(8, d.Popup(d.Case).List.focused);
            Send(Typed("i"), Key(Keys.kbEnter));   // letter search goes on from the highlight
        });
        Step(() => Send(Key(Keys.kbEsc)));

        Assert.Equal(Views.cmCancel, Run(d.Dialog));
        Assert.Equal(9, d.Case.SelectedIndex);
    }

    [Theory]
    [InlineData(Keys.kbTab)]
    [InlineData(Keys.kbShiftTab)]
    public void Tab_commits_the_highlight_and_moves_the_focus_like_a_closed_combo_box(ushort tab)
    {
        // Where the key moves the focus with the dropdown closed...
        TView? expected = null;
        Step(() => Send(Key(tab)));
        Step(() =>
        {
            expected = _d.Dialog.current;
            Assert.NotSame(_d.Case, expected);
            _d.Case.Select();
            Send(AltDown, Key(Keys.kbDown), Key(tab));
        });
        // ...is where it moves it from an open dropdown, after committing the highlight.
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Assert.Equal(1, _d.Case.SelectedIndex);
            Assert.Same(expected, _d.Dialog.current);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void An_empty_dropdown_shows_the_empty_marker_and_commits_nothing()
    {
        var d = new ComboDialog(caseItems: Array.Empty<string>());
        Step(() => Send(Key(Keys.kbDown), AltDown));
        Step(() =>
        {
            TComboBoxDropDown popup = d.Popup(d.Case);
            Assert.Equal(3, popup.size.y);
            TPoint row = At(popup.List, 0, 0);
            Assert.Contains("<empty>", _app.Row(row.y, row.x, popup.List.size.x));
            Send(Key(Keys.kbEnter));
        });
        Step(() =>
        {
            Assert.False(d.Case.IsDropDownOpen);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run(d.Dialog));
        Assert.Equal(-1, d.Case.SelectedIndex);
    }

    // ── editable ────────────────────────────────────────────────────────────

    [Fact]
    public void Editable_text_takes_arbitrary_and_Unicode_input()
    {
        var events = new ComboEvents(_d.Encoding);
        _d.Encoding.Select();
        Step(() =>
        {
            Type("Žluť \U0001F600 ");
            Send(Key(Keys.kbBack), Key(Keys.kbLeft), Key(Keys.kbLeft), Typed("č"));
        });
        Step(() => Send(Key(Keys.kbEnter)));

        Assert.Equal(Views.cmOK, Run());
        Assert.Equal("Žluť č\U0001F600", _d.Encoding.Text);
        Assert.Equal(-1, _d.Encoding.SelectedIndex);
        Assert.Equal(9, events.Text);   // 7 typed elements, Back and the inserted č; the arrows change nothing
        Assert.Equal(0, events.Selection);
    }

    [Fact]
    public void Typing_an_item_selects_it_and_editing_away_clears_the_selection()
    {
        var events = new ComboEvents(_d.Encoding);
        _d.Encoding.Select();
        Step(() => Type("UTF-8"));
        Step(() =>
        {
            Assert.Equal(0, _d.Encoding.SelectedIndex);
            Send(Key(Keys.kbBack), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal("UTF-", _d.Encoding.Text);
        Assert.Equal(-1, _d.Encoding.SelectedIndex);
        Assert.Equal(2, events.Selection);
    }

    [Fact]
    public void A_suggestion_replaces_the_draft_with_one_notification_each()
    {
        _d.Encoding.Select();
        Step(() => Type("x"));
        ComboEvents? events = null;
        Step(() =>
        {
            events = new ComboEvents(_d.Encoding);
            Send(Key(Keys.kbDown));   // plain Down opens an editable dropdown
        });
        Step(() =>
        {
            Assert.True(_d.Encoding.IsDropDownOpen);
            Assert.Equal("x", _d.Encoding.Text);
            Send(Key(Keys.kbDown), Key(Keys.kbEnter));
        });
        Step(() =>
        {
            Assert.Equal("UTF-16 LE", _d.Encoding.Text);
            Assert.Equal(1, _d.Encoding.SelectedIndex);
            Assert.Equal(new[] { "text:UTF-16 LE", "sel:1" }, events!.Log);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Esc_in_the_dropdown_keeps_the_draft()
    {
        _d.Encoding.Select();
        Step(() => Type("dra"));
        ComboEvents? events = null;
        Step(() =>
        {
            events = new ComboEvents(_d.Encoding);
            Send(AltDown, Key(Keys.kbDown), Key(Keys.kbDown), Key(Keys.kbEsc));
        });
        Step(() =>
        {
            Assert.False(_d.Encoding.IsDropDownOpen);
            Assert.Equal("dra", _d.Encoding.Text);
            Assert.Empty(events!.Log);
            Send(Typed("f"), Key(Keys.kbEnter));   // still editing; Enter now reaches the dialog
        });

        Assert.Equal(Views.cmOK, Run());
        Assert.Equal("draf", _d.Encoding.Text);
    }

    [Fact]
    public void The_dropdown_highlights_the_first_item_the_draft_starts()
    {
        _d.Encoding.Select();
        Step(() => Send(Typed("w"), AltDown));
        Step(() =>
        {
            Assert.Equal(2, Popup(_d.Encoding).List.focused);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Pasting_into_an_editable_combo_box_notifies_once()
    {
        using var scope = new ClipboardServiceScope();
        var clipboard = new InMemoryClipboardService();
        clipboard.SetText("Windows-1250");
        ClipboardService.Current = clipboard;
        var events = new ComboEvents(_d.Encoding);
        _d.Encoding.Select();
        Step(() => Send(Key(Keys.kbCtrlV, Keys.kbLeftCtrl), Key(Keys.kbEsc)));

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal("Windows-1250", _d.Encoding.Text);
        Assert.Equal(new[] { "text:Windows-1250", "sel:2" }, events.Log);
    }

    // ── mouse ───────────────────────────────────────────────────────────────

    [Fact]
    public void Clicking_opens_and_clicking_an_item_commits_it()
    {
        _d.Line.Select();
        Step(() => Click(At(_d.Case, 2, 0)));
        Step(() =>
        {
            Assert.True(_d.Case.IsDropDownOpen);
            Assert.Same(_d.Case, _d.Dialog.current);   // the click moved the focus
            Click(At(Popup(_d.Case).List, 1, 2));
        });
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Assert.Equal("Upper", _d.Case.Text);
            Send(Key(Keys.kbEsc));   // the mouse is released: keys reach the dialog again
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Dragging_over_the_list_highlights_and_releasing_outside_commits_nothing()
    {
        Step(() => Send(AltDown));
        Step(() =>
        {
            TComboBoxList list = Popup(_d.Case).List;
            Send(Mouse(Events.evMouseDown, At(list, 1, 0)), Mouse(Events.evMouseMove, At(list, 1, 1)));
        });
        Step(() =>
        {
            TComboBoxList list = Popup(_d.Case).List;
            Assert.Equal(1, list.focused);
            Send(Mouse(Events.evMouseUp, At(list, 1, list.size.y + 3)));
        });
        Step(() =>
        {
            Assert.True(_d.Case.IsDropDownOpen);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal(-1, _d.Case.SelectedIndex);
    }

    [Fact]
    public void A_click_outside_closes_the_dropdown_and_does_not_reach_the_view_under_it()
    {
        Step(() => Send(AltDown, Key(Keys.kbDown)));
        Step(() => Click(At(_d.Ok, 3, 0)));
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Assert.Equal(-1, _d.Case.SelectedIndex);
            Send(Key(Keys.kbEsc));   // the OK button was not pressed: the dialog is still running
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Clicking_the_text_of_an_editable_combo_box_edits_and_the_arrow_opens()
    {
        Step(() => Click(At(_d.Encoding, 1, 0)));
        Step(() =>
        {
            Assert.False(_d.Encoding.IsDropDownOpen);
            Assert.Same(_d.Encoding, _d.Dialog.current);
            Click(At(_d.Encoding, _d.Encoding.size.x - 2, 0));
        });
        Step(() =>
        {
            Assert.True(_d.Encoding.IsDropDownOpen);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void A_disabled_combo_box_ignores_clicks()
    {
        _d.Encoding.SetState(Views.sfDisabled, true);
        Step(() => Click(At(_d.Encoding, _d.Encoding.size.x - 2, 0)));
        Step(() =>
        {
            Assert.False(_d.Encoding.IsDropDownOpen);
            Assert.Same(_d.Case, _d.Dialog.current);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    // ── lifetime ────────────────────────────────────────────────────────────

    [Fact]
    public void The_dialog_ending_while_the_dropdown_is_open_dismisses_it()
    {
        Step(() => Send(AltDown, Key(Keys.kbDown)));
        TComboBoxDropDown? popup = null;
        Step(() =>
        {
            popup = Popup(_d.Case);
            _d.Dialog.Post(Views.cmCancel);   // a timer or worker closing the dialog
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.False(_d.Case.IsDropDownOpen);
        Assert.Null(popup!.owner);
        Assert.Equal(-1, _d.Case.SelectedIndex);
    }

    [Fact]
    public void Removing_the_combo_box_while_open_dismisses_the_dropdown()
    {
        Step(() => Send(AltDown));
        Step(() => _d.Dialog.Remove(_d.Case));
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
        _d.Case.ShutDown();
    }

    [Fact]
    public void Quitting_the_program_while_open_unwinds_every_loop()
    {
        Step(() => Send(AltDown));
        Step(() =>
        {
            var quit = new TEvent { What = Events.evCommand };
            quit.message.command = Views.cmQuit;
            Send(quit);
        });

        Assert.Equal(Views.cmCancel, Run());
        Assert.False(_d.Case.IsDropDownOpen);
    }

    // ── geometry ────────────────────────────────────────────────────────────

    [Fact]
    public void The_dropdown_follows_the_dialog_when_it_moves_and_keeps_the_highlight()
    {
        Step(() => Send(AltDown, Key(Keys.kbDown)));
        Step(() => _d.Dialog.MoveTo(_d.Dialog.origin.x + 5, _d.Dialog.origin.y + 2));
        Step(() =>
        {
            TComboBoxDropDown popup = Popup(_d.Case);
            Assert.Equal(At(_d.Case, 0, 1), At(popup, 0, 0));
            Assert.Equal(1, popup.List.focused);
            _d.Case.ChangeBounds(new TRect(12, 2, 40, 3));
        });
        Step(() =>
        {
            Assert.Equal(28, Popup(_d.Case).size.x);
            Send(Key(Keys.kbEnter));
        });
        Step(() => Send(Key(Keys.kbEsc)));

        Assert.Equal(Views.cmCancel, Run());
        Assert.Equal(1, _d.Case.SelectedIndex);
    }

    [Fact]
    public void Near_the_bottom_of_the_desktop_the_dropdown_opens_above()
    {
        var d = new ComboDialog(new TRect(10, 12, 60, 23));
        d.Case.ChangeBounds(new TRect(12, 8, 30, 9));   // two rows above the bottom of the desktop
        Step(() => Send(AltDown));
        Step(() =>
        {
            TComboBoxDropDown popup = d.Popup(d.Case);
            Assert.Equal(At(d.Case, 0, 0).y, At(popup, 0, popup.size.y).y);
            Assert.True(popup.origin.y >= 0);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run(d.Dialog));
    }

    [Fact]
    public void Near_the_right_edge_a_wide_dropdown_stays_on_the_desktop()
    {
        var d = new ComboDialog(new TRect(40, 3, 80, 16),
                                new[] { "short", "a rather long item that does not fit the control" });
        Step(() => Send(AltDown));
        Step(() =>
        {
            TComboBoxDropDown popup = d.Popup(d.Case);
            Assert.Equal(_app.DeskTop!.size.x, popup.origin.x + popup.size.x);
            Assert.True(popup.size.x > d.Case.size.x);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run(d.Dialog));
    }

    [Fact]
    public void A_narrow_combo_box_still_gets_a_readable_dropdown()
    {
        _d.Case.ChangeBounds(new TRect(12, 2, 16, 3));
        Step(() => Send(AltDown));
        Step(() =>
        {
            TComboBoxDropDown popup = Popup(_d.Case);
            Assert.Equal("Lower".Length + 4, popup.size.x);
            TPoint row = At(popup.List, 0, 1);
            Assert.Equal(" Lower", _app.Row(row.y, row.x, 6));
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_control_draws_its_item_and_arrow_inside_its_bounds()
    {
        _d.Case.SelectedIndex = 0;
        _d.Encoding.Text = "UTF-8";
        Step(() =>
        {
            TPoint c = At(_d.Case, 0, 0), e = At(_d.Encoding, 0, 0);
            Assert.Equal(" Keep" + new string(' ', 10) + " ▼ ", _app.Row(c.y, c.x, _d.Case.size.x));
            Assert.Equal(" UTF-8", _app.Row(e.y, e.x, 6));
            Assert.Equal('▼', _app.Cell(e.x + _d.Encoding.size.x - 2, e.y).Character);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Long_and_Unicode_items_are_clipped_without_touching_neighbours()
    {
        string longItem = "Příliš žluťoučký kůň \U0001F600 úpěl ďábelské ódy";
        var d = new ComboDialog(caseItems: new[] { longItem });
        var neighbour = new TStaticText(new TRect(30, 2, 40, 3), "NEIGHBOUR!");
        d.Dialog.Insert(neighbour);
        d.Case.SelectedIndex = 0;
        Step(() =>
        {
            TPoint c = At(d.Case, 0, 0);
            string field = _app.Row(c.y, c.x, d.Case.size.x - 3);
            Assert.Equal(" " + longItem.Substring(0, field.Length - 2) + ">", field);
            Assert.Equal("NEIGHBOUR!", _app.Row(c.y, c.x + d.Case.size.x, 10));
            Send(AltDown);
        });
        Step(() =>
        {
            TComboBoxDropDown popup = d.Popup(d.Case);
            TPoint row = At(popup.List, 0, 0);
            string shown = _app.Row(row.y, row.x, popup.List.size.x);
            Assert.StartsWith(" Příliš žluťoučký kůň \U0001F600", shown);
            Assert.False(char.IsHighSurrogate(shown[^1]));
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run(d.Dialog));
    }

    [Fact]
    public void The_open_dropdown_writes_only_inside_its_bounds_and_shadow_and_closing_restores_the_screen()
    {
        TScreenChar[]? before = null;
        Step(() =>
        {
            before = _app.Snapshot();
            Send(AltDown);
        });
        Step(() =>
        {
            TComboBoxDropDown popup = Popup(_d.Case);
            TPoint a = At(popup, 0, 0);
            var covered = new TRect(a.x, a.y, a.x + popup.size.x + TView.shadowSize.x, a.y + popup.size.y + TView.shadowSize.y);
            TScreenChar[] open = _app.Snapshot();
            int width = _app.size.x;
            for (int i = 0; i < open.Length; i++)
            {
                var p = new TPoint(i % width, i / width);
                if (!covered.Contains(p))
                    Assert.True(before![i].Character == open[i].Character, $"cell {p.x},{p.y} changed outside the dropdown");
            }
            Assert.Equal('╔', _app.Cell(a.x, a.y).Character);
            Send(Key(Keys.kbEsc));
        });
        Step(() =>
        {
            TScreenChar[] after = _app.Snapshot();
            for (int i = 0; i < after.Length; i++)
                Assert.True(before![i].Character == after[i].Character && before[i].Attr == after[i].Attr,
                            $"cell {i % _app.size.x},{i / _app.size.x} not restored");
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void A_disabled_combo_box_is_drawn_in_the_disabled_colour()
    {
        _d.Encoding.Text = "UTF-8";
        TColorAttr enabled = default;
        Step(() =>
        {
            TPoint e = At(_d.Encoding, 1, 0);
            enabled = _app.Cell(e.x, e.y).Attr;
            _d.Encoding.SetState(Views.sfDisabled, true);
        });
        Step(() =>
        {
            TPoint e = At(_d.Encoding, 1, 0);
            Assert.Equal('U', _app.Cell(e.x, e.y).Character);
            Assert.NotEqual(enabled, _app.Cell(e.x, e.y).Attr);
            Assert.Equal(_d.Case.MapColor(0x1F), (byte)_app.Cell(e.x, e.y).Attr);
            Send(Key(Keys.kbEsc));
        });

        Assert.Equal(Views.cmCancel, Run());
    }
}
