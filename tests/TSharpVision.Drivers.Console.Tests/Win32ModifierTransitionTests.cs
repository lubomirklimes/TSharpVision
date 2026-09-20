using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class Win32ModifierTransitionTests
{
    private const ushort LShift = 0xA0;
    private const ushort RShift = 0xA1;
    private const ushort LCtrl = 0xA2;
    private const ushort RCtrl = 0xA3;
    private const ushort LAlt = 0xA4;
    private const ushort RAlt = 0xA5;

    [Fact]
    public void ConsoleAdvertisesKeyReleaseAndModifierTransitionCapabilities()
        => Assert.Equal(
            KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions,
            new Win32ConsoleDriver().KeyboardCapabilities);

    [Theory]
    [InlineData(LShift, Keys.kbShift)]
    [InlineData(LCtrl, Keys.kbCtrlShift)]
    [InlineData(LAlt, Keys.kbAltShift)]
    public void SingleModifierTransitionsReturnToZero(ushort key, uint logical)
        => AssertStates([(key, true), (key, false)], [logical, 0]);

    [Fact]
    public void PairReleaseOrdersReportCompleteRemainingState()
    {
        AssertStates([(LShift, true), (LCtrl, true), (LShift, false), (LCtrl, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift, Keys.kbCtrlShift, 0]);
        AssertStates([(LShift, true), (LCtrl, true), (LCtrl, false), (LShift, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift, Keys.kbShift, 0]);
        AssertStates([(LShift, true), (LAlt, true), (LShift, false), (LAlt, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbAltShift, Keys.kbAltShift, 0]);
        AssertStates([(LShift, true), (LAlt, true), (LAlt, false), (LShift, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbAltShift, Keys.kbShift, 0]);
        AssertStates([(LCtrl, true), (LAlt, true), (LCtrl, false), (LAlt, false)],
            [Keys.kbCtrlShift, Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbAltShift, 0]);
        AssertStates([(LCtrl, true), (LAlt, true), (LAlt, false), (LCtrl, false)],
            [Keys.kbCtrlShift, Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbCtrlShift, 0]);
    }

    [Fact]
    public void ThreeModifierReleaseOrdersReportEveryCompleteState()
    {
        AssertStates(
            [(LShift, true), (LCtrl, true), (LAlt, true), (LShift, false), (LCtrl, false), (LAlt, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift,
             Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift,
             Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbAltShift, 0]);
        AssertStates(
            [(LShift, true), (LCtrl, true), (LAlt, true), (LAlt, false), (LCtrl, false), (LShift, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift,
             Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift,
             Keys.kbShift | Keys.kbCtrlShift, Keys.kbShift, 0]);
    }

    [Theory]
    [InlineData(LShift, RShift, Keys.kbShift, Win32KeyTranslator.SHIFT_PRESSED)]
    [InlineData(LCtrl, RCtrl, Keys.kbCtrlShift, Win32KeyTranslator.LEFT_CTRL_PRESSED | Win32KeyTranslator.RIGHT_CTRL_PRESSED)]
    [InlineData(LAlt, RAlt, Keys.kbAltShift, Win32KeyTranslator.LEFT_ALT_PRESSED | Win32KeyTranslator.RIGHT_ALT_PRESSED)]
    public void LeftAndRightAggregateAndDuplicateDownIsSuppressed(
        ushort left, ushort right, uint logical, uint nativeState)
    {
        var translator = new Win32InputTranslator();
        AssertModifier(translator, left, true, logical);
        Assert.False(Translate(translator, left, true, out _));
        Assert.False(Translate(translator, right, true, out _));
        Assert.True(translator.TryTranslate(true, 0x28, 0x50, '\0', nativeState, out TEvent down));
        Assert.True(translator.TryTranslate(false, 0x28, 0x50, '\0', nativeState, out TEvent up));
        Assert.Equal(Events.evKeyDown, down.What);
        Assert.Equal(Events.evKeyUp, up.What);
        Assert.Equal(logical, down.Modifiers);
        Assert.Equal(logical, up.Modifiers);
        Assert.False(Translate(translator, left, false, out _));
        AssertModifier(translator, right, false, 0);
    }

    [Fact]
    public void OrdinaryKeysKeepTheirModifierStateAndNativeOrder()
    {
        var translator = new Win32InputTranslator();
        AssertModifier(translator, LShift, true, Keys.kbShift);
        Assert.True(translator.TryTranslate(true, 'A', 0x1E, 'A', Win32KeyTranslator.SHIFT_PRESSED, out TEvent first));
        Assert.Equal(Events.evKeyDown, first.What);
        Assert.Equal(Keys.kbShift, first.Modifiers);
        Assert.True(translator.TryTranslate(false, 'A', 0x1E, '\0', Win32KeyTranslator.SHIFT_PRESSED, out TEvent firstUp));
        Assert.Equal(Events.evKeyUp, firstUp.What);
        Assert.Equal(first.keyDown.keyCode, firstUp.keyDown.keyCode);
        Assert.Equal(Keys.kbShift, firstUp.Modifiers);
        AssertModifier(translator, LCtrl, true, Keys.kbShift | Keys.kbCtrlShift);
        Assert.True(translator.TryTranslate(true, 'A', 0x1E, '\u0001',
            Win32KeyTranslator.SHIFT_PRESSED | Win32KeyTranslator.LEFT_CTRL_PRESSED, out TEvent second));
        Assert.Equal(Events.evKeyDown, second.What);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, second.Modifiers);
        Assert.True(translator.TryTranslate(false, 'A', 0x1E, '\0',
            Win32KeyTranslator.SHIFT_PRESSED | Win32KeyTranslator.LEFT_CTRL_PRESSED, out TEvent secondUp));
        Assert.Equal(Events.evKeyUp, secondUp.What);
        Assert.Equal(second.keyDown.keyCode, secondUp.keyDown.keyCode);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, secondUp.Modifiers);
        AssertModifier(translator, LCtrl, false, Keys.kbShift);
        AssertModifier(translator, LShift, false, 0);
    }

    [Fact]
    public void GenericWin32ModifierIdentityUsesScanAndEnhancedBits()
    {
        var translator = new Win32InputTranslator();
        Assert.True(translator.TryTranslate(true, 0x10, 0x2A, '\0', Win32KeyTranslator.SHIFT_PRESSED, out TEvent left));
        Assert.Equal(Keys.kbShift, left.Modifiers);
        Assert.False(translator.TryTranslate(true, 0x10, 0x36, '\0', Win32KeyTranslator.SHIFT_PRESSED, out _));
        Assert.False(translator.TryTranslate(false, 0x10, 0x2A, '\0', Win32KeyTranslator.SHIFT_PRESSED, out _));
        Assert.True(translator.TryTranslate(false, 0x10, 0x36, '\0', 0, out TEvent zero));
        Assert.Equal(0u, zero.Modifiers);
    }

    [Fact]
    public void DriverQueuePreservesModifierKeyModifierOrdering()
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, LShift, 0x2A, '\0', Win32KeyTranslator.SHIFT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(true, 'A', 0x1E, 'A', Win32KeyTranslator.SHIFT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, 'A', 0x1E, '\0', Win32KeyTranslator.SHIFT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, LShift, 0x2A, '\0', 0));

        TEvent[] events = Drain(driver);
        Assert.Equal(4, events.Length);
        AssertEvent(events[0], Events.evModifierChanged, 0, Keys.kbShift);
        AssertEvent(events[1], Events.evKeyDown, 'A', Keys.kbShift);
        AssertEvent(events[2], Events.evKeyUp, 'A', Keys.kbShift);
        AssertEvent(events[3], Events.evModifierChanged, 0, 0);
        Assert.False(driver.ReadKeyEvent(out _));
    }

    [Fact]
    public void NavigationKeyDownAndUpHaveStableIdentity()
    {
        var translator = new Win32InputTranslator();
        Assert.True(translator.TryTranslate(true, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY, out TEvent down));
        Assert.True(translator.TryTranslate(false, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY, out TEvent up));

        Assert.Equal(Events.evKeyDown, down.What);
        Assert.Equal(Events.evKeyUp, up.What);
        Assert.Equal(down.keyDown.keyCode, up.keyDown.keyCode);
        Assert.Equal(Keys.kbDown, up.keyDown.keyCode);
    }

    [Fact]
    public void NativeRepeatRecordsRemainOneKeyDownPerRecord()
    {
        var driver = new Win32ConsoleDriver();
        for (int repeat = 0; repeat < 3; repeat++)
            Assert.True(driver.ProcessKeyRecord(true, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY));
        Assert.True(driver.ProcessKeyRecord(false, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY));

        TEvent[] events = Drain(driver);
        Assert.Equal(
            [Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp],
            events.Select(ev => ev.What));
        Assert.All(events, ev => Assert.Equal(Keys.kbDown, ev.keyDown.keyCode));
    }

    [Fact]
    public void Win32RepeatCountIsIntentionallyNotExpanded()
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY, repeatCount: 4));
        Assert.True(driver.ProcessKeyRecord(false, 0x28, 0x50, '\0', Win32KeyTranslator.ENHANCED_KEY));

        Assert.Equal([Events.evKeyDown, Events.evKeyUp], Drain(driver).Select(ev => ev.What));
    }

    [Fact]
    public void ComplexSequencePreservesExactNativeOrderAndCompleteState()
    {
        var driver = new Win32ConsoleDriver();
        Assert.True(driver.ProcessKeyRecord(true, LCtrl, 0x1D, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED));
        Assert.True(driver.ProcessKeyRecord(true, 'A', 0x1E, '\u0001', Win32KeyTranslator.LEFT_CTRL_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, 'A', 0x1E, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED));
        Assert.True(driver.ProcessKeyRecord(true, LAlt, 0x38, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED | Win32KeyTranslator.LEFT_ALT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(true, 'B', 0x30, '\u0002', Win32KeyTranslator.LEFT_CTRL_PRESSED | Win32KeyTranslator.LEFT_ALT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, LCtrl, 0x1D, '\0', Win32KeyTranslator.LEFT_ALT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, 'B', 0x30, '\0', Win32KeyTranslator.LEFT_ALT_PRESSED));
        Assert.True(driver.ProcessKeyRecord(false, LAlt, 0x38, '\0', 0));

        TEvent[] events = Drain(driver);
        Assert.Equal(
            [Events.evModifierChanged, Events.evKeyDown, Events.evKeyUp,
             Events.evModifierChanged, Events.evKeyDown, Events.evModifierChanged,
             Events.evKeyUp, Events.evModifierChanged],
            events.Select(ev => ev.What));
        Assert.Equal(
            [Keys.kbCtrlShift, Keys.kbCtrlShift, Keys.kbCtrlShift,
             Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbCtrlShift | Keys.kbAltShift,
             Keys.kbAltShift, Keys.kbAltShift, 0],
            events.Select(ev => ev.Modifiers));
        Assert.Equal(events[1].keyDown.keyCode, events[2].keyDown.keyCode);
        Assert.Equal(events[4].keyDown.keyCode, events[6].keyDown.keyCode);
    }

    private static void AssertStates((ushort Key, bool Down)[] sequence, uint[] expected)
    {
        var translator = new Win32InputTranslator();
        var actual = new List<uint>();
        foreach ((ushort key, bool down) in sequence)
            if (Translate(translator, key, down, out TEvent ev)) actual.Add(ev.Modifiers);
        Assert.Equal(expected, actual);
    }

    private static bool Translate(Win32InputTranslator translator, ushort key, bool down, out TEvent ev)
        => translator.TryTranslate(down, key, 0, '\0', 0, out ev);

    private static void AssertModifier(Win32InputTranslator translator, ushort key, bool down, uint expected)
    {
        Assert.True(Translate(translator, key, down, out TEvent ev));
        Assert.Equal(Events.evModifierChanged, ev.What);
        Assert.Equal(expected, ev.Modifiers);
    }

    private static TEvent[] Drain(IDriver driver)
    {
        var events = new List<TEvent>();
        while (driver.ReadKeyEvent(out TEvent ev)) events.Add(ev);
        return events.ToArray();
    }

    private static void AssertEvent(TEvent ev, ushort what, ushort keyCode, uint modifiers)
    {
        Assert.Equal(what, ev.What);
        if (keyCode != 0) Assert.Equal(keyCode, ev.keyDown.keyCode);
        Assert.Equal(modifiers, ev.Modifiers);
    }
}
