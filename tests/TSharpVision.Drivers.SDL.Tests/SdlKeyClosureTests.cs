using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

/// <summary>
/// KEYBOARD-CLOSURE through SDL: Space vs '4' (N1), Backspace with modifiers (N2), keypad Enter (N3) and combined
/// modifiers on function keys (N5) — through the pure translator and through both drivers' press/release path.
/// </summary>
public sealed class SdlKeyClosureTests
{
    private const ushort LShift = SdlKeyTranslator.SDL_KMOD_LSHIFT;
    private const ushort LCtrl = SdlKeyTranslator.SDL_KMOD_LCTRL;
    private const ushort LAlt = SdlKeyTranslator.SDL_KMOD_LALT;
    private const uint Backspace = SdlKeyTranslator.SDLK_BACKSPACE;
    private const uint KeypadEnter = SdlKeyTranslator.SDLK_KP_ENTER;

    private static TEvent Translate(uint keycode, ushort mod, char text = '\0')
    {
        Assert.True(SdlKeyTranslator.TryTranslate(keycode, mod, text, out TEvent ev), $"0x{keycode:X} not translated");
        return ev;
    }

    [Fact]
    public void SpaceIsKbSpaceAndFourIsTheDigit()
    {
        Assert.Equal(Keys.kbSpace, Translate(' ', 0, ' ').keyDown.keyCode);
        TEvent four = Translate('4', 0, '4');
        Assert.Equal((ushort)'4', four.keyDown.keyCode);
        Assert.NotEqual(Keys.kbSpace, four.keyDown.keyCode);
    }

    [Theory]
    [InlineData((ushort)0, Keys.kbBack)]
    [InlineData(LShift, Keys.kbBack)]
    [InlineData(LCtrl, Keys.kbCtrlBack)]
    [InlineData(LAlt, Keys.kbAltBack)]
    [InlineData((ushort)(LCtrl | LShift), Keys.kbCtrlBack)]
    [InlineData((ushort)(LAlt | LShift), Keys.kbAltBack)]
    [InlineData((ushort)(LCtrl | LAlt), Keys.kbAltBack)]
    public void BackspaceKeepsItsModifierIdentity(ushort mod, ushort expected)
    {
        TEvent ev = Translate(Backspace, mod);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData((ushort)0, Keys.kbKeypadEnter)]
    [InlineData(LShift, Keys.kbKeypadEnter)]
    [InlineData(LCtrl, Keys.kbKeypadEnter)]
    [InlineData(LAlt, Keys.kbKeypadEnter)]
    [InlineData((ushort)(LCtrl | LShift), Keys.kbKeypadEnter)]
    public void KeypadEnterIsDistinct(ushort mod, ushort expected)
    {
        TEvent ev = Translate(KeypadEnter, mod);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
        Assert.NotEqual(Translate(SdlKeyTranslator.SDLK_RETURN, mod).keyDown.keyCode, ev.keyDown.keyCode);
    }

    [Theory]
    [InlineData(SdlKeyTranslator.SDLK_F1, (ushort)(LShift | LAlt), Keys.kbAltF1)]
    [InlineData(SdlKeyTranslator.SDLK_F2, (ushort)(LCtrl | LShift), Keys.kbCtrlF2)]
    [InlineData(SdlKeyTranslator.SDLK_F3, (ushort)(LCtrl | LAlt), Keys.kbAltF3)]
    [InlineData(SdlKeyTranslator.SDLK_F4, (ushort)(LCtrl | LShift | LAlt), Keys.kbAltF4)]
    [InlineData(SdlKeyTranslator.SDLK_F11, (ushort)(LShift | LAlt), Keys.kbAltF11)]
    [InlineData(SdlKeyTranslator.SDLK_F12, (ushort)(LCtrl | LShift), Keys.kbCtrlF12)]
    public void CombinedModifiersTakeTheHighestPrecedenceCode(uint keycode, ushort mod, ushort expected)
    {
        TEvent ev = Translate(keycode, mod);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData(false, Backspace, LCtrl, Keys.kbCtrlBack)]
    [InlineData(true, Backspace, LCtrl, Keys.kbCtrlBack)]
    [InlineData(false, Backspace, LAlt, Keys.kbAltBack)]
    [InlineData(true, Backspace, LAlt, Keys.kbAltBack)]
    [InlineData(false, KeypadEnter, (ushort)0, Keys.kbKeypadEnter)]
    [InlineData(true, KeypadEnter, (ushort)0, Keys.kbKeypadEnter)]
    [InlineData(false, KeypadEnter, LCtrl, Keys.kbKeypadEnter)]
    [InlineData(true, KeypadEnter, LCtrl, Keys.kbKeypadEnter)]
    public void PressAndReleaseCarryTheIdentity(bool gpu, uint keycode, ushort mod, ushort expected)
    {
        IDriver driver;
        if (gpu)
        {
            var concrete = new SDLGpuDriver();
            Assert.True(concrete.ProcessOrdinaryKeyDown(keycode, mod));
            Assert.True(concrete.ProcessOrdinaryKeyUp(keycode, mod));
            driver = concrete;
        }
        else
        {
            var concrete = new SDLDriver();
            Assert.True(concrete.ProcessOrdinaryKeyDown(keycode, mod));
            Assert.True(concrete.ProcessOrdinaryKeyUp(keycode, mod));
            driver = concrete;
        }

        var events = new List<TEvent>();
        while (driver.ReadKeyEvent(out TEvent ev)) events.Add(ev);
        Assert.Equal([Events.evKeyDown, Events.evKeyUp], events.Select(ev => ev.What));
        Assert.All(events, ev => Assert.Equal(expected, ev.keyDown.keyCode));
    }
}
