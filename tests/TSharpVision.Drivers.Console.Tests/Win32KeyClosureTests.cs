using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

/// <summary>
/// KEYBOARD-CLOSURE through the Win32 console translator: Space is kbSpace and a typed '4' is not (N1); a function key
/// with several modifiers takes the code of the highest-precedence one — Alt, then Ctrl, then Shift — and keeps the
/// full combination in controlKeyState (N5); Backspace keeps its modifier identities (N2 reference).
/// </summary>
public sealed class Win32KeyClosureTests
{
    private const uint Shift = Win32KeyTranslator.SHIFT_PRESSED;
    private const uint Ctrl = Win32KeyTranslator.LEFT_CTRL_PRESSED;
    private const uint Alt = Win32KeyTranslator.LEFT_ALT_PRESSED;

    private static TEvent Translate(ushort vk, uint state, char ch = '\0')
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, vk, ch, state, out TEvent ev));
        Assert.Equal(Events.evKeyDown, ev.What);
        return ev;
    }

    [Fact]
    public void SpaceIsKbSpaceAndFourIsTheDigit()
    {
        TEvent space = Translate(0x20, 0, ' ');
        Assert.Equal(Keys.kbSpace, space.keyDown.keyCode);
        Assert.Equal((byte)' ', space.keyDown.charScan.charCode);

        TEvent four = Translate(0x34, 0, '4');
        Assert.Equal((ushort)'4', four.keyDown.keyCode);
        Assert.NotEqual(Keys.kbSpace, four.keyDown.keyCode);
        Assert.Equal("4", four.keyDown.text);
    }

    [Theory]
    [InlineData(0x70, Shift | Alt, Keys.kbAltF1)]
    [InlineData(0x71, Ctrl | Shift, Keys.kbCtrlF2)]
    [InlineData(0x72, Ctrl | Alt, Keys.kbAltF3)]
    [InlineData(0x73, Ctrl | Shift | Alt, Keys.kbAltF4)]
    [InlineData(0x7A, Shift | Alt, Keys.kbAltF11)]
    [InlineData(0x7B, Ctrl | Shift, Keys.kbCtrlF12)]
    public void CombinedModifiersTakeTheHighestPrecedenceCode(ushort vk, uint state, ushort expected)
    {
        TEvent ev = Translate(vk, state);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(Win32KeyTranslator.ToControlKeyState(state), ev.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData(0u, Keys.kbBack)]
    [InlineData(Shift, Keys.kbBack)]
    [InlineData(Ctrl, Keys.kbCtrlBack)]
    [InlineData(Alt, Keys.kbAltBack)]
    [InlineData(Ctrl | Shift, Keys.kbCtrlBack)]
    [InlineData(Alt | Shift, Keys.kbAltBack)]
    [InlineData(Ctrl | Alt, Keys.kbAltBack)]
    public void BackspaceModifierIdentities(uint state, ushort expected)
        => Assert.Equal(expected, Translate(0x08, state).keyDown.keyCode);
}
