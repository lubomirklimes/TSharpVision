// Tests for SDL text-input key translation and cursor state tracking.
// All tests are pure: no SDL runtime, no window, no P/Invoke.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class SdlTextInputAndCursorTests
{
    // ── Text input via SdlKeyTranslator (simulates SDL_TEXTINPUT path) ───

    [Fact]
    public void TextInput_LowercaseA_ProducesLowercaseA()
    {
        // The SDL driver receives SDL_TEXTINPUT "a" and calls TryTranslate with
        // the textChar from the event. The key translator must produce 'a'.
        bool ok = SdlKeyTranslator.TryTranslate('a', 0, 'a', out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal((ushort)'a', ev.keyDown.keyCode);
        Assert.Equal((byte)'a', ev.keyDown.charScan.charCode);
    }

    [Fact]
    public void TextInput_UppercaseA_ProducesUppercaseA()
    {
        // Shift+A: the OS layout produces 'A' in TextInput.
        bool ok = SdlKeyTranslator.TryTranslate('a', SdlKeyTranslator.SDL_KMOD_LSHIFT, 'A', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)'A', ev.keyDown.keyCode);
        Assert.Equal((byte)'A', ev.keyDown.charScan.charCode);
    }

    [Fact]
    public void TextInput_Space_ProducesSpace()
    {
        bool ok = SdlKeyTranslator.TryTranslate(' ', 0, ' ', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)' ', ev.keyDown.keyCode);
        Assert.Equal((byte)' ', ev.keyDown.charScan.charCode);
    }

    [Fact]
    public void TextInput_Digit5_ProducesDigit5()
    {
        bool ok = SdlKeyTranslator.TryTranslate('5', 0, '5', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)'5', ev.keyDown.keyCode);
        Assert.Equal((byte)'5', ev.keyDown.charScan.charCode);
    }

    [Fact]
    public void TextInput_Exclamation_ProducesExclamation()
    {
        // '!' is in the ASCII printable range (0x21); textChar from the layout.
        bool ok = SdlKeyTranslator.TryTranslate('!', 0, '!', out var ev);
        Assert.True(ok);
        Assert.Equal((ushort)'!', ev.keyDown.keyCode);
        Assert.Equal((byte)'!', ev.keyDown.charScan.charCode);
    }

    [Fact]
    public void TextInput_Backspace_IsKeyEvent_NotTextInput()
    {
        // Backspace is a control key: TryTranslate must produce a key event,
        // and it must NOT go through the printable TextInput path.
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_BACKSPACE, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbBack, ev.keyDown.keyCode);
    }

    [Fact]
    public void TextInput_Enter_IsKeyEvent_NotTextInput()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_RETURN, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbEnter, ev.keyDown.keyCode);
    }

    [Fact]
    public void TextInput_Escape_IsKeyEvent_NotTextInput()
    {
        bool ok = SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_ESCAPE, 0, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbEsc, ev.keyDown.keyCode);
    }

    [Fact]
    public void TextInput_CtrlA_IsCtrlEvent_NotPrintable()
    {
        // Ctrl+A must not produce a printable 'a' even if a textChar arrives.
        bool ok = SdlKeyTranslator.TryTranslate('a', SdlKeyTranslator.SDL_KMOD_LCTRL, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlA, ev.keyDown.keyCode);
        Assert.NotEqual((ushort)'a', ev.keyDown.keyCode);
    }

    [Fact]
    public void TextInput_AltX_IsAltEvent_NotPrintable()
    {
        bool ok = SdlKeyTranslator.TryTranslate('x', SdlKeyTranslator.SDL_KMOD_LALT, '\0', out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltX, ev.keyDown.keyCode);
        Assert.NotEqual((ushort)'x', ev.keyDown.keyCode);
    }

    // ── Cursor state tracking in SDLDriver (headless) ────────────────────

    [Fact]
    public void SDLDriver_DefaultCursorType_Is100()
    {
        // The startup cursor type must be 100 (block) so TScreen.SetCrtData()
        // saves a non-zero CursorLines value before hiding the cursor.
        var d = new SDLDriver();
        Assert.Equal(100, d.GetCursorType());
    }

    [Fact]
    public void SDLDriver_SetCursorType_HidesWhenZero()
    {
        var d = new SDLDriver();
        d.SetCursorType(0);
        Assert.Equal(0, d.GetCursorType());
    }

    [Fact]
    public void SDLDriver_SetCursorType_BlockCursor()
    {
        var d = new SDLDriver();
        d.SetCursorType(100);
        Assert.Equal(100, d.GetCursorType());
    }

    [Fact]
    public void SDLDriver_SetCursorType_UnderlineCursor()
    {
        // 0x0C0D is the underline cursor value used by TView.ResetCursor.
        var d = new SDLDriver();
        d.SetCursorType(0x0C0D);
        Assert.Equal(0x0C0D, d.GetCursorType());
    }

    [Fact]
    public void SDLDriver_GetCursorType_ReturnsLastSet()
    {
        // Regression: previously GetCursorType() always returned hardcoded 100.
        var d = new SDLDriver();
        d.SetCursorType(25);
        Assert.Equal(25, d.GetCursorType());
        d.SetCursorType(0);
        Assert.Equal(0, d.GetCursorType());
    }
}
