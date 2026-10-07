using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Drivers.Console.Tests;

/// <summary>
/// KEYPAD-CLOSURE on the console: what a native keypad record means once <see cref="KeypadKeys.Normalize"/> has seen
/// the event the driver made of it. The driver output itself is pinned by <see cref="KeypadConformanceTests"/>.
/// </summary>
public sealed class KeypadSemanticsTests
{
    private const uint Shift = 0x0010, Ctrl = 0x0008, Alt = 0x0002, NumLock = 0x0020, Enhanced = 0x0100;

    private static TEvent Report(ushort vk, ushort scan, char character, uint flags, bool down = true)
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(down, vk, scan, character, flags));
        Assert.True(driver.ReadKeyEvent(out TEvent ev));
        return ev;
    }

    private static TEvent Dispatched(ushort vk, ushort scan, char character, uint flags, bool down = true)
    {
        TEvent ev = Report(vk, scan, character, flags, down);
        KeypadKeys.Normalize(ref ev);
        return ev;
    }

    /// <summary>Navigation records: (virtual key, keypad scan, keypad identity). The gray cursor block sends the same virtual key with ENHANCED_KEY.</summary>
    public static readonly (ushort Vk, ushort Scan, ushort Identity)[] Navigation =
    {
        (0x2D, 0x52, Keys.kbKeypad0), (0x23, 0x4F, Keys.kbKeypad1), (0x28, 0x50, Keys.kbKeypad2),
        (0x22, 0x51, Keys.kbKeypad3), (0x25, 0x4B, Keys.kbKeypad4), (0x27, 0x4D, Keys.kbKeypad6),
        (0x24, 0x47, Keys.kbKeypad7), (0x26, 0x48, Keys.kbKeypad8), (0x21, 0x49, Keys.kbKeypad9),
        (0x2E, 0x53, Keys.kbKeypadDecimal),
    };

    public static IEnumerable<object[]> NavigationCases()
    {
        foreach (var key in Navigation)
            foreach (uint modifiers in new[] { 0u, Shift, Ctrl, Ctrl | Shift, Alt, Ctrl | Alt })
                yield return new object[] { key.Vk, key.Scan, key.Identity, modifiers };
    }

    /// <summary>
    /// NumLock off: the keypad key is the cursor key, with exactly the code and state the gray key has under the same
    /// modifiers — the behaviour before the keypad identities, and the identity beside it.
    /// </summary>
    [Theory, MemberData(nameof(NavigationCases))]
    public void WithNumLockOffAKeypadKeyIsWhatTheGrayCursorKeyIs(ushort vk, ushort scan, ushort identity, uint modifiers)
    {
        foreach (bool down in new[] { true, false })
        {
            TEvent main = Report(vk, scan, '\0', modifiers | Enhanced, down);
            TEvent keypad = Dispatched(vk, scan, '\0', modifiers, down);

            Assert.False(KeypadKeys.IsKeypadIdentity(main.keyDown.keyCode));
            Assert.Equal(main.What, keypad.What);
            Assert.Equal(main.keyDown.keyCode, keypad.keyDown.keyCode);
            Assert.Equal(main.keyDown.charScan.ToUShort(), keypad.keyDown.charScan.ToUShort());
            Assert.Equal(main.keyDown.controlKeyState, keypad.keyDown.controlKeyState);
            Assert.Equal(string.Empty, keypad.keyDown.text);
            Assert.Equal(identity, keypad.keyDown.keypadKey);
            Assert.Equal((byte)scan, keypad.keyDown.raw_scanCode);
        }
    }

    [Theory]
    [InlineData(0x60, 0x52, '0')]
    [InlineData(0x61, 0x4F, '1')]
    [InlineData(0x65, 0x4C, '5')]
    [InlineData(0x69, 0x49, '9')]
    [InlineData(0x6E, 0x53, '.')]
    [InlineData(0x6E, 0x53, ',')]
    [InlineData(0x6A, 0x37, '*')]
    public void WithNumLockOnAKeypadKeyTypesItsCharacterOnce(int vk, int scan, char character)
    {
        TEvent reported = Report((ushort)vk, (ushort)scan, character, NumLock);
        TEvent ev = Dispatched((ushort)vk, (ushort)scan, character, NumLock);

        Assert.True(KeypadKeys.IsKeypadIdentity(reported.keyDown.keyCode));
        Assert.Equal(character, ev.keyDown.keyCode);
        Assert.Equal(character.ToString(), ev.keyDown.text);
        Assert.Equal(character.ToString(), KeyText.PrintableText(ev.keyDown));
        Assert.Equal(reported.keyDown.keyCode, ev.keyDown.keypadKey);
        Assert.Equal(Keys.kbNumState, ev.keyDown.controlKeyState);

        // The release has no text; it keeps the identity and is paired with the press by keypadKey.
        TEvent release = Dispatched((ushort)vk, (ushort)scan, '\0', NumLock, down: false);
        Assert.Equal(Events.evKeyUp, release.What);
        Assert.Equal(reported.keyDown.keyCode, release.keyDown.keypadKey);
        Assert.Equal(string.Empty, KeyText.PrintableText(release.keyDown));
    }

    /// <summary>Divide is an enhanced key; minus and plus keep the historical gray codes applications bind.</summary>
    [Theory]
    [InlineData(0x6F, 0x35, '/', Enhanced, (ushort)'/', Keys.kbKeypadDivide)]
    [InlineData(0x6D, 0x4A, '-', 0u, Keys.kbGrayMinus, Keys.kbGrayMinus)]
    [InlineData(0x6B, 0x4E, '+', 0u, Keys.kbGrayPlus, Keys.kbGrayPlus)]
    public void OperatorsTypeOnceInEitherLockState(int vk, int scan, char character, uint flags, ushort code, ushort identity)
    {
        foreach (uint numLock in new[] { 0u, NumLock })
        {
            TEvent ev = Dispatched((ushort)vk, (ushort)scan, character, flags | numLock);

            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.Equal(character.ToString(), KeyText.PrintableText(ev.keyDown));
            Assert.Equal(identity, ev.keyDown.keypadKey);
        }
    }

    /// <summary>
    /// A numeric record without a character (Ctrl held, or an Alt+digits code being composed) is neither text nor
    /// navigation, as before the keypad identities, when such a record produced no event at all.
    /// </summary>
    [Theory]
    [InlineData(Ctrl)]
    [InlineData(Alt)]
    public void WithNumLockOnADigitWithoutACharacterDoesNothing(uint modifiers)
    {
        TEvent ev = Dispatched(0x64, 0x4B, '\0', NumLock | modifiers);

        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keypadKey);
        Assert.Equal(string.Empty, KeyText.PrintableText(ev.keyDown));
    }

    /// <summary>Keypad 5 with NumLock off is VK_CLEAR, which never was a key here.</summary>
    [Fact]
    public void KeypadFiveWithNumLockOffIsNoKey()
    {
        TEvent ev = Dispatched(0x0C, 0x4C, '\0', 0);

        Assert.Equal(Keys.kbKeypad5, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad5, ev.keyDown.keypadKey);
        Assert.Equal(string.Empty, KeyText.PrintableText(ev.keyDown));
    }

    /// <summary>
    /// Known limit, pinned so that a change is deliberate. With NumLock on, Shift makes Windows send the navigation
    /// record (VK_LEFT with the keypad scan) while the lock state still says on. The identity kbKeypad4 does not say
    /// which form it was, so the event is treated like every other text-less key under NumLock: not navigation.
    /// </summary>
    [Fact]
    public void ANavigationRecordWhileNumLockIsOnIsNotNavigation()
    {
        TEvent ev = Dispatched(0x25, 0x4B, '\0', NumLock);

        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keypadKey);
    }

    [Fact]
    public void KeypadEnterIsStillEnterAndCtrlEnter()
    {
        Assert.Equal(Keys.kbEnter, Dispatched(0x0D, 0x1C, '\r', Enhanced).keyDown.keyCode);
        Assert.Equal(Keys.kbEnter, Dispatched(0x0D, 0x1C, '\r', Enhanced | NumLock | Shift).keyDown.keyCode);
        TEvent ctrl = Dispatched(0x0D, 0x1C, '\n', Enhanced | Ctrl);
        Assert.Equal(Keys.kbCtrlEnter, ctrl.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypadEnter, ctrl.keyDown.keypadKey);
    }
}
