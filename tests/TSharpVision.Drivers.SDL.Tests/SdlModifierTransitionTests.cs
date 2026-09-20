using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlModifierTransitionTests
{
    [Fact]
    public void BothSdlDriversAdvertiseKeyReleaseAndModifierTransitionCapabilities()
    {
        KeyboardCapabilities expected = KeyboardCapabilities.KeyReleaseEvents
            | KeyboardCapabilities.StandaloneModifierTransitions;
        Assert.Equal(expected, new SDLDriver().KeyboardCapabilities);
        Assert.Equal(expected, new SDLGpuDriver().KeyboardCapabilities);
    }

    [Theory]
    [InlineData(SdlKeyTranslator.SDLK_LSHIFT, Keys.kbShift)]
    [InlineData(SdlKeyTranslator.SDLK_LCTRL, Keys.kbCtrlShift)]
    [InlineData(SdlKeyTranslator.SDLK_LALT, Keys.kbAltShift)]
    public void SingleModifierTransitionsReturnToZero(uint key, uint logical)
        => AssertStates([(key, true), (key, false)], [logical, 0]);

    [Fact]
    public void CombinationsAndTwoReleaseOrdersMatchConsoleSemantics()
    {
        AssertStates(
            [(SdlKeyTranslator.SDLK_LSHIFT, true), (SdlKeyTranslator.SDLK_LCTRL, true),
             (SdlKeyTranslator.SDLK_LALT, true), (SdlKeyTranslator.SDLK_LSHIFT, false),
             (SdlKeyTranslator.SDLK_LCTRL, false), (SdlKeyTranslator.SDLK_LALT, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift,
             Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift,
             Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbAltShift, 0]);
        AssertStates(
            [(SdlKeyTranslator.SDLK_LSHIFT, true), (SdlKeyTranslator.SDLK_LCTRL, true),
             (SdlKeyTranslator.SDLK_LALT, true), (SdlKeyTranslator.SDLK_LALT, false),
             (SdlKeyTranslator.SDLK_LCTRL, false), (SdlKeyTranslator.SDLK_LSHIFT, false)],
            [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift,
             Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift,
             Keys.kbShift | Keys.kbCtrlShift, Keys.kbShift, 0]);
    }

    [Theory]
    [InlineData(SdlKeyTranslator.SDLK_LSHIFT, SdlKeyTranslator.SDLK_RSHIFT, Keys.kbShift, SdlKeyTranslator.SDL_KMOD_SHIFT)]
    [InlineData(SdlKeyTranslator.SDLK_LCTRL, SdlKeyTranslator.SDLK_RCTRL, Keys.kbCtrlShift, SdlKeyTranslator.SDL_KMOD_CTRL)]
    [InlineData(SdlKeyTranslator.SDLK_LALT, SdlKeyTranslator.SDLK_RALT, Keys.kbAltShift, SdlKeyTranslator.SDL_KMOD_ALT)]
    public void PhysicalSidesAggregateAndRepeatsAreSuppressed(
        uint left, uint right, uint logical, ushort nativeState)
    {
        var translator = new SdlModifierTranslator();
        AssertModifier(translator, left, true, logical);
        Assert.False(translator.TryTranslate(left, true, out _));
        Assert.False(translator.TryTranslate(right, true, out _));
        var held = new SdlHeldKeyTracker();
        Assert.True(SdlKeyTranslator.TryTranslate(
            SdlKeyTranslator.SDLK_DOWN, nativeState, '\0', out TEvent down));
        held.KeyDown(SdlKeyTranslator.SDLK_DOWN, nativeState);
        Assert.True(held.TryKeyUp(SdlKeyTranslator.SDLK_DOWN, nativeState, out TEvent up));
        Assert.Equal(Events.evKeyDown, down.What);
        Assert.Equal(Events.evKeyUp, up.What);
        Assert.Equal(Keys.kbDown, up.keyDown.keyCode);
        Assert.Equal(logical, down.Modifiers);
        Assert.Equal(logical, up.Modifiers);
        Assert.False(translator.TryTranslate(left, false, out _));
        AssertModifier(translator, right, false, 0);
    }

    [Fact]
    public void OrdinaryKeyBetweenTransitionsRetainsModifiersAndOrder()
    {
        var modifiers = new SdlModifierTranslator();
        AssertModifier(modifiers, SdlKeyTranslator.SDLK_LSHIFT, true, Keys.kbShift);
        AssertModifier(modifiers, SdlKeyTranslator.SDLK_LCTRL, true, Keys.kbShift | Keys.kbCtrlShift);
        Assert.True(SdlKeyTranslator.TryTranslate('a',
            SdlKeyTranslator.SDL_KMOD_LSHIFT | SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out TEvent first));
        Assert.Equal(Events.evKeyDown, first.What);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, first.Modifiers);
        AssertModifier(modifiers, SdlKeyTranslator.SDLK_LCTRL, false, Keys.kbShift);
        Assert.True(SdlKeyTranslator.TryTranslate('a', SdlKeyTranslator.SDL_KMOD_LSHIFT, 'A', out TEvent second));
        Assert.Equal(Events.evKeyDown, second.What);
        Assert.Equal(Keys.kbShift, second.Modifiers);
        AssertModifier(modifiers, SdlKeyTranslator.SDLK_LSHIFT, false, 0);
    }

    [Fact]
    public void FocusLossEmitsOneZeroResetOnlyWhenNeeded()
    {
        var translator = new SdlModifierTranslator();
        Assert.False(translator.TryReset(out _));
        AssertModifier(translator, SdlKeyTranslator.SDLK_LSHIFT, true, Keys.kbShift);
        AssertModifier(translator, SdlKeyTranslator.SDLK_LALT, true, Keys.kbShift | Keys.kbAltShift);
        Assert.True(translator.TryReset(out TEvent reset));
        Assert.Equal(Events.evModifierChanged, reset.What);
        Assert.Equal(0u, reset.Modifiers);
        Assert.False(translator.TryReset(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DriverQueueEmitsPressReleaseAndFocusResetInOrder(bool gpu)
    {
        if (gpu)
        {
            var driver = new SDLGpuDriver();
            Assert.True(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.False(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LCTRL, true));
            Assert.True(driver.ProcessModifierFocusLost());
            Assert.False(driver.ProcessModifierFocusLost());
            AssertDriverStates(driver, [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift, 0]);
        }
        else
        {
            var driver = new SDLDriver();
            Assert.True(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.False(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(driver.ProcessModifierKey(SdlKeyTranslator.SDLK_LCTRL, true));
            Assert.True(driver.ProcessModifierFocusLost());
            Assert.False(driver.ProcessModifierFocusLost());
            AssertDriverStates(driver, [Keys.kbShift, Keys.kbShift | Keys.kbCtrlShift, 0]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigationRepeatsEmitEveryKeyDownThenOneStableKeyUp(bool gpu)
    {
        IDriver driver;
        if (gpu)
        {
            var concrete = new SDLGpuDriver();
            for (int repeat = 0; repeat < 3; repeat++)
                Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, 0));
            Assert.True(concrete.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_DOWN, 0));
            driver = concrete;
        }
        else
        {
            var concrete = new SDLDriver();
            for (int repeat = 0; repeat < 3; repeat++)
                Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, 0));
            Assert.True(concrete.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_DOWN, 0));
            driver = concrete;
        }

        TEvent[] events = Drain(driver);
        Assert.Equal(
            [Events.evKeyDown, Events.evKeyDown, Events.evKeyDown, Events.evKeyUp],
            events.Select(ev => ev.What));
        Assert.All(events, ev => Assert.Equal(Keys.kbDown, ev.keyDown.keyCode));
        Assert.All(events, ev => Assert.Equal(0u, ev.Modifiers));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModifierAndOrdinaryEventsRemainDistinctAndOrdered(bool gpu)
    {
        IDriver driver;
        ushort nativeShift = SdlKeyTranslator.SDL_KMOD_LSHIFT;
        if (gpu)
        {
            var concrete = new SDLGpuDriver();
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, false));
            driver = concrete;
        }
        else
        {
            var concrete = new SDLDriver();
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, false));
            driver = concrete;
        }

        TEvent[] events = Drain(driver);
        Assert.Equal(
            [Events.evModifierChanged, Events.evKeyDown, Events.evKeyUp, Events.evModifierChanged],
            events.Select(ev => ev.What));
        Assert.Equal([Keys.kbShift, Keys.kbShift, Keys.kbShift, 0], events.Select(ev => ev.Modifiers));
        Assert.Equal(Keys.kbDown, events[1].keyDown.keyCode);
        Assert.Equal(events[1].keyDown.keyCode, events[2].keyDown.keyCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusLossReleasesHeldOrdinaryKeysBeforeResettingModifiers(bool gpu)
    {
        IDriver driver;
        ushort nativeShift = SdlKeyTranslator.SDL_KMOD_LSHIFT;
        if (gpu)
        {
            var concrete = new SDLGpuDriver();
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessModifierFocusLost());
            Assert.False(concrete.ProcessModifierFocusLost());
            driver = concrete;
        }
        else
        {
            var concrete = new SDLDriver();
            Assert.True(concrete.ProcessModifierKey(SdlKeyTranslator.SDLK_LSHIFT, true));
            Assert.True(concrete.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_DOWN, nativeShift));
            Assert.True(concrete.ProcessModifierFocusLost());
            Assert.False(concrete.ProcessModifierFocusLost());
            driver = concrete;
        }

        TEvent[] events = Drain(driver);
        Assert.Equal(
            [Events.evModifierChanged, Events.evKeyDown, Events.evKeyUp, Events.evModifierChanged],
            events.Select(ev => ev.What));
        Assert.Equal([Keys.kbShift, Keys.kbShift, Keys.kbShift, 0], events.Select(ev => ev.Modifiers));
        Assert.Equal(events[1].keyDown.keyCode, events[2].keyDown.keyCode);
    }

    private static void AssertDriverStates(IDriver driver, uint[] expected)
    {
        var actual = new List<uint>();
        while (driver.ReadKeyEvent(out TEvent ev))
        {
            Assert.Equal(Events.evModifierChanged, ev.What);
            actual.Add(ev.Modifiers);
        }
        Assert.Equal(expected, actual);
    }

    private static void AssertStates((uint Key, bool Down)[] sequence, uint[] expected)
    {
        var translator = new SdlModifierTranslator();
        var actual = new List<uint>();
        foreach ((uint key, bool down) in sequence)
            if (translator.TryTranslate(key, down, out TEvent ev)) actual.Add(ev.Modifiers);
        Assert.Equal(expected, actual);
    }

    private static void AssertModifier(SdlModifierTranslator translator, uint key, bool down, uint expected)
    {
        Assert.True(translator.TryTranslate(key, down, out TEvent ev));
        Assert.Equal(Events.evModifierChanged, ev.What);
        Assert.Equal(expected, ev.Modifiers);
    }

    private static TEvent[] Drain(IDriver driver)
    {
        var events = new List<TEvent>();
        while (driver.ReadKeyEvent(out TEvent ev)) events.Add(ev);
        return events.ToArray();
    }
}
