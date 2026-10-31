# Progress bars

`TProgressBar` (namespace `TSharpVision`, `src/TSharpVision/Dialogs/TProgressBar.cs`) is a one-row gauge that shows how
much of a range of work is done (determinate) or that work is going on when the total is unknown (indeterminate). It
only renders progress: labels, file names, speeds, Cancel buttons and the work itself belong to the application.

```text
 Reading source-file.raw
 ████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  20%
 Writing destination-file.raw
 ██████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  15%
```

## Construction

```csharp
var bar = new TProgressBar(new TRect(2, 3, 48, 4));                      // 0..100, at 0
var bytes = new TProgressBar(new TRect(2, 5, 48, 6), 0, fileLength)      // any long range, at its minimum
{
    ShowPercentage = true,
};
var scan = new TProgressBar(new TRect(2, 7, 48, 8)) { IsIndeterminate = true };
dialog.Insert(bar);
dialog.Insert(bytes);
dialog.Insert(scan);
```

The view is one row high; taller bounds draw the bar on the first row and fill the rest with the track background.
`growMode` is `gfGrowHiX`, so a bar spanning a resizable window widens with it. The bar is not selectable and handles no
events: Tab, Enter, Esc and every other key stay with the dialog. Several bars in one view are fully independent.

## Range and value

| Member | Meaning |
| --- | --- |
| `Minimum`, `Maximum` | `long` ends of the range. Setting `Minimum` above `Maximum` raises `Maximum` to it, and vice versa. |
| `SetRange(min, max)` | Sets both at once; throws `ArgumentOutOfRangeException` when `max < min` (so does the constructor). |
| `Value` | Work done. Values outside the range are **clamped**, never rejected, so a growing source cannot throw. |
| `Percent` | `(Value - Minimum) * 100 / (Maximum - Minimum)`, rounded down, 0–100. |
| `FilledCells` | Filled cells at the current width, rounded down the same way; 0 in indeterminate mode. |
| `BarWidth` | Cells of the bar itself (the view width less the percentage column when it is shown). |
| `Reset()` | `Value = Minimum` and the activity block back to its start. |

The arithmetic is done in 128-bit integers, so it is exact and cannot overflow anywhere in the `long` range (for example
`long.MinValue..long.MaxValue`, or terabyte byte counts). No floating point is used. Rounding down means the bar is full
and reads 100 % only when `Value == Maximum`; 99.9 % reads 99 %.

**Empty range.** When `Minimum == Maximum` there is no work left, so the bar is drawn complete (all cells filled, 100 %).
This is the natural result for a zero-length file.

## Determinate drawing

The track is drawn with `░` (`TProgressBar.EmptyChar`) and the filled part, from the left, with `█`
(`TProgressBar.FilledChar`). With `ShowPercentage` the last five cells (`PercentageWidth`) hold a space and the
right-aligned percentage (`"  0%"` … `"100%"`), so the text never overlaps the bar. When the view is five cells wide or
less the percentage column is dropped and the bar uses the full width. Nothing is ever written outside the view's bounds.

## Indeterminate mode

With `IsIndeterminate` a block of up to three cells (`ActivityLength`) moves along the track: it enters at the left edge,
moves one cell for each `Advance()` call, leaves at the right edge and starts again. `GetActivityCells()` returns its
current first cell and length. The percentage column stays blank: no fraction is claimed when the total is unknown.
`Value` is kept while indeterminate and shown again when `IsIndeterminate` is cleared.

The control has **no timer and no thread**. The block moves only when the application calls `Advance()` — for each
directory scanned, each block read, or from its own `TProgram.Idle`/`GetIdleWaitTimeout` deadline if it wants a steady
animation — and stops as soon as the application stops reporting work. `Advance()` on a determinate bar only counts the
step (`ActivityStep`) and does not redraw.

## Redrawing

Setters redraw the bar (only its own row, through the normal `DrawView` path) only when something visible changes: a
different number of filled cells, a different percentage while it is shown, a mode change, or an activity step. Setting
`Value` thousands of times per second therefore costs a few redraws. Resizing (`Locate`, `ChangeBounds`, window growth)
recomputes the fill for the new width; the owner redraws the area the bar gave up. Disabling the view
(`SetState(sfDisabled, true)`) redraws it in the disabled role.

## Palette

The bar uses dialog palette roles, so it matches the dialog it is in on every driver and in the color, black-and-white
and monochrome palettes:

| Part | Dialog role | Default color palette, gray dialog |
| --- | --- | --- |
| Track `░` | `0x07` normal label | black on gray |
| Filled `█` and activity block | `0x09` highlighted label | yellow on gray |
| Percentage | `0x06` static text | black on gray |
| Everything, when disabled | `0x1F` disabled cluster | dark gray on cyan |

The track uses normal label text and the completed region uses emphasis. These roles remain distinct in the standard
color, black-and-white and monochrome palettes, unlike the scrollbar page/control roles. Colors are resolved through
the owner on each draw, so existing application palette edits and window mappings are respected.

## UI thread and updates from workers

A progress bar is a view and belongs to the event-loop thread, like every view. A worker must not set `Value`, call
`Advance()` or draw. Hand progress to the UI thread with `TView.Post`, which is safe from any thread and reaches its
target even while a modal dialog is running:

```csharp
using TSharpVision;
using TSharpVision.Constants;

sealed class ScanDialog : TDialog
{
    public const ushort cmScanned = 1001;
    private readonly TProgressBar _files;
    private readonly TProgressBar _activity;
    private long _done, _total;

    public ScanDialog() : base(new TRect(10, 5, 60, 13), "Scanning")
    {
        _files = new TProgressBar(new TRect(2, 2, 48, 3)) { ShowPercentage = true };
        _activity = new TProgressBar(new TRect(2, 4, 48, 5)) { IsIndeterminate = true };
        Insert(_files);
        Insert(_activity);
        Insert(new TButton(new TRect(19, 5, 31, 7), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));
    }

    // Called on a worker thread.
    public void Report(long done, long total)
    {
        Interlocked.Exchange(ref _done, done);
        Interlocked.Exchange(ref _total, total);
        Post(cmScanned);
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evCommand && @event.message.command == cmScanned)
        {
            long total = Interlocked.Read(ref _total);
            _files.IsIndeterminate = total <= 0;           // unknown total: activity only
            if (total > 0) _files.SetRange(0, total);
            _files.Value = Interlocked.Read(ref _done);
            _activity.Advance();
            ClearEvent(ref @event);
        }
    }
}
```

Posts to a view that has left the view tree are dropped, so a worker that outlives its dialog does no harm; still,
cancel it when the dialog closes. TVDemo's progress demo (below) uses exactly this pattern.

## TVDemo

**Demo → Pro~g~ress bars...** (`samples/TSharpVision.Samples.TVDemo/ProgressDemo.cs`) shows a determinate bar over a
simulated 250 MiB with its percentage and an indeterminate bar. **Step** adds 5 % and moves the activity block,
**Run** starts a worker that posts a tick every 60 ms (1 % each) until the bar is full or **Stop**, **Reset** returns
both bars to the start. The dialog stays responsive while it runs.

## Limitations

- Horizontal, one row, left-to-right only; no vertical bar, no custom glyphs or captions inside the bar.
- Whole-cell resolution (no partial-block glyphs), so a 40-cell bar moves in 2.5 % steps.
- `DataSize()` is zero (inherited); it cannot be written to a stream (`Write` throws `NotSupportedException`).
- Cells are single-width: the glyphs are `█` U+2588 and `░` U+2591, which every TSharpVision driver renders.
