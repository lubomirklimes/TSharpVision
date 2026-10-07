using System.Collections.Concurrent;
using System.Diagnostics;
using TSharpVision.Constants;
using System.Runtime.Versioning;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Target;

/// <summary>
/// The measurement endpoint: a TSharpVision application that records every keyboard event before any
/// routing and reports it to the controller. It makes no verdicts and has no menu or status line.
/// </summary>
internal sealed class TargetApp : TApplication
{
    private const uint LogicalModifiers = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift;

    private readonly PipeChannel _channel;
    private readonly ConcurrentQueue<Message?> _inbox = new();
    private readonly List<RecordedEvent> _events = new();
    private readonly Stopwatch _clock = new();
    private readonly TargetView _view;
    private int _step = -1;
    private End? _ending;
    private double _endRequestedAt, _lastEventAt;
    private uint _modifiersDown;
    private ushort _awaitedRelease;
    private bool _quit;
    private ITargetInjector? _injector;

    public TargetApp(PipeChannel channel, string token)
    {
        _channel = channel;
        _view = new TargetView(DeskTop!.GetExtent()) { Driver = DriverName };
        DeskTop.Insert(_view);

        (long hwnd, string host) = OperatingSystem.IsWindows() ? DescribeWindow(DriverName) : (0, string.Empty);
        _channel.Send(new Hello(token, Environment.ProcessId, DriverName, (int)Capabilities,
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux", Wire.Version)
        {
            Hwnd = hwnd, Host = host,
        });
        new Thread(ReadMessages) { IsBackground = true, Name = "keyboard-diag pipe" }.Start();
    }

    /// <summary>Runs the target role; returns a process exit code.</summary>
    public static int RunTarget()
    {
        string? pipe = Environment.GetEnvironmentVariable(Wire.PipeVariable);
        string? token = Environment.GetEnvironmentVariable(Wire.TokenVariable);
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("The target role is started by the controller; use 'run' instead.");
            return 2;
        }

        using var channel = new PipeChannel(PipeChannel.Connect(pipe, 10_000));
        return TSharpVisionRuntime.Run(() => new TargetApp(channel, token), "Keyboard Diagnostic Target");
    }

    // The window that receives this target's keyboard input, and what hosts it. A console window that
    // is not the classic host's own is resolved to its owner and named, never assumed to be focused.
    [SupportedOSPlatform("windows")]
    private static (long Hwnd, string Host) DescribeWindow(string driver)
    {
        if (driver.StartsWith("SDL", StringComparison.Ordinal))
        {
            using Process self = Process.GetCurrentProcess();
            long window = self.MainWindowHandle.ToInt64();
            return (window, window != 0 ? WindowsSendInputSource.SdlHost : string.Empty);
        }
        if (driver != "Win32ConsoleDriver") return (0, string.Empty);

        IntPtr console = WindowsNative.GetConsoleWindow();
        if (console == IntPtr.Zero) return (0, string.Empty);
        if (WindowsNative.ClassName(console) == "ConsoleWindowClass" && WindowsNative.IsWindowVisible(console))
            return (console.ToInt64(), WindowsSendInputSource.ConhostHost);
        IntPtr owner = WindowsNative.GetAncestor(console, WindowsNative.GaRootOwner);
        string ownerClass = WindowsNative.ClassName(owner);
        return (owner.ToInt64(), ownerClass == "CASCADIA_HOSTING_WINDOW_CLASS"
            ? "Windows Terminal" : $"unknown console host ({ownerClass})");
    }

    // Level B: native events go into the queue this driver really reads.
    private ITargetInjector? CreateInjector() => DriverName switch
    {
        "Win32ConsoleDriver" when OperatingSystem.IsWindows() => new Win32ConsoleBufferInjector(),
        "SDLDriver" or "SDLGpuDriver" => new SdlEventQueueInjector(),
        _ => null,
    };

    private static string DriverName => TDisplay.driver?.GetType().Name ?? "none";

    private static KeyboardCapabilities Capabilities =>
        TDisplay.driver?.KeyboardCapabilities ?? KeyboardCapabilities.None;

    // Nothing may translate or consume a key before it is recorded.
    public override TStatusLine? InitStatusLine(TRect r) => null;
    public override TMenuBar? InitMenuBar(TRect r) => null;
    protected override void NormalizeKeyEvent(ref TEvent ev) { }

    public override void GetEvent(ref TEvent ev)
    {
        base.GetEvent(ref ev);
        if (RecordedEvent.IsKeyboard(ev.What))
        {
            Record(ev);
            ev = default;
        }

        ProcessMessages();
        if (ev.What != Events.evNothing) return;
        if (_quit)
        {
            ev.What = Events.evCommand;
            ev.message.command = Views.cmQuit;
        }
        else
            Thread.Sleep(1);
    }

    private void Record(TEvent ev)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        RecordedEvent recorded = RecordedEvent.From(ev, _events.Count, now);
        _view.Last = recorded;
        _view.DrawView();
        if (_step < 0) return;

        _events.Add(recorded);
        _lastEventAt = now;
        if (ev.What == Events.evModifierChanged)
            _modifiersDown = ev.keyDown.controlKeyState & LogicalModifiers;
        else if (Capabilities.HasFlag(KeyboardCapabilities.KeyReleaseEvents))
        {
            // The step's first identified press is still down until its release arrives.
            if (ev.What == Events.evKeyUp && ev.keyDown.keyCode == _awaitedRelease) _awaitedRelease = 0;
            else if (ev.What == Events.evKeyDown && _awaitedRelease == 0 && !_events.SkipLast(1).Any(IsPress))
                _awaitedRelease = ev.keyDown.keyCode;
        }
    }

    private void Send(Message message)
    {
        try { _channel.Send(message); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { _quit = true; }
    }

    private static bool IsPress(RecordedEvent ev) => ev.What == RecordedEvent.KeyDown;

    private void ReadMessages()
    {
        while (true)
        {
            Message? message;
            try { message = _channel.ReceiveAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { message = null; }
            _inbox.Enqueue(message);
            // A closed or unreadable pipe means the controller is gone: leave.
            if (message is null or Quit) return;
        }
    }

    private void ProcessMessages()
    {
        while (_inbox.TryDequeue(out Message? message))
        {
            switch (message)
            {
                case Begin begin:
                    _injector?.Release();
                    _step = begin.Step;
                    _ending = null;
                    _events.Clear();
                    _modifiersDown = 0;
                    _awaitedRelease = 0;
                    _clock.Restart();
                    _lastEventAt = 0;
                    _view.Profile = begin.Profile;
                    _view.Step = $"{begin.Index} / {begin.Total}";
                    _view.Caption = begin.Caption;
                    _view.Expected = begin.Expected;
                    _view.Status = "Recording...";
                    _view.DrawView();
                    Send(new Ready(begin.Step));
                    break;
                case Inject inject when inject.Step == _step:
                    _injector ??= CreateInjector();
                    _injector?.Inject(inject.Events);
                    break;
                case Inject:
                    break;
                case End end when end.Step == _step:
                    _ending = end;
                    _endRequestedAt = _clock.Elapsed.TotalMilliseconds;
                    break;
                case End:
                    break;
                default:
                    _injector?.Dispose();
                    _injector = null;
                    _quit = true;
                    break;
            }
        }

        if (_ending is not { } ending) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        // Quiet means: something arrived, nothing is still held, and no event for the settle period.
        bool settled = _events.Count != 0 && _modifiersDown == 0 && _awaitedRelease == 0
            && now - _lastEventAt >= ending.SettleMs;
        if (!settled && now - _endRequestedAt < ending.TimeoutMs) return;

        Send(new Result(_step, (int)Capabilities, _events.ToArray()));
        _step = -1;
        _ending = null;
        _view.Status = "Waiting for controller...";
        _view.DrawView();
    }
}
