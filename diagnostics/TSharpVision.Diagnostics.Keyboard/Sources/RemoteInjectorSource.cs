using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>
/// Level B: asks the target to put native events into its own input queue (the console input buffer
/// with WriteConsoleInputW, or the SDL event queue with SDL_PushEvent), so that the running driver
/// reads them through its real pump. Deterministic and independent of focus; the layout is bypassed,
/// because each event already carries what the layout would have produced.
/// </summary>
public sealed class RemoteInjectorSource : IInputSource
{
    private readonly TargetProcess _process = new();
    private readonly TargetConsole _console;
    private PipeChannel? _channel;
    private LayoutProfile? _profile;
    private bool _sdl;

    public RemoteInjectorSource(TargetConsole console) => _console = console;

    public string Name => "native";
    public string Level => "B (native events through the driver's real input queue)";
    public string Label => SourceLabels.NativeInjected;
    public bool SharesConsole => false;
    public bool Interactive => false;
    public bool VerifiesLayout => false;
    public bool TransportSteps => false;
    public int SettleMs => 150;
    public int TimeoutMs => 3000;
    public int? TargetProcessId => _process.Id;
    public bool TargetExited => _process.Exited;

    /// <summary>The queue a driver reads, by the driver's type name; null when Level B has no injector for it.</summary>
    public static string? QueueOf(string driver) => driver switch
    {
        "Win32ConsoleDriver" => "console input buffer (WriteConsoleInputW)",
        "SDLDriver" or "SDLGpuDriver" => "SDL event queue (SDL_PushEvent)",
        _ => null,
    };

    public void Start(TargetLaunch launch) => _process.Start(launch, _console);

    public string? Attach(PipeChannel channel, Hello hello, LayoutProfile profile, TextWriter output)
    {
        if (QueueOf(hello.Driver) is not { } queue) return $"native injection has no injector for {hello.Driver}";
        _channel = channel;
        _profile = profile;
        _sdl = hello.Driver.StartsWith("SDL", StringComparison.Ordinal);
        output.WriteLine($"Injecting into the {queue}.");
        return null;
    }

    public bool WaitReady(TimeSpan timeout) => true;

    public string? CannotDeliver(Step step) =>
        step.SdlScript != null && !_sdl ? "an SDL event order" : null;

    public void Deliver(Step step, int stepId)
    {
        IReadOnlyList<NativeEvent> events = _sdl
            ? NativeEvents.ForSdl(step, _profile!)
            : NativeEvents.ForConsole(step, _profile!);
        _channel!.Send(new Inject(stepId, events));
    }

    public void Dispose() => _process.Dispose();
}
