using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;
namespace TSharpVision.Drivers.SDL.Tests;

// TV-1.1-FIX-3: Shift+arrow keeps the plain arrow code and reports Shift in controlKeyState;
// TView.DragView resizes on that combination.
public sealed class ShiftedArrowContractTests
{
    [Theory]
    [InlineData(SdlKeyTranslator.SDLK_LEFT,  Keys.kbLeft)]
    [InlineData(SdlKeyTranslator.SDLK_RIGHT, Keys.kbRight)]
    [InlineData(SdlKeyTranslator.SDLK_UP,    Keys.kbUp)]
    [InlineData(SdlKeyTranslator.SDLK_DOWN,  Keys.kbDown)]
    public void ShiftArrowIsArrowWithShift(uint native, ushort code)
    {
        foreach (ushort mod in new[] { SdlKeyTranslator.SDL_KMOD_LSHIFT, SdlKeyTranslator.SDL_KMOD_RSHIFT })
        {
            Assert.True(SdlKeyTranslator.TryTranslate(native, mod, '\0', out var ev));
            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.NotEqual(0u, ev.keyDown.controlKeyState & Keys.kbShift);
        }
        Assert.True(SdlKeyTranslator.TryTranslate(native, 0, '\0', out var plain));
        Assert.Equal(code, plain.keyDown.keyCode);
        Assert.Equal(0u, plain.keyDown.controlKeyState & Keys.kbShift);
    }
}
