using System.IO.Pipes;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;
using TSharpVision.Diagnostics.Keyboard.Sources;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Controller;

public sealed record RunOptions(string Driver, LayoutProfile Profile, string Suite, string Source)
{
    public string? ReportPath { get; init; }
    /// <summary>Kitty flags the simulated terminal confirms (PTY source only).</summary>
    public int KittyFlags { get; init; }
    /// <summary>Include steps that press a lock key; its state is restored afterwards.</summary>
    public bool LockKeys { get; init; }
}

/// <summary>
/// The controller: starts the target, owns the pipe server, runs the steps one at a time, compares
/// what the target observed with the profile, and prints verdicts.
/// </summary>
public sealed class DiagnosticRun
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(5);

    private readonly RunOptions _options;
    private readonly TextWriter _output;
    private readonly IInputSource _source;
    private PipeChannel _channel = null!;
    private KeyboardCapabilities _capabilities;
    private int _nextStep;

    public DiagnosticRun(RunOptions options, IInputSource source, TextWriter output)
    {
        _options = options;
        _source = source;
        _output = output;
    }

    /// <summary>The value of TSHARPVISION_DRIVER for a CLI driver name; null when the name is unknown.</summary>
    public static string? DriverVariable(string driver) => driver switch
    {
        "console" => "console",
        "sdl" => "SDLDriver",
        "sdl-gpu" => "SDLGpuDriver",
        "terminal" => "AnsiTerminalDriver",
        _ => null,
    };

    /// <summary>Whether the driver the target reports is the one that was asked for.</summary>
    public static bool IsRequestedDriver(string driver, string reported) => driver switch
    {
        "console" => reported.Length != 0 && reported != "none" && reported != "NullDriver"
            && !reported.StartsWith("SDL", StringComparison.Ordinal),
        _ => reported == DriverVariable(driver),
    };

    public int Execute()
    {
        string token = Wire.NewToken();
        using NamedPipeServerStream server = PipeChannel.CreateServer(out string pipeName);
        ConsoleCancelEventHandler cancel = (_, _) => _source.Dispose();
        Console.CancelKeyPress += cancel;
        try
        {
            _source.Start(TargetLaunch.ForThisExecutable(new Dictionary<string, string>
            {
                [Wire.PipeVariable] = pipeName,
                [Wire.TokenVariable] = token,
                ["TSHARPVISION_DRIVER"] = DriverVariable(_options.Driver)!,
            }));

            if (!Connect(server)) return Invalid("the target did not connect to the session pipe");
            using var channel = new PipeChannel(server);
            _channel = channel;

            Message? first = channel.Receive(StartupTimeout);
            if (Wire.ValidateHello(first, token, _source.TargetProcessId) is { } refused)
                return Invalid($"handshake refused: {refused}");
            var hello = (Hello)first!;
            if (!IsRequestedDriver(_options.Driver, hello.Driver))
                return Invalid($"the target runs {hello.Driver}, not the requested '{_options.Driver}' driver");
            _capabilities = (KeyboardCapabilities)hello.Capabilities;

            try { return RunSteps(hello); }
            finally
            {
                try { channel.Send(new Quit()); } catch (IOException) { }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            _source.Dispose();
        }
    }

    private bool Connect(NamedPipeServerStream server)
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        Task connection = server.WaitForConnectionAsync(timeout.Token);
        while (!connection.IsCompleted)
        {
            if (_source.TargetExited)
            {
                timeout.Cancel();
                break;
            }
            Thread.Sleep(50);
        }
        try { connection.GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException) { return false; }
        return server.IsConnected;
    }

    private int RunSteps(Hello hello)
    {
        // While the target owns this terminal nothing may be written to it; the table follows afterwards.
        TextWriter live = _source.SharesConsole ? TextWriter.Null : _output;

        // The layout of the target's input thread, read from outside. Reliable where the target owns
        // its window; a console host's window thread is informative only, and the fingerprint decides.
        ActiveLayout active = OperatingSystem.IsWindows() && hello.Hwnd != 0
            ? WindowsLayoutProbe.OfWindow(hello.Hwnd)
            : new ActiveLayout(0, string.Empty, string.Empty);
        IReadOnlyList<OracleDisagreement> disagreements = [];
        if (_source.VerifiesLayout)
        {
            bool reliable = hello.Host == WindowsSendInputSource.SdlHost;
            if (LayoutCheck.Mismatch(_options.Profile, active, reliable) is { } mismatch) return Invalid(mismatch);
            if (OperatingSystem.IsWindows() && active.Klid.Equals(_options.Profile.LayoutId, StringComparison.OrdinalIgnoreCase))
                disagreements = AskOracle(_options.Profile, active.Hkl);
        }

        if (_source.Attach(_channel, hello, _options.Profile, live) is { } refusal) return Invalid(refusal);

        string? negotiation = null;
        if (_source.TransportSteps)
        {
            KeyboardCapabilities expected = KittyCapabilities(_options.KittyFlags);
            if (!_source.WaitReady(StartupTimeout) || !AwaitCapabilities(expected))
                return Invalid($"keyboard negotiation did not settle on {expected} (driver reports {_capabilities})");
            negotiation = _options.KittyFlags switch
            {
                0 => "legacy ANSI: the terminal did not offer the Kitty keyboard protocol",
                31 => null,
                int flags => $"partial Kitty negotiation: flags {flags} of 31 confirmed",
            };
        }

        var info = new RunInfo(
            _source.TransportSteps ? "none (transport bytes)" : _options.Profile.Name, hello.Driver, _source.Name,
            _source.Level, _source.Label, hello.Host.Length != 0 ? hello.Host : "not reported", hello.Os,
            active.ToString(), _capabilities);

        IReadOnlyList<Step> steps = _source.TransportSteps
            ? Suites.Pty(_options.KittyFlags, _options.Suite)
            : Suites.Build(_options.Profile, _options.Suite,
                latin1Identity: hello.Driver == "Win32ConsoleDriver", _options.LockKeys);

        if (_source.Interactive)
        {
            live.WriteLine("Focus the target window and follow the action it shows; the target ends each step by itself.");
            if (!_source.SharesConsole && !Console.IsInputRedirected)
                live.WriteLine("In this window: S skips the current step, Q ends the run.");
            live.WriteLine();
        }
        if (_source.VerifiesLayout && CheckLayout(live, info.Profile) is { } wrongLayout) return Invalid(wrongLayout);

        Report.WriteHeader(live, info, disagreements);
        var reports = new List<StepReport>();
        string? stopped = null;
        for (int i = 0; i < steps.Count; i++)
        {
            StepReport report = stopped != null
                ? new StepReport(steps[i], StepOutcome.Of(Verdict.Skipped, stopped), [])
                : RunStep(steps[i], i + 1, steps.Count, info.Profile, ref stopped);
            reports.Add(report);
            Report.WriteRow(live, report);
            if (report.Outcome.Verdict == Verdict.Invalid && _source.TargetExited) break;
        }

        info = info with { Capabilities = _capabilities };
        IReadOnlyList<string> limitations = Report.Limitations(_capabilities, reports, negotiation);
        int exitCode = reports.Count == 0 ? Verdicts.ExitInvalid : Verdicts.ExitCode(reports.Select(r => r.Outcome.Verdict));
        if (_source.SharesConsole)
        {
            // Let the target restore the terminal before the table is printed on it.
            try { _channel.Send(new Quit()); } catch (IOException) { }
            _source.Dispose();
            Report.WriteHeader(_output, info, disagreements);
            foreach (StepReport report in reports) Report.WriteRow(_output, report);
        }
        if (reports.Count == 0) _output.WriteLine($"The '{_options.Suite}' suite has no steps for this source.");
        Report.WriteSummary(_output, reports, limitations, exitCode);
        if (_options.ReportPath is { } path)
            Report.WriteJsonLines(path, info, reports, limitations, disagreements, exitCode);
        return exitCode;
    }

    // The OS layout table as an independent witness for the pinned profile, where they name the same layout.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IReadOnlyList<OracleDisagreement> AskOracle(LayoutProfile profile, long hkl) =>
        LayoutCheck.Disagreements(profile, (key, level) => WindowsLayoutProbe.Text(hkl, key, level));

    // A layout-specific run on the wrong layout is noise, not a set of failures: compare the text of
    // a few discriminating keys first. Nothing is installed, loaded or switched; the operator selects.
    // For an injecting source this is also the echo check: no event means the keys went elsewhere.
    private string? CheckLayout(TextWriter live, string profileName)
    {
        live.WriteLine($"Layout check for {_options.Profile.Name} ({_options.Profile.LayoutId}):");
        int index = 0;
        foreach ((PhysicalKey key, string text) in _options.Profile.Fingerprint)
        {
            Step step = Suites.TextKey(_options.Profile, key, KeyLevel.Plain, "layout", latin1Identity: true);
            string? stopped = null;
            Result? result = Exchange(step, ++index, _options.Profile.Fingerprint.Count,
                $"{profileName} (layout check)", out bool skipped, ref stopped);
            if (stopped != null) return stopped;
            if (result is null) return "the target stopped answering during the layout check";
            if (skipped || result.Events.Count == 0) return "no key was observed during the layout check";
            string observed = string.Concat(Presses(result.Events).Select(e => e.Text).Distinct());
            live.WriteLine($"  {key}: expected {KeyNames.Quote(text)}, got {KeyNames.Quote(observed)}");
            if (LayoutCheck.FingerprintMismatch(_options.Profile, text, observed) is { } mismatch) return mismatch;
        }
        live.WriteLine();
        return null;
    }

    private StepReport RunStep(Step step, int index, int total, string profileName, ref string? stopped)
    {
        if ((_capabilities & step.Expect.Requires) != step.Expect.Requires)
            return new StepReport(step, SequenceMatcher.Evaluate(step.Expect, [], _capabilities), []);
        if (_source.CannotDeliver(step) is { } reason)
            return new StepReport(step, StepOutcome.Of(Verdict.Skipped, $"not deliverable by this source: {reason}"), []);

        Result? result = Exchange(step, index, total, profileName, out bool skipped, ref stopped);
        if (stopped != null)
        {
            string abort = stopped;
            stopped = "the run was stopped";
            return new StepReport(step, StepOutcome.Of(skipped ? Verdict.Skipped : Verdict.Invalid, abort), result?.Events ?? []);
        }
        if (result is null)
            return new StepReport(step, StepOutcome.Of(Verdict.Invalid,
                _source.TargetExited ? "the target exited" : "the target did not answer (IPC failure or timeout)"), []);
        if (skipped)
            return new StepReport(step, StepOutcome.Of(Verdict.Skipped, "skipped by the operator"), result.Events);

        IReadOnlyList<RecordedEvent> events = result.Events;
        // A release that precedes every press belongs to the key of the previous step, held a little long.
        if (_source.Interactive)
            events = events.SkipWhile(e => e.What == RecordedEvent.KeyUp).ToArray();
        return new StepReport(step, SequenceMatcher.Evaluate(step.Expect, events, _capabilities), events);
    }

    // One strict request/response exchange: begin, ready, deliver, end, result.
    // "stopped" is set when nothing more may be delivered: the operator ended the run, or a source
    // aborted for safety (the step is then INVALID).
    private Result? Exchange(Step step, int index, int total, string profileName, out bool skipped, ref string? stopped)
    {
        skipped = false;
        int id = ++_nextStep;
        try
        {
            _channel.Send(new Begin(id, index, total, profileName, step.Prompt, step.Expected));
            if (_channel.Receive(AckTimeout) is not Ready ready || ready.Step != id) return null;
            try { _source.Deliver(step, id); }
            catch (InputAbortedException aborted)
            {
                stopped = aborted.Message;
                _channel.Send(new End(id, 0, 0));
                return _channel.Receive(AckTimeout) as Result;
            }
            _channel.Send(new End(id, _source.SettleMs, _source.TimeoutMs));

            Task<Message?> reply = _channel.ReceiveAsync();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_source.TimeoutMs + 5000);
            bool keys = _source.Interactive && !_source.SharesConsole && !Console.IsInputRedirected;
            while (!reply.Wait(50))
            {
                if (_source.TargetExited || DateTime.UtcNow > deadline) return null;
                if (!keys || skipped || !Console.KeyAvailable) continue;
                ConsoleKey key = Console.ReadKey(intercept: true).Key;
                if (key is not (ConsoleKey.S or ConsoleKey.Q)) continue;
                skipped = true;
                if (key == ConsoleKey.Q) stopped = "the run was ended by the operator";
                _channel.Send(new End(id, 0, 0));
            }

            if (reply.Result is not Result result || result.Step != id) return null;
            _capabilities = (KeyboardCapabilities)result.Capabilities;
            return result;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or AggregateException)
        {
            return null;
        }
    }

    // Terminal capabilities appear only after the driver has read the negotiation replies.
    private bool AwaitCapabilities(KeyboardCapabilities expected)
    {
        DateTime deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            int id = ++_nextStep;
            _channel.Send(new Begin(id, 0, 0, string.Empty, "negotiating", string.Empty));
            if (_channel.Receive(AckTimeout) is not Ready) return false;
            _channel.Send(new End(id, 0, 100));
            if (_channel.Receive(TimeSpan.FromSeconds(5)) is not Result result || result.Step != id) return false;
            _capabilities = (KeyboardCapabilities)result.Capabilities;
            if (_capabilities == expected) return true;
        }
        return false;
    }

    /// <summary>The capabilities the Terminal driver derives from confirmed Kitty flags.</summary>
    public static KeyboardCapabilities KittyCapabilities(int flags)
    {
        KeyboardCapabilities capabilities = KeyboardCapabilities.None;
        if ((flags & (2 | 8)) == (2 | 8))
            capabilities |= KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions;
        if ((flags & 8) != 0) capabilities |= KeyboardCapabilities.DistinctNumericKeypad;
        return capabilities;
    }

    private static IEnumerable<RecordedEvent> Presses(IEnumerable<RecordedEvent> events) =>
        events.Where(e => e.What == RecordedEvent.KeyDown);

    private int Invalid(string reason)
    {
        _source.Dispose();
        _output.WriteLine($"INVALID: {reason}");
        return Verdicts.ExitInvalid;
    }
}
