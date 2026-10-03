using System.Text;

namespace TSharpVision.Terminal;

/// <summary>The parameters of one control sequence, reused between sequences.</summary>
internal sealed class VtParameters
{
    /// <summary>More are ignored; a sequence cannot grow the parser.</summary>
    public const int MaximumCount = 32;

    /// <summary>Parameter values are capped here, as xterm caps them.</summary>
    public const int MaximumValue = 65535;

    private readonly int[] _values = new int[MaximumCount];
    private readonly bool[] _sub = new bool[MaximumCount];

    public int Count { get; private set; }

    /// <summary>The raw value; -1 when the parameter was omitted.</summary>
    public int this[int index] => index < Count ? _values[index] : -1;

    /// <summary>The value, or <paramref name="fallback"/> when omitted or 0 (the usual "default" rule for counts).</summary>
    public int CountOrDefault(int index, int fallback = 1)
    {
        int value = this[index];
        return value <= 0 ? fallback : value;
    }

    /// <summary>The value, or <paramref name="fallback"/> when omitted.</summary>
    public int ValueOrDefault(int index, int fallback = 0)
    {
        int value = this[index];
        return value < 0 ? fallback : value;
    }

    /// <summary>Whether this parameter was introduced by ':' — a sub-parameter of the one before it.</summary>
    public bool IsSubParameter(int index) => index < Count && _sub[index];

    internal void Clear()
    {
        Count = 0;
        _values[0] = -1;
        _sub[0] = false;
    }

    internal void Begin()
    {
        if (Count == 0) Count = 1;
    }

    internal void AddDigit(int digit)
    {
        Begin();
        int index = Count - 1;
        if (index >= MaximumCount) return;
        int value = _values[index] < 0 ? 0 : _values[index];
        _values[index] = Math.Min(MaximumValue, value * 10 + digit);
    }

    internal void Next(bool sub)
    {
        Begin();
        if (Count >= MaximumCount)
        {
            Count = MaximumCount + 1;   // remembered as overflow; further parameters are ignored
            return;
        }

        _values[Count] = -1;
        _sub[Count] = sub;
        Count++;
    }

    internal void Seal()
    {
        if (Count > MaximumCount) Count = MaximumCount;
    }
}

/// <summary>What the parser recognized, in order.</summary>
internal interface IVtHandler
{
    void Print(int codePoint);

    void Execute(int control);

    /// <summary>An escape sequence: ESC [intermediate] final.</summary>
    void EscapeDispatch(char intermediate, char final);

    /// <summary>A control sequence: CSI [private marker] parameters [intermediate] final.</summary>
    void CsiDispatch(VtParameters parameters, char privateMarker, char intermediate, char final);

    /// <summary>An operating system command, without its terminator.</summary>
    void OscDispatch(string data);
}

/// <summary>
/// The VT/xterm control-sequence state machine (after the DEC ANSI parser model) over decoded code points. It never
/// throws, holds bounded state (32 parameters, 4 KiB of OSC, nothing of DCS/SOS/PM/APC payloads), and any byte sequence
/// leaves it in a state from which the next valid sequence parses correctly. Sequences may be split at any point.
/// </summary>
internal sealed class VtParser
{
    /// <summary>Longest OSC payload kept; the rest of a longer one is discarded.</summary>
    public const int MaximumOscLength = 4096;

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        StringIgnore,
    }

    private readonly IVtHandler _handler;
    private readonly VtParameters _parameters = new();
    private readonly StringBuilder _osc = new();
    private State _state;
    private char _privateMarker;
    private char _intermediate;
    private bool _intermediateOverflow;

    public VtParser(IVtHandler handler) => _handler = handler;

    /// <summary>Whether the parser is between sequences.</summary>
    public bool IsInGround => _state == State.Ground;

    public void Reset()
    {
        _state = State.Ground;
        _osc.Clear();
        ClearSequence();
    }

    public void Advance(int c)
    {
        // Transitions from any state.
        if (c == 0x18 || c == 0x1A)   // CAN, SUB: abort the sequence
        {
            if (_state == State.OscString) _osc.Clear();
            _state = State.Ground;
            return;
        }

        if (c == 0x1B)
        {
            if (_state == State.OscString) DispatchOsc();   // ESC \ (ST) ends it; the '\' is then ignored in Escape
            _state = State.Escape;
            ClearSequence();
            return;
        }

        switch (_state)
        {
            case State.Ground:
                if (c < 0x20) _handler.Execute(c);
                else if (c == 0x7F || (c >= 0x80 && c < 0xA0)) { }   // DEL and C1 are not printed
                else _handler.Print(c);
                return;

            case State.Escape:
                if (c < 0x20) { _handler.Execute(c); return; }
                if (c <= 0x2F) { Collect(c); _state = State.EscapeIntermediate; return; }
                switch (c)
                {
                    case '[': _state = State.CsiEntry; return;
                    case ']': _state = State.OscString; _osc.Clear(); return;
                    case 'P': case 'X': case '^': case '_': _state = State.StringIgnore; return;   // DCS, SOS, PM, APC
                }

                if (c <= 0x7E) _handler.EscapeDispatch('\0', (char)c);
                if (c != 0x7F) _state = State.Ground;
                return;

            case State.EscapeIntermediate:
                if (c < 0x20) { _handler.Execute(c); return; }
                if (c <= 0x2F) { Collect(c); return; }
                if (c <= 0x7E && !_intermediateOverflow) _handler.EscapeDispatch(_intermediate, (char)c);
                if (c != 0x7F) _state = State.Ground;
                return;

            case State.CsiEntry:
            case State.CsiParam:
                if (c < 0x20) { _handler.Execute(c); return; }
                if (c >= '0' && c <= '9') { _parameters.AddDigit(c - '0'); _state = State.CsiParam; return; }
                if (c == ';' || c == ':') { _parameters.Next(sub: c == ':'); _state = State.CsiParam; return; }
                if (c >= 0x3C && c <= 0x3F)
                {
                    if (_state == State.CsiEntry) { _privateMarker = (char)c; _state = State.CsiParam; }
                    else _state = State.CsiIgnore;
                    return;
                }

                if (c <= 0x2F) { Collect(c); _state = State.CsiIntermediate; return; }
                if (c >= 0x40 && c <= 0x7E) { DispatchCsi((char)c); return; }
                if (c != 0x7F) _state = State.CsiIgnore;
                return;

            case State.CsiIntermediate:
                if (c < 0x20) { _handler.Execute(c); return; }
                if (c <= 0x2F) { Collect(c); return; }
                if (c >= 0x40 && c <= 0x7E) { DispatchCsi((char)c); return; }
                if (c != 0x7F) _state = State.CsiIgnore;
                return;

            case State.CsiIgnore:
                if (c < 0x20) { _handler.Execute(c); return; }
                if (c >= 0x40 && c <= 0x7E) _state = State.Ground;
                return;

            case State.OscString:
                if (c == 0x07) { DispatchOsc(); _state = State.Ground; return; }   // BEL terminator
                if (c < 0x20) return;                                             // other controls are ignored
                if (_osc.Length < MaximumOscLength)
                {
                    if (c > 0xFFFF) _osc.Append(char.ConvertFromUtf32(c));
                    else _osc.Append((char)c);
                }

                return;

            case State.StringIgnore:
                // Everything up to ST (ESC \, handled above) is discarded unstored. BEL also ends it, as xterm allows.
                if (c == 0x07) _state = State.Ground;
                return;
        }
    }

    private void Collect(int c)
    {
        if (_intermediate == '\0') _intermediate = (char)c;
        else _intermediateOverflow = true;
    }

    private void DispatchCsi(char final)
    {
        _parameters.Seal();
        if (!_intermediateOverflow) _handler.CsiDispatch(_parameters, _privateMarker, _intermediate, final);
        _state = State.Ground;
    }

    private void DispatchOsc()
    {
        string data = _osc.ToString();
        _osc.Clear();
        _handler.OscDispatch(data);
    }

    private void ClearSequence()
    {
        _parameters.Clear();
        _privateMarker = '\0';
        _intermediate = '\0';
        _intermediateOverflow = false;
    }
}
