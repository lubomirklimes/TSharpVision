using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// CalendarModel — pure date logic, no UI dependency.
// Testable without any TView or TEvent.
// ---------------------------------------------------------------------------
public sealed class CalendarModel
{
    // English month names (1-indexed, entry 0 is empty).
    public static readonly string[] MonthNames =
    {
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    // Days in each month for a non-leap year (1-indexed, entry 0 is empty).
    private static readonly int[] BaseDays = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    // Current displayed month/year.
    public int Month { get; private set; }
    public int Year  { get; private set; }

    // Today's date for highlighting.
    public int TodayDay   { get; }
    public int TodayMonth { get; }
    public int TodayYear  { get; }

    public CalendarModel()
    {
        var now = DateTime.Today;
        TodayDay   = now.Day;
        TodayMonth = now.Month;
        TodayYear  = now.Year;
        Month = TodayMonth;
        Year  = TodayYear;
    }

    // Constructor for a specific month/year (used in smoke tests).
    public CalendarModel(int month, int year)
    {
        Month = month;
        Year  = year;
        TodayDay   = 1;
        TodayMonth = month;
        TodayYear  = year;
    }

    // Returns true if the given year is a leap year.
    public static bool IsLeapYear(int year) =>
        (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);

    // Returns the number of days in the model's current month.
    public int DaysInMonth() => DaysInMonth(Month, Year);

    public static int DaysInMonth(int month, int year)
    {
        int d = BaseDays[month];
        if (month == 2 && IsLeapYear(year)) d++;
        return d;
    }

    // Returns day-of-week (0=Sunday..6=Saturday) for day 1 of the current month.
    // Uses Zeller's formula, same as upstream tvdemo calendar.cc.
    public int FirstWeekday() => FirstWeekday(Month, Year);

    public static int FirstWeekday(int month, int year)
    {
        int m = month, y = year;
        if (m < 3) { m += 10; y--; }
        else        m -= 2;
        int c  = y / 100;
        int yr = y % 100;
        int dw = ((26 * m - 2) / 10 + 1 + yr + yr / 4 + c / 4 - 2 * c) % 7;
        if (dw < 0) dw += 7;
        return dw;
    }

    // Navigate to the next month.
    public void NextMonth()
    {
        if (++Month > 12) { Month = 1; Year++; }
    }

    // Navigate to the previous month.
    public void PrevMonth()
    {
        if (--Month < 1) { Month = 12; Year--; }
    }

    // Returns a 6×7 grid of day numbers (0 = empty cell).
    // Row 0..5, column 0..6 (Sun..Sat).
    public int[,] BuildGrid()
    {
        int[,] grid = new int[6, 7];
        int first = FirstWeekday();
        int days  = DaysInMonth();
        int day   = 1;
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                int cell = row * 7 + col;
                if (cell >= first && day <= days)
                    grid[row, col] = day++;
            }
        }
        return grid;
    }
}

// ---------------------------------------------------------------------------
// CalendarView — TView that draws the calendar grid.
// Handles keyboard (+ / - / Up / Down) and mouse click for prev/next month.
// ---------------------------------------------------------------------------
internal sealed class CalendarView : TView
{
    internal CalendarModel Model { get; }

    // Width = 7 cols × 3 chars + (no trailing) = 20; Height = 8 (header+days+6 rows).
    public const int ViewW = 20;
    public const int ViewH = 8;

    // Header row shows "▲  October 2025 ▼" — but we use ASCII arrows.
    private const char ArrowUp   = '^';   // CP437 0x1E, use ASCII '^'
    private const char ArrowDown = 'v';   // CP437 0x1F, use ASCII 'v'

    public CalendarView(TRect bounds, CalendarModel model) : base(bounds)
    {
        Model = model;
        options |= Views.ofSelectable;
        eventMask |= Events.evMouseDown | Events.evMouseAuto;
    }

    public override void Draw()
    {
        var color     = (char)GetColor(1);
        var boldColor = (char)GetColor(2);

        // Row 0: "^  MonthName YYYY  v"
        var b0 = new TDrawBuffer();
        b0.moveChar(0, ' ', color, size.x);
        string header = $"{ArrowUp}  {CalendarModel.MonthNames[Model.Month],-10} {Model.Year}  {ArrowDown}";
        b0.moveStr(0, header, color);
        WriteLine(0, 0, size.x, 1, b0);

        // Row 1: "Su Mo Tu We Th Fr Sa"
        var b1 = new TDrawBuffer();
        b1.moveChar(0, ' ', color, size.x);
        b1.moveStr(0, "Su Mo Tu We Th Fr Sa", color);
        WriteLine(0, 1, size.x, 1, b1);

        // Rows 2-7: day grid.
        int[,] grid = Model.BuildGrid();
        for (int row = 0; row < 6; row++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            for (int col = 0; col < 7; col++)
            {
                int day = grid[row, col];
                if (day == 0) continue;
                // Highlight today in boldColor.
                char c = (Model.Year == Model.TodayYear &&
                          Model.Month == Model.TodayMonth &&
                          day == Model.TodayDay) ? boldColor : color;
                b.moveStr(col * 3, $"{day,2}", c);
            }
            WriteLine(0, row + 2, size.x, 1, b);
        }
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        bool changed = false;

        if (ev.What == Events.evKeyboard)
        {
            char ch = (char)ev.keyDown.charScan.charCode;
            if (ch == '+' || ev.keyDown.keyCode == Keys.kbDown)
            {
                Model.NextMonth();
                changed = true;
            }
            else if (ch == '-' || ev.keyDown.keyCode == Keys.kbUp)
            {
                Model.PrevMonth();
                changed = true;
            }
            if (changed) ClearEvent(ref ev);
        }
        else if ((ev.What == Events.evMouseDown || ev.What == Events.evMouseAuto) && owner != null)
        {
            var pt = MakeLocal(ev.mouse.where);
            if (pt.y == 0)
            {
                if (pt.x == 0) { Model.PrevMonth(); changed = true; }
                else if (pt.x == size.x - 1) { Model.NextMonth(); changed = true; }
            }
            if (changed) ClearEvent(ref ev);
        }

        if (changed) DrawView();
    }
}

// ---------------------------------------------------------------------------
// CalendarDialog — TDialog that hosts the CalendarView.
// Fixed size, no resize/grow/zoom.  Opened modeless by TVDemoApp.
// ---------------------------------------------------------------------------
public sealed class CalendarDialog : TDialog
{
    // Dialog inner width = CalendarView.ViewW + 2 (frame) = 22.
    public const int DlgW = CalendarView.ViewW + 2;   // 22
    // Dialog inner height = CalendarView.ViewH + 2 (frame) = 10.
    public const int DlgH = CalendarView.ViewH + 2;   // 10

    internal CalendarView View { get; }

    public CalendarDialog(int left = 5, int top = 3)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Calendar")
    {
        // TDialog already sets growMode=0, flags=wfMove|wfClose.
        var model = new CalendarModel();
        View = new CalendarView(new TRect(1, 1, DlgW - 1, DlgH - 1), model);
        Insert(View);
    }

    // Expose the model for smoke tests.
    public CalendarModel Model => View.Model;
}
