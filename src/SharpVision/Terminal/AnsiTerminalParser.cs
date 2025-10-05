using SharpVision.Constants;
using System;
using System.Text;

namespace SharpVision;

/// <summary>
/// Incremental ANSI/VT100 escape-sequence parser for TTerminal.
/// Processes text one character at a time and maintains state across calls so
/// that sequences split over multiple Write() invocations are handled correctly.
/// All unsupported or malformed sequences are silently discarded; the parser
/// never throws on bad input.
/// </summary>
internal sealed class AnsiTerminalParser
{
    private enum State { Normal, Escape, Csi, Osc }

    private State _state = State.Normal;
    private readonly StringBuilder _csiBuf = new();
    private bool _csiPrivate;   // true when CSI started with ESC[?

    // Current SGR state
    private byte _fg = DefaultFg;
    private byte _bg = DefaultBg;
    private bool _bold;

    // Default VGA terminal: white on black
    private const byte DefaultFg = Colors.fgWhite;  // 0x0F
    private const byte DefaultBg = 0x00;             // bgBlack

    /// <summary>VGA attribute byte for the current SGR state.</summary>
    public ushort CurrentAttr => (ushort)(_fg | _bg);

    /// <summary>
    /// The last OSC title received (ESC]0;...BEL or ESC]0;...ESC\).
    /// Null until the first title sequence is received.
    /// </summary>
    public string LastWindowTitle { get; private set; }

    /// <summary>
    /// Tracks cursor visibility from ESC[?25h (true) / ESC[?25l (false).
    /// Starts as true (cursor visible by default).
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>
    /// Resets parser state and SGR attributes to defaults.
    /// Called when ANSI mode is toggled off.
    /// </summary>
    public void ResetState()
    {
        _state = State.Normal;
        _csiBuf.Clear();
        _csiPrivate = false;
        _fg = DefaultFg;
        _bg = DefaultBg;
        _bold = false;
    }

    /// <summary>
    /// Processes <paramref name="text"/> incrementally.
    /// All callbacks are optional (null is accepted).
    /// </summary>
    /// <param name="text">Input text, possibly containing ANSI escape sequences.</param>
    /// <param name="onChar">Called with each printable character and its VGA attribute.</param>
    /// <param name="onNewLine">Called on LF (\n).</param>
    /// <param name="onCarriageReturn">Called on CR (\r); does not commit the line.</param>
    /// <param name="onBackspace">Called on BS (\b).</param>
    /// <param name="onClearScreen">Called when ESC[2J is received.</param>
    /// <param name="onEraseInLine">Called for ESC[K variants: 0=to end, 1=to start, 2=whole line.</param>
    /// <param name="onCursorColumn">Called for ESC[nG; argument is 1-based column.</param>
    /// <param name="onCursorRight">Called for ESC[nC; argument is number of columns.</param>
    /// <param name="onCursorLeft">Called for ESC[nD; argument is number of columns.</param>
    /// <param name="onCursorPosition">Called for ESC[row;colH and ESC[row;colf (CUP/HVP);
    /// both arguments are 1-based; missing values default to 1.</param>
    public void Parse(
        string text,
        Action<char, ushort> onChar,
        Action onNewLine,
        Action onCarriageReturn,
        Action onBackspace,
        Action onClearScreen,
        Action<int> onEraseInLine        = null,
        Action<int> onCursorColumn       = null,
        Action<int> onCursorRight        = null,
        Action<int> onCursorLeft         = null,
        Action<int, int> onCursorPosition = null)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            switch (_state)
            {
                case State.Normal:
                    switch (c)
                    {
                        case '\x1B':
                            _state = State.Escape;
                            break;
                        case '\r':
                            onCarriageReturn?.Invoke();
                            break;
                        case '\n':
                            onNewLine?.Invoke();
                            break;
                        case '\b':
                            onBackspace?.Invoke();
                            break;
                        default:
                            if (c >= ' ')
                                onChar?.Invoke(c, CurrentAttr);
                            break;
                    }
                    break;

                case State.Escape:
                    if (c == '[')
                    {
                        _state = State.Csi;
                        _csiBuf.Clear();
                        _csiPrivate = false;
                    }
                    else if (c == ']')
                    {
                        _state = State.Osc;
                        _csiBuf.Clear();
                    }
                    else
                    {
                        // Two-character escape — not supported; discard and return to normal.
                        _state = State.Normal;
                    }
                    break;

                case State.Csi:
                    if (c == '?' && _csiBuf.Length == 0 && !_csiPrivate)
                    {
                        // ESC[? introduces a private-mode sequence.
                        _csiPrivate = true;
                    }
                    else if ((c >= '0' && c <= '9') || c == ';')
                    {
                        _csiBuf.Append(c);
                    }
                    else
                    {
                        string paramStr = _csiBuf.ToString();
                        bool wasPrivate = _csiPrivate;
                        _state = State.Normal;
                        _csiBuf.Clear();
                        _csiPrivate = false;
                        DispatchCsi(c, paramStr, wasPrivate,
                            onClearScreen, onEraseInLine,
                            onCursorColumn, onCursorRight, onCursorLeft,
                            onCursorPosition);
                    }
                    break;

                case State.Osc:
                    if (c == '\x07')
                    {
                        // BEL terminates the OSC sequence.
                        DispatchOsc(_csiBuf.ToString());
                        _csiBuf.Clear();
                        _state = State.Normal;
                    }
                    else if (c == '\x1B')
                    {
                        // ESC inside OSC: could be start of ST (ESC \).
                        // Peek at next char; if it's '\', consume both and end OSC.
                        // Otherwise absorb the ESC into the OSC payload (rare).
                        if (i + 1 < text.Length && text[i + 1] == '\\')
                        {
                            i++; // consume the '\'
                            DispatchOsc(_csiBuf.ToString());
                            _csiBuf.Clear();
                            _state = State.Normal;
                        }
                        // else: stay in OSC — malformed ST will eventually hit BEL or end of input
                    }
                    else
                    {
                        _csiBuf.Append(c);
                    }
                    break;
            }

            i++;
        }
    }

    private void DispatchCsi(
        char command, string paramStr, bool isPrivate,
        Action onClearScreen,
        Action<int> onEraseInLine,
        Action<int> onCursorColumn,
        Action<int> onCursorRight,
        Action<int> onCursorLeft,
        Action<int, int> onCursorPosition)
    {
        if (isPrivate)
        {
            // Private-mode sequences: ESC[?Pmc
            switch (command)
            {
                case 'h':
                    if (paramStr == "25") CursorVisible = true;
                    break;
                case 'l':
                    if (paramStr == "25") CursorVisible = false;
                    break;
                // All other private-mode sequences are silently consumed.
            }
            return;
        }

        switch (command)
        {
            case 'm':
                ApplySgr(paramStr);
                break;

            case 'J':
                // ESC[2J = clear entire screen; other variants are ignored.
                if (paramStr == "2")
                    onClearScreen?.Invoke();
                break;

            case 'K':
                // EL — Erase in Line: 0=cursor to end, 1=start to cursor, 2=whole line.
                onEraseInLine?.Invoke(ParseParam(paramStr, 0));
                break;

            case 'G':
                // CHA — Cursor Horizontal Absolute (1-based column; 0 treated as 1).
                onCursorColumn?.Invoke(Math.Max(1, ParseParam(paramStr, 1)));
                break;

            case 'C':
                // CUF — Cursor Forward (right).
                onCursorRight?.Invoke(Math.Max(1, ParseParam(paramStr, 1)));
                break;

            case 'D':
                // CUB — Cursor Back (left).
                onCursorLeft?.Invoke(Math.Max(1, ParseParam(paramStr, 1)));
                break;

            case 'H':
            case 'f':
            {
                // CUP / HVP — Cursor Position: ESC[row;colH (both 1-based; default 1).
                int semi = paramStr.IndexOf(';');
                int row, col;
                if (semi < 0)
                {
                    row = ParseParam(paramStr, 1);
                    col = 1;
                }
                else
                {
                    row = ParseParam(paramStr[..semi], 1);
                    col = ParseParam(paramStr[(semi + 1)..], 1);
                }
                row = Math.Max(1, row);
                col = Math.Max(1, col);
                onCursorPosition?.Invoke(row, col);
                break;
            }

            // All other final bytes are silently consumed.
        }
    }

    private static int ParseParam(string paramStr, int fallback)
    {
        if (string.IsNullOrEmpty(paramStr)) return fallback;
        return int.TryParse(paramStr,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int v)
               ? v : fallback;
    }

    private void DispatchOsc(string payload)
    {
        // ESC]0;title → window/icon title (most common).
        // Other OSC commands are also consumed safely without display.
        int semi = payload.IndexOf(';');
        if (semi >= 0)
        {
            string cmd = payload[..semi];
            if (cmd == "0" || cmd == "2")
                LastWindowTitle = payload[(semi + 1)..];
        }
    }

    private void ApplySgr(string paramStr)
    {
        if (string.IsNullOrEmpty(paramStr))
        {
            // ESC[m = reset
            ResetSgr();
            return;
        }

        foreach (string part in paramStr.Split(';'))
        {
            if (int.TryParse(part, System.Globalization.NumberStyles.None,
                             System.Globalization.CultureInfo.InvariantCulture, out int code))
                ApplySgrCode(code);
        }
    }

    private void ApplySgrCode(int code)
    {
        switch (code)
        {
            case 0:
                ResetSgr();
                break;

            case 1:
                _bold = true;
                if (_fg < 8) _fg = (byte)(_fg + 8);
                break;

            case 22:
                _bold = false;
                if (_fg >= 8) _fg = (byte)(_fg - 8);
                break;

            // Standard foreground (30–37)
            case 30: case 31: case 32: case 33:
            case 34: case 35: case 36: case 37:
                _fg = AnsiStdFgToVga[code - 30];
                if (_bold && _fg < 8) _fg = (byte)(_fg + 8);
                break;

            case 39:
                _fg = DefaultFg;
                break;

            // Standard background (40–47)
            case 40: case 41: case 42: case 43:
            case 44: case 45: case 46: case 47:
                _bg = AnsiStdBgToVga[code - 40];
                break;

            case 49:
                _bg = DefaultBg;
                break;

            // Bright foreground (90–97)
            case 90: case 91: case 92: case 93:
            case 94: case 95: case 96: case 97:
                _fg = AnsiBrightFgToVga[code - 90];
                break;

            // Bright background (100–107) — VGA text mode has no bright background;
            // each bright color is mapped to the nearest standard VGA background color.
            case 100: case 101: case 102: case 103:
            case 104: case 105: case 106: case 107:
                _bg = AnsiStdBgToVga[code - 100];
                break;

            // All other codes are silently ignored.
        }
    }

    private void ResetSgr()
    {
        _fg = DefaultFg;
        _bg = DefaultBg;
        _bold = false;
    }

    // ── Color mapping tables ──────────────────────────────────────────────────
    //
    // ANSI 30–37 maps to VGA foreground nibble values by the standard ANSI→VGA
    // ordering: black(0), red(4), green(2), brown(6), blue(1), magenta(5),
    // cyan(3), light-gray(7).

    private static readonly byte[] AnsiStdFgToVga =
    {
        Colors.fgBlack,        // 30
        Colors.fgRed,          // 31
        Colors.fgGreen,        // 32
        Colors.fgBrown,        // 33
        Colors.fgBlue,         // 34
        Colors.fgMagenta,      // 35
        Colors.fgCyan,         // 36
        Colors.fgLightGray,    // 37
    };

    // ANSI 90–97: bright foreground variants.
    private static readonly byte[] AnsiBrightFgToVga =
    {
        Colors.fgDarkGray,     // 90
        Colors.fgLightRed,     // 91
        Colors.fgLightGreen,   // 92
        Colors.fgYellow,       // 93
        Colors.fgLightBlue,    // 94
        Colors.fgLightMagenta, // 95
        Colors.fgLightCyan,    // 96
        Colors.fgWhite,        // 97
    };

    // ANSI 40–47: standard background. VGA background nibble values are
    // pre-shifted into the high nibble of the attribute byte.
    private static readonly byte[] AnsiStdBgToVga =
    {
        Colors.bgBlack,        // 40 / 100
        Colors.bgRed,          // 41 / 101
        Colors.bgGreen,        // 42 / 102
        Colors.bgBrown,        // 43 / 103
        Colors.bgBlue,         // 44 / 104
        Colors.bgMagenta,      // 45 / 105
        Colors.bgCyan,         // 46 / 106
        Colors.bgLightGray,    // 47 / 107
    };
}
