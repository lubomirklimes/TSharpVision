// KEYBOARD-NORM: the terminal driver's key identities — F11/F12 against Shift+F4/F5, modified F1–F4 (S10), Shift+Tab
// (S11), Shift+Insert/Delete (S2) — through the real mixed ANSI/Kitty input path, plus the protocol limits that stay
// (S1: 0x08, S3: no releases in legacy mode). ESC is written \u001b throughout: "\x1b" swallows a following hex digit.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Terminal;
using TSharpVision.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class KeyboardNormalizationTests
{
    private const uint Shift = Keys.kbShift;
    private const uint Ctrl = Keys.kbCtrlShift;
    private const uint Alt = Keys.kbAltShift;

    /// <summary>One key through the driver's decoder (legacy mode: no Kitty negotiation), all bytes consumed.</summary>
    private static TEvent Legacy(string sequence)
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        Assert.True(decoder.TryRead(out TEvent ev), $"no event for {Printable(sequence)}");
        Assert.False(decoder.TryRead(out TEvent extra), $"{Printable(sequence)} also produced 0x{extra.keyDown.keyCode:X4}");
        return ev;
    }

    private static void AssertKey(string sequence, ushort keyCode, uint modifiers)
    {
        TEvent ev = Legacy(sequence);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.True(keyCode == ev.keyDown.keyCode,
            $"{Printable(sequence)}: expected 0x{keyCode:X4}, got 0x{ev.keyDown.keyCode:X4}");
        Assert.Equal(modifiers, ev.keyDown.controlKeyState);
    }

    private static string Printable(string sequence) => sequence.Replace("\u001b", "ESC ");

    // ── F11 / F12 against Shift+F4 / Shift+F5 ────────────────────────────────

    [Theory]
    [InlineData("\u001b[23~", Keys.kbF11, 0u)]
    [InlineData("\u001b[24~", Keys.kbF12, 0u)]
    [InlineData("\u001b[1;2S", Keys.kbShiftF4, Shift)]
    [InlineData("\u001b[15;2~", Keys.kbShiftF5, Shift)]
    [InlineData("\u001b[23;2~", Keys.kbShiftF11, Shift)]
    [InlineData("\u001b[24;2~", Keys.kbShiftF12, Shift)]
    [InlineData("\u001b[23;5~", Keys.kbCtrlF11, Ctrl)]
    [InlineData("\u001b[24;5~", Keys.kbCtrlF12, Ctrl)]
    [InlineData("\u001b[23;3~", Keys.kbAltF11, Alt)]
    [InlineData("\u001b[24;3~", Keys.kbAltF12, Alt)]
    public void LegacyFunctionKeysKeepTheirPhysicalIdentity(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(sequence, keyCode, modifiers);

    [Fact]
    public void LegacyF11IsNotShiftF4AndF12IsNotShiftF5()
    {
        Assert.NotEqual(Legacy("\u001b[1;2S").keyDown.keyCode, Legacy("\u001b[23~").keyDown.keyCode);
        Assert.NotEqual(Legacy("\u001b[15;2~").keyDown.keyCode, Legacy("\u001b[24~").keyDown.keyCode);
    }

    // ── S10: xterm modified F1–F4, CSI 1;<modifier> P/Q/R/S ──────────────────

    [Theory]
    [InlineData("\u001b[1;2P", Keys.kbShiftF1, Shift)]
    [InlineData("\u001b[1;2Q", Keys.kbShiftF2, Shift)]
    [InlineData("\u001b[1;2R", Keys.kbShiftF3, Shift)]
    [InlineData("\u001b[1;2S", Keys.kbShiftF4, Shift)]
    [InlineData("\u001b[1;5P", Keys.kbCtrlF1, Ctrl)]
    [InlineData("\u001b[1;5Q", Keys.kbCtrlF2, Ctrl)]
    [InlineData("\u001b[1;5R", Keys.kbCtrlF3, Ctrl)]
    [InlineData("\u001b[1;5S", Keys.kbCtrlF4, Ctrl)]
    [InlineData("\u001b[1;3P", Keys.kbAltF1, Alt)]
    [InlineData("\u001b[1;3Q", Keys.kbAltF2, Alt)]
    [InlineData("\u001b[1;3R", Keys.kbAltF3, Alt)]
    [InlineData("\u001b[1;3S", Keys.kbAltF4, Alt)]
    [InlineData("\u001b[1;6R", Keys.kbCtrlF3, Ctrl | Shift)]
    public void ModifiedF1ToF4AreDecoded(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(sequence, keyCode, modifiers);

    [Theory]
    [InlineData("\u001bOP", Keys.kbF1)]
    [InlineData("\u001bOQ", Keys.kbF2)]
    [InlineData("\u001bOR", Keys.kbF3)]
    [InlineData("\u001bOS", Keys.kbF4)]
    public void UnmodifiedF1ToF4StayTheSs3Form(string sequence, ushort keyCode) => AssertKey(sequence, keyCode, 0u);

    /// <summary>
    /// Only the key form (first parameter 1 or absent, with a modifier) is a function key; anything else with those
    /// final bytes — a cursor-position report such as CSI 12;40 R — is still consumed without inventing a key.
    /// </summary>
    [Theory]
    [InlineData("\u001b[12;40R")]
    [InlineData("\u001b[2;5P")]
    public void OtherCsiWithFinalPToSIsNotAFunctionKey(string sequence)
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        Assert.False(decoder.TryRead(out _));
    }

    // ── S11: Shift+Tab ───────────────────────────────────────────────────────

    [Fact]
    public void CsiZIsShiftTab() => AssertKey("\u001b[Z", Keys.kbShiftTab, Shift);

    [Fact]
    public void TabStaysTab() => AssertKey("\t", Keys.kbTab, 0u);

    // ── S2: Shift+Insert / Shift+Delete carry their modifier in CSI 2;m~ / 3;m~ ─────

    [Theory]
    [InlineData("\u001b[2~", Keys.kbIns, 0u)]
    [InlineData("\u001b[3~", Keys.kbDel, 0u)]
    [InlineData("\u001b[2;2~", Keys.kbShiftIns, Shift)]
    [InlineData("\u001b[3;2~", Keys.kbShiftDel, Shift)]
    [InlineData("\u001b[2;5~", Keys.kbCtrlIns, Ctrl)]
    [InlineData("\u001b[3;5~", Keys.kbCtrlDel, Ctrl)]
    [InlineData("\u001b[2;6~", Keys.kbCtrlShiftIns, Ctrl | Shift)]
    [InlineData("\u001b[3;6~", Keys.kbCtrlShiftDel, Ctrl | Shift)]
    public void InsertAndDeleteKeepTheirModifier(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(sequence, keyCode, modifiers);

    // ── S1: 0x08 is not a key identity of its own in legacy mode ────────────────

    /// <summary>
    /// Protocol ambiguity, pinned: a legacy terminal sends BS (0x08) for Ctrl+H, and — depending on its configuration —
    /// for Backspace or Ctrl+Backspace as well; DEL (0x7F) is the usual Backspace. The byte stream carries nothing that
    /// tells them apart, so both bytes stay Backspace (never break Backspace to gain Ctrl+H).
    /// </summary>
    [Theory]
    [InlineData("\u0008")]
    [InlineData("\u007f")]
    public void LegacyBackspaceBytesAreBackspace(string sequence) => AssertKey(sequence, Keys.kbBack, 0u);

    // ── Kitty keyboard protocol: the same identities, and what legacy mode cannot tell ──

    private static TEvent Kitty(string sequence)
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?0u"u8, _ => { });
        decoder.Feed("\u001b[?31u"u8, _ => { });
        Assert.Equal(KittyNegotiationState.Active, decoder.NegotiationState);
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        Assert.True(decoder.TryRead(out TEvent ev), $"no event for {Printable(sequence)}");
        return ev;
    }

    [Theory]
    [InlineData("\u001b[23~", Keys.kbF11, 0u)]
    [InlineData("\u001b[24~", Keys.kbF12, 0u)]
    [InlineData("\u001b[23;1:1~", Keys.kbF11, 0u)]
    [InlineData("\u001b[24;1:1~", Keys.kbF12, 0u)]
    [InlineData("\u001b[1;2:1S", Keys.kbShiftF4, Shift)]
    [InlineData("\u001b[1;2S", Keys.kbShiftF4, Shift)]
    [InlineData("\u001b[15;2:1~", Keys.kbShiftF5, Shift)]
    [InlineData("\u001b[23;5:1~", Keys.kbCtrlF11, Ctrl)]
    [InlineData("\u001b[24;3:1~", Keys.kbAltF12, Alt)]
    [InlineData("\u001b[23;2:1~", Keys.kbShiftF11, Shift)]
    [InlineData("\u001b[1;3P", Keys.kbAltF1, Alt)]
    [InlineData("\u001b[13;5:1~", Keys.kbCtrlF3, Ctrl)]
    [InlineData("\u001b[13;5u", Keys.kbCtrlEnter, Ctrl)]
    [InlineData("\u001b[13u", Keys.kbEnter, 0u)]
    [InlineData("\u001b[9;2u", Keys.kbShiftTab, Shift)]
    [InlineData("\u001b[104;5u", Keys.kbCtrlH, Ctrl)]
    [InlineData("\u001b[127u", Keys.kbBack, 0u)]
    [InlineData("\u001b[2;2:1~", Keys.kbShiftIns, Shift)]
    [InlineData("\u001b[3;2:1~", Keys.kbShiftDel, Shift)]
    public void KittyKeysKeepTheirPhysicalIdentity(string sequence, ushort keyCode, uint modifiers)
    {
        TEvent ev = Kitty(sequence);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.True(keyCode == ev.keyDown.keyCode,
            $"{Printable(sequence)}: expected 0x{keyCode:X4}, got 0x{ev.keyDown.keyCode:X4}");
        Assert.Equal(modifiers, ev.keyDown.controlKeyState & (Shift | Ctrl | Alt));
    }

    // ── S3: legacy mode carries no releases and no standalone modifiers ─────────

    [Fact]
    public void LegacyModeReportsNoReleaseOrModifierCapability()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?1;2c"u8, _ => { });   // the terminal answers DA only: no Kitty protocol

        Assert.Equal(KittyNegotiationState.Unsupported, decoder.NegotiationState);
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    // ── Encoder ↔ decoder symmetry for every function-key identity ──────────────

    public static TheoryData<ushort, uint> FunctionKeyIdentities()
    {
        ushort[][] rows =
        [
            [Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12],
            [Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5, Keys.kbShiftF6, Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10, Keys.kbShiftF11, Keys.kbShiftF12],
            [Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5, Keys.kbCtrlF6, Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10, Keys.kbCtrlF11, Keys.kbCtrlF12],
            [Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5, Keys.kbAltF6, Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12],
        ];
        uint[] state = [0u, Shift, Ctrl, Alt];
        var data = new TheoryData<ushort, uint>();
        for (int row = 0; row < rows.Length; row++)
            foreach (ushort code in rows[row])
            {
                data.Add(code, state[row]);
                if (row != 0) data.Add(code, 0u);   // the identity alone, without the driver's modifier state
            }
        return data;
    }

    [Theory]
    [MemberData(nameof(FunctionKeyIdentities))]
    public void EncodedFunctionKeysDecodeToTheSameIdentity(ushort keyCode, uint modifiers)
        => AssertRoundTrip(keyCode, modifiers);

    [Theory]
    [InlineData(Keys.kbShiftTab, Shift)]
    [InlineData(Keys.kbShiftIns, Shift)]
    [InlineData(Keys.kbShiftDel, Shift)]
    [InlineData(Keys.kbCtrlIns, Ctrl)]
    [InlineData(Keys.kbCtrlDel, Ctrl)]
    [InlineData(Keys.kbIns, 0u)]
    [InlineData(Keys.kbDel, 0u)]
    [InlineData(Keys.kbTab, 0u)]
    public void EncodedEditingKeysDecodeToTheSameIdentity(ushort keyCode, uint modifiers)
        => AssertRoundTrip(keyCode, modifiers);

    private static void AssertRoundTrip(ushort keyCode, uint modifiers)
    {
        var key = new KeyDownEvent { keyCode = keyCode, controlKeyState = modifiers };
        string? encoded = TerminalInputEncoder.EncodeKey(key, applicationCursorKeys: false);
        Assert.NotNull(encoded);
        TEvent ev = Legacy(encoded);
        Assert.True(keyCode == ev.keyDown.keyCode,
            $"0x{keyCode:X4} encoded as {Printable(encoded)} decodes as 0x{ev.keyDown.keyCode:X4}");
    }
}
