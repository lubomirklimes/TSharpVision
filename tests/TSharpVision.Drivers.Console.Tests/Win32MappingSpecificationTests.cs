using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using Xunit;
namespace TSharpVision.Tests.Drivers;

public sealed class Win32MappingSpecificationTests
{
    const uint Shift = Win32KeyTranslator.SHIFT_PRESSED;
    const uint Ctrl = Win32KeyTranslator.LEFT_CTRL_PRESSED;
    const uint Alt = Win32KeyTranslator.LEFT_ALT_PRESSED;
    static uint State(int bits) => ((bits & 1) != 0 ? Shift : 0) | ((bits & 2) != 0 ? Ctrl : 0) | ((bits & 4) != 0 ? Alt : 0);

    public static IEnumerable<object[]> Functions()
    {
        ushort[][] rows =
        [
            [Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12],
            [Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5, Keys.kbShiftF6, Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10, Keys.kbShiftF11, Keys.kbShiftF12],
            [Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5, Keys.kbCtrlF6, Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10, Keys.kbCtrlF11, Keys.kbCtrlF12],
            [Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5, Keys.kbAltF6, Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12]
        ];
        for (int modifier = 0; modifier < 8; modifier++)
            for (int key = 0; key < 12; key++)
                yield return [0x70 + key, State(modifier), rows[(modifier & 4) != 0 ? 3 : (modifier & 2) != 0 ? 2 : modifier & 1][key]];
    }
    [Theory, MemberData(nameof(Functions))]
    public void FunctionCommandsMatchPublicConstants(int vk, uint state, ushort expected) => AssertCommand(vk, '\0', state, expected);

    public static IEnumerable<object[]> Navigation()
    {
        (int Vk, ushort Normal, ushort Control)[] rows =
        [
            (0x24, Keys.kbHome, Keys.kbCtrlHome), (0x23, Keys.kbEnd, Keys.kbCtrlEnd),
            (0x21, Keys.kbPgUp, Keys.kbCtrlPgUp), (0x22, Keys.kbPgDn, Keys.kbCtrlPgDn),
            (0x25, Keys.kbLeft, Keys.kbCtrlLeft), (0x27, Keys.kbRight, Keys.kbCtrlRight),
            (0x26, Keys.kbUp, Keys.kbUp), (0x28, Keys.kbDown, Keys.kbDown)
        ];
        foreach (var row in rows)
            for (int modifier = 0; modifier < 8; modifier++)
                yield return [row.Vk, State(modifier), (modifier & 2) != 0 ? row.Control : row.Normal];
    }
    [Theory, MemberData(nameof(Navigation))]
    public void NavigationUsesSupportedNamedVariants(int vk, uint state, ushort expected) => AssertCommand(vk, '\0', state, expected);

    [Theory]
    [InlineData(0x2D, 0u, Keys.kbIns)] [InlineData(0x2E, 0u, Keys.kbDel)]
    [InlineData(0x2D, Shift, Keys.kbShiftIns)] [InlineData(0x2E, Shift, Keys.kbShiftDel)]
    [InlineData(0x2D, Ctrl, Keys.kbCtrlIns)] [InlineData(0x2E, Ctrl, Keys.kbCtrlDel)]
    [InlineData(0x2D, Ctrl | Shift, Keys.kbCtrlShiftIns)] [InlineData(0x2E, Ctrl | Shift, Keys.kbCtrlShiftDel)]
    [InlineData(0x08, 0u, Keys.kbBack)] [InlineData(0x08, Ctrl, Keys.kbCtrlBack)] [InlineData(0x08, Alt, Keys.kbAltBack)]
    [InlineData(0x09, 0u, Keys.kbTab)] [InlineData(0x09, Shift | Ctrl, Keys.kbShiftTab)]
    [InlineData(0x0D, 0u, Keys.kbEnter)] [InlineData(0x0D, Ctrl | Alt, Keys.kbCtrlEnter)]
    [InlineData(0x1B, 0u, Keys.kbEsc)] [InlineData(0x20, Alt, Keys.kbAltSpace)]
    [InlineData(0xBD, Alt, Keys.kbAltMinus)] [InlineData(0xBB, Alt, Keys.kbAltEqual)]
    [InlineData(0x2C, Ctrl, Keys.kbCtrlPrtSc)]
    public void EditingCommandsHaveNoAccidentalPrintablePayload(int vk, uint state, ushort expected) => AssertCommand(vk, '\0', state, expected);

    [Theory]
    [InlineData('A', 'a', 0u)] [InlineData('A', 'A', Shift)]
    [InlineData('A', 'A', Win32KeyTranslator.CAPSLOCK_ON)]
    [InlineData('E', 'é', 0u)] [InlineData('Z', 'ž', 0u)] [InlineData(0xE7, '漢', 0u)]
    [InlineData('Q', '@', Ctrl | Win32KeyTranslator.RIGHT_ALT_PRESSED)]
    [InlineData('E', '€', Ctrl | Alt)]
    [InlineData(0x61, '1', Win32KeyTranslator.NUMLOCK_ON)]
    [InlineData(0x6B, '+', 0u)] [InlineData(0x6D, '-', 0u)] [InlineData(0x6F, '/', Win32KeyTranslator.ENHANCED_KEY)]
    [InlineData(0x20, ' ', 0u)] [InlineData(0xE7, '\uD83D', 0u)] [InlineData(0xE7, '\uDE00', 0u)]
    public void SuppliedTextPreservesCodeUnitAndDoesNotTruncate(int vk, char text, uint state)
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, (ushort)vk, text, state, out var ev));
        Assert.Equal(Events.evKeyDown, ev.What); Assert.Equal(text.ToString(), ev.keyDown.text);
        Assert.Equal(text <= 255 ? (ushort)text : (ushort)0, ev.keyDown.keyCode);
        Assert.Equal(text <= 255 ? (byte)text : (byte)0, ev.keyDown.charScan.charCode);
        Assert.Equal(0, ev.keyDown.raw_scanCode); Assert.Equal(0, ev.keyDown.charScan.scanCode);
    }

    [Fact]
    public void EveryLetterSupportsControlAndAltWithoutRequiringText()
    {
        ushort[] controls =
        [
            Keys.kbCtrlA, Keys.kbCtrlB, Keys.kbCtrlC, Keys.kbCtrlD, Keys.kbCtrlE, Keys.kbCtrlF,
            Keys.kbCtrlG, Keys.kbCtrlH, Keys.kbCtrlI, Keys.kbCtrlJ, Keys.kbCtrlK, Keys.kbCtrlL,
            Keys.kbCtrlM, Keys.kbCtrlN, Keys.kbCtrlO, Keys.kbCtrlP, Keys.kbCtrlQ, Keys.kbCtrlR,
            Keys.kbCtrlS, Keys.kbCtrlT, Keys.kbCtrlU, Keys.kbCtrlV, Keys.kbCtrlW, Keys.kbCtrlX,
            Keys.kbCtrlY, Keys.kbCtrlZ
        ];
        ushort[] alts =
        [
            Keys.kbAltA, Keys.kbAltB, Keys.kbAltC, Keys.kbAltD, Keys.kbAltE, Keys.kbAltF,
            Keys.kbAltG, Keys.kbAltH, Keys.kbAltI, Keys.kbAltJ, Keys.kbAltK, Keys.kbAltL,
            Keys.kbAltM, Keys.kbAltN, Keys.kbAltO, Keys.kbAltP, Keys.kbAltQ, Keys.kbAltR,
            Keys.kbAltS, Keys.kbAltT, Keys.kbAltU, Keys.kbAltV, Keys.kbAltW, Keys.kbAltX,
            Keys.kbAltY, Keys.kbAltZ
        ];
        for (int index = 0; index < 26; index++)
        {
            ushort vk = (ushort)('A' + index);
            foreach (char payload in new[] { '\0', (char)(index + 1) })
                AssertCommand(vk, payload, Ctrl, controls[index]);
            AssertCommand(vk, '\0', Alt | Shift, alts[index]);
        }
    }
    [Theory]
    [InlineData('0', Keys.kbAlt0)] [InlineData('1', Keys.kbAlt1)] [InlineData('9', Keys.kbAlt9)]
    public void AltDigitsUsePublicContract(int vk, ushort expected) => AssertCommand(vk, '\0', Alt, expected);

    [Fact]
    public void KeyUpAndDeadInputsAreFiltered()
    {
        for (ushort vk = 0; vk < 256; vk++)
        {
            Assert.False(Win32KeyTranslator.TryTranslate(false, vk, 'a', Ctrl | Alt | Shift, out var ev));
            Assert.Equal(Events.evNothing, ev.What);
        }
        foreach (ushort vk in new ushort[] { 0, 0x10, 0x11, 0x12, 0x14, 0x5B, 0x5C, 0x90, 0x91, 0xA0, 0xA5, 0x41, 0x7C })
            Assert.False(Win32KeyTranslator.TryTranslate(true, vk, '\0', 0, out _));
    }
    [Fact]
    public void ModifierLocationsAndLocksMapToTargetStateWithoutChangingText()
    {
        foreach (uint control in new[] { Ctrl, Win32KeyTranslator.RIGHT_CTRL_PRESSED })
            foreach (uint alt in new[] { Alt, Win32KeyTranslator.RIGHT_ALT_PRESSED })
            {
                uint flags = control | alt | Shift | Win32KeyTranslator.CAPSLOCK_ON | Win32KeyTranslator.NUMLOCK_ON | Win32KeyTranslator.SCROLLLOCK_ON;
                Assert.True(Win32KeyTranslator.TryTranslate(true, 0x45, '€', flags, out var ev));
                Assert.Equal(Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbCapsState | Keys.kbNumState | Keys.kbScrollState, ev.keyDown.controlKeyState);
                Assert.Equal("€", ev.keyDown.text);
            }
    }
    [Theory]
    [InlineData(0x24, Keys.kbHome)] [InlineData(0x2D, Keys.kbIns)] [InlineData(0x0D, Keys.kbEnter)]
    public void KeypadAndEnhancedLocationsShareTheCommandContract(int vk, ushort expected)
    {
        AssertCommand(vk, '\0', 0, expected); AssertCommand(vk, '\0', Win32KeyTranslator.ENHANCED_KEY, expected);
    }
    [Theory]
    [InlineData('\u0001')] [InlineData('\u001F')] [InlineData('\u007F')]
    public void RawControlUnitsAreNotMarkedAsPrintable(char character)
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, 0, character, 0, out var ev));
        Assert.Same(string.Empty, ev.keyDown.text); Assert.Equal((ushort)character, ev.keyDown.keyCode);
    }
    static void AssertCommand(int vk, char character, uint state, ushort expected)
    {
        Assert.True(Win32KeyTranslator.TryTranslate(true, (ushort)vk, character, state, out var ev));
        Assert.Equal(Events.evKeyDown, ev.What); Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Same(string.Empty, ev.keyDown.text);
        var packed = new CharScanType(expected);
        Assert.Equal(packed.charCode, ev.keyDown.charScan.charCode);
        Assert.Equal(packed.scanCode, ev.keyDown.charScan.scanCode);
    }
}
