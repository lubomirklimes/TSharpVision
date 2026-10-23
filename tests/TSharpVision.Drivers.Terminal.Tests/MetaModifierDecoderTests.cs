// MACOS-1B: a terminal sequence that explicitly carries the Meta bit (xterm's modifier parameter bit 8) or Kitty's
// Super bit becomes kbMetaShift. Nothing else does: ESC-prefixed text stays Alt, and Kitty's Hyper and Meta bits
// are not mapped. These are decoder facts only; which terminals send such sequences is a separate question.
// ESC is written \u001b throughout: "\x1b" swallows a following hex digit.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class MetaModifierDecoderTests
{
    private const uint Shift = Keys.kbShift;
    private const uint Ctrl = Keys.kbCtrlShift;
    private const uint Alt = Keys.kbAltShift;
    private const uint Meta = Keys.kbMetaShift;
    private const uint AllModifiers = Shift | Ctrl | Alt | Meta;

    private static string Printable(string sequence) => sequence.Replace("\u001b", "ESC ");

    private static TEvent[] ReadAll(TerminalInputDecoder decoder, string sequence)
    {
        decoder.Feed(Encoding.ASCII.GetBytes(sequence), _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out TEvent ev)) events.Add(ev);
        return events.ToArray();
    }

    private static TEvent Single(TEvent[] events, string sequence)
    {
        Assert.True(events.Length == 1, $"{Printable(sequence)}: {events.Length} events");
        return events[0];
    }

    private static TEvent Legacy(string sequence) => Single(ReadAll(new TerminalInputDecoder(), sequence), sequence);

    private static TerminalInputDecoder KittyDecoder()
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?0u"u8, _ => { });
        decoder.Feed("\u001b[?31u"u8, _ => { });
        Assert.Equal(KittyNegotiationState.Active, decoder.NegotiationState);
        return decoder;
    }

    private static TEvent Kitty(string sequence) => Single(ReadAll(KittyDecoder(), sequence), sequence);

    private static void AssertKey(TEvent ev, string sequence, ushort keyCode, uint modifiers)
    {
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.True(keyCode == ev.keyDown.keyCode,
            $"{Printable(sequence)}: expected 0x{keyCode:X4}, got 0x{ev.keyDown.keyCode:X4}");
        Assert.True(modifiers == (ev.keyDown.controlKeyState & AllModifiers),
            $"{Printable(sequence)}: expected state 0x{modifiers:X}, got 0x{ev.keyDown.controlKeyState:X}");
    }

    // ── xterm / CSI modifier parameter: 1 + (Shift 1 | Alt 2 | Ctrl 4 | Meta 8) ──

    [Theory]
    [InlineData("\u001b[15;9~", Keys.kbF5, Meta)]
    [InlineData("\u001b[15;10~", Keys.kbShiftF5, Shift | Meta)]
    [InlineData("\u001b[15;13~", Keys.kbCtrlF5, Ctrl | Meta)]
    [InlineData("\u001b[15;11~", Keys.kbAltF5, Alt | Meta)]
    [InlineData("\u001b[15;14~", Keys.kbCtrlF5, Shift | Ctrl | Meta)]
    [InlineData("\u001b[15;16~", Keys.kbAltF5, Shift | Ctrl | Alt | Meta)]
    [InlineData("\u001b[21;9~", Keys.kbF10, Meta)]
    [InlineData("\u001b[1;9P", Keys.kbF1, Meta)]
    [InlineData("\u001b[1;10P", Keys.kbShiftF1, Shift | Meta)]
    [InlineData("\u001b[1;13S", Keys.kbCtrlF4, Ctrl | Meta)]
    [InlineData("\u001bO9P", Keys.kbF1, Meta)]
    [InlineData("\u001bO1;11Q", Keys.kbAltF2, Alt | Meta)]
    [InlineData("\u001b[1;9A", Keys.kbUp, Meta)]
    [InlineData("\u001b[1;13D", Keys.kbCtrlLeft, Ctrl | Meta)]
    [InlineData("\u001b[3;9~", Keys.kbDel, Meta)]
    public void XtermMetaParameterIsPreserved(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Legacy(sequence), sequence, keyCode, modifiers);

    [Theory]
    [InlineData("\u001b[15~", Keys.kbF5, 0u)]
    [InlineData("\u001b[15;2~", Keys.kbShiftF5, Shift)]
    [InlineData("\u001b[15;3~", Keys.kbAltF5, Alt)]
    [InlineData("\u001b[15;5~", Keys.kbCtrlF5, Ctrl)]
    [InlineData("\u001b[15;8~", Keys.kbAltF5, Shift | Ctrl | Alt)]
    [InlineData("\u001bOP", Keys.kbF1, 0u)]
    [InlineData("\u001bO5R", Keys.kbCtrlF3, Ctrl)]
    [InlineData("\u001b[1;5C", Keys.kbCtrlRight, Ctrl)]
    // Bits above Meta (Kitty's Hyper 16) are not Meta.
    [InlineData("\u001b[15;17~", Keys.kbF5, 0u)]
    public void OrdinarySequencesAreUnchanged(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Legacy(sequence), sequence, keyCode, modifiers);

    [Theory]
    [InlineData("\u001ba", Keys.kbAltA, Alt)]
    [InlineData("\u001bA", Keys.kbAltA, Alt | Shift)]
    [InlineData("\u001b5", Keys.kbAlt5, Alt)]
    public void EscPrefixedTextIsAltNeverMeta(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Legacy(sequence), sequence, keyCode, modifiers);

    [Fact]
    public void LegacyModeDoesNotAdvertiseMetaTransitions()
    {
        var decoder = new TerminalInputDecoder();
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?1;2c"u8, _ => { });
        Assert.False(decoder.KeyboardCapabilities.HasFlag(KeyboardCapabilities.StandaloneMetaTransitions));
    }

    // ── Kitty: Shift 1, Alt 2, Ctrl 4, Super 8, Hyper 16, Meta 32 ─────────────

    [Theory]
    [InlineData("\u001b[15;9:1~", Keys.kbF5, Meta)]
    [InlineData("\u001b[15;10:1~", Keys.kbShiftF5, Shift | Meta)]
    [InlineData("\u001b[15;13:1~", Keys.kbCtrlF5, Ctrl | Meta)]
    [InlineData("\u001b[15;11:1~", Keys.kbAltF5, Alt | Meta)]
    [InlineData("\u001b[15;14:1~", Keys.kbCtrlF5, Shift | Ctrl | Meta)]
    [InlineData("\u001b[1;9:1P", Keys.kbF1, Meta)]
    [InlineData("\u001b[21;9:1~", Keys.kbF10, Meta)]
    [InlineData("\u001b[97;9u", (ushort)'a', Meta)]
    [InlineData("\u001b[97;13u", Keys.kbCtrlA, Ctrl | Meta)]
    [InlineData("\u001b[97;11u", Keys.kbAltA, Alt | Meta)]
    public void KittySuperIsMeta(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Kitty(sequence), sequence, keyCode, modifiers);

    [Theory]
    [InlineData("\u001b[15;1:1~", Keys.kbF5, 0u)]
    [InlineData("\u001b[15;5:1~", Keys.kbCtrlF5, Ctrl)]
    [InlineData("\u001b[97;3u", Keys.kbAltA, Alt)]
    // Hyper (16) and Kitty's own Meta (32) are not the platform GUI modifier and are not mapped.
    [InlineData("\u001b[15;17:1~", Keys.kbF5, 0u)]
    [InlineData("\u001b[15;33:1~", Keys.kbF5, 0u)]
    [InlineData("\u001b[15;35:1~", Keys.kbAltF5, Alt)]
    public void KittyWithoutSuperIsUnchanged(string sequence, ushort keyCode, uint modifiers)
        => AssertKey(Kitty(sequence), sequence, keyCode, modifiers);

    [Fact]
    public void KittyReleaseCarriesMetaAndKeepsThePressIdentity()
    {
        TerminalInputDecoder decoder = KittyDecoder();
        TEvent down = Single(ReadAll(decoder, "\u001b[15;9:1~"), "press");
        TEvent up = Single(ReadAll(decoder, "\u001b[15;9:3~"), "release");
        Assert.Equal(Events.evKeyUp, up.What);
        Assert.Equal(down.keyDown.keyCode, up.keyDown.keyCode);
        Assert.Equal(Meta, up.keyDown.controlKeyState & AllModifiers);
    }

    [Fact]
    public void KittySuperKeysAreStandaloneMetaTransitions()
    {
        TerminalInputDecoder decoder = KittyDecoder();
        Assert.True(decoder.KeyboardCapabilities.HasFlag(KeyboardCapabilities.StandaloneMetaTransitions));
        uint[] Modifiers(string sequence)
            => ReadAll(decoder, sequence).Select(ev =>
            {
                Assert.Equal(Events.evModifierChanged, ev.What);
                return ev.Modifiers;
            }).ToArray();

        Assert.Equal([Meta], Modifiers("\u001b[57444;9:1u"));              // left Super down
        Assert.Empty(Modifiers("\u001b[57450;9:1u"));                      // right Super down: no logical change
        Assert.Empty(Modifiers("\u001b[57444;9:3u"));                      // left up, right still held
        Assert.Equal([Keys.kbShift | Meta], Modifiers("\u001b[57441;10:1u"));
        Assert.Equal([Keys.kbShift], Modifiers("\u001b[57450;2:3u"));
        Assert.Equal([0u], Modifiers("\u001b[57441;1:3u"));
    }

    [Fact]
    public void KittyHyperAndMetaKeysProduceNothing()
    {
        TerminalInputDecoder decoder = KittyDecoder();
        foreach (string sequence in new[] { "\u001b[57445;17:1u", "\u001b[57446;33:1u", "\u001b[57451;17:3u", "\u001b[57452;1:3u" })
            Assert.Empty(ReadAll(decoder, sequence));
    }
}
