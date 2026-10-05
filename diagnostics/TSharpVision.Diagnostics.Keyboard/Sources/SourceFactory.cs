namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>What the command line asked for, and the facts of the machine it runs on.</summary>
public sealed record SourceRequest(string Source, string Driver)
{
    /// <summary><c>auto</c>, <c>conhost</c> or <c>default</c>: which console host a Console target gets on Windows.</summary>
    public string Host { get; init; } = "auto";
    public int KittyFlags { get; init; }
    public int TimeoutMs { get; init; } = 30_000;
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    public bool Elevated { get; init; } = Environment.IsPrivilegedProcess;
    public bool PosixPty { get; init; } = PtyByteSource.IsSupported;
}

/// <summary>The chosen source, or why there is none. A usage error is the caller's mistake; the other kind is the environment's.</summary>
public sealed record SourceSelection(IInputSource? Source, string? Error = null, bool Usage = false)
{
    /// <summary>Where the target's console comes from.</summary>
    public TargetConsole Console { get; init; }
}

/// <summary>Decides which input source a run gets. Unsupported combinations are refused, never faked.</summary>
public static class SourceFactory
{
    public static readonly IReadOnlyList<string> Names = ["human", "native", "os", "pty"];
    public static readonly IReadOnlyList<string> Hosts = ["auto", "conhost", "default"];

    public static SourceSelection Create(SourceRequest request)
    {
        static SourceSelection UsageError(string message) => new(null, message, Usage: true);
        static SourceSelection Unsupported(string message) => new(null, message);

        string driver = request.Driver;
        bool console = driver == "console";
        if (!Hosts.Contains(request.Host)) return UsageError($"Unknown host '{request.Host}'.");
        if (request.Host != "auto" && !(console && request.Windows))
            return UsageError("--host applies to the console driver on Windows only.");
        if (request.KittyFlags != 0 && request.Source != "pty") return UsageError("--kitty applies to --source pty only.");
        // The Terminal driver reads a POSIX TTY; on Windows its initialisation is a no-op.
        if (driver == "terminal" && request.Windows) return Unsupported("The terminal driver needs Linux or macOS.");

        // An injected run must know where its keys go, so it gets the classic console host unless
        // told otherwise; a person can look at whatever host Windows opens.
        TargetConsole Console(bool classicByDefault) =>
            !(console && request.Windows) ? TargetConsole.Inherit
            : request.Host == "conhost" || (request.Host == "auto" && classicByDefault) ? TargetConsole.Conhost
            : TargetConsole.NewDefault;

        switch (request.Source)
        {
            case "human":
                bool shares = !request.Windows && driver is "console" or "terminal";
                return new SourceSelection(new PromptedHumanSource(Console(false), shares, request.TimeoutMs)) { Console = Console(false) };

            case "pty":
                if (driver != "terminal") return UsageError("--source pty drives the terminal driver only.");
                if (!request.PosixPty) return Unsupported("The PTY source needs Linux or macOS.");
                return new SourceSelection(new PtyByteSource(request.KittyFlags));

            case "native":
                if (driver == "terminal") return UsageError("--source native has no terminal injector; use --source pty.");
                if (console && !request.Windows)
                    return Unsupported("Native injection for the console driver needs Windows (WriteConsoleInputW).");
                return new SourceSelection(new RemoteInjectorSource(Console(true))) { Console = Console(true) };

            case "os":
                if (WindowsSendInputSource.Refusal(request.Windows, request.Elevated) is { } refusal)
                    return Unsupported(refusal);
                if (driver == "terminal") return UsageError("--source os drives the console and SDL drivers only.");
                return new SourceSelection(new WindowsSendInputSource(Console(true))) { Console = Console(true) };

            default:
                return UsageError($"Unknown source '{request.Source}'.");
        }
    }
}
