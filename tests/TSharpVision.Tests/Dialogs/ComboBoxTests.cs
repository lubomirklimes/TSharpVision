// TComboBox: selection and text semantics, notifications, item replacement and dropdown placement, without a
// running event loop. The modal behaviour is covered by ComboBoxModalTests.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ComboBoxTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public ComboBoxTests() => ComboInput.ResetQueue();

    public void Dispose() => _driver.Dispose();

    private static TComboBox Combo(bool editable = false, params string[] items)
        => new(new TRect(0, 0, 20, 1), items.Length == 0 ? ComboDialog.CaseItems : items, editable);

    private static TComboBox Hosted(TComboBox combo)
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(combo);
        combo.Select();
        return combo;
    }

    private static TEvent Handle(TComboBox combo, TEvent ev)
    {
        combo.HandleEvent(ref ev);
        return ev;
    }

    // ── construction and selection ──────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_new_combo_box_has_no_selection(bool editable)
    {
        var combo = Combo(editable);

        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
        Assert.Equal(string.Empty, combo.Text);
        Assert.Equal(editable, combo.IsEditable);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(TComboBox.DefaultMaxDropDownRows, combo.MaxDropDownRows);
        Assert.Equal(ComboDialog.CaseItems, combo.Items);
        Assert.Equal(0, combo.DataSize());
    }

    [Fact]
    public void The_items_are_a_copy_and_cannot_be_changed_through_the_view()
    {
        var source = new List<string> { "a", "b" };
        var combo = new TComboBox(new TRect(0, 0, 10, 1), source);
        source.Add("c");

        Assert.Equal(2, combo.Items.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)combo.Items).Add("d"));
    }

    [Fact]
    public void Null_items_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TComboBox(new TRect(0, 0, 10, 1), null!));
        Assert.Throws<ArgumentException>(() => new TComboBox(new TRect(0, 0, 10, 1), new[] { "a", null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TComboBox(new TRect(0, 0, 10, 1), new[] { "a" }, true, 0));
        Assert.Throws<ArgumentNullException>(() => Combo().SetItems(null!));
    }

    [Fact]
    public void Setting_the_index_selects_the_item_and_notifies_once()
    {
        var combo = Combo();
        var events = new ComboEvents(combo);

        combo.SelectedIndex = 2;
        combo.SelectedIndex = 2;

        Assert.Equal("Upper", combo.SelectedItem);
        Assert.Equal("Upper", combo.Text);
        Assert.Equal(1, events.Selection);
        Assert.Equal(0, events.Text);   // a non-editable combo box has no edit text to change

        combo.SelectedIndex = -1;
        Assert.Null(combo.SelectedItem);
        Assert.Equal(2, events.Selection);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(3)]
    public void An_index_outside_the_items_is_rejected(int index)
    {
        var combo = Combo();
        Assert.Throws<ArgumentOutOfRangeException>(() => combo.SelectedIndex = index);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void An_empty_combo_box_only_accepts_no_selection()
    {
        var combo = new TComboBox(new TRect(0, 0, 10, 1), Array.Empty<string>());
        combo.SelectedIndex = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => combo.SelectedIndex = 0);
        Assert.Equal(string.Empty, combo.Text);
    }

    [Fact]
    public void Non_editable_text_selects_a_matching_item_or_is_rejected()
    {
        var combo = Combo();

        combo.Text = "Lower";
        Assert.Equal(1, combo.SelectedIndex);

        Assert.Throws<ArgumentException>(() => combo.Text = "lower");
        Assert.Equal(1, combo.SelectedIndex);

        combo.Text = string.Empty;
        Assert.Equal(-1, combo.SelectedIndex);
    }

    // ── editable text ───────────────────────────────────────────────────────

    [Fact]
    public void Editable_text_may_differ_from_every_item()
    {
        var combo = Combo(editable: true, ComboDialog.EncodingItems);
        var events = new ComboEvents(combo);

        combo.Text = "CP852";

        Assert.Equal("CP852", combo.Text);
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Equal(1, events.Text);
        Assert.Equal(0, events.Selection);
    }

    [Fact]
    public void Editable_text_equal_to_an_item_selects_it()
    {
        var combo = Combo(editable: true, ComboDialog.EncodingItems);
        var events = new ComboEvents(combo);

        combo.Text = "ISO-8859-2";
        Assert.Equal(3, combo.SelectedIndex);
        Assert.Equal(new[] { "text:ISO-8859-2", "sel:3" }, events.Log);

        combo.Text = "iso-8859-2";   // ordinal: case matters
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void Selecting_an_item_replaces_the_text_with_one_notification_each()
    {
        var combo = Combo(editable: true, ComboDialog.EncodingItems);
        combo.Text = "draft";
        var events = new ComboEvents(combo);

        combo.SelectedIndex = 1;

        Assert.Equal("UTF-16 LE", combo.Text);
        Assert.Equal(new[] { "text:UTF-16 LE", "sel:1" }, events.Log);
    }

    [Fact]
    public void Clearing_the_selection_of_an_editable_combo_box_clears_the_selected_text_only()
    {
        var combo = Combo(editable: true, ComboDialog.EncodingItems);
        combo.SelectedIndex = 0;
        combo.SelectedIndex = -1;
        Assert.Equal(string.Empty, combo.Text);

        combo.Text = "custom";
        combo.SelectedIndex = -1;   // already no selection: the custom text stays
        Assert.Equal("custom", combo.Text);
    }

    [Fact]
    public void Duplicate_items_keep_the_chosen_one_while_the_text_still_matches_it()
    {
        var combo = Combo(editable: true, "A", "B", "A");
        combo.SelectedIndex = 2;
        combo.SetItems(new[] { "A", "B", "A", "C" });   // the text is matched again, first match wins
        Assert.Equal(0, combo.SelectedIndex);

        combo.SelectedIndex = 2;
        combo.Text = "B";
        combo.Text = "A";
        Assert.Equal(0, combo.SelectedIndex);
        combo.SelectedIndex = 2;
        combo.Text = "A";   // unchanged text keeps the chosen duplicate
        Assert.Equal(2, combo.SelectedIndex);
    }

    [Fact]
    public void Editable_text_is_truncated_without_splitting_a_surrogate_pair()
    {
        var combo = new TComboBox(new TRect(0, 0, 20, 1), Array.Empty<string>(), editable: true, maxLength: 4);

        combo.Text = "ab\U0001F600cd";   // a, b, high, low, c, d: the pair would be cut after its high half

        Assert.Equal("ab\U0001F600", combo.Text);
        combo.Text = "abc\U0001F600";
        Assert.Equal("abc", combo.Text);
    }

    [Theory]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("ab\U0001F600", 3, "ab")]
    [InlineData("ab\U0001F600", 4, "ab\U0001F600")]
    [InlineData("abc", 0, "")]
    public void Clipping_never_splits_a_surrogate_pair(string text, int units, string expected)
        => Assert.Equal(expected, TComboBox.Clip(text, units));

    // ── replacing items ─────────────────────────────────────────────────────

    [Fact]
    public void Replacing_items_keeps_the_selected_item_when_it_is_still_offered()
    {
        var combo = Combo();
        combo.SelectedIndex = 1;   // Lower
        var events = new ComboEvents(combo);

        combo.SetItems(new[] { "Upper", "Lower" });
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal(0, events.Selection);

        combo.SetItems(new[] { "Lower", "Upper" });
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal(1, events.Selection);

        combo.SetItems(new[] { "Title" });
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Equal(2, events.Selection);

        combo.SetItems(Array.Empty<string>());
        Assert.Empty(combo.Items);
        Assert.Equal(2, events.Selection);
    }

    [Fact]
    public void Replacing_items_of_an_editable_combo_box_keeps_the_text()
    {
        var combo = Combo(editable: true, ComboDialog.EncodingItems);
        combo.Text = "CP852";
        var events = new ComboEvents(combo);

        combo.SetItems(new[] { "CP437", "CP852" });

        Assert.Equal("CP852", combo.Text);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal(new[] { "sel:1" }, events.Log);
    }

    // ── keys of a closed combo box ──────────────────────────────────────────

    [Fact]
    public void Arrow_and_letter_keys_change_a_closed_non_editable_selection()
    {
        var combo = Hosted(Combo());
        var events = new ComboEvents(combo);

        foreach (TEvent key in new[]
                 {
                     ComboInput.Key(Keys.kbDown), ComboInput.Key(Keys.kbDown), ComboInput.Key(Keys.kbDown),
                     ComboInput.Key(Keys.kbDown), ComboInput.Key(Keys.kbUp), ComboInput.Key(Keys.kbHome),
                     ComboInput.Key(Keys.kbEnd), ComboInput.Typed("l"), ComboInput.Typed("K"),
                     ComboInput.Key(Keys.kbPgDn), ComboInput.Key(Keys.kbPgUp),
                 })
            Assert.Equal(Events.evNothing, Handle(combo, key).What);

        // Down past the end and the repeated letter search change nothing and say nothing.
        Assert.Equal(new[] { "sel:0", "sel:1", "sel:2", "sel:1", "sel:0", "sel:2", "sel:1", "sel:0", "sel:2", "sel:0" },
                     events.Log);
    }

    [Fact]
    public void Enter_Esc_Tab_and_Alt_letters_are_left_to_the_dialog()
    {
        var combo = Hosted(Combo());

        foreach (TEvent key in new[]
                 {
                     ComboInput.Key(Keys.kbEnter), ComboInput.Key(Keys.kbEsc), ComboInput.Key(Keys.kbTab),
                     ComboInput.Key(Keys.kbShiftTab), ComboInput.Key(Keys.kbAltK, Keys.kbLeftAlt),
                 })
            Assert.Equal(Events.evKeyDown, Handle(combo, key).What);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void Keys_of_an_empty_combo_box_are_consumed_without_effect()
    {
        var combo = Hosted(new TComboBox(new TRect(0, 0, 10, 1), Array.Empty<string>()));
        var events = new ComboEvents(combo);

        Assert.Equal(Events.evNothing, Handle(combo, ComboInput.Key(Keys.kbDown)).What);
        Assert.Equal(Events.evNothing, Handle(combo, ComboInput.Key(Keys.kbEnd)).What);
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Empty(events.Log);
    }

    [Fact]
    public void A_disabled_combo_box_neither_opens_nor_changes()
    {
        var combo = Hosted(Combo());
        combo.SetState(Views.sfDisabled, true);

        Assert.False(combo.OpenDropDown());
        Assert.Equal(Events.evKeyDown, Handle(combo, ComboInput.AltDown).What);
        Assert.Equal(Events.evKeyDown, Handle(combo, ComboInput.Key(Keys.kbDown)).What);
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);

        combo.SelectedIndex = 1;   // code may still change it
        Assert.Equal(1, combo.SelectedIndex);
    }

    [Fact]
    public void A_detached_combo_box_does_not_open()
    {
        Assert.False(Combo().OpenDropDown());
    }

    [Fact]
    public void A_combo_box_is_not_streamable_and_stays_out_of_dialog_records()
    {
        var dialog = new TDialog(new TRect(0, 0, 40, 10), "Data");
        var line = new TInputLine(new TRect(2, 2, 20, 3), 11);
        var combo = new TComboBox(new TRect(2, 4, 20, 5), ComboDialog.EncodingItems, editable: true);
        dialog.Insert(line);
        dialog.Insert(combo);
        combo.Text = "UTF-8";
        line.SetData("abc");

        object rec = new object();
        dialog.GetData(ref rec);
        var record = Assert.IsType<TDataRecord>(rec);
        Assert.Equal(line.DataSize(), record.Size);
        dialog.SetData(record);
        Assert.Equal("UTF-8", combo.Text);

        Assert.Throws<NotSupportedException>(() => combo.Write(null!));
    }

    [Fact]
    public void The_arrow_takes_three_cells_or_one_on_a_narrow_control()
    {
        var wide = new TComboBox(new TRect(0, 0, 12, 1), ComboDialog.CaseItems);
        var narrow = new TComboBox(new TRect(0, 0, 4, 1), ComboDialog.CaseItems);

        Assert.Equal(new[] { 9, 3 }, FieldAndButtonWidths(wide));
        Assert.Equal(new[] { 3, 1 }, FieldAndButtonWidths(narrow));

        wide.ChangeBounds(new TRect(0, 0, 5, 1));
        Assert.Equal(new[] { 4, 1 }, FieldAndButtonWidths(wide));
    }

    private static int[] FieldAndButtonWidths(TComboBox combo)
    {
        var widths = new List<(int x, int w)>();
        combo.ForEachView(v => widths.Add((v.origin.x, v.size.x)));
        widths.Sort();
        return widths.Select(p => p.w).ToArray();
    }

    // ── placement ───────────────────────────────────────────────────────────

    private static readonly TRect Desk = new(0, 0, 80, 23);

    [Fact]
    public void The_dropdown_opens_below_the_control_at_least_as_wide_as_it()
    {
        TRect r = Place(new TRect(10, 5, 30, 6), Desk, items: 3, longest: 5);
        Assert.Equal(new TRect(10, 6, 30, 11), r);   // 3 rows + frame
    }

    [Fact]
    public void A_long_item_widens_the_dropdown_up_to_the_area()
    {
        Assert.Equal(new TRect(10, 6, 46, 11), Place(new TRect(10, 5, 30, 6), Desk, items: 3, longest: 32));
        Assert.Equal(new TRect(0, 6, 80, 11), Place(new TRect(10, 5, 30, 6), Desk, items: 3, longest: 200));
    }

    [Fact]
    public void Rows_are_bounded_by_the_maximum()
    {
        Assert.Equal(10, Height(Place(new TRect(10, 2, 30, 3), Desk, items: 100, longest: 5)));
        Assert.Equal(5, Height(Place(new TRect(10, 2, 30, 3), Desk, items: 100, longest: 5, maxRows: 3)));
        Assert.Equal(3, Height(Place(new TRect(10, 2, 30, 3), Desk, items: 0, longest: 0)));   // "<empty>" row
    }

    [Fact]
    public void Without_room_below_the_dropdown_opens_above()
    {
        TRect r = Place(new TRect(10, 20, 30, 21), Desk, items: 5, longest: 5);
        Assert.Equal(new TRect(10, 13, 30, 20), r);
    }

    [Fact]
    public void Without_room_on_either_side_the_larger_side_is_used_and_the_list_scrolls()
    {
        // 20 items want 10 rows; below has 7, above 6: the dropdown takes the 7 below.
        Assert.Equal(new TRect(10, 7, 30, 14), Place(new TRect(10, 6, 30, 7), new TRect(0, 0, 80, 14), items: 20, longest: 5));
        Assert.Equal(new TRect(10, 0, 30, 6), Place(new TRect(10, 6, 30, 7), new TRect(0, 0, 80, 11), items: 20, longest: 5));
    }

    [Fact]
    public void With_no_usable_side_the_dropdown_stays_inside_the_area()
    {
        var area = new TRect(0, 0, 80, 5);
        TRect r = Place(new TRect(10, 2, 30, 3), area, items: 8, longest: 5);
        Assert.True(r.a.y >= 0 && r.b.y <= 5, r.ToString());
        Assert.Equal(5, Height(r));
    }

    [Fact]
    public void Near_the_right_edge_the_dropdown_moves_left_into_the_area()
    {
        TRect r = Place(new TRect(70, 5, 80, 6), Desk, items: 3, longest: 30);
        Assert.Equal(new TRect(46, 6, 80, 11), r);
        TRect left = Place(new TRect(-5, 5, 10, 6), Desk, items: 3, longest: 5);
        Assert.Equal(0, left.a.x);
    }

    [Fact]
    public void An_area_too_small_for_a_framed_row_gives_no_dropdown()
    {
        Assert.Null(TComboBox.PlaceDropDown(new TRect(0, 0, 10, 1), new TRect(0, 0, 80, 2), 3, 5, 8));
        Assert.Null(TComboBox.PlaceDropDown(new TRect(0, 0, 10, 1), new TRect(0, 0, 2, 20), 3, 5, 8));
    }

    private static TRect Place(TRect control, TRect area, int items, int longest, int maxRows = 8)
        => Assert.IsType<TRect>(TComboBox.PlaceDropDown(control, area, items, longest, maxRows));

    private static int Height(TRect r) => r.b.y - r.a.y;
}
