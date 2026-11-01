using TSharpVision.Constants;
using TSharpVision.Text;
namespace TSharpVision.Samples.TVDemo;

public sealed class AsciiTableRow
{
    // DOS displays graphical symbols in control-character cells.
    private const string ControlGlyphs = " ☺☻♥♦♣♠•◘○◙♂♀♪♫☼►◄↕‼¶§▬↨↑↓→←∟↔▲▼";
    public int Code { get; }
    public char Display { get; }
    public string Name => $"0x{Code:X2}";
    public bool IsPrintable => Code >= 32 && Code != 127;
    public AsciiTableRow(int code)
    {
        if ((uint)code >= 256) throw new ArgumentOutOfRangeException(nameof(code));
        Code = code;
        Display = code < 32 ? ControlGlyphs[code] : code == 127 ? '⌂'
            : LegacyTextEncodings.Cp437.DecodeByte((byte)code);
    }
    public string Format() => $"Char: {Display}  Decimal: {Code,3}  Hex: {Code:X2}";
}

public static class AsciiTableModel
{
    public const int RowCount = 256;
    public static AsciiTableRow GetRow(int index) => new(index);
    public static AsciiTableRow[] AllRows() => Enumerable.Range(0, RowCount).Select(GetRow).ToArray();
}

public sealed class AsciiTableBody : TView
{
    public const int Cols = 32, Rows = 8;
    public int SelectedCode { get; private set; }
    public AsciiTableBody(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
        eventMask |= Events.evMouseDown;
    }
    public static string GetCellLabel(int code) => AsciiTableModel.GetRow(code).Display.ToString();
    // 1 = chart surface (static text), 2 = selected cell (list "selected" role). AsciiTableDialog maps them to the
    // classic 0x70 / 0x07 in Color; BlackWhite and Monochrome resolve them to 0x70 and bright inverse 0x0F.
    private static readonly TPalette _palette = new TPalette("\x06\x1C", 2);
    public override TPalette GetPalette() => _palette;
    public override void Draw()
    {
        char normal = (char)GetColor(1), selected = (char)GetColor(2);
        for (int row = 0; row < size.y; row++)
        {
            var buffer = new TDrawBuffer();
            buffer.moveChar(0, row == Rows ? '─' : ' ', normal, size.x);
            if (row < Rows)
                for (int col = 0; col < Cols; col++)
                {
                    int code = row * Cols + col;
                    buffer.moveChar(col, AsciiTableModel.GetRow(code).Display,
                        code == SelectedCode ? selected : normal, 1);
                }
            else if (row == Rows + 2)
                buffer.moveStr(0, AsciiTableModel.GetRow(SelectedCode).Format(), normal);
            WriteLine(0, row, size.x, 1, buffer);
        }
    }
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        int selected = SelectedCode;
        if (ev.What == Events.evMouseDown)
        {
            var point = MakeLocal(ev.mouse.where);
            if (point.x < 0 || point.x >= Cols || point.y < 0 || point.y >= Rows) return;
            selected = point.y * Cols + point.x;
        }
        else if (ev.What == Events.evKeyboard)
        {
            selected = ev.keyDown.keyCode switch
            {
                Keys.kbLeft => Math.Max(0, selected - 1),
                Keys.kbRight => Math.Min(255, selected + 1),
                Keys.kbUp => Math.Max(0, selected - Cols),
                Keys.kbDown => Math.Min(255, selected + Cols),
                Keys.kbHome => 0,
                Keys.kbEnd => 255,
                _ => -1
            };
            if (selected < 0) return;
        }
        else return;
        SelectedCode = selected;
        DrawView();
        ClearEvent(ref ev);
    }
}

public sealed class AsciiTableDialog : TDialog
{
    public const int DlgW = 34, DlgH = 13;
    public AsciiTableDialog(int x = 5, int y = 2)
        : base(new TRect(x, y, x + DlgW, y + DlgH), "ASCII Chart")
    {
        Insert(new AsciiTableBody(new TRect(1, 1, DlgW - 1, DlgH - 1)));
    }
    // Classic gray chart with a black selected cell in the Color family only; BlackWhite and Monochrome resolve
    // every role through the standard dialog palette.
    public override byte MapColor(int index) =>
        !ShowcasePalette.IsClassic(this) ? base.MapColor(index) : index == 28 ? (byte)0x07 : DemoAppearance.Gray;
}
