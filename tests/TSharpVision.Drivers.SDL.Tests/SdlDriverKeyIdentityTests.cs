using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

/// <summary>
/// KEYBOARD-NORM through both SDL drivers' ordinary key path (not only the pure translator): F11/F12 are not
/// Shift+F4/Shift+F5, and Ctrl+Enter is not Enter — on the press and on its release.
/// </summary>
public sealed class SdlDriverKeyIdentityTests
{
    private const ushort LCtrl = SdlKeyTranslator.SDL_KMOD_LCTRL;
    private const ushort LShift = SdlKeyTranslator.SDL_KMOD_LSHIFT;

    [Theory]
    [InlineData(false, SdlKeyTranslator.SDLK_RETURN, (ushort)0, Keys.kbEnter)]
    [InlineData(true, SdlKeyTranslator.SDLK_RETURN, (ushort)0, Keys.kbEnter)]
    [InlineData(false, SdlKeyTranslator.SDLK_RETURN, LCtrl, Keys.kbCtrlEnter)]
    [InlineData(true, SdlKeyTranslator.SDLK_RETURN, LCtrl, Keys.kbCtrlEnter)]
    [InlineData(false, SdlKeyTranslator.SDLK_F11, (ushort)0, Keys.kbF11)]
    [InlineData(true, SdlKeyTranslator.SDLK_F12, (ushort)0, Keys.kbF12)]
    [InlineData(false, SdlKeyTranslator.SDLK_F4, LShift, Keys.kbShiftF4)]
    [InlineData(true, SdlKeyTranslator.SDLK_F5, LShift, Keys.kbShiftF5)]
    public void PressAndReleaseCarryThePhysicalIdentity(bool gpu, uint keycode, ushort mod, ushort expected)
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
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), events[0].keyDown.controlKeyState);
    }
    [Theory]
    [InlineData("ě")]
    [InlineData("š")]
    [InlineData("č")]
    [InlineData("ř")]
    [InlineData("😀")]
    [InlineData("ěščř")]
    [InlineData("aě😀")]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("2")]
    [InlineData("?")]
    [InlineData(" ")]
    public void BothNativeTextPathsPreserveTextAndOnlyAsciiIdentity(string text)
    {
        const ushort mod = SdlKeyTranslator.SDL_KMOD_LSHIFT | SdlKeyTranslator.SDL_KMOD_LCTRL | SdlKeyTranslator.SDL_KMOD_LALT;
        var renderer = new SDLDriver();
        var gpu = new SDLGpuDriver();
        renderer.ProcessTextInput(text, mod);
        gpu.ProcessTextInput(text, mod);
        Assert.True(renderer.ReadKeyEvent(out var left));
        Assert.True(gpu.ReadKeyEvent(out var right));
        foreach (var ev in new[] { left, right })
        {
            Assert.Equal(Events.evKeyDown, ev.What);
            Assert.Equal(text, ev.keyDown.text);
            ushort expected = text[0] <= 0x7F ? text[0] : (ushort)0;
            Assert.Equal(expected, ev.keyDown.keyCode);
            Assert.Equal((byte)expected, ev.keyDown.charScan.charCode);
            Assert.Equal((byte)0, ev.keyDown.charScan.scanCode);
            Assert.Equal((byte)0, ev.keyDown.raw_scanCode);
            Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
            Assert.NotEqual(Keys.kbEsc, ev.keyDown.keyCode);
            Assert.NotEqual(Keys.kbEnter, ev.keyDown.keyCode);
            Assert.NotEqual(Keys.kbCtrlM, ev.keyDown.keyCode);
        }
        Assert.False(renderer.ReadKeyEvent(out _));
        Assert.False(gpu.ReadKeyEvent(out _));
    }

    [Theory]
    [InlineData(false, SdlKeyTranslator.SDLK_ESCAPE, (ushort)0, Keys.kbEsc)]
    [InlineData(true, SdlKeyTranslator.SDLK_ESCAPE, (ushort)0, Keys.kbEsc)]
    [InlineData(false, SdlKeyTranslator.SDLK_F10, (ushort)0, Keys.kbF10)]
    [InlineData(true, SdlKeyTranslator.SDLK_F11, (ushort)0, Keys.kbF11)]
    [InlineData(false, SdlKeyTranslator.SDLK_F12, (ushort)0, Keys.kbF12)]
    public void NamedKeysRemainIndependentOfText(bool gpu, uint keycode, ushort mod, ushort expected) =>
        PressAndReleaseCarryThePhysicalIdentity(gpu, keycode, mod, expected);
}
