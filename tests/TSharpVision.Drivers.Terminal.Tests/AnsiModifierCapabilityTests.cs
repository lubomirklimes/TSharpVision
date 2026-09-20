using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class AnsiModifierCapabilityTests
{
    [Fact]
    public void PlainAnsiExplicitlyAdvertisesNoKeyboardCapabilities()
        => Assert.Equal(KeyboardCapabilities.None, new AnsiTerminalDriver().KeyboardCapabilities);

    [Fact]
    public void ModifiedOrdinaryKeyKeepsExistingKeyDownSemantics()
    {
        int consumed = AnsiKeyDecoder.TryDecode("\x1b[1;2A"u8, out TEvent ev, out bool complete);
        Assert.True(complete);
        Assert.Equal(6, consumed);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.NotEqual(0u, ev.Modifiers & Keys.kbShift);
    }

    [Fact]
    public void DecoderDoesNotSynthesizeStandaloneReleaseEvents()
    {
        int consumed = AnsiKeyDecoder.TryDecode("\x1b[1;5A"u8, out TEvent ev, out bool complete);
        Assert.True(complete);
        Assert.Equal(6, consumed);
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.NotEqual(0u, ev.Modifiers & Keys.kbCtrlShift);

        Assert.Equal(0, AnsiKeyDecoder.TryDecode(ReadOnlySpan<byte>.Empty, out TEvent none, out complete));
        Assert.False(complete);
        Assert.Equal(Events.evNothing, none.What);
    }

    [Fact]
    public void RepeatedAnsiSequencesRemainRepeatedKeyDownEvents()
    {
        ReadOnlySpan<byte> sequence = "\x1b[A\x1b[A\x1b[A"u8;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            int consumed = AnsiKeyDecoder.TryDecode(sequence, out TEvent ev, out bool complete);
            Assert.True(complete);
            Assert.Equal(3, consumed);
            Assert.Equal(Events.evKeyDown, ev.What);
            Assert.NotEqual(Events.evKeyUp, ev.What);
            sequence = sequence[consumed..];
        }
        Assert.True(sequence.IsEmpty);
    }
}
