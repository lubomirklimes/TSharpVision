using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class TerminalIdleWaitTests
{
    [Fact]
    public void EscapeDeadlineTracksFakeTimeAndEmitsExactlyOnce()
    {
        long now = 1000;
        var decoder = new TerminalInputDecoder(() => now);
        Assert.Equal(-1, decoder.NextWaitTimeout);
        decoder.Feed(new byte[] { 27 }, _ => { });
        Assert.Equal(50, decoder.NextWaitTimeout);
        now += 49;
        Assert.Equal(1, decoder.NextWaitTimeout);
        Assert.False(decoder.TryRead(out _));
        now++;
        Assert.Equal(0, decoder.NextWaitTimeout);
        Assert.True(decoder.TryRead(out var ev));
        Assert.Equal(Keys.kbEsc, ev.keyDown.keyCode);
        Assert.Equal(-1, decoder.NextWaitTimeout);
        Assert.False(decoder.TryRead(out _));
    }

    [Theory]
    [InlineData(-1, -1, 100)]
    [InlineData(20, 50, 20)]
    [InlineData(-1, 50, 50)]
    [InlineData(500, -1, 100)]
    [InlineData(500, 0, 0)]
    public void EarliestApplicationDecoderOrResizeDeadlineWins(int app, int decoder, int expected)
        => Assert.Equal(expected, AnsiTerminalDriver.ComputeWaitTimeout(app, decoder));

    [Fact]
    public void ShutdownPreventsFurtherWaiting()
    {
        using var driver = new AnsiTerminalDriver();
        using var signal = new ManualResetEvent(false);
        driver.Shutdown();
        Assert.Throws<ObjectDisposedException>(() => driver.WaitForEvent(-1, signal));
    }
}
