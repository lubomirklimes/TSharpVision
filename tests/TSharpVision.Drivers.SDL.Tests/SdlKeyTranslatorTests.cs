// SDL key translator tests.
// Pure translation: no SDL runtime, no window, no P/Invoke.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlKeyTranslatorTests
{
    // ── Plain letters ─────────────────────────────────────────────────────

    [Fact]
    public void SDL_PlainA_NoModifiers()
    {
        bool ok = SdlKeyTranslator.TryTranslate('a', 0, 'a', out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal((ushort)'a', ev.keyDown.keyCode);
        Assert.Equal(0u, ev.keyDown.controlKeyState);
    }

    [Fact]
    public void SDL_ShiftA_UpperCase()
    {
        bool ok = SdlKeyTranslator.TryTranslate('a', SdlKeyTranslator.SDL_KMOD_LSHIFT, 'A', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)'A', ev.keyDown.keyCode);
        Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbShift);
    }

    [Fact]
    public void SDL_CtrlC()
    {
        bool ok = SdlKeyTranslator.TryTranslate('c', SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlC, ev.keyDown.keyCode);
        Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbCtrlShift);
    }

    [Fact]
    public void SDL_AltX()
    {
        bool ok = SdlKeyTranslator.TryTranslate('x', SdlKeyTranslator.SDL_KMOD_LALT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltX, ev.keyDown.keyCode);
        Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbAltShift);
    }

    [Fact]
    public void SDL_RightAlt1()
    {
        bool ok = SdlKeyTranslator.TryTranslate('1', SdlKeyTranslator.SDL_KMOD_RALT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAlt1, ev.keyDown.keyCode);
    }

    // ── Function keys ─────────────────────────────────────────────────────

    [Fact]
    public void SDL_F1()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F1, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbF1, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_ShiftF1()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F1, SdlKeyTranslator.SDL_KMOD_LSHIFT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbShiftF1, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_CtrlF12()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F12, SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlF12, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_AltF4()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F4, SdlKeyTranslator.SDL_KMOD_LALT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltF4, ev.keyDown.keyCode);
    }

    // ── Navigation keys ───────────────────────────────────────────────────

    [Fact]
    public void SDL_Up()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_UP, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbUp, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_CtrlLeft()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_LEFT, SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlLeft, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_CtrlHome()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_HOME, SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlHome, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_Tab()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_TAB, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbTab, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_ShiftTab()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_TAB, SdlKeyTranslator.SDL_KMOD_LSHIFT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbShiftTab, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_Esc()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_ESCAPE, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbEsc, ev.keyDown.keyCode);
    }

    [Fact]
    public void SDL_Enter()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_RETURN, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbEnter, ev.keyDown.keyCode);
    }

    // ── F11/F12 identity and Enter with modifiers (KEYBOARD-NORM) ─────────

    [Theory]
    [InlineData(SdlKeyTranslator.SDLK_F11, (ushort)0, Keys.kbF11)]
    [InlineData(SdlKeyTranslator.SDLK_F12, (ushort)0, Keys.kbF12)]
    [InlineData(SdlKeyTranslator.SDLK_F4, SdlKeyTranslator.SDL_KMOD_LSHIFT, Keys.kbShiftF4)]
    [InlineData(SdlKeyTranslator.SDLK_F5, SdlKeyTranslator.SDL_KMOD_LSHIFT, Keys.kbShiftF5)]
    [InlineData(SdlKeyTranslator.SDLK_F11, SdlKeyTranslator.SDL_KMOD_LSHIFT, Keys.kbShiftF11)]
    [InlineData(SdlKeyTranslator.SDLK_F12, SdlKeyTranslator.SDL_KMOD_RSHIFT, Keys.kbShiftF12)]
    [InlineData(SdlKeyTranslator.SDLK_F11, SdlKeyTranslator.SDL_KMOD_LCTRL, Keys.kbCtrlF11)]
    [InlineData(SdlKeyTranslator.SDLK_F12, SdlKeyTranslator.SDL_KMOD_RCTRL, Keys.kbCtrlF12)]
    [InlineData(SdlKeyTranslator.SDLK_F11, SdlKeyTranslator.SDL_KMOD_LALT, Keys.kbAltF11)]
    [InlineData(SdlKeyTranslator.SDLK_F12, SdlKeyTranslator.SDL_KMOD_LALT, Keys.kbAltF12)]
    public void SDL_FunctionKeysKeepTheirPhysicalIdentity(uint keycode, ushort mod, ushort expected)
    {
        Assert.True(SdlKeyTranslator.TryTranslate(keycode, mod, '\0', out var ev));
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
    }

    [Fact]
    public void SDL_F11IsNotShiftF4AndF12IsNotShiftF5()
    {
        SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F11, 0, '\0', out var f11);
        SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F4, SdlKeyTranslator.SDL_KMOD_LSHIFT, '\0', out var shiftF4);
        SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F12, 0, '\0', out var f12);
        SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F5, SdlKeyTranslator.SDL_KMOD_LSHIFT, '\0', out var shiftF5);
        Assert.NotEqual(shiftF4.keyDown.keyCode, f11.keyDown.keyCode);
        Assert.NotEqual(shiftF5.keyDown.keyCode, f12.keyDown.keyCode);
    }

    /// <summary>
    /// Ctrl+Enter has a key code of its own (kbCtrlEnter), as the Console driver and the Kitty protocol report it;
    /// Shift+Enter and Alt+Enter have none, so they stay Enter with their modifier state preserved.
    /// </summary>
    [Theory]
    [InlineData((ushort)0, Keys.kbEnter)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LSHIFT, Keys.kbEnter)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LCTRL, Keys.kbCtrlEnter)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_RCTRL, Keys.kbCtrlEnter)]
    [InlineData(SdlKeyTranslator.SDL_KMOD_LALT, Keys.kbEnter)]
    [InlineData((ushort)(SdlKeyTranslator.SDL_KMOD_LCTRL | SdlKeyTranslator.SDL_KMOD_LSHIFT), Keys.kbCtrlEnter)]
    public void SDL_EnterWithModifiers(ushort mod, ushort expected)
    {
        Assert.True(SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_RETURN, mod, '\0', out var ev));
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(SdlKeyTranslator.ToShiftState(mod), ev.keyDown.controlKeyState);
    }

    // ── Modifier-only keys are filtered ───────────────────────────────────

    [Fact]
    public void SDL_BareShift_Filtered()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_LSHIFT, SdlKeyTranslator.SDL_KMOD_LSHIFT, '\0', out _);
        Assert.False(ok);
    }

    // ── Non-ASCII TextInput path ───────────────────────────────────────────

    [Fact]
    public void SDL_NonAsciiTextInput_NotHandledByTranslator()
    {
        // 0x00E1 is the SDL keycode for 'á' (Latin small a with acute).
        // The SDL driver handles non-ASCII via SDL_TEXTINPUT events; the
        // key translator must return false so the driver takes the text path.
        bool ok = SdlKeyTranslator.TryTranslate(0x00E1, 0, 'á', out _);
        Assert.False(ok);
    }
    [Theory]
    [InlineData('ě')]
    [InlineData('š')]
    [InlineData('č')]
    [InlineData('ř')]
    [InlineData('\uD83D')]
    public void OptionalUnicodeTextNeverBecomesLegacyIdentity(char textChar)
    {
        foreach (uint native in new uint[] { 'a', '2', '?' })
        {
            Assert.True(SdlKeyTranslator.TryTranslate(native, SdlKeyTranslator.SDL_KMOD_LSHIFT, textChar, out var ev));
            Assert.Equal((ushort)0, ev.keyDown.keyCode);
            Assert.Equal((byte)0, ev.keyDown.charScan.charCode);
            Assert.Equal((byte)0, ev.keyDown.charScan.scanCode);
            Assert.Equal(textChar.ToString(), ev.keyDown.text);
            Assert.Equal(Keys.kbShift, ev.keyDown.controlKeyState);
        }
    }
}
