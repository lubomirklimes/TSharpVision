using System.Runtime.Versioning;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>
/// Level C on Windows without a person: scan-code SendInput. Only the key position is injected; the
/// layout of the receiving thread turns it into a virtual key and a character, so the complete real
/// pipeline (layout, host, driver) is exercised. SendInput has no target parameter, so this source
/// is bound by every safeguard of <see cref="PhysicalInjector"/> and refuses any host whose input
/// focus it cannot verify.
/// </summary>
public sealed class WindowsSendInputSource : IInputSource
{
    private const int CountdownSeconds = 3, KeyDelayMs = 25;
    public const string ConhostHost = "conhost", SdlHost = "SDL window";

    private readonly TargetProcess _process = new();
    private readonly TargetConsole _console;
    private PhysicalInjector? _injector;

    public WindowsSendInputSource(TargetConsole console) => _console = console;

    public string Name => "os";
    public string Level => "C (physical key position through the active layout, SendInput scan codes)";
    public string Label => SourceLabels.RealLayout;
    public bool SharesConsole => false;
    public bool Interactive => false;
    public bool VerifiesLayout => true;
    public bool TransportSteps => false;
    public int SettleMs => 250;
    public int TimeoutMs => 3000;
    public int? TargetProcessId => _process.Id;
    public bool TargetExited => _process.Exited;

    /// <summary>Why this environment may not be driven by OS injection, or null.</summary>
    public static string? Refusal(bool windows, bool elevated) =>
        !windows ? "--source os needs Windows (SendInput); Linux and macOS injection is not implemented"
        : elevated ? "refusing to inject keys from an elevated process: run without administrator rights"
        : null;

    /// <summary>
    /// Why keys must not be sent to this host, or null. Only a host whose foreground window proves
    /// where the keys go is accepted: a tabbed terminal can keep the foreground while another tab types.
    /// </summary>
    public static string? HostRefusal(Hello hello) =>
        hello.Hwnd == 0 ? "the target reported no window to verify the foreground against"
        : hello.Host is ConhostHost or SdlHost ? null
        : $"host '{hello.Host}' cannot prove which tab or pane has the input focus; use --host conhost";

    public void Start(TargetLaunch launch) => _process.Start(launch, _console);

    public string? Attach(PipeChannel channel, Hello hello, LayoutProfile profile, TextWriter output)
    {
        if (!OperatingSystem.IsWindows()) return Refusal(false, false);
        if (HostRefusal(hello) is { } refusal) return refusal;
        return AttachWindows(hello, output);
    }

    [SupportedOSPlatform("windows")]
    private string? AttachWindows(Hello hello, TextWriter output)
    {
        var window = (IntPtr)hello.Hwnd;
        output.WriteLine($"Automated keyboard input goes to the target window (0x{hello.Hwnd:X}, {hello.Host}).");
        output.WriteLine("Do not touch the keyboard. Switching to another window stops the run at once.");
        for (int second = CountdownSeconds; second > 0; second--)
        {
            output.WriteLine($"  starting in {second}...");
            WindowsNative.SetForegroundWindow(window);
            Thread.Sleep(1000);
        }
        if (WindowsNative.GetForegroundWindow() != window)
            return "the target window is not in the foreground (click it during the countdown); nothing was injected";

        _injector = new PhysicalInjector(() => WindowsNative.GetForegroundWindow().ToInt64(), Send, hello.Hwnd);
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static void Send(IReadOnlyList<KeyStroke> strokes)
    {
        var keys = strokes.Select(s =>
        {
            (byte scan, bool extended) = PhysicalKeys.ScanCode(s.Key);
            return (scan, extended, s.Down);
        }).ToArray();
        if (!WindowsNative.SendScanCodes(keys))
            throw new InputAbortedException("SendInput was blocked (another process holds input, or integrity levels differ)");
    }

    public bool WaitReady(TimeSpan timeout) => true;

    public string? CannotDeliver(Step step)
    {
        if (step.NativeOnly) return "states a lock state or event order that only native injection can supply";
        if (step.Strokes.Count == 0) return "no physical key sequence";
        // conhost keeps F11 for its own full-screen toggle; pressing it would resize the window.
        if (_console != TargetConsole.Inherit && step.Strokes.Any(s => s.Key == PhysicalKey.F11))
            return "F11 is reserved by the console host (full screen)";
        return InputSafety.Denied(step.Strokes);
    }

    public void Deliver(Step step, int stepId)
    {
        if (_injector is not { } injector || !OperatingSystem.IsWindows())
            throw new InputAbortedException("the OS source is not attached to a target window");
        DeliverWindows(injector, step);
    }

    [SupportedOSPlatform("windows")]
    private static void DeliverWindows(PhysicalInjector injector, Step step)
    {
        // A lock key is pressed only on explicit opt-in, and is put back whatever happens.
        LockRestorer? numLock = step.Strokes.Any(s => s.Key == PhysicalKey.NumLock)
            ? new LockRestorer(
                () => (WindowsNative.GetKeyState(WindowsNative.VkNumLock) & 1) != 0,
                () => injector.Play([new KeyStroke(PhysicalKey.NumLock, true), new KeyStroke(PhysicalKey.NumLock, false)],
                    pause: Pause))
            : null;
        try
        {
            IReadOnlyList<KeyStroke> strokes = Expand(step);
            if (step.Batch) injector.Play(strokes, batch: true);
            else injector.Play(strokes, pause: Pause);
        }
        catch
        {
            injector.ReleaseAll();
            throw;
        }
        finally
        {
            if (numLock != null)
            {
                Thread.Sleep(100);
                numLock.Restore();
            }
        }
    }

    // A held key repeats: further key-downs of the last key before its release.
    public static IReadOnlyList<KeyStroke> Expand(Step step)
    {
        if (step.Repeats == 0) return step.Strokes;
        var strokes = new List<KeyStroke>(step.Strokes);
        int index = strokes.FindLastIndex(s => s.Down && !NativeEvents.IsModifier(s.Key));
        if (index >= 0)
            strokes.InsertRange(index + 1, Enumerable.Repeat(strokes[index], step.Repeats));
        return strokes;
    }

    private static void Pause() => Thread.Sleep(KeyDelayMs);

    public void Dispose()
    {
        try { _injector?.ReleaseAll(); } catch (InputAbortedException) { }
        _process.Dispose();
    }
}
