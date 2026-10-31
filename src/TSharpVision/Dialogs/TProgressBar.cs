using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// One-row gauge that shows how much of a range of work is done, or, when the total is unknown, that work is going on.
/// </summary>
/// <remarks>
/// <para>
/// Determinate mode fills the bar in proportion to <c>(Value - Minimum) / (Maximum - Minimum)</c>, rounded down to whole
/// cells, so the bar is full only when <see cref="Value"/> reaches <see cref="Maximum"/>. The arithmetic is exact for the
/// whole <see cref="long"/> range. An empty range (<see cref="Minimum"/> equal to <see cref="Maximum"/>) has no work left
/// and is drawn complete (100 %). With <see cref="ShowPercentage"/> the rounded-down percentage is drawn right-aligned
/// after the bar in a five-cell column, when the view is wide enough to keep at least one bar cell.
/// </para>
/// <para>
/// Indeterminate mode (<see cref="IsIndeterminate"/>) draws a short block that moves one cell along the track for every
/// <see cref="Advance"/> call and shows no percentage. The control has no timer and starts no thread: the block moves only
/// when the application calls <see cref="Advance"/>, so it stops as soon as the application stops reporting activity.
/// </para>
/// <para>
/// Like every view, a progress bar belongs to the event-loop thread. Code running on a worker must not touch it; it hands
/// its progress to the UI thread with <see cref="TView.Post"/> to a view that owns the bar (typically the dialog), whose
/// <see cref="TView.HandleEvent"/> then sets <see cref="Value"/> or calls <see cref="Advance"/>. Setting a value that does
/// not change what is drawn does not redraw, so frequent updates are cheap.
/// </para>
/// <para>
/// The bar uses the dialog palette: the normal label role for the track and the highlighted label role for the filled
/// part and the activity block, the static text role for the percentage, and
/// the disabled cluster role when the view is disabled. It is not selectable and handles no input. It draws only its first
/// row; the rows below, if any, are filled with the track's background.
/// </para>
/// </remarks>
public class TProgressBar : TView
{
    /// <summary>Type identifier of this view class.</summary>
    public new static readonly string Name = "TProgressBar";

    /// <summary>Glyph of a filled cell and of the indeterminate activity block.</summary>
    public const char FilledChar = '█';   // █ FULL BLOCK

    /// <summary>Glyph of an unfilled track cell.</summary>
    public const char EmptyChar = TSharpVisionGlyphs.BackgroundFillLight;   // ░ LIGHT SHADE

    /// <summary>Cells reserved after the bar for the percentage: a separating space and up to "100%".</summary>
    public const int PercentageWidth = 5;

    /// <summary>Longest indeterminate activity block, in cells.</summary>
    public const int ActivityLength = 3;

    // Dialog roles: normal label (track), highlighted label (filled), static text (percentage), disabled cluster.
    // Scrollbar page/controls share an attribute in Color mode; text/emphasis roles keep this gauge distinct.
    private static readonly TPalette _palette = new TPalette("\x07\x09\x06\x1F", 4);

    private long _minimum;
    private long _maximum;
    private long _value;
    private bool _indeterminate;
    private bool _showPercentage;
    private long _activityStep;

    /// <summary>Creates a determinate progress bar from 0 to 100 at 0, without a percentage.</summary>
    /// <param name="bounds">Owner-relative cell bounds; the bar uses the first row.</param>
    public TProgressBar(TRect bounds) : this(bounds, 0, 100)
    {
    }

    /// <summary>Creates a determinate progress bar for a range, at <paramref name="minimum"/>, without a percentage.</summary>
    /// <param name="bounds">Owner-relative cell bounds; the bar uses the first row.</param>
    /// <param name="minimum">Value at which no work is done.</param>
    /// <param name="maximum">Value at which all work is done; not less than <paramref name="minimum"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximum"/> is less than <paramref name="minimum"/>.</exception>
    public TProgressBar(TRect bounds, long minimum, long maximum) : base(bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        _minimum = minimum;
        _maximum = maximum;
        _value = minimum;
        growMode = Views.gfGrowHiX;
    }

    /// <summary>Value at which no work is done.</summary>
    /// <remarks>Setting a minimum above <see cref="Maximum"/> raises the maximum to it; <see cref="Value"/> is kept inside the range.</remarks>
    public long Minimum
    {
        get => _minimum;
        set => Update(value, Math.Max(value, _maximum), _value);
    }

    /// <summary>Value at which all work is done.</summary>
    /// <remarks>Setting a maximum below <see cref="Minimum"/> lowers the minimum to it; <see cref="Value"/> is kept inside the range.</remarks>
    public long Maximum
    {
        get => _maximum;
        set => Update(Math.Min(value, _minimum), value, _value);
    }

    /// <summary>Amount of work done, always between <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
    /// <remarks>A value outside the range is clamped to it. Kept, but not drawn, in indeterminate mode.</remarks>
    public long Value
    {
        get => _value;
        set => Update(_minimum, _maximum, value);
    }

    /// <summary>Whether the total is unknown: an activity block moved by <see cref="Advance"/> replaces the fill and percentage.</summary>
    public bool IsIndeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            DrawView();
        }
    }

    /// <summary>Whether the percentage is drawn after the bar in determinate mode.</summary>
    public bool ShowPercentage
    {
        get => _showPercentage;
        set
        {
            if (_showPercentage == value) return;
            _showPercentage = value;
            DrawView();
        }
    }

    /// <summary>Completed percentage, 0 to 100, rounded down; 100 for an empty range.</summary>
    public int Percent => (int)Scale(100);

    /// <summary>Number of <see cref="Advance"/> calls since construction or the last <see cref="Reset"/>.</summary>
    public long ActivityStep => _activityStep;

    /// <summary>Width of the bar itself, in cells: the view width less the percentage column when it is shown.</summary>
    public int BarWidth => HasPercentageColumn ? size.x - PercentageWidth : Math.Max(0, size.x);

    /// <summary>Cells drawn filled in determinate mode at the current width; zero in indeterminate mode.</summary>
    public int FilledCells => _indeterminate ? 0 : (int)Scale(BarWidth);

    /// <summary>Sets both ends of the range at once and keeps <see cref="Value"/> inside it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximum"/> is less than <paramref name="minimum"/>.</exception>
    public void SetRange(long minimum, long maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        Update(minimum, maximum, _value);
    }

    /// <summary>Moves the indeterminate activity block one cell; does nothing visible in determinate mode.</summary>
    public void Advance()
    {
        _activityStep = _activityStep == long.MaxValue ? 0 : _activityStep + 1;
        if (_indeterminate) DrawView();
    }

    /// <summary>Returns <see cref="Value"/> to <see cref="Minimum"/> and the activity block to its start.</summary>
    public void Reset()
    {
        bool moved = _activityStep != 0;
        _activityStep = 0;
        if (!Update(_minimum, _maximum, _minimum) && moved && _indeterminate)
            DrawView();
    }

    /// <summary>
    /// First cell and length of the indeterminate activity block at the current width: it enters at the left edge, moves
    /// right one cell per step, leaves at the right edge and starts again. The length is clipped at both edges.
    /// </summary>
    public (int Start, int Length) GetActivityCells()
    {
        int width = BarWidth;
        if (width <= 0) return (0, 0);
        int block = Math.Min(ActivityLength, width);
        int cycle = width + block - 1;
        int head = (int)(_activityStep % cycle);          // last cell of the block, from 0 to cycle - 1
        int start = Math.Max(0, head - block + 1);
        int end = Math.Min(width, head + 1);
        return (start, end - start);
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
    public override void Draw()
    {
        int width = Math.Max(0, size.x);
        Span<TScreenChar> row = stackalloc TScreenChar[width > 0 ? width : 1];
        var b = new TDrawBuffer(row);
        bool disabled = (state & Views.sfDisabled) != 0;
        ushort track = disabled ? GetColor(4) : GetColor(1);
        ushort fill = disabled ? GetColor(4) : GetColor(2);
        ushort text = disabled ? GetColor(4) : GetColor(3);

        int bar = BarWidth;
        b.moveChar(0, EmptyChar, track, bar);
        if (_indeterminate)
        {
            (int start, int length) = GetActivityCells();
            if (length > 0) b.moveChar(start, FilledChar, fill, length);
        }
        else if (FilledCells is int filled and > 0)
        {
            b.moveChar(0, FilledChar, fill, filled);
        }

        if (HasPercentageColumn)
        {
            b.moveChar(bar, ' ', text, PercentageWidth);
            if (!_indeterminate)
            {
                string percent = Percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
                b.moveStr(width - percent.Length, percent, text);
            }
        }
        WriteLine(0, 0, width, 1, b);

        if (size.y > 1)
        {
            b.moveChar(0, ' ', track, width);
            WriteLine(0, 1, width, size.y - 1, b);
        }
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfDisabled) DrawView();
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a progress bar cannot be written to a stream.</exception>
    public override void Write(Opstream os)
        => throw new NotSupportedException("TProgressBar does not support streaming.");

    /// <inheritdoc />
    public override string StreamableName() => Name;

    // ── private ──────────────────────────────────────────────────────────────

    private bool HasPercentageColumn => _showPercentage && size.x > PercentageWidth;

    // (Value - Minimum) * units / (Maximum - Minimum), rounded down, exact for every long; units for an empty range.
    private long Scale(int units)
    {
        if (units <= 0) return 0;
        Int128 total = (Int128)_maximum - _minimum;
        if (total == 0) return units;
        Int128 done = (Int128)_value - _minimum;
        return (long)(done * units / total);
    }

    // Applies a range and value, clamping the value; redraws only when what is drawn changes. Returns whether it redrew.
    private bool Update(long minimum, long maximum, long value)
    {
        value = Math.Clamp(value, minimum, maximum);
        if (minimum == _minimum && maximum == _maximum && value == _value) return false;

        int cells = FilledCells, percent = Percent;
        _minimum = minimum;
        _maximum = maximum;
        _value = value;
        if (_indeterminate || (cells == FilledCells && (!HasPercentageColumn || percent == Percent))) return false;
        DrawView();
        return true;
    }
}
