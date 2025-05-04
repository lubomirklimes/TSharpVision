// SDL key translator tests.
// Pure translation: no SDL runtime, no window, no P/Invoke.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers.SDL;
using Xunit;

namespace SharpVision.Tests.Drivers;

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
        Assert.Equal(0, ev.keyDown.shiftState);
    }

    [Fact]
    public void SDL_ShiftA_UpperCase()
    {
        bool ok = SdlKeyTranslator.TryTranslate('a', SdlKeyTranslator.SDL_KMOD_LSHIFT, 'A', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)'A', ev.keyDown.keyCode);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbShift);
    }

    [Fact]
    public void SDL_CtrlC()
    {
        bool ok = SdlKeyTranslator.TryTranslate('c', SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlC, ev.keyDown.keyCode);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbCtrlShift);
    }

    [Fact]
    public void SDL_AltX()
    {
        bool ok = SdlKeyTranslator.TryTranslate('x', SdlKeyTranslator.SDL_KMOD_LALT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltX, ev.keyDown.keyCode);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbAltShift);
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
}
