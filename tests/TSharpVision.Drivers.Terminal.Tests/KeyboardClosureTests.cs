// KEYBOARD-CLOSURE through the terminal driver's decoder (legacy ANSI and Kitty): Space vs '4' (N1), Alt+Backspace
// (N2), keypad keys under Kitty (N3), SS3 modified F1–F4 and cursor keys with parser safety (N4), and one precedence
// rule for combined modifiers — Alt, then Ctrl, then Shift, full state in controlKeyState (N5).
// ESC is written \u001b throughout: "\x1b" swallows a following hex digit.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Terminal;
using TSharpVision.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class KeyboardClosureTests
{
    private const uint Shift = Keys.kbShift;
    private const uint Ctrl = Keys.kbCtrlShift;
    private const uint Alt = Keys.kbAltShift;

    private static string Printable(string sequence) => sequence.Replace("\u001b", "ESC ");

    private static TEvent[] LegacyAll(string sequence)
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out TEvent ev)) events.Add(ev);
        return events.ToArray();
    }

    private static TEvent Legacy(string sequence)
    {
        TEvent[] events = LegacyAll(sequence);
        Assert.True(events.Length == 1,
            $"{Printable(sequence)}: {events.Length} events [{string.Join(", ", events.Select(e => $"0x{e.keyDown.keyCode:X4}"))}]");
        return events[0];
    }

    private static TEvent Kitty(string sequence)
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?0u"u8, _ => { });
        decoder.Feed("\u001b[?31u"u8, _ => { });
        Assert.Equal(KittyNegotiationState.Active, decoder.NegotiationState);
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        Assert.True(decoder.TryRead(out TEvent ev), $"no event for {Printable(sequence)}");
        Assert.False(decoder.TryRead(out _), $"{Printable(sequence)} produced more than one event");
        return ev;
    }

    private static void AssertKey(TEvent ev, string sequence, ushort keyCode, uint modifiers)
    {
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.True(keyCode == ev.keyDown.keyCode,
            $"{Printable(sequence)}: expected 0x{keyCode:X4}, got 0x{ev.keyDown.keyCode:X4}");
        Assert.Equal(modifiers, ev.keyDown.controlKeyState & (Shift | Ctrl | Alt));
    }

    // ── N1: Space and the digit 4 ───────────────────────────────────────────

    [Theory]
    [InlineData(false, " ", Keys.kbSpace)]
    [InlineData(false, "4", (ushort)'4')]
    [InlineData(true, "\u001b[32u", Keys.kbSpace)]
    [InlineData(true, "\u001b[52u", (ushort)'4')]
    public void SpaceIsKbSpaceAndFourIsTheDigit(bool kitty, string sequence, ushort keyCode)
    {
        TEvent ev = kitty ? Kitty(sequence) : Legacy(sequence);
        AssertKey(ev, sequence, keyCode, 0);
        Assert.Equal((byte)keyCode, ev.keyDown.charScan.charCode);
    }

    // ── N2: Backspace ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, "\u007f", Keys.kbBack, 0u)]
    [InlineData(false, "\u001b\u007f", Keys.kbAltBack, Alt)]
    [InlineData(true, "\u001b[127u", Keys.kbBack, 0u)]
    [InlineData(true, "\u001b[127;5u", Keys.kbCtrlBack, Ctrl)]
    [InlineData(true, "\u001b[127;3u", Keys.kbAltBack, Alt)]
    public void BackspaceKeepsItsModifierIdentity(bool kitty, string sequence, ushort keyCode, uint modifiers)
        => AssertKey(kitty ? Kitty(sequence) : Legacy(sequence), sequence, keyCode, modifiers);

    // ── N3: keypad keys under Kitty (report-all-keys sends keypad codes) ─────

    [Theory]
    [InlineData("\u001b[57414u", Keys.kbKeypadEnter, 0u)]
    [InlineData("\u001b[57414;2u", Keys.kbKeypadEnter, Shift)]
    [InlineData("\u001b[57414;5u", Keys.kbKeypadEnter, Ctrl)]
    [InlineData("\u001b[57414;3u", Keys.kbKeypadEnter, Alt)]
    [InlineData("\u001b[57414;6u", Keys.kbKeypadEnter, Ctrl | Shift)]
    [InlineData("\u001b[57417u", Keys.kbKeypad4, 0u)]
    [InlineData("\u001b[57418u", Keys.kbKeypad6, 0u)]
    [InlineData("\u001b[57419u", Keys.kbKeypad8, 0u)]
    [InlineData("\u001b[57420u", Keys.kbKeypad2, 0u)]
    [InlineData("\u001b[57421u", Keys.kbKeypad9, 0u)]
    [InlineData("\u001b[57422u", Keys.kbKeypad3, 0u)]
    [InlineData("\u001b[57423u", Keys.kbKeypad7, 0u)]
    [InlineData("\u001b[57424u", Keys.kbKeypad1, 0u)]
    [InlineData("\u001b[57425u", Keys.kbKeypad0, 0u)]
    [InlineData("\u001b[57426u", Keys.kbKeypadDecimal, 0u)]
    [InlineData("\u001b[57423;5u", Keys.kbKeypad7, Ctrl)]
    [InlineData("\u001b[57417;5u", Keys.kbKeypad4, Ctrl)]
    public void KittyKeypadKeysAreDistinct(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Kitty(sequence), sequence, keyCode, modifiers);

    [Fact]
    public void KittyKeypadEnterReleaseKeepsTheIdentity()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?0u"u8, _ => { });
        decoder.Feed("\u001b[?31u"u8, _ => { });
        decoder.Feed("\u001b[57414;5:1u\u001b[57414;5:3u"u8, _ => { });

        Assert.True(decoder.TryRead(out TEvent press));
        Assert.True(decoder.TryRead(out TEvent release));
        Assert.Equal(Events.evKeyDown, press.What);
        Assert.Equal(Events.evKeyUp, release.What);
        Assert.Equal(Keys.kbKeypadEnter, press.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypadEnter, release.keyDown.keyCode);
    }

    [Fact]
    public void LegacyKeypadEnterIsCarriageReturn() => AssertKey(Legacy("\r"), "CR", Keys.kbEnter, 0);

    // ── N4: SS3 with a modifier ─────────────────────────────────────────────

    public static TheoryData<string, string> Ss3AndCsiForms()
    {
        var data = new TheoryData<string, string>();
        foreach (char final in "PQRS")
            for (int mod = 2; mod <= 8; mod++)
            {
                data.Add($"\u001bO{mod}{final}", $"\u001b[1;{mod}{final}");
                data.Add($"\u001bO1;{mod}{final}", $"\u001b[1;{mod}{final}");
            }
        foreach (char final in "ABCDHF")
            foreach (int mod in new[] { 2, 3, 5 })
                data.Add($"\u001bO{mod}{final}", $"\u001b[1;{mod}{final}");
        return data;
    }

    [Theory]
    [MemberData(nameof(Ss3AndCsiForms))]
    public void Ss3ModifierFormsDecodeLikeTheirCsiForms(string ss3, string csi)
    {
        TEvent a = Legacy(ss3), b = Legacy(csi);
        Assert.True(a.keyDown.keyCode == b.keyDown.keyCode,
            $"{Printable(ss3)} → 0x{a.keyDown.keyCode:X4}, {Printable(csi)} → 0x{b.keyDown.keyCode:X4}");
        Assert.Equal(b.keyDown.controlKeyState, a.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData("\u001bO2P", Keys.kbShiftF1, Shift)]
    [InlineData("\u001bO3P", Keys.kbAltF1, Alt)]
    [InlineData("\u001bO5P", Keys.kbCtrlF1, Ctrl)]
    [InlineData("\u001bO2Q", Keys.kbShiftF2, Shift)]
    [InlineData("\u001bO5R", Keys.kbCtrlF3, Ctrl)]
    [InlineData("\u001bO3S", Keys.kbAltF4, Alt)]
    [InlineData("\u001bO1;2S", Keys.kbShiftF4, Shift)]
    [InlineData("\u001bO1P", Keys.kbF1, 0u)]
    [InlineData("\u001bOP", Keys.kbF1, 0u)]
    [InlineData("\u001bOS", Keys.kbF4, 0u)]
    [InlineData("\u001bOA", Keys.kbUp, 0u)]
    [InlineData("\u001bO5C", Keys.kbCtrlRight, Ctrl)]
    public void Ss3Forms(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Legacy(sequence), sequence, keyCode, modifiers);

    /// <summary>A well-formed SS3 key with a modifier the xterm scheme does not define is consumed without a key.</summary>
    [Theory]
    [InlineData("\u001bO9P")]
    [InlineData("\u001bO0Q")]
    [InlineData("\u001bO1;17R")]
    [InlineData("\u001bO2;2S")]
    public void Ss3WithAnUnsupportedModifierIsConsumedWithoutAKey(string sequence)
        => Assert.Empty(LegacyAll(sequence));

    /// <summary>A truncated SS3 sequence waits for its final byte and consumes nothing, as every other sequence does.</summary>
    [Theory]
    [InlineData("\u001bO")]
    [InlineData("\u001bO2")]
    [InlineData("\u001bO1;")]
    [InlineData("\u001bO1;5")]
    public void TruncatedSs3WaitsForMoreBytes(string sequence)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(sequence);
        int consumed = AnsiKeyDecoder.TryDecode(bytes, out _, out bool complete);
        Assert.Equal(0, consumed);
        Assert.False(complete);
    }

    [Fact]
    public void TruncatedSs3CompletesWhenTheFinalByteArrives()
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed("\u001bO"u8, _ => { });
        decoder.Feed("2"u8, _ => { });
        Assert.False(decoder.TryRead(out _));
        decoder.Feed("P"u8, _ => { });
        Assert.True(decoder.TryRead(out TEvent ev));
        Assert.Equal(Keys.kbShiftF1, ev.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    /// <summary>
    /// Malformed SS3 keeps the old fallback — Esc, then the bytes as ordinary input — so nothing typed is swallowed. A
    /// digit run longer than any modifier form is malformed rather than a reason to keep waiting.
    /// </summary>
    [Theory]
    [InlineData("\u001bO2x", new[] { Keys.kbEsc, (ushort)'O', (ushort)'2', (ushort)'x' })]
    [InlineData("\u001bOx", new[] { Keys.kbEsc, (ushort)'O', (ushort)'x' })]
    [InlineData("\u001bO1234567P", new[] { Keys.kbEsc, (ushort)'O', (ushort)'1', (ushort)'2', (ushort)'3', (ushort)'4', (ushort)'5', (ushort)'6', (ushort)'7', (ushort)'P' })]
    public void MalformedSs3FallsBackToEscAndText(string sequence, ushort[] keyCodes)
        => Assert.Equal(keyCodes, LegacyAll(sequence).Select(e => e.keyDown.keyCode).ToArray());

    [Fact]
    public void EscFollowedByOrdinaryTextIsStillAltLetter()
    {
        AssertKey(Legacy("\u001bx"), "ESC x", Keys.kbAltX, Alt);
        Assert.Equal([Keys.kbEsc, Keys.kbEsc], LegacyAll("\u001b\u001b\u001b").Take(2).Select(e => e.keyDown.keyCode));
    }

    // ── N5: combined modifiers — Alt, then Ctrl, then Shift ─────────────────

    [Theory]
    [InlineData("\u001b[1;4P", Keys.kbAltF1, Shift | Alt)]
    [InlineData("\u001b[1;6Q", Keys.kbCtrlF2, Ctrl | Shift)]
    [InlineData("\u001b[1;7R", Keys.kbAltF3, Ctrl | Alt)]
    [InlineData("\u001b[1;8S", Keys.kbAltF4, Ctrl | Shift | Alt)]
    [InlineData("\u001b[15;4~", Keys.kbAltF5, Shift | Alt)]
    [InlineData("\u001b[23;4~", Keys.kbAltF11, Shift | Alt)]
    [InlineData("\u001b[24;6~", Keys.kbCtrlF12, Ctrl | Shift)]
    [InlineData("\u001b[24;7~", Keys.kbAltF12, Ctrl | Alt)]
    [InlineData("\u001b[2;4~", Keys.kbShiftIns, Shift | Alt)]
    [InlineData("\u001b[3;7~", Keys.kbCtrlDel, Ctrl | Alt)]
    [InlineData("\u001b[2;8~", Keys.kbCtrlShiftIns, Ctrl | Shift | Alt)]
    public void LegacyCombinedModifiersFollowThePrecedenceRule(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Legacy(sequence), sequence, keyCode, modifiers);

    [Theory]
    [InlineData("\u001b[1;4:1P", Keys.kbAltF1, Shift | Alt)]
    [InlineData("\u001b[1;6:1Q", Keys.kbCtrlF2, Ctrl | Shift)]
    [InlineData("\u001b[13;7:1~", Keys.kbAltF3, Ctrl | Alt)]
    [InlineData("\u001b[1;8:1S", Keys.kbAltF4, Ctrl | Shift | Alt)]
    [InlineData("\u001b[23;4:1~", Keys.kbAltF11, Shift | Alt)]
    [InlineData("\u001b[24;6:1~", Keys.kbCtrlF12, Ctrl | Shift)]
    public void KittyCombinedModifiersFollowThePrecedenceRule(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Kitty(sequence), sequence, keyCode, modifiers);

    public static TheoryData<ushort, uint> CombinedIdentities => new()
    {
        { Keys.kbAltF1, Shift | Alt },
        { Keys.kbCtrlF2, Ctrl | Shift },
        { Keys.kbAltF3, Ctrl | Alt },
        { Keys.kbAltF4, Ctrl | Shift | Alt },
        { Keys.kbAltF11, Shift | Alt },
        { Keys.kbCtrlF12, Ctrl | Shift },
        { Keys.kbAltBack, Alt },
    };

    /// <summary>What the encoder sends for a combined identity decodes to the same identity and state.</summary>
    [Theory]
    [MemberData(nameof(CombinedIdentities))]
    public void EncodedCombinedIdentitiesRoundTrip(ushort keyCode, uint modifiers)
    {
        var key = new KeyDownEvent { keyCode = keyCode, controlKeyState = modifiers };
        string? encoded = TerminalInputEncoder.EncodeKey(key, applicationCursorKeys: false);
        Assert.NotNull(encoded);
        AssertKey(Legacy(encoded), encoded, keyCode, modifiers);
    }
}
