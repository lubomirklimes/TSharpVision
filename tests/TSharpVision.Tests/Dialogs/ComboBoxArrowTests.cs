// Rendering of the TComboBox dropdown arrow: one button-colored surface in every state, black arrow when idle,
// yellow arrow when the control is focused or its dropdown is open, the disabled role when disabled.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Dialogs.ComboInput;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ComboBoxArrowTests : IDisposable
{
    // Default color palette, gray dialog: button normal (0x0A), button shortcut (0x0E), disabled cluster (0x1F).
    private const byte Normal = 0x20;      // black on green
    private const byte Highlight = 0x2E;   // yellow on green
    private const byte Disabled = 0x38;    // dark gray on cyan

    private readonly DriverScope _driver = new(80, 25);
    private readonly ComboApp _app;
    private readonly ComboDialog _d = new();

    public ComboBoxArrowTests()
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

    private ushort Run()
    {
        ushort result = _app.DeskTop!.ExecView(_d.Dialog);
        Assert.False(_app.TimedOut, "the script did not close every modal loop");
        Assert.Equal(0, _app.StepsLeft);
        _d.Dialog.ShutDown();
        return result;
    }

    // The last `width` cells of the control's row: (character, attribute byte).
    private (char Ch, byte Attr)[] Button(TComboBox combo, int width = 3)
    {
        TPoint p = At(combo, combo.size.x - width, 0);
        var cells = new (char, byte)[width];
        for (int i = 0; i < width; i++)
        {
            TScreenChar c = _app.Cell(p.x + i, p.y);
            cells[i] = (c.Character, (byte)c.Attr);
        }
        return cells;
    }

    private static void AssertButton((char Ch, byte Attr)[] cells, byte surface, byte arrow)
    {
        Assert.Equal(3, cells.Length);
        Assert.Equal((' ', surface), cells[0]);
        Assert.Equal(('▼', arrow), cells[1]);
        Assert.Equal((' ', surface), cells[2]);
        // One background across the three cells.
        Assert.Equal(cells[0].Attr & 0xF0, cells[1].Attr & 0xF0);
    }

    [Fact]
    public void The_roles_resolve_to_the_approved_colors_in_the_default_palette()
    {
        Step(() =>
        {
            Assert.Equal(Normal, _d.Case.MapColor(0x0A));
            Assert.Equal(Highlight, _d.Case.MapColor(0x0E));
            Assert.Equal(Disabled, _d.Case.MapColor(0x1F));
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void An_unfocused_arrow_is_black_on_one_green_surface_and_a_focused_one_is_yellow()
    {
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Normal, Normal);   // not focused
            AssertButton(Button(_d.Case), Normal, Highlight);    // focused
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Focus_transitions_move_the_highlight_between_arrows()
    {
        Step(() => Send(Key(Keys.kbTab)));
        Step(() =>
        {
            Assert.True(_d.Encoding.GetState(Views.sfFocused));
            AssertButton(Button(_d.Case), Normal, Normal);
            AssertButton(Button(_d.Encoding), Normal, Highlight);
            Send(Key(Keys.kbTab));   // to the plain input line: no combo box focused
        });
        Step(() =>
        {
            AssertButton(Button(_d.Case), Normal, Normal);
            AssertButton(Button(_d.Encoding), Normal, Normal);
            Send(Key(Keys.kbShiftTab));
        });
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Normal, Highlight);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void The_arrow_stays_highlighted_while_the_dropdown_is_open_and_after_it_closes()
    {
        Step(() => Send(AltDown));
        Step(() =>
        {
            Assert.True(_d.Case.IsDropDownOpen);
            // The dropdown takes the focus from the dialog, yet the arrow of the open combo box stays highlighted.
            Assert.False(_d.Case.GetState(Views.sfFocused));
            AssertButton(Button(_d.Case), Normal, Highlight);   // the popup opens below; the control row stays visible
            Send(Key(Keys.kbEsc));
        });
        Step(() =>
        {
            Assert.False(_d.Case.IsDropDownOpen);
            AssertButton(Button(_d.Encoding), Normal, Normal);
            AssertButton(Button(_d.Case), Normal, Highlight);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void An_editable_combo_box_opened_by_a_click_on_its_arrow_highlights_it()
    {
        Step(() => Click(At(_d.Encoding, _d.Encoding.size.x - 2, 0)));
        Step(() =>
        {
            Assert.True(_d.Encoding.IsDropDownOpen);
            AssertButton(Button(_d.Encoding), Normal, Highlight);
            AssertButton(Button(_d.Case), Normal, Normal);
            Send(Key(Keys.kbEsc), Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void A_disabled_arrow_uses_the_disabled_role_on_all_three_cells()
    {
        Step(() =>
        {
            _d.Encoding.SetState(Views.sfDisabled, true);
            _d.Case.SetState(Views.sfDisabled, true);   // focused while being disabled
        });
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Disabled, Disabled);
            AssertButton(Button(_d.Case), Disabled, Disabled);
            Assert.False(_d.Case.OpenDropDown());
            _d.Encoding.SetState(Views.sfDisabled, false);
        });
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Normal, Normal);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void The_field_and_the_button_are_adjacent_and_the_button_never_leaks_the_dialog_background()
    {
        Step(() =>
        {
            TPoint field = At(_d.Case, _d.Case.size.x - 4, 0);
            TScreenChar last = _app.Cell(field.x, field.y);
            Assert.Equal((byte)_d.Case.MapColor(0x14), (byte)last.Attr);   // focused input-line role up to the button
            TPoint after = At(_d.Case, _d.Case.size.x, 0);
            TPoint interior = At(_d.Dialog, _d.Dialog.size.x - 3, _d.Case.origin.y);
            Assert.Equal(_app.Cell(interior.x, interior.y).Attr, _app.Cell(after.x, after.y).Attr);   // dialog interior
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(2)]
    [InlineData(1)]
    public void A_narrow_control_draws_a_single_arrow_cell_inside_its_bounds(int width)
    {
        var narrow = new TComboBox(new TRect(40, 2, 40 + width, 3), ComboDialog.CaseItems);
        _d.Dialog.Insert(narrow);
        _d.Case.Select();
        Step(() =>
        {
            TPoint p = At(narrow, width - 1, 0);
            TScreenChar arrow = _app.Cell(p.x, p.y);
            Assert.Equal('▼', arrow.Character);
            Assert.Equal(Normal, (byte)arrow.Attr);
            TPoint after = At(narrow, width, 0);
            Assert.NotEqual('▼', _app.Cell(after.x, after.y).Character);
            narrow.Select();
        });
        Step(() =>
        {
            TPoint p = At(narrow, width - 1, 0);
            Assert.Equal(Highlight, (byte)_app.Cell(p.x, p.y).Attr);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Six_columns_is_the_narrowest_three_cell_button()
    {
        var combo = new TComboBox(new TRect(40, 2, 46, 3), ComboDialog.CaseItems);
        _d.Dialog.Insert(combo);
        _d.Case.Select();
        Step(() =>
        {
            AssertButton(Button(combo), Normal, Normal);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }

    [Fact]
    public void Resizing_and_moving_redraw_the_button_without_leaving_artifacts()
    {
        TPoint oldArrow = default;
        Step(() =>
        {
            oldArrow = At(_d.Encoding, _d.Encoding.size.x - 2, 0);
            TRect r = _d.Encoding.GetBounds();
            _d.Encoding.Locate(new TRect(r.a.x, r.a.y, r.b.x - 6, r.b.y));   // shrink
        });
        Step(() =>
        {
            Assert.NotEqual('▼', _app.Cell(oldArrow.x, oldArrow.y).Character);
            AssertButton(Button(_d.Encoding), Normal, Normal);
            TRect r = _d.Encoding.GetBounds();
            _d.Encoding.Locate(new TRect(r.a.x, r.a.y, r.b.x + 10, r.b.y));  // grow
        });
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Normal, Normal);
            _d.Dialog.MoveTo(_d.Dialog.origin.x + 3, _d.Dialog.origin.y + 1);
        });
        Step(() =>
        {
            AssertButton(Button(_d.Encoding), Normal, Normal);
            AssertButton(Button(_d.Case), Normal, Highlight);
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, Run());
    }
}
