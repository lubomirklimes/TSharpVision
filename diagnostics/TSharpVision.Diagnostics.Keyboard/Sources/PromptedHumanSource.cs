using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>
/// Level C everywhere, with no privileges: a person presses the key the step names. The key goes through
/// the real layout, host and driver.
/// </summary>
public sealed class PromptedHumanSource : IInputSource
{
    private readonly TargetProcess _process = new();
    private readonly TargetConsole _console;

    /// <param name="console">Where the target's console comes from.</param>
    /// <param name="sharesConsole">The target takes over the terminal the controller was started from.</param>
    /// <param name="timeoutMs">How long one step waits for the key.</param>
    public PromptedHumanSource(TargetConsole console, bool sharesConsole, int timeoutMs)
    {
        _console = console;
        SharesConsole = sharesConsole;
        TimeoutMs = timeoutMs;
    }

    public string Name => "human";
    public string Level => "C (physical key through the active layout, prompted human)";
    public string Label => SourceLabels.RealLayout;
    public bool SharesConsole { get; }
    public bool Interactive => true;
    public bool VerifiesLayout => true;
    public bool TransportSteps => false;
    public int SettleMs => 600;
    public int TimeoutMs { get; }
    public int? TargetProcessId => _process.Id;
    public bool TargetExited => _process.Exited;

    public void Start(TargetLaunch launch) => _process.Start(launch, _console);

    public string? Attach(PipeChannel channel, Hello hello, LayoutProfile profile, TextWriter output) => null;

    public bool WaitReady(TimeSpan timeout) => true;

    public string? CannotDeliver(Step step) =>
        step.NativeOnly ? "states a lock state or event order that only native injection can supply" : null;

    public void Deliver(Step step, int stepId) { }

    public void Dispose() => _process.Dispose();
}
