using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// CalcState — upstream TCalcState equivalent.
// ---------------------------------------------------------------------------
public enum CalcState { First = 1, Valid, Error }

// ---------------------------------------------------------------------------
// CalculatorEngine — pure calculation logic, no UI dependency.
// Matches upstream TCalcDisplay behaviour (calcdisp.cc).
// Testable without any TView or TEvent.
// ---------------------------------------------------------------------------
public sealed class CalculatorEngine
{
    public const int DisplayLen = 25;

    // Current display string (always non-null, non-empty).
    public string Display { get; private set; } = "0";

    // Sign character: ' ' for positive, '-' for negative.
    public char Sign { get; private set; } = ' ';

    // Pending operator character: '=', '+', '-', '*', '/'.
    public char Operator { get; private set; } = '=';

    // Operand saved when an operator is pressed.
    public double Operand { get; private set; } = 0.0;

    public CalcState State { get; private set; } = CalcState.First;

    // Full signed numeric value currently shown.
    public double Value => GetDisplay() * (Sign == '-' ? -1.0 : 1.0);

    // Returns true when the display shows an error condition.
    public bool IsError => State == CalcState.Error;

    public void Clear()
    {
        State = CalcState.First;
        Display = "0";
        Sign = ' ';
        Operator = '=';
    }

    // Process a character key.  key is the ASCII char ('0'-'9', '+', '-', '*',
    // '/', '=', '.', 'C' for Clear, '\b'/8 for backspace, '%', '_' for sign).
    // Enter (13) is treated like '='.
    // Matches calcdisp.cc TCalcDisplay::calcKey.
    public void ProcessKey(char key)
    {
        key = char.ToUpper(key);

        // In error state only 'C' is accepted.
        if (State == CalcState.Error && key != 'C')
            return;

        switch (key)
        {
            case '0': case '1': case '2': case '3': case '4':
            case '5': case '6': case '7': case '8': case '9':
                CheckFirst();
                if (Display.Length < 15)
                {
                    if (Display == "0") Display = string.Empty;
                    Display += key;
                }
                break;

            case '\b':  // backspace — delete last digit
                CheckFirst();
                if (Display.Length == 1)
                    Display = "0";
                else
                    Display = Display[..^1];
                break;

            case '_':   // toggle sign
                Sign = Sign == ' ' ? '-' : ' ';
                break;

            case '.':
                CheckFirst();
                if (!Display.Contains('.'))
                    Display += '.';
                break;

            case '+': case '-': case '*': case '/':
            case '=': case '%': case '\r':
                if (State == CalcState.Valid)
                {
                    State = CalcState.First;
                    double r = Value;
                    if (key == '%')
                    {
                        r = (Operator == '+' || Operator == '-')
                            ? (Operand * r) / 100.0
                            : r / 100.0;
                    }
                    switch (Operator)
                    {
                        case '+': SetDisplay(Operand + r); break;
                        case '-': SetDisplay(Operand - r); break;
                        case '*': SetDisplay(Operand * r); break;
                        case '/':
                            if (r == 0.0) Error();
                            else SetDisplay(Operand / r);
                            break;
                    }
                }
                Operator = key == '\r' ? '=' : key;
                Operand  = Value;
                break;

            case 'C':
                Clear();
                break;
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private void CheckFirst()
    {
        if (State == CalcState.First)
        {
            State = CalcState.Valid;
            Display = "0";
            Sign = ' ';
        }
    }

    private void Error()
    {
        State = CalcState.Error;
        Display = "Error";
        Sign = ' ';
    }

    private void SetDisplay(double r)
    {
        if (r < 0.0) { Sign = '-'; r = -r; }
        else Sign = ' ';

        string s = r.ToString("G15");
        if (s.Length > DisplayLen)
            Error();
        else
            Display = s;
    }

    private double GetDisplay()
    {
        if (double.TryParse(Display, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            return v;
        return 0.0;
    }
}

// ---------------------------------------------------------------------------
// CalculatorDisplay — TView that renders the current calculator display.
// Listens for evBroadcast cmCalcButton+n from the digit/operator buttons.
// ---------------------------------------------------------------------------
internal sealed class CalculatorDisplay : TView
{
    // Command base for calculator buttons (matches upstream cmCalcButton = 200,
    // but we put it in the local sample range 400+ to avoid any future conflict).
    public const ushort CmCalcBase = 400;

    // The 20 button key characters in the upstream layout order:
    //   C  <- %  +-    7 8 9 /    4 5 6 *    1 2 3 -    0 . = +
    internal static readonly char[] ButtonKeys =
    {
        'C', '\b', '%', '_',
        '7', '8',  '9', '/',
        '4', '5',  '6', '*',
        '1', '2',  '3', '-',
        '0', '.',  '=', '+'
    };

    internal readonly CalculatorEngine Engine = new CalculatorEngine();

    private static readonly TPalette _palette = new TPalette("\x13", 1);
    public override TPalette GetPalette() => _palette;

    public CalculatorDisplay(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
        eventMask = Events.evKeyboard | Events.evBroadcast;
    }

    public override void Draw()
    {
        var b = new TDrawBuffer();
        char color = (char)GetColor(1);
        int len = Engine.Display.Length;
        // Right-align: sign + number at the right.
        int i = size.x - len - 2;
        if (i < 0) i = 0;
        b.moveChar(0, ' ', color, size.x);
        b.moveChar(i, Engine.Sign, color, 1);
        b.moveStr(i + 1, Engine.Display, color);
        WriteLine(0, 0, size.x, 1, b);
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        switch (ev.What)
        {
            case Events.evKeyboard:
            {
                char ch = ev.keyDown.charScan.charCode == 0
                    ? (char)0
                    : (char)ev.keyDown.charScan.charCode;
                // Map kbEsc → 'C' (clear), kbEnter → '='
                if (ev.keyDown.keyCode == Keys.kbEsc)  ch = 'C';
                if (ev.keyDown.keyCode == Keys.kbEnter) ch = '\r';
                if (ch != 0)
                {
                    Engine.ProcessKey(ch);
                    DrawView();
                }
                ClearEvent(ref ev);
                break;
            }
            case Events.evBroadcast:
            {
                int idx = ev.message.command - CmCalcBase;
                if (idx >= 0 && idx < ButtonKeys.Length)
                {
                    Engine.ProcessKey(ButtonKeys[idx]);
                    DrawView();
                    ClearEvent(ref ev);
                }
                break;
            }
        }
    }
}

// ---------------------------------------------------------------------------
// CalculatorDialog — TDialog that wires 20 broadcast buttons + display.
// Upstream layout: 5 rows of 4 buttons below the display.
// ---------------------------------------------------------------------------
public sealed class CalculatorDialog : TDialog
{
    // Button labels matching the upstream keyChar[20] layout.
    private static readonly string[] ButtonLabels =
    {
        "C",  "<-", "%",  "+-",
        "7",  "8",  "9",  "/",
        "4",  "5",  "6",  "*",
        "1",  "2",  "3",  "-",
        "0",  ".",  "=",  "+"
    };

    // Column widths: 4 columns of 6 chars each, offset from left by 3.
    private const int BtnW = 6;
    private const int BtnH = 2;
    private const int BtnOffX = 3;
    private const int BtnOffY = 4;   // first button row (0-based inside dialog)

    // Dialog width = BtnOffX + 4*BtnW + BtnOffX = 3+24+3 = 30
    // Dialog height = frame(1) + display(1) + blank(1) + 5*BtnH(10) + frame(1) = 16
    private const int DlgW = 30;
    private const int DlgH = 16;

    internal CalculatorDisplay Display { get; }

    public CalculatorDialog(int left = 25, int top = 4)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Calculator")
    {
        options |= Views.ofFirstClick;

        // Display at row 2, columns 3..27
        var dispRect = new TRect(BtnOffX, 2, DlgW - BtnOffX, 3);
        Display = new CalculatorDisplay(dispRect);
        Insert(Display);

        // 20 buttons: bfBroadcast so that TButton.Press() broadcasts to the
        // dialog's children — picked up by CalculatorDisplay.HandleEvent.
        for (int i = 0; i < 20; i++)
        {
            int col = i % 4;
            int row = i / 4;
            int x = BtnOffX + col * BtnW;
            int y = BtnOffY + row * BtnH;
            var btn = new TButton(
                new TRect(x, y, x + BtnW, y + BtnH),
                ButtonLabels[i],
                (ushort)(CalculatorDisplay.CmCalcBase + i),
                ButtonConstants.bfBroadcast);
            // Buttons do not receive selection focus; display handles keyboard.
            btn.options &= unchecked((ushort)~Views.ofSelectable);
            Insert(btn);
        }
    }
}
