using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TSharpVision.Constants;

namespace TSharpVision.Diagnostics.Keyboard.Protocol;

/// <summary>One line of the controller/target conversation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Begin), "begin")]
[JsonDerivedType(typeof(Ready), "ready")]
[JsonDerivedType(typeof(Inject), "inject")]
[JsonDerivedType(typeof(End), "end")]
[JsonDerivedType(typeof(Result), "result")]
[JsonDerivedType(typeof(Quit), "quit")]
public abstract record Message;

/// <summary>Target to controller, once: proves the target is the process the controller launched.</summary>
public sealed record Hello(string Token, int Pid, string Driver, int Capabilities, string Os, int ProtocolVersion)
    : Message
{
    /// <summary>The window that receives the target's keyboard input (Windows HWND); zero when unknown.</summary>
    public long Hwnd { get; init; }
    /// <summary>What hosts that window: <c>SDL window</c>, <c>conhost</c>, <c>Windows Terminal</c>, or empty.</summary>
    public string Host { get; init; } = string.Empty;
}

/// <summary>
/// Controller to target (Level B): put these native events into the target's own input queue, so that
/// the running driver reads them the way it reads real input. The layout is bypassed: the events
/// already say what the layout would have produced.
/// </summary>
public sealed record Inject(int Step, IReadOnlyList<NativeEvent> Events) : Message;

/// <summary>One native input event, in the terms of the queue it is written to.</summary>
/// <remarks>
/// Console: a KEY_EVENT_RECORD (<c>Key</c> = virtual key, <c>Scan</c> = virtual scan code, <c>Char</c> =
/// UnicodeChar, <c>State</c> = dwControlKeyState). SDL: a keyboard event (<c>Key</c> = SDL keycode,
/// <c>Scan</c> = SDL scancode, <c>State</c> = SDL keymod), a text-input event, or a focus-lost event.
/// </remarks>
public sealed record NativeEvent(string Kind, bool Down = false, uint Key = 0, ushort Scan = 0, ushort Char = 0,
    uint State = 0, bool Repeat = false, string Text = "")
{
    public const string KeyKind = "key";
    public const string TextKind = "text";
    public const string FocusLostKind = "focus-lost";
}

/// <summary>Controller to target: clear the event buffer and start recording step <paramref name="Step"/>.</summary>
public sealed record Begin(int Step, int Index, int Total, string Profile, string Caption, string Expected)
    : Message;

/// <summary>
/// Target to controller: the buffer is clear and the step is recording. Input is delivered only after
/// this, so an event can never be attributed to the wrong step or lost before recording started.
/// </summary>
public sealed record Ready(int Step) : Message;

/// <summary>
/// Controller to target: report the step once keyboard input has been quiet for <paramref name="SettleMs"/>,
/// or after <paramref name="TimeoutMs"/> at the latest. A timeout of zero reports immediately.
/// </summary>
public sealed record End(int Step, int SettleMs, int TimeoutMs) : Message;

/// <summary>Target to controller: every keyboard event of the step, in order.</summary>
/// <remarks>Capabilities travel with each result because Terminal negotiation changes them at runtime.</remarks>
public sealed record Result(int Step, int Capabilities, IReadOnlyList<RecordedEvent> Events) : Message;

/// <summary>Controller to target: leave.</summary>
public sealed record Quit : Message;

/// <summary>One keyboard <see cref="TEvent"/> as the target received it, before any routing.</summary>
public sealed record RecordedEvent(int Seq, string What, ushort KeyCode, byte CharCode, byte LegacyScan,
    byte RawScan, uint State, string Text, double TMs)
{
    public const string KeyDown = "KeyDown";
    public const string KeyUp = "KeyUp";
    public const string ModifierChanged = "ModifierChanged";

    public static bool IsKeyboard(ushort what) =>
        what is Events.evKeyDown or Events.evKeyUp or Events.evModifierChanged;

    public static RecordedEvent From(TEvent ev, int seq, double elapsedMs = 0) => new(
        seq,
        ev.What switch { Events.evKeyDown => KeyDown, Events.evKeyUp => KeyUp, _ => ModifierChanged },
        ev.keyDown.keyCode, ev.keyDown.charScan.charCode, ev.keyDown.charScan.scanCode,
        ev.keyDown.raw_scanCode, ev.keyDown.controlKeyState, ev.keyDown.text, elapsedMs);

    /// <summary>Converts events drained from a driver seam, for Level-A tests.</summary>
    public static IReadOnlyList<RecordedEvent> FromAll(IEnumerable<TEvent> events) =>
        events.Where(ev => IsKeyboard(ev.What)).Select((ev, i) => From(ev, i)).ToArray();
}

/// <summary>Wire format and handshake rules shared by both roles.</summary>
public static class Wire
{
    public const int Version = 2;
    public const string PipeVariable = "TSHARPVISION_KBDIAG_PIPE";
    public const string TokenVariable = "TSHARPVISION_KBDIAG_TOKEN";

    // The default encoder escapes non-ASCII, which keeps invisible characters visible in logs.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>One JSON object on one line, without the line terminator.</summary>
    public static string Serialize(Message message) => JsonSerializer.Serialize(message, Options);

    /// <summary>Parses one line; returns null for anything that is not a known message.</summary>
    public static Message? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<Message>(line, Options); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Returns why a hello is unacceptable, or null when it is the launched target.</summary>
    public static string? ValidateHello(Message? message, string token, int? launchedPid)
    {
        if (message is not Hello hello) return "the target's first message was not a hello";
        if (hello.ProtocolVersion != Version)
            return $"protocol version {hello.ProtocolVersion} is not {Version}";
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hello.Token ?? string.Empty), Encoding.UTF8.GetBytes(token)))
            return "session token mismatch";
        if (launchedPid is int pid && hello.Pid != pid)
            return $"hello came from process {hello.Pid}, not the launched target {pid}";
        return null;
    }
}
