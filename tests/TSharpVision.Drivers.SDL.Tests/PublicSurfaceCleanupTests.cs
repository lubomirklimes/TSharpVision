using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Renderer;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class PublicSurfaceCleanupTests
{
    [Fact]
    public void BackendTranslationAndRenderingHelpersAreInternal()
    {
        Assert.False(typeof(SdlKeyTranslator).IsPublic);
        Assert.False(typeof(SdlMouseTranslator).IsPublic);
        Assert.False(typeof(SdlMouseEventKind).IsPublic);
        Assert.False(typeof(SdlPalette).IsPublic);
        Assert.False(typeof(ISDLRenderer).IsPublic);
        Assert.False(typeof(SDLRenderer).IsPublic);
    }

    [Fact]
    public void SdlTranslatedKeysAlwaysExposeNonNullText()
    {
        Assert.True(SdlKeyTranslator.TryTranslate(SdlKeyTranslator.SDLK_F1, 0, '\0', out TEvent ev));
        Assert.NotNull(ev.keyDown.text);
        Assert.Same(string.Empty, ev.keyDown.text);
    }
}
