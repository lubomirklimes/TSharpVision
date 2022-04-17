using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// AsciiTableRow — one entry in the ASCII table model.
// ---------------------------------------------------------------------------
public sealed class AsciiTableRow
{
    public int Code { get; }
    public char Display { get; }       // safe printable representation
    public string Name { get; }        // short label (mnemonic or hex code)
    public bool IsPrintable { get; }

    public AsciiTableRow(int code)
    {
        Code = code;
        IsPrintable = code >= 0x20 && code <= 0x7E;
        Display = IsPrintable ? (char)code : ' ';
        Name = GetName(code);
    }

    // Returns a short human-readable name for the character.
    private static string GetName(int c) => c switch
    {
        0x00 => "NUL", 0x01 => "SOH", 0x02 => "STX", 0x03 => "ETX",
        0x04 => "EOT", 0x05 => "ENQ", 0x06 => "ACK", 0x07 => "BEL",
        0x08 => "BS",  0x09 => "HT",  0x0A => "LF",  0x0B => "VT",
        0x0C => "FF",  0x0D => "CR",  0x0E => "SO",  0x0F => "SI",
        0x10 => "DLE", 0x11 => "DC1", 0x12 => "DC2", 0x13 => "DC3",
        0x14 => "DC4", 0x15 => "NAK", 0x16 => "SYN", 0x17 => "ETB",
        0x18 => "CAN", 0x19 => "EM",  0x1A => "SUB", 0x1B => "ESC",
        0x1C => "FS",  0x1D => "GS",  0x1E => "RS",  0x1F => "US",
        0x7F => "DEL",
        0x20 => "SP",
        _ => $"0x{c:X2}"
    };

    // Format one row for display: "  32  0x20  SP   ' '"
    public string Format() =>
        $"  {Code,3}  0x{Code:X2}  {Name,-4}  {(IsPrintable ? $"'{Display}'" : "   ")}";
}

// ---------------------------------------------------------------------------
// AsciiTableModel — generates rows for codes 0x00..0x7F.
// Separated so it can be tested without UI.
// ---------------------------------------------------------------------------
public static class AsciiTableModel
{
    // Total number of rows (0x00..0x7F inclusive).
    public const int RowCount = 128;

    // Returns the row for the given index (0-based, maps to char code).
    public static AsciiTableRow GetRow(int index)
    {
        if (index < 0 || index >= RowCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new AsciiTableRow(index);
    }

    // Returns all rows.
    public static AsciiTableRow[] AllRows()
    {
        var rows = new AsciiTableRow[RowCount];
        for (int i = 0; i < RowCount; i++)
            rows[i] = GetRow(i);
        return rows;
    }
}

// ---------------------------------------------------------------------------
// AsciiTableBody — custom TView that draws all 128 ASCII entries in a
// fixed 8-column × 16-row grid.  No scrollbar required.
//
// Layout: 8 columns, each column showing 16 consecutive codes.
//   Col 0: 0..15   Col 1: 16..31  Col 2: 32..47  Col 3: 48..63
//   Col 4: 64..79  Col 5: 80..95  Col 6: 96..111 Col 7: 112..127
//
// Each cell: "DDD Xxx " (ColWidth = 8 chars)
//   DDD = 3-digit decimal, Xxx = 3-char label, trailing space.
//
// Fits in body area 64 wide × 16 tall (inside AsciiTableDialog frame).
// ---------------------------------------------------------------------------
public sealed class AsciiTableBody : TView
{
    internal const int Cols       = 8;
    internal const int RowsPerCol = 16;    // 128 / 8 = 16
    internal const int ColWidth   = 8;     // "DDD Xxx " = 7 chars + 1 trailing space

    public AsciiTableBody(TRect bounds) : base(bounds) { }

    // Returns a 3-character display label for the given char code.
    // Used both for rendering and smoke-test verification.
    public static string GetCellLabel(int code)
    {
        if (code == 0x20)
            return "SP ";                          // space abbreviation
        if (code >= 0x21 && code <= 0x7E)
            return $"{(char)code}  ";              // printable char + 2 spaces
        if (code == 0x7F)
            return "DEL";
        // Control chars 0x00..0x1F: use the 3-char mnemonic from the model.
        string name = AsciiTableModel.GetRow(code).Name;
        return (name + "   ")[..3];
    }

    public override void Draw()
    {
        // GetColor(1) routes through TDialog's 32-entry palette and up to
        // TApplication, which resolves it to a visible dialog-text color.
        var color = (char)GetColor(1);
        for (int row = 0; row < RowsPerCol; row++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            for (int col = 0; col < Cols; col++)
            {
                int code = col * RowsPerCol + row;
                if (code >= AsciiTableModel.RowCount) break;
                // "DDD Xxx " — always ColWidth chars wide.
                string cell = $"{code,3} {GetCellLabel(code),-3} ";
                b.moveStr(col * ColWidth, cell, color);
            }
            WriteLine(0, row, size.x, 1, b);
        }
    }
}

// ---------------------------------------------------------------------------
// AsciiTableDialog — TDialog hosting the AsciiTableBody.
// Fixed size, no resize/grow/zoom icons.  Opened modeless by TVDemoApp.
// ---------------------------------------------------------------------------
public sealed class AsciiTableDialog : TDialog
{
    // Body inner width = Cols × ColWidth = 8×8 = 64; dialog = 64+2 frame = 66.
    public const int DlgW = AsciiTableBody.Cols * AsciiTableBody.ColWidth + 2; // 66
    // Body height = RowsPerCol = 16; dialog = 16+2 frame = 18.
    public const int DlgH = AsciiTableBody.RowsPerCol + 2;                     // 18

    public AsciiTableDialog(int x = 5, int y = 2)
        : base(new TRect(x, y, x + DlgW, y + DlgH), "ASCII Table")
    {
        // TDialog ctor already sets: flags = wfMove|wfClose, growMode = 0.
        // No wfGrow, no wfZoom — dialog is fixed-size.
        Insert(new AsciiTableBody(new TRect(1, 1, DlgW - 1, DlgH - 1)));
    }
}
