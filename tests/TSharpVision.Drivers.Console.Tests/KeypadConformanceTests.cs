using TSharpVision.Constants;
using Xunit;
namespace TSharpVision.Drivers.Console.Tests;

public sealed class KeypadConformanceTests
{
    [Fact]
    public void MainAndKeypadEnterCanBeHeldAndReleasedIndependently()
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, 0x0D, 0x1C, '\0', 0));
        Assert.True(driver.ProcessKeyRecord(true, 0x0D, 0x1C, '\0', 0x100));
        Assert.True(driver.ProcessKeyRecord(false, 0x0D, 0x1C, '\0', 0x100));
        Assert.True(driver.ProcessKeyRecord(false, 0x0D, 0x1C, '\0', 0));
        foreach (ushort code in new[] { Keys.kbEnter, Keys.kbKeypadEnter, Keys.kbKeypadEnter, Keys.kbEnter })
        {
            Assert.True(driver.ReadKeyEvent(out var ev));
            Assert.Equal(code, ev.keyDown.keyCode);
        }
    }

    [Theory]
    [InlineData(',')]
    [InlineData('.')]
    public void DecimalLayoutTextAndModifiersDoNotChangeIdentity(char text)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, 0x6E, 0x53, text, 0x3A));
        Assert.True(driver.ReadKeyEvent(out var ev));
        Assert.Equal(Keys.kbKeypadDecimal, ev.keyDown.keyCode);
        Assert.Equal(text.ToString(), ev.keyDown.text);
        Assert.Equal(Keys.kbNumState | Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift, ev.keyDown.controlKeyState);
    }

    public static IEnumerable<object[]> Cases()
    {
        (ushort Vk, ushort Scan, ushort Code, char Text, uint Flags)[] keys =
        [
            (0x60, 0x52, Keys.kbKeypad0, '0', 0u),
            (0x61, 0x4F, Keys.kbKeypad1, '1', 0u),
            (0x62, 0x50, Keys.kbKeypad2, '2', 0u),
            (0x63, 0x51, Keys.kbKeypad3, '3', 0u),
            (0x64, 0x4B, Keys.kbKeypad4, '4', 0u),
            (0x65, 0x4C, Keys.kbKeypad5, '5', 0u),
            (0x66, 0x4D, Keys.kbKeypad6, '6', 0u),
            (0x67, 0x47, Keys.kbKeypad7, '7', 0u),
            (0x68, 0x48, Keys.kbKeypad8, '8', 0u),
            (0x69, 0x49, Keys.kbKeypad9, '9', 0u),
            (0x6E, 0x53, Keys.kbKeypadDecimal, ',', 0u),
            (0x6F, 0x35, Keys.kbKeypadDivide, '\0', 0x100u),
            (0x6A, 0x37, Keys.kbKeypadMultiply, '\0', 0u),
            (0x0D, 0x1C, Keys.kbKeypadEnter, '\0', 0x100u),
            (0x90, 0x45, Keys.kbNumLock, '\0', 0u),
            (0x6D, 0x4A, Keys.kbGrayMinus, '\0', 0u),
            (0x6B, 0x4E, Keys.kbGrayPlus, '\0', 0u),
            (0x2D, 0x52, Keys.kbKeypad0, '\0', 0u),
            (0x23, 0x4F, Keys.kbKeypad1, '\0', 0u),
            (0x28, 0x50, Keys.kbKeypad2, '\0', 0u),
            (0x22, 0x51, Keys.kbKeypad3, '\0', 0u),
            (0x25, 0x4B, Keys.kbKeypad4, '\0', 0u),
            (0x0C, 0x4C, Keys.kbKeypad5, '\0', 0u),
            (0x27, 0x4D, Keys.kbKeypad6, '\0', 0u),
            (0x24, 0x47, Keys.kbKeypad7, '\0', 0u),
            (0x26, 0x48, Keys.kbKeypad8, '\0', 0u),
            (0x21, 0x49, Keys.kbKeypad9, '\0', 0u),
            (0x2E, 0x53, Keys.kbKeypadDecimal, '\0', 0u),
        ];
        foreach (var key in keys)
            foreach (uint num in new uint[] { 0, 0x20 })
                yield return new object[] { key.Vk, key.Scan, key.Code, key.Text, key.Flags | num };
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeRecordPreservesIdentityStateTextAndRawScan(ushort vk, ushort scan, ushort code, char text, uint flags)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, vk, scan, text, flags));
        Assert.True(driver.ProcessKeyRecord(true, vk, scan, text, flags, 2));
        Assert.True(driver.ProcessKeyRecord(false, vk, scan, '\0', flags));
        foreach (ushort kind in new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyUp })
        {
            Assert.True(driver.ReadKeyEvent(out var ev));
            Assert.Equal(kind, ev.What);
            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.Equal((byte)scan, ev.keyDown.raw_scanCode);
            Assert.Equal(new CharScanType(code).ToUShort(), ev.keyDown.charScan.ToUShort());
            Assert.Equal((flags & 0x20) == 0 ? 0u : Keys.kbNumState, ev.keyDown.controlKeyState);
            Assert.Equal(kind == Events.evKeyDown && text != '\0' ? text.ToString() : "", ev.keyDown.text);
        }
        Assert.False(driver.ReadKeyEvent(out _));
    }
    [Theory]
    [InlineData(0x31, 0x02, '1', 0u, (ushort)'1')]
    [InlineData(0x24, 0x47, '\0', 0x100u, Keys.kbHome)]
    [InlineData(0x2E, 0x53, '\0', 0x100u, Keys.kbDel)]
    [InlineData(0x0D, 0x1C, '\0', 0u, Keys.kbEnter)]
    [InlineData(0x24, 0x00, '\0', 0u, Keys.kbHome)]
    public void MainRowAndUnknownOriginsDoNotBecomeKeypad(int vk, int scan, char text, uint flags, ushort expected)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, (ushort)vk, (ushort)scan, text, flags));
        Assert.True(driver.ReadKeyEvent(out var ev));
        Assert.Equal(expected, ev.keyDown.keyCode);
    }

    [Fact]
    public void ConsoleAdvertisesDistinctNumericKeypad()
        => Assert.True(new Win32ConsoleDriver().KeyboardCapabilities.HasFlag(
            TSharpVision.Drivers.KeyboardCapabilities.DistinctNumericKeypad));

    // Standard keys: (virtual key, scan, character, Win32 control state) -> key code and text.
    [Theory]
    [InlineData(0x1B, 0x01, '\u001b', 0u, Keys.kbEsc, "")]
    [InlineData(0x0D, 0x1C, '\r', 0u, Keys.kbEnter, "")]
    [InlineData(0x09, 0x0F, '\t', 0u, Keys.kbTab, "")]
    [InlineData(0x09, 0x0F, '\t', 0x10u, Keys.kbShiftTab, "")]
    [InlineData(0x08, 0x0E, '\b', 0u, Keys.kbBack, "")]
    [InlineData(0x70, 0x3B, '\0', 0u, Keys.kbF1, "")]
    [InlineData(0x71, 0x3C, '\0', 0u, Keys.kbF2, "")]
    [InlineData(0x72, 0x3D, '\0', 0u, Keys.kbF3, "")]
    [InlineData(0x73, 0x3E, '\0', 0u, Keys.kbF4, "")]
    [InlineData(0x74, 0x3F, '\0', 0u, Keys.kbF5, "")]
    [InlineData(0x75, 0x40, '\0', 0u, Keys.kbF6, "")]
    [InlineData(0x76, 0x41, '\0', 0u, Keys.kbF7, "")]
    [InlineData(0x77, 0x42, '\0', 0u, Keys.kbF8, "")]
    [InlineData(0x78, 0x43, '\0', 0u, Keys.kbF9, "")]
    [InlineData(0x79, 0x44, '\0', 0u, Keys.kbF10, "")]
    [InlineData(0x7A, 0x57, '\0', 0u, Keys.kbF11, "")]
    [InlineData(0x7B, 0x58, '\0', 0u, Keys.kbF12, "")]
    [InlineData(0x79, 0x44, '\0', 0x10u, Keys.kbShiftF10, "")]
    [InlineData(0x7A, 0x57, '\0', 0x08u, Keys.kbCtrlF11, "")]
    [InlineData(0x7B, 0x58, '\0', 0x02u, Keys.kbAltF12, "")]
    [InlineData(0x2D, 0x52, '\0', 0x100u, Keys.kbIns, "")]
    [InlineData(0x24, 0x47, '\0', 0x100u, Keys.kbHome, "")]
    [InlineData(0x21, 0x49, '\0', 0x100u, Keys.kbPgUp, "")]
    [InlineData(0x2E, 0x53, '\0', 0x100u, Keys.kbDel, "")]
    [InlineData(0x23, 0x4F, '\0', 0x100u, Keys.kbEnd, "")]
    [InlineData(0x22, 0x51, '\0', 0x100u, Keys.kbPgDn, "")]
    [InlineData(0x26, 0x48, '\0', 0x100u, Keys.kbUp, "")]
    [InlineData(0x25, 0x4B, '\0', 0x100u, Keys.kbLeft, "")]
    [InlineData(0x28, 0x50, '\0', 0x100u, Keys.kbDown, "")]
    [InlineData(0x27, 0x4D, '\0', 0x100u, Keys.kbRight, "")]
    [InlineData(0x25, 0x4B, '\0', 0x108u, Keys.kbCtrlLeft, "")]
    [InlineData(0x30, 0x0B, '0', 0u, (ushort)'0', "0")]
    [InlineData(0x31, 0x02, '1', 0u, (ushort)'1', "1")]
    [InlineData(0x39, 0x0A, '9', 0u, (ushort)'9', "9")]
    [InlineData(0x31, 0x02, '!', 0x10u, (ushort)'!', "!")]
    [InlineData(0xBD, 0x0C, '-', 0u, (ushort)'-', "-")]
    [InlineData(0xBB, 0x0D, '=', 0u, (ushort)'=', "=")]
    [InlineData(0xDB, 0x1A, '[', 0u, (ushort)'[', "[")]
    [InlineData(0xDD, 0x1B, ']', 0u, (ushort)']', "]")]
    [InlineData(0xDC, 0x2B, '\\', 0u, (ushort)'\\', "\\")]
    [InlineData(0xBA, 0x27, ';', 0u, (ushort)';', ";")]
    [InlineData(0xDE, 0x28, '\'', 0u, (ushort)'\'', "'")]
    [InlineData(0xBC, 0x33, ',', 0u, (ushort)',', ",")]
    [InlineData(0xBE, 0x34, '.', 0u, (ushort)'.', ".")]
    [InlineData(0xBF, 0x35, '/', 0u, (ushort)'/', "/")]
    [InlineData(0xC0, 0x29, '`', 0u, (ushort)'`', "`")]
    [InlineData(0x20, 0x39, ' ', 0u, (ushort)' ', " ")]
    [InlineData(0x41, 0x1E, 'a', 0u, (ushort)'a', "a")]
    [InlineData(0x41, 0x1E, 'A', 0x80u, (ushort)'A', "A")]
    [InlineData(0x41, 0x1E, '\u0001', 0x08u, Keys.kbCtrlA, "")]
    [InlineData(0x58, 0x2D, 'x', 0x02u, Keys.kbAltX, "")]
    public void StandardKeysPressRepeatAndReleaseKeepOneIdentity(
        int vk, int scan, char character, uint flags, ushort code, string text)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, (ushort)vk, (ushort)scan, character, flags));
        Assert.True(driver.ProcessKeyRecord(true, (ushort)vk, (ushort)scan, character, flags, 2));
        Assert.True(driver.ProcessKeyRecord(false, (ushort)vk, (ushort)scan, character, flags));
        uint state = Win32KeyTranslator.ToControlKeyState(flags);
        foreach (ushort kind in new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyUp })
        {
            Assert.True(driver.ReadKeyEvent(out var ev));
            Assert.Equal(kind, ev.What);
            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.Equal((byte)scan, ev.keyDown.raw_scanCode);
            Assert.Equal(state, ev.keyDown.controlKeyState);
            Assert.Equal(kind == Events.evKeyDown ? text : "", ev.keyDown.text);
            Assert.False(code is >= Keys.kbKeypad0 and <= Keys.kbNumLock);
        }
        Assert.False(driver.ReadKeyEvent(out _));
    }

    [Theory]
    [InlineData(0x20u, Keys.kbNumState)]
    [InlineData(0x40u, Keys.kbScrollState)]
    [InlineData(0x80u, Keys.kbCapsState)]
    [InlineData(0xE0u, Keys.kbNumState | Keys.kbScrollState | Keys.kbCapsState)]
    public void LockStateIsReportedWithoutChangingIdentity(uint flags, uint expected)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, 0x70, 0x3B, '\0', flags));
        Assert.True(driver.ReadKeyEvent(out var ev));
        Assert.Equal(Keys.kbF1, ev.keyDown.keyCode);
        Assert.Equal(expected, ev.keyDown.controlKeyState);
    }

    [Fact]
    public void MainDigitAndKeypadDigitAreDistinctWithTheSameText()
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, 0x31, 0x02, '1', 0x20));
        Assert.True(driver.ProcessKeyRecord(true, 0x61, 0x4F, '1', 0x20));
        Assert.True(driver.ReadKeyEvent(out var main));
        Assert.True(driver.ReadKeyEvent(out var keypad));
        Assert.Equal((ushort)'1', main.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad1, keypad.keyDown.keyCode);
        Assert.Equal(main.keyDown.text, keypad.keyDown.text);
    }
}
