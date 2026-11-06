using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

/// <summary>The console reports a character outside the BMP as two key records, one surrogate each (TV13d).</summary>
public sealed class Win32SurrogateInputTests
{
    private const ushort VkPacket = 0xE7;
    private const string Emoji = "\U0001F600";

    private static bool Press(Win32InputTranslator translator, char character, out TEvent ev)
        => translator.TryTranslate(true, VkPacket, 0, character, 0, out ev);

    [Fact]
    public void ASurrogatePairBecomesOneTextEvent()
    {
        var translator = new Win32InputTranslator();
        Assert.False(Press(translator, Emoji[0], out _));
        Assert.True(Press(translator, Emoji[1], out TEvent ev));
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(Emoji, ev.keyDown.text);
    }

    [Fact]
    public void ReleasesBetweenTheHalvesDoNotBreakThePair()
    {
        var translator = new Win32InputTranslator();
        Assert.False(Press(translator, Emoji[0], out _));
        translator.TryTranslate(false, VkPacket, 0, Emoji[0], 0, out _);
        Assert.True(Press(translator, Emoji[1], out TEvent ev));
        Assert.Equal(Emoji, ev.keyDown.text);
    }

    [Fact]
    public void AnUnpairedHighSurrogateIsDroppedAndTheNextKeyIsUnaffected()
    {
        var translator = new Win32InputTranslator();
        Assert.False(Press(translator, '\uD83D', out _));
        Assert.True(translator.TryTranslate(true, 'A', 0x1E, 'a', 0, out TEvent ev));
        Assert.Equal("a", ev.keyDown.text);
        Assert.True(Press(translator, 'b', out ev));
        Assert.Equal("b", ev.keyDown.text);
    }

    [Fact]
    public void BmpTextIsUnchanged()
    {
        var translator = new Win32InputTranslator();
        Assert.True(Press(translator, 'č', out TEvent ev));
        Assert.Equal("č", ev.keyDown.text);
    }
}
