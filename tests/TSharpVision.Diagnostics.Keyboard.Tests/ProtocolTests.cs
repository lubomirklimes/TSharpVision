using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Tests;

public sealed class ProtocolTests
{
    private static readonly RecordedEvent Ecaron = new(0, RecordedEvent.KeyDown, 0, 0, 0, 0x03, 0, "ě", 4.5);

    public static IEnumerable<object[]> Messages() =>
    [
        [new Hello("TOKEN", 1234, "SDLDriver", 7, "windows", Wire.Version)],
        [new Begin(17, 3, 48, "Czech QWERTY", "Tap Digit2", "\"ě\"")],
        [new Ready(17)],
        [new End(17, 120, 2000)],
        [new Quit()],
    ];

    [Theory, MemberData(nameof(Messages))]
    public void MessagesRoundTripAsOneJsonLine(Message message)
    {
        string line = Wire.Serialize(message);

        Assert.DoesNotContain('\n', line);
        Assert.StartsWith("{\"type\":\"", line);
        Assert.Equal(message, Wire.Parse(line));
    }

    [Fact]
    public void ResultCarriesEveryRecordedFieldAndTheCapabilities()
    {
        var result = new Result(17, 7, [Ecaron, new RecordedEvent(1, RecordedEvent.KeyUp, 0, 0, 0, 0x03, 0, "", 80)]);

        string line = Wire.Serialize(result);
        var parsed = Assert.IsType<Result>(Wire.Parse(line));

        Assert.Equal(17, parsed.Step);
        Assert.Equal(7, parsed.Capabilities);
        Assert.Equal(result.Events, parsed.Events);
        // Non-ASCII text stays visible in a log as an escape, never as a raw byte sequence.
        Assert.Contains("\\u011B", line);
        Assert.All(line, c => Assert.InRange(c, ' ', '~'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"poke\",\"step\":1}")]
    [InlineData("[1,2,3]")]
    public void UnknownOrMalformedLinesAreNotMessages(string line) => Assert.Null(Wire.Parse(line));

    [Fact]
    public void RecordedEventCopiesEveryKeyboardFieldOfATEvent()
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = Keys.kbF5;
        ev.keyDown.charScan = new CharScanType(Keys.kbF5);
        ev.keyDown.raw_scanCode = 0x3F;
        ev.keyDown.controlKeyState = Keys.kbLeftShift;
        ev.keyDown.text = "x";

        RecordedEvent recorded = RecordedEvent.From(ev, 5, 12.5);

        Assert.Equal(new RecordedEvent(5, RecordedEvent.KeyDown, Keys.kbF5, 0x00, 0x3F, 0x3F, Keys.kbLeftShift, "x", 12.5),
            recorded);
    }

    [Fact]
    public void OnlyKeyboardEventsAreConverted()
    {
        TEvent mouse = default, up = default, modifier = default;
        mouse.What = Events.evMouseDown;
        up.What = Events.evKeyUp;
        modifier.What = Events.evModifierChanged;
        modifier.Modifiers = Keys.kbLeftShift;

        IReadOnlyList<RecordedEvent> events = RecordedEvent.FromAll([mouse, up, modifier]);

        Assert.Equal([RecordedEvent.KeyUp, RecordedEvent.ModifierChanged], events.Select(e => e.What));
        Assert.Equal([0, 1], events.Select(e => e.Seq));
        Assert.Equal(Keys.kbLeftShift, events[1].State);
    }

    [Fact]
    public void HelloFromTheLaunchedTargetIsAccepted()
    {
        string token = Wire.NewToken();
        var hello = new Hello(token, 42, "Win32ConsoleDriver", 7, "windows", Wire.Version);

        Assert.Null(Wire.ValidateHello(hello, token, 42));
        Assert.Null(Wire.ValidateHello(hello, token, launchedPid: null));
    }

    [Fact]
    public void HelloIsRefusedForAWrongTokenVersionProcessOrMessage()
    {
        string token = Wire.NewToken();
        var hello = new Hello(token, 42, "SDLDriver", 7, "linux", Wire.Version);

        Assert.Contains("token", Wire.ValidateHello(hello with { Token = Wire.NewToken() }, token, 42));
        Assert.Contains("token", Wire.ValidateHello(hello with { Token = "" }, token, 42));
        Assert.Contains("version", Wire.ValidateHello(hello with { ProtocolVersion = Wire.Version + 1 }, token, 42));
        Assert.Contains("process", Wire.ValidateHello(hello, token, 43));
        Assert.Contains("hello", Wire.ValidateHello(new Quit(), token, 42));
        Assert.Contains("hello", Wire.ValidateHello(null, token, 42));
    }

    [Fact]
    public void TokensAreLongAndNeverRepeat()
    {
        string first = Wire.NewToken(), second = Wire.NewToken();

        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ChannelFramesMessagesAsLinesAndReportsTheEndOfTheStream()
    {
        using var stream = new MemoryStream();
        using (var writer = new PipeChannel(new NonClosingStream(stream)))
        {
            writer.Send(new Begin(1, 1, 2, "US English", "Tap F5", "kbF5"));
            writer.Send(new Result(1, 0, [Ecaron]));
        }
        Assert.Equal(2, stream.ToArray().Count(b => b == (byte)'\n'));
        Assert.DoesNotContain((byte)'\r', stream.ToArray());

        stream.Position = 0;
        using var reader = new PipeChannel(stream);
        Assert.IsType<Begin>(await reader.ReceiveAsync());
        Assert.Equal("ě", Assert.Single(Assert.IsType<Result>(await reader.ReceiveAsync()).Events).Text);
        Assert.Null(await reader.ReceiveAsync());
    }

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
