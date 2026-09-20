// Kitty keyboard protocol specification:
// https://sw.kovidgoyal.net/kitty/keyboard-protocol/
using System.Globalization;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Drivers.Terminal;

internal enum KittyNegotiationState
{
    Disabled,
    AwaitingSupport,
    AwaitingConfirmation,
    Active,
    Unsupported,
}

/// <summary>Negotiates Kitty progressive keyboard enhancements without timing guesses.</summary>
internal sealed class KittyKeyboardNegotiator
{
    internal const int RequestedFlags = 1 | 2 | 4 | 8 | 16;
    internal const string QuerySupport = "\x1b[?u\x1b[c";
    internal const string PushAndConfirm = "\x1b[>31u\x1b[?u";
    internal const string PopMode = "\x1b[<u";

    internal KittyNegotiationState State { get; private set; }
    internal bool ModePushed { get; private set; }
    internal int ActiveFlags { get; private set; }

    internal KeyboardCapabilities Capabilities
    {
        get
        {
            KeyboardCapabilities capabilities = KeyboardCapabilities.None;
            // Event types alone omit releases for text-producing keys. The public
            // release capability is truthful only when all keys are encoded too.
            if ((ActiveFlags & (2 | 8)) == (2 | 8))
                capabilities |= KeyboardCapabilities.KeyReleaseEvents;
            if ((ActiveFlags & (2 | 8)) == (2 | 8))
                capabilities |= KeyboardCapabilities.StandaloneModifierTransitions;
            return capabilities;
        }
    }

    internal string Begin()
    {
        State = KittyNegotiationState.AwaitingSupport;
        ModePushed = false;
        ActiveFlags = 0;
        return QuerySupport;
    }

    internal string? End()
    {
        string? restore = ModePushed ? PopMode : null;
        State = KittyNegotiationState.Disabled;
        ModePushed = false;
        ActiveFlags = 0;
        return restore;
    }

    internal int TryConsume(ReadOnlySpan<byte> buffer, out bool complete, out string? output)
    {
        complete = true;
        output = null;
        if (buffer.Length < 3 || buffer[0] != 0x1B || buffer[1] != '[' || buffer[2] != '?')
            return 0;

        int finalIndex = 3;
        while (finalIndex < buffer.Length && (buffer[finalIndex] < 0x40 || buffer[finalIndex] > 0x7E))
            finalIndex++;
        if (finalIndex >= buffer.Length)
        {
            if (buffer.Length > 128) return 1;
            complete = false;
            return 0;
        }

        byte final = buffer[finalIndex];
        if (final is not ((byte)'u') and not ((byte)'c'))
            return 0;

        int consumed = finalIndex + 1;
        if (final == (byte)'c')
        {
            if (State == KittyNegotiationState.AwaitingSupport)
                State = KittyNegotiationState.Unsupported;
            return consumed;
        }

        if (!TryParseUnsigned(buffer.Slice(3, finalIndex - 3), out int flags))
            return consumed;

        if (State == KittyNegotiationState.AwaitingSupport)
        {
            State = KittyNegotiationState.AwaitingConfirmation;
            ModePushed = true;
            output = PushAndConfirm;
        }
        else if (State == KittyNegotiationState.AwaitingConfirmation)
        {
            ActiveFlags = flags & RequestedFlags;
            State = KittyNegotiationState.Active;
        }
        return consumed;
    }

    private static bool TryParseUnsigned(ReadOnlySpan<byte> value, out int result)
    {
        result = 0;
        if (value.IsEmpty) return false;
        foreach (byte b in value)
        {
            if (b < '0' || b > '9') return false;
            try { result = checked(result * 10 + b - '0'); }
            catch (OverflowException) { return false; }
        }
        return true;
    }
}

/// <summary>Stateful Kitty key decoder with stable release identity and logical modifier aggregation.</summary>
internal sealed class KittyKeyboardDecoder
{
    private const int LeftShift = 57441;
    private const int LeftControl = 57442;
    private const int LeftAlt = 57443;
    private const int RightShift = 57447;
    private const int RightControl = 57448;
    private const int RightAlt = 57449;

    private static readonly ushort[] ControlLetters =
    [
        Keys.kbCtrlA, Keys.kbCtrlB, Keys.kbCtrlC, Keys.kbCtrlD, Keys.kbCtrlE, Keys.kbCtrlF,
        Keys.kbCtrlG, Keys.kbCtrlH, Keys.kbCtrlI, Keys.kbCtrlJ, Keys.kbCtrlK, Keys.kbCtrlL,
        Keys.kbCtrlM, Keys.kbCtrlN, Keys.kbCtrlO, Keys.kbCtrlP, Keys.kbCtrlQ, Keys.kbCtrlR,
        Keys.kbCtrlS, Keys.kbCtrlT, Keys.kbCtrlU, Keys.kbCtrlV, Keys.kbCtrlW, Keys.kbCtrlX,
        Keys.kbCtrlY, Keys.kbCtrlZ
    ];

    private static readonly ushort[] AltLetters =
    [
        Keys.kbAltA, Keys.kbAltB, Keys.kbAltC, Keys.kbAltD, Keys.kbAltE, Keys.kbAltF,
        Keys.kbAltG, Keys.kbAltH, Keys.kbAltI, Keys.kbAltJ, Keys.kbAltK, Keys.kbAltL,
        Keys.kbAltM, Keys.kbAltN, Keys.kbAltO, Keys.kbAltP, Keys.kbAltQ, Keys.kbAltR,
        Keys.kbAltS, Keys.kbAltT, Keys.kbAltU, Keys.kbAltV, Keys.kbAltW, Keys.kbAltX,
        Keys.kbAltY, Keys.kbAltZ
    ];

    private static readonly ushort[] AltDigits =
        [Keys.kbAlt0, Keys.kbAlt1, Keys.kbAlt2, Keys.kbAlt3, Keys.kbAlt4,
         Keys.kbAlt5, Keys.kbAlt6, Keys.kbAlt7, Keys.kbAlt8, Keys.kbAlt9];

    private static readonly ushort[] FunctionKeys =
        [Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6,
         Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12];
    private static readonly ushort[] ShiftFunctionKeys =
        [Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5, Keys.kbShiftF6,
         Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10, Keys.kbShiftF11, Keys.kbShiftF12];
    private static readonly ushort[] ControlFunctionKeys =
        [Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5, Keys.kbCtrlF6,
         Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10, Keys.kbCtrlF11, Keys.kbCtrlF12];
    private static readonly ushort[] AltFunctionKeys =
        [Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5, Keys.kbAltF6,
         Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12];

    private readonly Dictionary<int, KeyDownEvent> _heldKeys = new();
    private byte _physicalModifiers;

    internal void Reset()
    {
        _heldKeys.Clear();
        _physicalModifiers = 0;
    }

    internal int TryDecode(ReadOnlySpan<byte> buffer, out TEvent ev, out bool complete)
    {
        ev = default;
        complete = true;
        if (buffer.Length < 2 || buffer[0] != 0x1B || buffer[1] != '[')
            return 0;

        int finalIndex = 2;
        while (finalIndex < buffer.Length && (buffer[finalIndex] < 0x40 || buffer[finalIndex] > 0x7E))
            finalIndex++;
        if (finalIndex >= buffer.Length)
        {
            if (buffer.Length > 128) return 1;
            complete = false;
            return 0;
        }

        byte final = buffer[finalIndex];
        if (final != (byte)'u' && final != (byte)'~'
            && final is not ((byte)'A' or (byte)'B' or (byte)'C' or (byte)'D'
                or (byte)'F' or (byte)'H' or (byte)'P' or (byte)'Q' or (byte)'S'))
            return 0;

        string parameters = Encoding.ASCII.GetString(buffer.Slice(2, finalIndex - 2));
        if (parameters.Length == 0 || parameters[0] is '?' or '>' or '<' or '=')
            return 0;

        string[] fields = parameters.Split(';');
        string[] keyParts = fields[0].Split(':');
        if (!TryParse(keyParts[0], out int reportedKey))
            return finalIndex + 1;

        int shiftedKey = keyParts.Length > 1 && TryParse(keyParts[1], out int shifted) ? shifted : 0;
        int baseLayoutKey = keyParts.Length > 2 && TryParse(keyParts[2], out int baseLayout) ? baseLayout : 0;
        int modifierParameter = 1;
        int eventType = 1;
        bool explicitEventType = false;
        if (fields.Length > 1)
        {
            string[] modifierParts = fields[1].Split(':');
            if (modifierParts[0].Length > 0 && !TryParse(modifierParts[0], out modifierParameter))
                return finalIndex + 1;
            if (modifierParts.Length > 1)
            {
                explicitEventType = true;
                if (!TryParse(modifierParts[1], out eventType))
                    return finalIndex + 1;
            }
        }

        // Legacy CSI navigation is left to AnsiKeyDecoder unless Kitty supplied an event type.
        if (final != 'u' && !explicitEventType)
            return 0;
        if (eventType is < 1 or > 3)
            return finalIndex + 1;

        uint modifiers = DecodeModifiers(modifierParameter);
        int key = final switch
        {
            (byte)'A' => -1,
            (byte)'B' => -2,
            (byte)'C' => -3,
            (byte)'D' => -4,
            (byte)'H' => -5,
            (byte)'F' => -6,
            (byte)'P' => -7,
            (byte)'Q' => -8,
            (byte)'S' => -10,
            (byte)'~' => NumericFunctionKey(reportedKey),
            _ => reportedKey,
        };

        if (TryModifier(key, eventType, out ev))
        {
            if (ev.What != Events.evNothing)
                InputTrace.LogEvent("Stage2-Kitty(modifier-transition)", ev);
            return finalIndex + 1;
        }
        if (IsModifier(key))
            return finalIndex + 1;

        string text = fields.Length > 2 ? DecodeText(fields[2]) : string.Empty;
        int identityKey = ((modifiers & (Keys.kbCtrlShift | Keys.kbAltShift)) != 0 && baseLayoutKey != 0)
            ? baseLayoutKey : key;
        if ((modifiers & Keys.kbShift) != 0 && shiftedKey != 0 && text.Length == 0)
            text = ScalarText(shiftedKey);

        if (eventType == 3 && _heldKeys.Remove(key, out KeyDownEvent pressed))
        {
            pressed.controlKeyState = modifiers;
            pressed.text = string.Empty;
            ev.What = Events.evKeyUp;
            ev.keyDown = pressed;
            InputTrace.LogEvent("Stage2-Kitty(release)", ev);
            return finalIndex + 1;
        }
        if (eventType == 2 && _heldKeys.TryGetValue(key, out KeyDownEvent repeated))
        {
            repeated.controlKeyState = modifiers;
            if (text.Length != 0) repeated.text = text;
            ev.What = Events.evKeyDown;
            ev.keyDown = repeated;
            InputTrace.LogEvent("Stage2-Kitty(native-repeat)", ev);
            return finalIndex + 1;
        }

        if (!TryTranslate(identityKey, modifiers, text, out KeyDownEvent payload))
            return finalIndex + 1;

        if (eventType == 1)
            _heldKeys[key] = payload;

        ev.What = eventType == 3 ? Events.evKeyUp : Events.evKeyDown;
        ev.keyDown = payload;
        InputTrace.LogEvent(eventType switch
        {
            2 => "Stage2-Kitty(native-repeat)",
            3 => "Stage2-Kitty(release)",
            _ => "Stage2-Kitty(press)",
        }, ev);
        return finalIndex + 1;
    }

    private bool TryModifier(int key, int eventType, out TEvent ev)
    {
        ev = default;
        int bit = key switch
        {
            LeftShift => 0, RightShift => 1,
            LeftControl => 2, RightControl => 3,
            LeftAlt => 4, RightAlt => 5,
            _ => -1,
        };
        if (bit < 0) return false;

        byte old = _physicalModifiers;
        byte mask = (byte)(1 << bit);
        if (eventType == 3) _physicalModifiers &= (byte)~mask;
        else _physicalModifiers |= mask;
        uint before = ProjectModifiers(old);
        uint after = ProjectModifiers(_physicalModifiers);
        if (before != after)
        {
            ev.What = Events.evModifierChanged;
            ev.Modifiers = after;
        }
        return true;
    }

    private static bool IsModifier(int key)
        => key is LeftShift or LeftControl or LeftAlt or RightShift or RightControl or RightAlt;

    private static uint ProjectModifiers(byte physical)
    {
        uint result = 0;
        if ((physical & 0b000011) != 0) result |= Keys.kbShift;
        if ((physical & 0b001100) != 0) result |= Keys.kbCtrlShift;
        if ((physical & 0b110000) != 0) result |= Keys.kbAltShift;
        return result;
    }

    private static uint DecodeModifiers(int encoded)
    {
        int bits = Math.Max(0, encoded - 1);
        uint result = 0;
        if ((bits & 1) != 0) result |= Keys.kbShift;
        if ((bits & 2) != 0) result |= Keys.kbAltShift;
        if ((bits & 4) != 0) result |= Keys.kbCtrlShift;
        if ((bits & 64) != 0) result |= Keys.kbCapsState;
        if ((bits & 128) != 0) result |= Keys.kbNumState;
        return result;
    }

    private static int NumericFunctionKey(int number) => number switch
    {
        2 => -11, 3 => -12, 5 => -13, 6 => -14, 7 => -5, 8 => -6,
        11 => -7, 12 => -8, 13 => -9, 14 => -10,
        15 => -15, 17 => -16, 18 => -17, 19 => -18,
        20 => -19, 21 => -20, 23 => -21, 24 => -22,
        _ => 0,
    };

    private static bool TryTranslate(int key, uint modifiers, string text, out KeyDownEvent payload)
    {
        payload = default;
        bool shift = (modifiers & Keys.kbShift) != 0;
        bool control = (modifiers & Keys.kbCtrlShift) != 0;
        bool alt = (modifiers & Keys.kbAltShift) != 0;
        ushort code = SpecialKeyCode(key, shift, control, alt);

        if (code == 0 && key >= 'a' && key <= 'z' && (control || alt))
            code = (alt ? AltLetters : ControlLetters)[key - 'a'];
        else if (code == 0 && key >= 'A' && key <= 'Z' && (control || alt))
            code = (alt ? AltLetters : ControlLetters)[key - 'A'];
        else if (code == 0 && alt && key >= '0' && key <= '9')
            code = AltDigits[key - '0'];
        else if (code == 0 && alt && key == ' ') code = Keys.kbAltSpace;
        else if (code == 0 && alt && key == '-') code = Keys.kbAltMinus;
        else if (code == 0 && alt && key == '=') code = Keys.kbAltEqual;

        if (code == 0)
        {
            int scalar = FirstScalar(text);
            if (scalar is >= 0x20 and <= 0x7E)
                code = (ushort)scalar;
            else if (key is >= 0x20 and <= 0x7E && text.Length == 0)
                code = (ushort)key;
        }
        if (code == 0 && text.Length == 0) return false;

        payload.keyCode = code;
        payload.charScan = new CharScanType(code);
        payload.controlKeyState = modifiers;
        payload.text = text;
        if (control && key >= 'a' && key <= 'z')
            payload.charScan.charCode = (byte)(key - 'a' + 1);
        return true;
    }

    private static ushort SpecialKeyCode(int key, bool shift, bool control, bool alt)
    {
        if (key == 27) return Keys.kbEsc;
        if (key == 13) return control ? Keys.kbCtrlEnter : Keys.kbEnter;
        if (key == 9) return shift ? Keys.kbShiftTab : Keys.kbTab;
        if (key == 127) return alt ? Keys.kbAltBack : control ? Keys.kbCtrlBack : Keys.kbBack;
        if (key == -11) return control && shift ? Keys.kbCtrlShiftIns : control ? Keys.kbCtrlIns : shift ? Keys.kbShiftIns : Keys.kbIns;
        if (key == -12) return control && shift ? Keys.kbCtrlShiftDel : control ? Keys.kbCtrlDel : shift ? Keys.kbShiftDel : Keys.kbDel;
        if (key == -13) return control ? Keys.kbCtrlPgUp : Keys.kbPgUp;
        if (key == -14) return control ? Keys.kbCtrlPgDn : Keys.kbPgDn;
        if (key == -1) return Keys.kbUp;
        if (key == -2) return Keys.kbDown;
        if (key == -3) return control ? Keys.kbCtrlRight : Keys.kbRight;
        if (key == -4) return control ? Keys.kbCtrlLeft : Keys.kbLeft;
        if (key == -5) return control ? Keys.kbCtrlHome : Keys.kbHome;
        if (key == -6) return control ? Keys.kbCtrlEnd : Keys.kbEnd;
        if (key is <= -7 and >= -22)
        {
            int index = key <= -15 ? -key - 15 + 4 : -key - 7;
            if (index >= 0 && index < 12)
                return alt ? AltFunctionKeys[index] : control ? ControlFunctionKeys[index]
                    : shift ? ShiftFunctionKeys[index] : FunctionKeys[index];
        }
        return 0;
    }

    private static string DecodeText(string value)
    {
        if (value.Length == 0) return string.Empty;
        var result = new StringBuilder();
        string[] codePoints = value.Split(':');
        if (codePoints.Length > 64) return string.Empty;
        foreach (string codePoint in codePoints)
        {
            if (!TryParse(codePoint, out int scalar) || scalar < 0x20 || scalar is >= 0x7F and <= 0x9F || !Rune.IsValid(scalar))
                return string.Empty;
            result.Append(char.ConvertFromUtf32(scalar));
        }
        return result.ToString();
    }

    private static string ScalarText(int scalar)
        => Rune.IsValid(scalar) && scalar >= 0x20 && scalar is not (>= 0x7F and <= 0x9F)
            ? char.ConvertFromUtf32(scalar) : string.Empty;

    private static int FirstScalar(string text)
        => text.Length == 0 ? 0 : Rune.GetRuneAt(text, 0).Value;

    private static bool TryParse(string value, out int result)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;
}

/// <summary>Incremental mixed ANSI/Kitty/mouse byte-stream decoder used by the terminal driver.</summary>
internal sealed class TerminalInputDecoder
{
    private readonly List<byte> _pending = new(128);
    private readonly Queue<TEvent> _events = new();
    private readonly KittyKeyboardNegotiator _negotiator = new();
    private readonly KittyKeyboardDecoder _kitty = new();

    internal KeyboardCapabilities KeyboardCapabilities => _negotiator.Capabilities;
    internal KittyNegotiationState NegotiationState => _negotiator.State;

    internal string BeginKeyboardNegotiation()
    {
        _kitty.Reset();
        return _negotiator.Begin();
    }

    internal string? EndKeyboardMode()
    {
        _kitty.Reset();
        _pending.Clear();
        _events.Clear();
        return _negotiator.End();
    }

    internal void Feed(ReadOnlySpan<byte> bytes, Action<string> controlOutput)
    {
        foreach (byte value in bytes) _pending.Add(value);
        Drain(controlOutput);
    }

    internal bool TryRead(out TEvent ev)
    {
        if (_events.Count != 0)
        {
            ev = _events.Dequeue();
            return true;
        }
        ev = default;
        return false;
    }

    private void Drain(Action<string> controlOutput)
    {
        while (_pending.Count != 0)
        {
            ReadOnlySpan<byte> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending);
            int consumed = AnsiMouseDecoder.TryDecode(span, out TEvent ev, out bool complete);
            if (consumed == 0 && !complete) return;

            if (consumed == 0)
            {
                consumed = _negotiator.TryConsume(span, out complete, out string? output);
                if (consumed == 0 && !complete) return;
                if (output != null)
                {
                    InputTrace.Log("Stage2-Kitty(negotiation)", "support confirmed; requesting flags 31");
                    controlOutput(output);
                }
            }
            if (consumed == 0)
            {
                consumed = _kitty.TryDecode(span, out ev, out complete);
                if (consumed == 0 && !complete) return;
            }
            if (consumed == 0)
            {
                consumed = AnsiKeyDecoder.TryDecode(span, out ev, out complete);
                if (consumed == 0 && !complete) return;
            }
            if (consumed == 0) return;
            if (ev.What != Events.evNothing) _events.Enqueue(ev);
            _pending.RemoveRange(0, consumed);
        }
    }
}
