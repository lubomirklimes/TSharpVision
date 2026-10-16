using System.Text;
using TSharpVision.Constants;
using Xunit;
using TSharpVision.Tests.Infrastructure;

namespace TSharpVision.Drivers.Terminal.Tests;

[Collection("NonParallel")]
public sealed class AnsiStabilizationTests
{
    [Fact]
    public void StaticTextLiteralPayloadUsesSanitizedCellOutput()
    {
        using var driver = new DriverScope(40, 1);
        var host = new TestGroup(new TRect(0, 0, 40, 1)) { buffer = new ScreenBuffer(40) };
        host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        host.Insert(new TStaticText(new TRect(0, 0, 40, 1), "\x1b]52;c;payload\a"));
        host.Redraw();
        Assert.Contains(host.buffer!.Data.ToArray(), cell => cell.Character == '\x1b');
        string output = AnsiTerminalDriver.FormatBuffer(0, 0, 40, 1, host.buffer.Data, 0, 0);
        Assert.DoesNotContain("\x1b]52", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\a", output, StringComparison.Ordinal);
        host.ShutDown();
    }

    [Fact]
    public void LoneEscapeExpiresExactlyOnceAndCannotModifyLaterKey()
    {
        long now = 0;
        var decoder = new TerminalInputDecoder(() => now);
        decoder.Feed("\x1b"u8, _ => { });
        Assert.False(decoder.TryRead(out _));
        now = TerminalInputDecoder.EscapeDelayMilliseconds - 1;
        Assert.False(decoder.TryRead(out _));
        now++;
        decoder.Feed("a"u8, _ => { });
        Assert.True(decoder.TryRead(out var escape));
        Assert.Equal(Keys.kbEsc, escape.keyDown.keyCode);
        Assert.True(decoder.TryRead(out var key));
        Assert.Equal((ushort)'a', key.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void IdlePollingFinalizesEscape()
    {
        long now = 0;
        var decoder = new TerminalInputDecoder(() => now);
        decoder.Feed("\x1b"u8, _ => { });
        now = TerminalInputDecoder.EscapeDelayMilliseconds;
        Assert.True(decoder.TryRead(out var key));
        Assert.Equal(Keys.kbEsc, key.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void RepeatedEscapeGetsItsOwnAmbiguityWindow()
    {
        long now = 0;
        var decoder = new TerminalInputDecoder(() => now);
        decoder.Feed("\x1b"u8, _ => { });
        now = TerminalInputDecoder.EscapeDelayMilliseconds - 1;
        decoder.Feed("\x1b"u8, _ => { });
        Assert.True(decoder.TryRead(out var first));
        Assert.Equal(Keys.kbEsc, first.keyDown.keyCode);
        now++;
        Assert.False(decoder.TryRead(out _));
        now += TerminalInputDecoder.EscapeDelayMilliseconds;
        Assert.True(decoder.TryRead(out var second));
        Assert.Equal(Keys.kbEsc, second.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    [Theory]
    [InlineData("[A", Keys.kbUp)]
    [InlineData("a", Keys.kbAltA)]
    public void PrefixWithinWindowStillDecodes(string suffix, ushort expected)
    {
        long now = 0;
        var decoder = new TerminalInputDecoder(() => now);
        decoder.Feed("\x1b"u8, _ => { });
        now = TerminalInputDecoder.EscapeDelayMilliseconds - 1;
        foreach (byte value in Encoding.ASCII.GetBytes(suffix)) decoder.Feed(new[] { value }, _ => { });
        Assert.True(decoder.TryRead(out var key));
        Assert.Equal(expected, key.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    [Theory]
    [InlineData("9999999999999999999999999999999999999999999")]
    [InlineData("0;999999999999999999999;1M")]
    [InlineData("0;;1M")]
    [InlineData("0;1!1M")]
    public void MalformedMouseIsBoundedAndLaterInputRecovers(string body)
    {
        var decoder = new TerminalInputDecoder();
        string malformed = "\x1b[<" + body;
        foreach (byte value in Encoding.ASCII.GetBytes(malformed))
        {
            decoder.Feed(new[] { value }, _ => { });
            Assert.InRange(decoder.PendingByteCount, 0, 32);
        }
        decoder.Feed("z\x1b[<0;11;6M"u8, _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out var ev)) events.Add(ev);
        Assert.Contains(events, ev => ev.What == Events.evKeyDown && ev.keyDown.keyCode == 'z');
        Assert.Contains(events, ev => ev.What == Events.evMouseDown && ev.mouse.where.x == 10 && ev.mouse.where.y == 5);
        Assert.Equal(0, decoder.PendingByteCount);
    }

    [Fact]
    public void VeryLongMalformedMouseFeedDoesNotRetainPrefix()
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes("\x1b[<" + new string('9', 100_000)), _ => { });
        Assert.InRange(decoder.PendingByteCount, 0, 32);
    }

    [Theory]
    [InlineData("\x1b[<0;;1M")]
    [InlineData("\x1b[<0;1!1M")]
    [InlineData("\x1b[<0;65536;1M")]
    [InlineData("\x1b[<0;000000;1M")]
    [InlineData("\x1b[<256;1;1M")]
    [InlineData("\x1b[<0;0;1M")]
    public void InvalidMouseDoesNotProduceMouseEvent(string input)
    {
        int consumed = AnsiMouseDecoder.TryDecode(Encoding.ASCII.GetBytes(input), out var ev, out bool complete);
        Assert.True(complete);
        Assert.InRange(consumed, 3, input.Length);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void AllC0AndC1CellControlsBecomeSpaces()
    {
        char[] controls = Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(i => (char)i).ToArray();
        var attr = new TColorAttr(7);
        var cells = controls.Select(ch => new TScreenChar(ch, attr)).ToArray();
        Assert.Equal("\x1b[1;1H" + AnsiTerminalDriver.AttrToSgr(attr) + new string(' ', cells.Length) + "\x1b[0m\x1b[1;1H",
            AnsiTerminalDriver.FormatBuffer(0, 0, cells.Length, 1, cells, 0, 0));
    }

    [Fact]
    public void FragmentedMouseRemainsValid()
    {
        var decoder = new TerminalInputDecoder();
        foreach (byte value in "\x1b[<0;11;6M"u8) decoder.Feed(new[] { value }, _ => { });
        Assert.True(decoder.TryRead(out var ev));
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(10, ev.mouse.where.x);
        Assert.False(decoder.TryRead(out _));
    }

    [Fact]
    public void LiteralControlsCannotInjectButGeneratedAnsiAndUnicodeRemain()
    {
        const string text = "\x1b]52;c;payload\a\x9b\x9d\x7f\nABC č─🙂";
        var attr = new TColorAttr(7);
        var cells = text.Select(ch => new TScreenChar(ch, attr)).ToArray();
        string output = AnsiTerminalDriver.FormatBuffer(0, 0, cells.Length, 1, cells, 2, 3);
        string safe = new(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        Assert.Equal("\x1b[1;1H" + AnsiTerminalDriver.AttrToSgr(attr) + safe + "\x1b[0m\x1b[4;3H", output);
        Assert.DoesNotContain("\x1b]52", output, StringComparison.Ordinal);
    }
}
