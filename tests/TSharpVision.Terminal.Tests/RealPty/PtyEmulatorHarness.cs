using System.Runtime.Versioning;
using TSharpVision.Terminal.Posix;
using TSharpVision.Terminal.Windows;
using Xunit;

namespace TSharpVision.Terminal.Tests.RealPty;

/// <summary>
/// A real PTY session connected straight to a <see cref="TerminalEmulator"/>, as <see cref="TTerminal"/> connects them:
/// output fed under a lock, queries answered, keys encoded by <see cref="TerminalInputEncoder"/>. Waits are bounded and
/// on screen conditions, never on time alone.
/// </summary>
internal sealed class PtyEmulatorHarness : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PtyEmulatorHarness(ITerminalSession session, int columns, int rows)
    {
        Session = session;
        Emulator = new TerminalEmulator(columns, rows, 500);
        session.OutputReceived += (_, e) =>
        {
            string? replies;
            lock (_gate)
            {
                Emulator.Feed(e.Data.Span);
                replies = Emulator.TakeResponses();
            }

            if (replies is not null) _ = session.SendTextAsync(replies);
        };
        session.Exited += (_, _) => _exited.TrySetResult();
    }

    public ITerminalSession Session { get; }

    public TerminalEmulator Emulator { get; }

    public Task Exited => _exited.Task;

    /// <summary>The terminal type the emulator implements, as the Commander tells POSIX programs.</summary>
    public static readonly IReadOnlyDictionary<string, string?> PosixEnvironment = new Dictionary<string, string?>
    {
        ["TERM"] = TTerminal.TermName,
        ["COLORTERM"] = null,
        ["LANG"] = "C.UTF-8",
        ["LC_ALL"] = "C.UTF-8",
        ["PS1"] = "$ ",
        ["HISTFILE"] = "/dev/null",
    };

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static PtyEmulatorHarness Posix(string fileName, string arguments = "", int columns = 80, int rows = 24,
        string? workingDirectory = null)
        => new(new PosixPtyTerminalSession(new PosixPtyTerminalSessionOptions
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
            InitialSize = new TerminalSize(columns, rows),
            Environment = PosixEnvironment,
        }), columns, rows);

    [SupportedOSPlatform("windows10.0.17763")]
    public static PtyEmulatorHarness ConPty(string fileName, string arguments = "", int columns = 80, int rows = 24,
        string? workingDirectory = null)
        => new(new ConPtyTerminalSession(new ConPtyTerminalSessionOptions
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
            InitialSize = new TerminalSize(columns, rows),
        }), columns, rows);

    public Task StartAsync() => Session.StartAsync();

    public Task TypeAsync(string text) => Session.SendTextAsync(text);

    public Task KeyAsync(ushort keyCode, uint modifiers = 0)
    {
        bool application;
        lock (_gate) application = Emulator.ApplicationCursorKeys;
        string encoded = TerminalInputEncoder.EncodeKey(new KeyDownEvent { keyCode = keyCode, controlKeyState = modifiers }, application)
                         ?? throw new InvalidOperationException($"No encoding for key 0x{keyCode:X4}.");
        return Session.SendTextAsync(encoded);
    }

    /// <summary>Resizes emulator then session, in the order <see cref="TTerminal"/> uses.</summary>
    public Task ResizeAsync(int columns, int rows)
    {
        lock (_gate) Emulator.Resize(columns, rows);
        return ((IResizableTerminalSession)Session).ResizeAsync(new TerminalSize(columns, rows));
    }

    /// <summary>The screen as rows of text.</summary>
    public string Screen
    {
        get
        {
            lock (_gate) return string.Join('\n', Enumerable.Range(0, Emulator.Rows).Select(Emulator.GetRowText));
        }
    }

    public T Read<T>(Func<TerminalEmulator, T> read)
    {
        lock (_gate) return read(Emulator);
    }

    /// <summary>Waits until <paramref name="condition"/> holds for the emulator; fails with the screen when it never does.</summary>
    public async Task WaitForAsync(Func<TerminalEmulator, bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            lock (_gate)
                if (condition(Emulator)) return;
            if (DateTime.UtcNow > deadline) Assert.Fail($"Never saw {what}. Screen:\n{Screen}");
            await Task.Delay(25);
        }
    }

    public Task WaitForTextAsync(string text)
        => WaitForAsync(e => Enumerable.Range(0, e.Rows).Any(r => e.GetRowText(r).Contains(text, StringComparison.Ordinal)),
            $"'{text}'");

    public async Task WaitForExitAsync()
    {
        Task finished = await Task.WhenAny(Exited, Task.Delay(Timeout));
        Assert.True(ReferenceEquals(finished, Exited), $"The program did not exit. Screen:\n{Screen}");
    }

    public void Dispose()
    {
        try { Session.StopAsync().Wait(TimeSpan.FromSeconds(10)); } catch { }
        Session.Dispose();
    }
}
