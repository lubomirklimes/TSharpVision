// SDL quit/window-close event translation tests.
// Pure: no SDL runtime, no window, no P/Invoke.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class SdlQuitEventTests
{
    // ── MakeQuitEvent (pure, no SDL required) ────────────────────────────

    [Fact]
    public void MakeQuitEvent_EventKindIsCommand()
    {
        var ev = SDLDriver.MakeQuitEvent();
        Assert.Equal(Events.evCommand, ev.What);
    }

    [Fact]
    public void MakeQuitEvent_CommandIsCmQuit()
    {
        var ev = SDLDriver.MakeQuitEvent();
        Assert.Equal(Views.cmQuit, ev.message.command);
    }

    [Fact]
    public void MakeQuitEvent_CommandIsNotZero()
    {
        // Guard against accidentally posting cmValid (0) which is a no-op.
        var ev = SDLDriver.MakeQuitEvent();
        Assert.NotEqual(Views.cmValid, ev.message.command);
    }

    [Fact]
    public void MakeQuitEvent_CommandMatchesCmQuitConstant()
    {
        // Regression: the driver previously posted 0xFFFF instead of cmQuit (1).
        var ev = SDLDriver.MakeQuitEvent();
        Assert.Equal((ushort)1, ev.message.command);
    }
}
