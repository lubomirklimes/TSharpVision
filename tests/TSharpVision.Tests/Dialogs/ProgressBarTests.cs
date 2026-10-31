// TProgressBar: range model, exact large-value arithmetic, determinate and indeterminate drawing, percentage column,
// redraw economy, clipping, resizing and palette, on the NullDriver inside a dialog on a real program desktop.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Dialogs.ComboInput;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ProgressBarTests : IDisposable
{
    private const char F = TProgressBar.FilledChar;
    private const char E = TProgressBar.EmptyChar;

    private readonly DriverScope _driver = new(80, 25);
    private readonly ComboApp _app;
    private readonly TDialog _dialog = new(new TRect(5, 3, 55, 15), "Progress");

    public ProgressBarTests()
    {
        ResetQueue();
        _app = new ComboApp();
        _app.DeskTop!.Insert(_dialog);
    }

    public void Dispose()
    {
        _dialog.ShutDown();
        _app.ShutDown();
        ResetQueue();
        _driver.Dispose();
    }

    private sealed class CountingBar : TProgressBar
    {
        public CountingBar(TRect bounds) : base(bounds) { }
        public CountingBar(TRect bounds, long minimum, long maximum) : base(bounds, minimum, maximum) { }
        public int Draws;
        public override void Draw()
        {
            Draws++;
            base.Draw();
        }
    }

    private T Add<T>(T bar) where T : TView
    {
        _dialog.Insert(bar);
        return bar;
    }

    private string Row(TView view, int width, int dy = 0)
    {
        TPoint p = At(view, 0, dy);
        return _app.Row(p.y, p.x, width);
    }

    private byte Attr(TView view, int x, int y = 0)
    {
        TPoint p = At(view, x, y);
        return (byte)_app.Cell(p.x, p.y).Attr;
    }

    private static string Bar(int filled, int empty) => new string(F, filled) + new string(E, empty);

    // ── model ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_bar_is_a_determinate_0_to_100_range_at_0_without_a_percentage()
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1));
        Assert.Equal(0, bar.Minimum);
        Assert.Equal(100, bar.Maximum);
        Assert.Equal(0, bar.Value);
        Assert.False(bar.IsIndeterminate);
        Assert.False(bar.ShowPercentage);
        Assert.Equal(0, bar.Percent);
        Assert.Equal(0, bar.FilledCells);
        Assert.Equal(0, bar.ActivityStep);
        Assert.Equal(0, bar.options & Views.ofSelectable);
        bar.ShutDown();
    }

    [Fact]
    public void A_range_constructor_starts_at_its_minimum_and_rejects_an_inverted_range()
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1), -50, 50);
        Assert.Equal(-50, bar.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TProgressBar(new TRect(0, 0, 20, 1), 10, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => bar.SetRange(1, 0));
        bar.ShutDown();
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    [InlineData(long.MinValue, 0)]
    [InlineData(long.MaxValue, 100)]
    public void Values_outside_the_range_are_clamped(long value, long expected)
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1)) { Value = value };
        Assert.Equal(expected, bar.Value);
        bar.ShutDown();
    }

    [Fact]
    public void Moving_one_end_past_the_other_moves_both_and_keeps_the_value_inside()
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1)) { Value = 40 };
        bar.Minimum = 200;
        Assert.Equal((200L, 200L, 200L), (bar.Minimum, bar.Maximum, bar.Value));
        bar.Maximum = 10;
        Assert.Equal((10L, 10L, 10L), (bar.Minimum, bar.Maximum, bar.Value));
        bar.SetRange(0, 1000);
        Assert.Equal(10, bar.Value);
        bar.SetRange(500, 600);
        Assert.Equal(500, bar.Value);
        bar.ShutDown();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(5, 5, 1)]
    [InlineData(33, 33, 6)]
    [InlineData(50, 50, 10)]
    [InlineData(99, 99, 19)]
    [InlineData(100, 100, 20)]
    public void Percent_and_cells_round_down_and_are_full_only_at_the_maximum(long value, int percent, int cells)
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1)) { Value = value };
        Assert.Equal(percent, bar.Percent);
        Assert.Equal(cells, bar.FilledCells);
        bar.ShutDown();
    }

    [Fact]
    public void The_minimum_need_not_be_zero()
    {
        var bar = new TProgressBar(new TRect(0, 0, 20, 1), 1000, 2000) { Value = 1500 };
        Assert.Equal(50, bar.Percent);
        Assert.Equal(10, bar.FilledCells);
        bar.SetRange(-300, -100);
        bar.Value = -250;
        Assert.Equal(25, bar.Percent);
        bar.ShutDown();
    }

    [Fact]
    public void Large_byte_counts_are_exact_without_overflow()
    {
        const long tera = 1L << 40;
        var bar = new TProgressBar(new TRect(0, 0, 20, 1), 0, 10 * tera) { Value = 10 * tera - 1 };
        Assert.Equal(99, bar.Percent);
        Assert.Equal(19, bar.FilledCells);

        bar.SetRange(long.MinValue, long.MaxValue);
        bar.Value = long.MaxValue - 1;
        Assert.Equal(99, bar.Percent);
        Assert.Equal(19, bar.FilledCells);
        bar.Value = 0;
        Assert.Equal(50, bar.Percent);
        bar.Value = long.MinValue;
        Assert.Equal(0, bar.Percent);
        bar.Value = long.MaxValue;
        Assert.Equal(100, bar.Percent);
        Assert.Equal(20, bar.FilledCells);
        bar.ShutDown();
    }

    [Fact]
    public void An_empty_range_is_drawn_complete()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 22, 3), 7, 7) { ShowPercentage = true });
        Assert.Equal(7, bar.Value);
        Assert.Equal(100, bar.Percent);
        Assert.Equal(15, bar.FilledCells);
        Assert.Equal(Bar(15, 0) + " 100%", Row(bar, 20));
    }

    // ── determinate drawing ─────────────────────────────────────────────────

    [Fact]
    public void Zero_half_and_complete_progress_are_drawn_in_proportion()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 22, 3)));
        Assert.Equal(Bar(0, 20), Row(bar, 20));
        bar.Value = 50;
        Assert.Equal(Bar(10, 10), Row(bar, 20));
        bar.Value = 100;
        Assert.Equal(Bar(20, 0), Row(bar, 20));
        bar.Reset();
        Assert.Equal(Bar(0, 20), Row(bar, 20));
    }

    [Fact]
    public void The_percentage_is_right_aligned_in_its_own_column()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 27, 3)) { ShowPercentage = true });
        Assert.Equal(20, bar.BarWidth);
        Assert.Equal(Bar(0, 20) + "   0%", Row(bar, 25));
        bar.Value = 60;
        Assert.Equal(Bar(12, 8) + "  60%", Row(bar, 25));
        bar.Value = 100;
        Assert.Equal(Bar(20, 0) + " 100%", Row(bar, 25));
        bar.ShowPercentage = false;
        Assert.Equal(25, bar.BarWidth);
        Assert.Equal(Bar(25, 0), Row(bar, 25));
    }

    [Fact]
    public void The_palette_uses_text_emphasis_and_disabled_roles()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 27, 3)) { ShowPercentage = true, Value = 50 });
        Assert.Equal(_dialog.MapColor(9), Attr(bar, 0));    // filled
        Assert.Equal(_dialog.MapColor(7), Attr(bar, 19));   // track
        Assert.Equal(_dialog.MapColor(6), Attr(bar, 24));   // percentage
        Assert.Equal(0x7E, Attr(bar, 0));                    // default color palette, gray dialog
        Assert.Equal(0x70, Attr(bar, 24));

        bar.SetState(Views.sfDisabled, true);
        for (int x = 0; x < 25; x++)
            Assert.Equal(_dialog.MapColor(0x1F), Attr(bar, x));
        Assert.Equal(Bar(10, 10) + "  50%", Row(bar, 25));
        bar.SetState(Views.sfDisabled, false);
        Assert.Equal(_dialog.MapColor(9), Attr(bar, 0));
    }

    [Theory]
    [InlineData(TProgram.AP.Color, 0x70, 0x7E, 0x38)]
    [InlineData(TProgram.AP.BlackWhite, 0x70, 0x7F, 0x07)]
    [InlineData(TProgram.AP.Monochrome, 0x70, 0x0F, 0x07)]
    public void Rendered_cells_follow_active_text_and_emphasis_roles(TProgram.AP mode, byte track, byte fill, byte disabled)
    {
        _app.AppPalette = mode;
        var bar = Add(new TProgressBar(new TRect(2, 2, 27, 3)) { ShowPercentage = true, Value = 50 });
        Check(track, fill);
        bar.SetState(Views.sfDisabled, true);
        for (int x = 0; x < 25; x++) Assert.Equal(disabled, Attr(bar, x));
        Assert.Equal(Bar(10, 10) + "  50%", Row(bar, 25));
        bar.SetState(Views.sfDisabled, false);
        Check(track, fill);

        // Edit existing root roles, not the control palette; restore shared tables even on assertion failure.
        var palette = _app.GetPalette();
        int trackRole = _dialog.GetPalette()[7];
        int textRole = _dialog.GetPalette()[6], emphasisRole = _dialog.GetPalette()[9];
        byte oldTrack = palette[trackRole];
        byte oldText = palette[textRole], oldEmphasis = palette[emphasisRole];
        try
        {
            palette[trackRole] = 0x17; palette[textRole] = 0x17; palette[emphasisRole] = 0x2E;
            bar.DrawView();
            Check(0x17, 0x2E);
            bar.IsIndeterminate = true;
            bar.Advance(); bar.Advance();
            Assert.Equal(F, Row(bar, 25)[0]);
            Assert.Equal(E, Row(bar, 25)[3]);
            Assert.Equal(_dialog.MapColor(9), Attr(bar, 0));
            Assert.Equal(_dialog.MapColor(7), Attr(bar, 3));
        }
        finally { palette[trackRole] = oldTrack; palette[textRole] = oldText; palette[emphasisRole] = oldEmphasis; }

        void Check(byte expectedTrack, byte expectedFill)
        {
            Assert.Equal(expectedFill, Attr(bar, 0));
            Assert.Equal(expectedTrack, Attr(bar, 19));
            Assert.Equal(expectedTrack, Attr(bar, 24));
            Assert.Equal(_dialog.MapColor(9), Attr(bar, 0));
            Assert.Equal(_dialog.MapColor(7), Attr(bar, 19));
            Assert.NotEqual(Attr(bar, 0), Attr(bar, 19));
            Assert.NotEqual(Attr(bar, 24) & 15, Attr(bar, 24) >> 4);
            Assert.Equal(Bar(10, 10) + "  50%", Row(bar, 25));
        }
    }

    [Theory]
    [InlineData(1, "░")]
    [InlineData(5, "███░░")]   // too narrow for the percentage column: bar only
    [InlineData(6, "░  60%")]                        // one bar cell is left
    public void Narrow_bars_stay_inside_their_bounds(int width, string expected)
    {
        var right = Add(new TStaticText(new TRect(2 + width, 2, 2 + width + 5, 3), "RIGHT"));
        var bar = Add(new TProgressBar(new TRect(2, 2, 2 + width, 3)) { ShowPercentage = true, Value = 60 });
        Assert.Equal(expected, Row(bar, width));
        Assert.Equal("RIGHT", Row(right, 5));
    }

    [Fact]
    public void A_taller_bar_draws_its_first_row_and_fills_the_rest_with_the_track_background()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 12, 4)) { Value = 50 });
        Assert.Equal(Bar(5, 5), Row(bar, 10));
        Assert.Equal(new string(' ', 10), Row(bar, 10, 1));
        Assert.Equal(_dialog.MapColor(7), Attr(bar, 0, 1));
    }

    // ── indeterminate ───────────────────────────────────────────────────────

    [Fact]
    public void The_activity_block_enters_moves_and_leaves_one_cell_per_advance()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 12, 3)) { IsIndeterminate = true });
        var expected = new (int, int)[] { (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3), (7, 3), (8, 2), (9, 1), (0, 1) };
        for (int step = 0; step < expected.Length; step++)
        {
            Assert.Equal(step, bar.ActivityStep);
            Assert.Equal(expected[step], bar.GetActivityCells());
            (int start, int length) = expected[step];
            Assert.Equal(new string(E, start) + new string(F, length) + new string(E, 10 - start - length), Row(bar, 10));
            bar.Advance();
        }
    }

    [Fact]
    public void Indeterminate_mode_claims_no_percentage_and_keeps_the_value()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 17, 3)) { ShowPercentage = true, Value = 40 });
        Assert.Equal(Bar(4, 6) + "  40%", Row(bar, 15));
        bar.IsIndeterminate = true;
        Assert.Equal(0, bar.FilledCells);
        Assert.Equal(F + new string(E, 9) + "     ", Row(bar, 15));
        bar.Value = 70;
        Assert.Equal(70, bar.Value);
        Assert.Equal(F + new string(E, 9) + "     ", Row(bar, 15));
        bar.IsIndeterminate = false;
        Assert.Equal(Bar(7, 3) + "  70%", Row(bar, 15));
    }

    [Fact]
    public void Without_advances_the_activity_block_does_not_move_and_nothing_is_redrawn()
    {
        var bar = Add(new CountingBar(new TRect(2, 2, 12, 3)) { IsIndeterminate = true });
        bar.Advance();
        bar.Advance();
        string before = Row(bar, 10);
        int draws = bar.Draws;

        // Unrelated input through the dialog, and value changes, leave the block where it is.
        TEvent key = Typed("a");
        _dialog.HandleEvent(ref key);
        bar.Value = 30;
        Assert.Equal(draws, bar.Draws);
        Assert.Equal(before, Row(bar, 10));

        bar.Advance();
        Assert.Equal(draws + 1, bar.Draws);
        bar.Reset();
        Assert.Equal(0, bar.ActivityStep);
        Assert.Equal(0, bar.Value);
        Assert.Equal(F + new string(E, 9), Row(bar, 10));
    }

    [Fact]
    public void Advancing_a_determinate_bar_counts_steps_without_redrawing()
    {
        var bar = Add(new CountingBar(new TRect(2, 2, 12, 3)));
        int draws = bar.Draws;
        bar.Advance();
        Assert.Equal(1, bar.ActivityStep);
        Assert.Equal(draws, bar.Draws);
    }

    // ── redraw economy ─────────────────────────────────────────────────────

    [Fact]
    public void Only_changes_that_alter_the_drawing_redraw()
    {
        var bar = Add(new CountingBar(new TRect(2, 2, 12, 3)));   // 10 cells, 10 units per cell
        int draws = bar.Draws;
        bar.Value = 0;                 // unchanged
        bar.Value = 5;                 // still 0 cells
        bar.SetRange(0, 100);          // unchanged
        bar.ShowPercentage = false;    // unchanged
        bar.IsIndeterminate = false;   // unchanged
        Assert.Equal(draws, bar.Draws);

        bar.Value = 10;                // 1 cell
        Assert.Equal(++draws, bar.Draws);
        for (long v = 11; v < 20; v++) bar.Value = v;
        Assert.Equal(draws, bar.Draws);

        bar.ShowPercentage = true;     // percentage column appears
        Assert.Equal(++draws, bar.Draws);
        bar.Value = 21;                // percentage changes, cells do not
        Assert.Equal(++draws, bar.Draws);
        bar.Value = 21;
        Assert.Equal(draws, bar.Draws);
    }

    [Fact]
    public void A_thousand_updates_redraw_at_most_once_per_visible_change()
    {
        var bar = Add(new CountingBar(new TRect(2, 2, 22, 3), 0, 1_000_000));
        int draws = bar.Draws;
        for (long v = 0; v <= 1_000_000; v += 1000) bar.Value = v;
        Assert.Equal(draws + 20, bar.Draws);
        Assert.Equal(Bar(20, 0), Row(bar, 20));
    }

    // ── dialog integration ──────────────────────────────────────────────────

    [Fact]
    public void Several_bars_in_one_dialog_are_independent()
    {
        var reading = Add(new TProgressBar(new TRect(2, 2, 22, 3)) { ShowPercentage = true });
        var writing = Add(new TProgressBar(new TRect(2, 4, 22, 5), 0, 3000) { ShowPercentage = true });
        var activity = Add(new TProgressBar(new TRect(2, 6, 22, 7)) { IsIndeterminate = true });
        reading.Value = 40;
        writing.Value = 900;
        activity.Advance();
        Assert.Equal(Bar(6, 9) + "  40%", Row(reading, 20));
        Assert.Equal(Bar(4, 11) + "  30%", Row(writing, 20));
        Assert.Equal(new string(F, 2) + new string(E, 18), Row(activity, 20));
        Assert.Equal(1, activity.ActivityStep);
        Assert.Equal(0, reading.ActivityStep);
    }

    [Fact]
    public void A_bar_takes_no_focus_and_leaves_input_to_the_dialog()
    {
        var line = Add(new TInputLine(new TRect(2, 4, 22, 5), 20));
        var bar = Add(new TProgressBar(new TRect(2, 2, 22, 3)));
        line.Select();
        TEvent tab = Key(Keys.kbTab);
        _dialog.HandleEvent(ref tab);
        Assert.NotSame(bar, _dialog.current);

        TEvent key = Typed("x");
        bar.HandleEvent(ref key);
        Assert.Equal(Events.evKeyDown, key.What);   // not consumed
    }

    [Fact]
    public void Resizing_redraws_at_the_new_width_and_leaves_no_artifacts()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 32, 3)) { ShowPercentage = true, Value = 50 });
        TPoint end = At(bar, 29, 0);
        bar.Locate(new TRect(2, 2, 17, 3));
        Assert.Equal(Bar(5, 5) + "  50%", Row(bar, 15));
        for (int x = 15; x < 30; x++)
        {
            char c = _app.Cell(At(bar, x, 0).x, end.y).Character;
            Assert.True(c != F && c != E && c != '%', $"stale cell at {x}: {c}");
        }
        bar.Locate(new TRect(2, 2, 42, 3));
        Assert.Equal(Bar(17, 18) + "  50%", Row(bar, 40));
        Assert.Equal(17, bar.FilledCells);
    }

    [Fact]
    public void The_bar_follows_its_dialog_when_the_dialog_moves()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 22, 3)) { Value = 25 });
        _dialog.MoveTo(_dialog.origin.x + 4, _dialog.origin.y + 2);
        Assert.Equal(Bar(5, 15), Row(bar, 20));
        bar.Value = 75;
        Assert.Equal(Bar(15, 5), Row(bar, 20));
    }

    [Fact]
    public void Growing_with_the_dialog_extends_the_bar()
    {
        var bar = Add(new TProgressBar(new TRect(2, 2, 22, 3)) { Value = 50 });
        Assert.Equal(Views.gfGrowHiX, bar.growMode);
        _dialog.flags |= Views.wfGrow;
        _dialog.Locate(new TRect(_dialog.origin.x, _dialog.origin.y, _dialog.origin.x + _dialog.size.x + 10, _dialog.origin.y + _dialog.size.y));
        Assert.Equal(30, bar.size.x);
        Assert.Equal(Bar(15, 15), Row(bar, 30));
    }

    [Fact]
    public void A_progress_bar_cannot_be_streamed()
    {
        var bar = new TProgressBar(new TRect(0, 0, 10, 1));
        Assert.Equal("TProgressBar", bar.StreamableName());
        Assert.Throws<NotSupportedException>(() => bar.Write(null!));
        bar.ShutDown();
    }
}
