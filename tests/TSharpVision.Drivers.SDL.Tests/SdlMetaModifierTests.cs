// MACOS-1B: SDL's GUI modifier (Command on macOS, Windows key, Linux Super) is the framework's kbMetaShift,
// on ordinary key events and as standalone modifier transitions, without disturbing Shift, Ctrl, Alt or locks.
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlMetaModifierTests
{
    private const ushort LGui = SdlKeyTranslator.SDL_KMOD_LGUI, RGui = SdlKeyTranslator.SDL_KMOD_RGUI;
    private const uint Meta = Keys.kbMetaShift;

    private static readonly uint[] FunctionKeycodes =
    [
        SdlKeyTranslator.SDLK_F1, SdlKeyTranslator.SDLK_F2, SdlKeyTranslator.SDLK_F3, SdlKeyTranslator.SDLK_F4,
        SdlKeyTranslator.SDLK_F5, SdlKeyTranslator.SDLK_F6, SdlKeyTranslator.SDLK_F7, SdlKeyTranslator.SDLK_F8,
        SdlKeyTranslator.SDLK_F9, SdlKeyTranslator.SDLK_F10,
    ];
    private static readonly ushort[] Plain =
        [Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10];

    [Fact]
    public void GuiBitsAreTheOnesTheSdl3BindingDefines()
    {
        Assert.Equal((ushort)SDL3.SDL.Keymod.LGUI, SdlKeyTranslator.SDL_KMOD_LGUI);
        Assert.Equal((ushort)SDL3.SDL.Keymod.RGUI, SdlKeyTranslator.SDL_KMOD_RGUI);
        Assert.Equal((ushort)SDL3.SDL.Keymod.GUI, SdlKeyTranslator.SDL_KMOD_GUI);
        Assert.Equal((uint)SDL3.SDL.Keycode.LGUI, SdlKeyTranslator.SDLK_LGUI);
        Assert.Equal((uint)SDL3.SDL.Keycode.RGUI, SdlKeyTranslator.SDLK_RGUI);
    }

    [Theory]
    [InlineData(LGui, Meta)]
    [InlineData(RGui, Meta)]
    [InlineData(LGui | RGui, Meta)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LSHIFT | RGui, Keys.kbShift | Meta)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_RCTRL | LGui, Keys.kbCtrlShift | Meta)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LALT | LGui, Keys.kbAltShift | Meta)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_SHIFT | SdlKeyTranslator.SDL_KMOD_CTRL | SdlKeyTranslator.SDL_KMOD_ALT | LGui,
        Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Meta)]
    [InlineData(LGui | SdlKeyTranslator.SDL_KMOD_NUM | SdlKeyTranslator.SDL_KMOD_CAPS | SdlKeyTranslator.SDL_KMOD_SCROLL,
        Meta | Keys.kbNumState | Keys.kbCapsState | Keys.kbScrollState)]
    // Unchanged without GUI.
    [InlineData(SdlKeyTranslator.SDL_KMOD_SHIFT, Keys.kbShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_CTRL, Keys.kbCtrlShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_ALT, Keys.kbAltShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_NUM | SdlKeyTranslator.SDL_KMOD_CAPS, Keys.kbNumState | Keys.kbCapsState)]
    [InlineData(0, 0u)]
    public void NativeStateMapsGuiToMetaAndKeepsEverythingElse(ushort native, uint expected)
        => Assert.Equal(expected, SdlKeyTranslator.ToShiftState(native));

    [Fact]
    public void MetaFunctionKeysKeepTheirOwnIdentity()
    {
        for (int i = 0; i < FunctionKeycodes.Length; i++)
            foreach (ushort gui in new[] { LGui, RGui })
            {
                Assert.True(SdlKeyTranslator.TryTranslate(FunctionKeycodes[i], gui, '\0', out TEvent ev));
                Assert.Equal(Events.evKeyDown, ev.What);
                Assert.Equal(Plain[i], ev.keyDown.keyCode);
                Assert.Equal(Meta, ev.keyDown.controlKeyState);
            }
    }

    [Theory]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LSHIFT, Keys.kbShiftF1, Keys.kbShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LCTRL, Keys.kbCtrlF1, Keys.kbCtrlShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LALT, Keys.kbAltF1, Keys.kbAltShift)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LSHIFT | SdlKeyTranslator.SDL_KMOD_LCTRL, Keys.kbCtrlF1, Keys.kbShift | Keys.kbCtrlShift)]
    public void MetaCombinationsKeepTheExistingKeyCodeAndEveryBit(ushort native, ushort keyCode, uint state)
    {
        Assert.True(SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F1, native, '\0', out TEvent without));
        Assert.Equal(keyCode, without.keyDown.keyCode);
        Assert.Equal(state, without.keyDown.controlKeyState);
        Assert.True(SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F1, (ushort)(native | LGui), '\0', out TEvent with));
        Assert.Equal(keyCode, with.keyDown.keyCode);
        Assert.Equal(state | Meta, with.keyDown.controlKeyState);
    }

    [Fact]
    public void BareGuiKeysAreModifiersNotKeys()
    {
        foreach (uint key in new[] { SdlKeyTranslator.SDLK_LGUI, SdlKeyTranslator.SDLK_RGUI })
        {
            Assert.True(SdlModifierTranslator.IsModifierKey(key));
            Assert.False(SdlKeyTranslator.TryTranslate(key, LGui, '\0', out _));
        }
    }

    [Fact]
    public void StandaloneGuiTransitionsAggregateLeftAndRight()
    {
        var translator = new SdlModifierTranslator();
        AssertModifier(translator, SdlKeyTranslator.SDLK_LGUI, true, Meta);
        Assert.False(translator.TryTranslate(SdlKeyTranslator.SDLK_LGUI, true, out _)); // repeat
        Assert.False(translator.TryTranslate(SdlKeyTranslator.SDLK_RGUI, true, out _));
        Assert.False(translator.TryTranslate(SdlKeyTranslator.SDLK_LGUI, false, out _));
        AssertModifier(translator, SdlKeyTranslator.SDLK_RGUI, false, 0);

        AssertModifier(translator, SdlKeyTranslator.SDLK_RGUI, true, Meta);
        AssertModifier(translator, SdlKeyTranslator.SDLK_LSHIFT, true, Keys.kbShift | Meta);
        AssertModifier(translator, SdlKeyTranslator.SDLK_LCTRL, true, Keys.kbShift | Keys.kbCtrlShift | Meta);
        AssertModifier(translator, SdlKeyTranslator.SDLK_RGUI, false, Keys.kbShift | Keys.kbCtrlShift);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, translator.LogicalState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DriverReportsCommandDownMetaF1AndCommandUp(bool gpu)
    {
        IDriver driver = gpu ? new SDLGpuDriver() : new SDLDriver();
        Assert.True(driver.KeyboardCapabilities.HasFlag(KeyboardCapabilities.StandaloneMetaTransitions));
        Assert.True(Process(driver, d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_LGUI, true),
            d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_LGUI, true)));
        Assert.True(Process(driver, d => d.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_F1, LGui),
            d => d.ProcessOrdinaryKeyDown(SdlKeyTranslator.SDLK_F1, LGui)));
        Assert.True(Process(driver, d => d.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_F1, LGui),
            d => d.ProcessOrdinaryKeyUp(SdlKeyTranslator.SDLK_F1, LGui)));
        Assert.True(Process(driver, d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_LGUI, false),
            d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_LGUI, false)));

        TEvent[] events = Drain(driver);
        Assert.Equal([Events.evModifierChanged, Events.evKeyDown, Events.evKeyUp, Events.evModifierChanged],
            events.Select(ev => ev.What));
        Assert.Equal([Meta, Meta, Meta, 0u], events.Select(ev => ev.Modifiers));
        Assert.Equal(Keys.kbF1, events[1].keyDown.keyCode);
        Assert.Equal(Keys.kbF1, events[2].keyDown.keyCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusLossReleasesMeta(bool gpu)
    {
        IDriver driver = gpu ? new SDLGpuDriver() : new SDLDriver();
        Assert.True(Process(driver, d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_RGUI, true),
            d => d.ProcessModifierKey(SdlKeyTranslator.SDLK_RGUI, true)));
        Assert.True(Process(driver, d => d.ProcessModifierFocusLost(), d => d.ProcessModifierFocusLost()));
        TEvent[] events = Drain(driver);
        Assert.Equal([Meta, 0u], events.Select(ev => ev.Modifiers));
        Assert.All(events, ev => Assert.Equal(Events.evModifierChanged, ev.What));
    }

    private static bool Process(IDriver driver, Func<SDLDriver, bool> renderer, Func<SDLGpuDriver, bool> gpu)
        => driver is SDLGpuDriver g ? gpu(g) : renderer((SDLDriver)driver);

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
