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
}
