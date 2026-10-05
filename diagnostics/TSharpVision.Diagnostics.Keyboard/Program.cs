using System.Text;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Sources;
using TSharpVision.Diagnostics.Keyboard.Target;

// ─────────────────────────────────────────────────────────────────────────────
// TSharpVision.Diagnostics.Keyboard ("keyboard-diag")
//
// One executable, two roles. The controller (list | run) owns the layout profiles, the expected
// results and the verdicts; it relaunches this executable as the target (--target), a minimal
// TSharpVision application on the driver under test that records every keyboard event and reports
// it over a current-user named pipe. Input comes from one of four sources:
//
//   human   a person presses each key                         Level C, real layout
//   os      Windows SendInput by scan code                    Level C, real layout
//   native  events written to the driver's own input queue    Level B, layout bypassed
//   pty     bytes written to a PTY (Terminal driver)          Level B, no layout involved
//
// Exit codes:  0 = no FAIL
//              1 = at least one FAIL
//              2 = usage or environment error, or an INVALID run
// ─────────────────────────────────────────────────────────────────────────────

string[] drivers = ["console", "sdl", "sdl-gpu", "terminal"];

if (args.Length == 0) return Usage("A command is required.");

switch (args[0])
{
    case "--target":
        return TargetApp.RunTarget();
    case "list":
        PrintList();
        return Verdicts.ExitOk;
    case "run":
        return Run(args[1..]);
    case "--help" or "-h":
        PrintUsage(Console.Out);
        return Verdicts.ExitOk;
    default:
        return Usage($"Unknown command '{args[0]}'.");
}

int Run(string[] options)
{
    string? driver = null, report = null;
    string profileId = "us", suite = "main", source = "human", kitty = "none", host = "auto";
    int timeoutSeconds = 30;
    bool lockKeys = false;

    for (int i = 0; i < options.Length; i++)
    {
        string option = options[i];
        if (option == "--locks")
        {
            lockKeys = true;
            continue;
        }
        if (option is not ("--driver" or "--profile" or "--suite" or "--source" or "--report" or "--kitty"
            or "--timeout" or "--host"))
            return Usage($"Unknown option '{option}'.");
        if (++i >= options.Length) return Usage($"{option} requires a value.");
        string value = options[i];
        switch (option)
        {
            case "--driver": driver = value; break;
            case "--profile": profileId = value; break;
            case "--suite": suite = value; break;
            case "--source": source = value; break;
            case "--report": report = Path.GetFullPath(value); break;
            case "--kitty": kitty = value; break;
            case "--host": host = value; break;
            case "--timeout":
                if (!int.TryParse(value, out timeoutSeconds) || timeoutSeconds is < 1 or > 600)
                    return Usage($"Invalid timeout '{value}' (1-600 seconds).");
                break;
        }
    }

    if (driver is null) return Usage("--driver is required.");
    if (!drivers.Contains(driver)) return Usage($"Unknown driver '{driver}'.");
    if (LayoutProfile.Find(profileId) is not { } profile) return Usage($"Unknown profile '{profileId}'.");
    if (!Suites.Names.Contains(suite)) return Usage($"Unknown suite '{suite}'.");
    int kittyFlags = kitty switch { "none" => 0, "full" => 31, _ => int.TryParse(kitty, out int flags) ? flags : -1 };
    if (kittyFlags is < 0 or > 31) return Usage($"Invalid --kitty value '{kitty}' (none, full, or flags 0-31).");

    SourceSelection selection = SourceFactory.Create(new SourceRequest(source, driver)
    {
        Host = host, KittyFlags = kittyFlags, TimeoutMs = timeoutSeconds * 1000,
    });
    if (selection.Source is not { } input)
        return selection.Usage ? Usage(selection.Error!) : Environment_(selection.Error!);

    Console.OutputEncoding = Encoding.UTF8;
    var run = new DiagnosticRun(new RunOptions(driver, profile, suite, source)
    {
        ReportPath = report, KittyFlags = kittyFlags, LockKeys = lockKeys,
    }, input, Console.Out);
    try { return run.Execute(); }
    catch (Exception ex) when (ex is InvalidOperationException or IOException
        or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
    {
        return Environment_(ex.Message);
    }
}

void PrintList()
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine("Profiles:");
    foreach (LayoutProfile profile in LayoutProfile.All)
        Console.WriteLine($"  {profile.Id,-10} {profile.Name} (Windows layout {profile.LayoutId})");
    Console.WriteLine("Drivers:");
    Console.WriteLine("  console    Win32 console on Windows; the terminal driver elsewhere");
    Console.WriteLine("  sdl        SDL renderer backend");
    Console.WriteLine("  sdl-gpu    SDL GPU backend");
    Console.WriteLine("  terminal   ANSI/Kitty terminal (Linux, macOS)");
    Console.WriteLine("Sources:");
    Console.WriteLine($"  human      {SourceLabels.RealLayout}: a person presses each key, any driver");
    Console.WriteLine($"  os         {SourceLabels.RealLayout}: SendInput scan codes, Windows, console and SDL");
    Console.WriteLine($"  native     {SourceLabels.NativeInjected}: WriteConsoleInputW or SDL_PushEvent");
    Console.WriteLine($"  pty        {SourceLabels.TransportBytes}: terminal driver behind a PTY");
    Console.WriteLine("Suites (steps per profile):");
    foreach (string suite in Suites.Names)
        Console.WriteLine($"  {suite,-12} " + string.Join("  ", LayoutProfile.All.Select(p =>
            $"{p.Id}: {Suites.Build(p, suite, latin1Identity: false).Count}")));
}

int Usage(string message)
{
    Console.Error.WriteLine("error: " + message);
    Console.Error.WriteLine();
    PrintUsage(Console.Error);
    return Verdicts.ExitInvalid;
}

int Environment_(string message)
{
    Console.Error.WriteLine("error: " + message);
    return Verdicts.ExitInvalid;
}

void PrintUsage(TextWriter output)
{
    output.WriteLine($"""
        TSharpVision.Diagnostics.Keyboard — cross-driver keyboard diagnostics

          keyboard-diag list
          keyboard-diag run --driver <{string.Join('|', drivers)}>
                            [--profile <{string.Join('|', LayoutProfile.All.Select(p => p.Id))}>]   (default: us)
                            [--suite <{string.Join('|', Suites.Names)}>]
                                                              (default: main)
                            [--source <{string.Join('|', SourceFactory.Names)}>]  (default: human)
                            [--host <{string.Join('|', SourceFactory.Hosts)}>]     console host on Windows
                            [--locks]                          also press NumLock (state is restored)
                            [--report <path>]                  JSON Lines report
                            [--timeout <seconds>]              per step, human source (default: 30)
                            [--kitty <none|full|flags>]        what the PTY's terminal confirms (default: none)

        human   {SourceLabels.RealLayout}. Select the layout yourself first; the run starts with a layout
                check and is INVALID when the active layout is not the profile's.
        os      {SourceLabels.RealLayout}, Windows only. SendInput by scan code into the target window, and
                only while it is the foreground window. Do not touch the keyboard during the run.
        native  {SourceLabels.NativeInjected}. The target writes native events into its
                own console input buffer or SDL event queue. Proves the driver, not the layout.
        pty     {SourceLabels.TransportBytes}. The controller plays the terminal.

        --host  conhost starts the classic console host, default opens whatever Windows uses
                (often Windows Terminal). auto: conhost for os and native, default for human.

        Exit codes: 0 = no FAIL, 1 = at least one FAIL, 2 = usage, environment or INVALID run.
        """);
}
