using System.Runtime.Versioning;
using System.Text;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Terminal;
using TSharpVision.Terminal.Posix;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>
/// Level B for the Terminal driver: the target runs behind a real PTY and the controller plays the
/// terminal, byte for byte, including the Kitty keyboard negotiation replies. Deterministic, and it
/// proves the driver and transport only: no physical key and no layout is involved.
/// </summary>
public sealed class PtyByteSource : IInputSource
{
    private const string SupportQuery = "\x1b[?u";
    private const string DeviceAttributesQuery = "\x1b[c";

    private readonly int _kittyFlags;
    private readonly object _gate = new();
    private readonly StringBuilder _output = new();
    private readonly ManualResetEventSlim _negotiated = new();
    private ITerminalSession? _session;
    private int _processId, _scanned, _queries;
    private volatile bool _exited;

    /// <param name="kittyFlags">The Kitty flags the simulated terminal confirms; zero is a plain ANSI terminal.</param>
    public PtyByteSource(int kittyFlags) => _kittyFlags = kittyFlags;

    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public string Name => "pty";
    public string Level => "B (bytes through a real PTY into the Terminal driver; no layout involved)";
    public bool SharesConsole => false;
    public string Label => SourceLabels.TransportBytes;
    public bool Interactive => false;
    public bool VerifiesLayout => false;
    public bool TransportSteps => true;
    public int SettleMs => 150;
    public int TimeoutMs => 3000;
    public int? TargetProcessId => _processId == 0 ? null : _processId;
    public bool TargetExited => _exited;

    public void Start(TargetLaunch launch)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) StartPosix(launch);
        else throw new PlatformNotSupportedException("The PTY source needs Linux or macOS.");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private void StartPosix(TargetLaunch launch)
    {
        var environment = launch.Environment.ToDictionary(v => v.Key, v => (string?)v.Value);
        environment["TERM"] = "xterm-256color";
        var session = new PosixPtyTerminalSession(new PosixPtyTerminalSessionOptions
        {
            FileName = launch.FileName,
            Arguments = string.Join(' ', launch.Arguments.Select(a => $"\"{a}\"")),
            InitialSize = new TerminalSize(80, 25),
            Environment = environment,
        });
        session.OutputReceived += (_, e) => Answer(e.Data.Span);
        session.Exited += (_, _) => _exited = true;
        _session = session;
        session.StartAsync().GetAwaiter().GetResult();
        _processId = session.ProcessId;
    }

    // The terminal side of the driver's negotiation: "CSI ? u" asks for the current flags,
    // "CSI c" is the device-attributes sentinel a terminal without the protocol answers alone.
    private void Answer(ReadOnlySpan<byte> data)
    {
        var replies = new StringBuilder();
        lock (_gate)
        {
            _output.Append(Encoding.Latin1.GetString(data));
            string text = _output.ToString();
            while (true)
            {
                int query = text.IndexOf(SupportQuery, _scanned, StringComparison.Ordinal);
                int attributes = text.IndexOf(DeviceAttributesQuery, _scanned, StringComparison.Ordinal);
                if (query < 0 && attributes < 0) break;
                if (query >= 0 && (attributes < 0 || query < attributes))
                {
                    _scanned = query + SupportQuery.Length;
                    if (_kittyFlags == 0) continue;
                    // First query: nothing enabled yet. Second: what the terminal agreed to.
                    replies.Append(_queries++ == 0 ? "\x1b[?0u" : $"\x1b[?{_kittyFlags}u");
                    if (_queries == 2) _negotiated.Set();
                }
                else
                {
                    _scanned = attributes + DeviceAttributesQuery.Length;
                    replies.Append("\x1b[?62c");
                    if (_kittyFlags == 0) _negotiated.Set();
                }
            }
            // Keep only a tail long enough to complete a query split across reads.
            if (_scanned > 4096)
            {
                _output.Remove(0, _scanned);
                _scanned = 0;
            }
        }

        if (replies.Length != 0) _session?.SendTextAsync(replies.ToString());
    }

    public bool WaitReady(TimeSpan timeout) => _negotiated.Wait(timeout);

    public string? Attach(Protocol.PipeChannel channel, Protocol.Hello hello, LayoutProfile profile, TextWriter output) => null;

    public string? CannotDeliver(Step step) => null;

    public void Deliver(Step step, int stepId)
    {
        if (_session is not { } session) return;
        foreach (byte[] chunk in step.Chunks)
        {
            session.SendInputAsync(chunk).GetAwaiter().GetResult();
            // Separate writes, so a sequence split between chunks can reach the driver as separate reads.
            Thread.Sleep(20);
        }
    }

    public void Dispose()
    {
        if (_session is not { } session) return;
        _session = null;
        try { session.StopAsync().Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        session.Dispose();
        _negotiated.Dispose();
    }
}
