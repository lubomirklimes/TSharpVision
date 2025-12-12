#pragma warning disable CA1416 // Runtime OS guards in each test body make these calls safe.

using System.Text;
using System.Threading.Tasks;
using TSharpVision.Terminal.Windows;
using Xunit;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// Integration tests for <see cref="ConPtyTerminalSession"/>.
/// Each test returns early on platforms where ConPTY is not available (non-Windows,
/// or Windows earlier than build 17763). Tests appear as "Passed" on those
/// platforms rather than "Failed".
/// </summary>
public sealed class ConPtyTerminalSessionTests
{
    private static bool IsConPtySupported
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    private static ConPtyTerminalSession CreateSession(string fileName, string arguments)
        => new(new ConPtyTerminalSessionOptions
        {
            FileName  = fileName,
            Arguments = arguments,
            InitialSize = new TerminalSize(80, 24)
        });

    // ── A. Unsupported platform ───────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_OnUnsupportedPlatform_ThrowsPlatformNotSupported()
    {
        if (IsConPtySupported) return; // test only runs on non-supported platforms

        using var session = CreateSession("cmd.exe", "/c echo test");
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => session.StartAsync());
    }

    // ── B. Simple command produces output ─────────────────────────────────────

    [Fact]
    public async Task StartAsync_SimpleCommand_OutputContainsExpectedText()
    {
        if (!IsConPtySupported) return;

        var output  = new StringBuilder();
        var exited  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c echo ConPTY_smoke_12345");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("ConPTY_smoke_12345", output.ToString());
    }

    [Fact]
    public async Task StartAsync_SimpleCommand_OutputIsNonEmpty()
    {
        if (!IsConPtySupported) return;

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c dir");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEmpty(output.ToString());
    }

    // ── C. Exited event fires exactly once ────────────────────────────────────

    [Fact]
    public async Task Exited_FiresExactlyOnce_AfterNaturalExit()
    {
        if (!IsConPtySupported) return;

        int exitedCount = 0;
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) =>
        {
            Interlocked.Increment(ref exitedCount);
            exited.TrySetResult(true);
        };

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(1, exitedCount);
    }

    [Fact]
    public async Task IsRunning_FalseAfterNaturalExit()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(session.IsRunning);
    }

    // ── D. ExitCode ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExitCode_IsZeroAfterSuccessfulCommand()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c exit 0");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public async Task ExitCode_IsNonZeroAfterFailedCommand()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c exit 42");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(42, session.ExitCode);
    }

    // ── E. Resize smoke test ──────────────────────────────────────────────────

    [Fact]
    public async Task ResizeAsync_WhileRunning_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);

        // Wait for the process to exit cleanly.
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ResizeAsync_BeforeStart_IsNoOp()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c echo x");
        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);
    }

    // ── F. Interrupt smoke test ───────────────────────────────────────────────

    [Fact]
    public async Task InterruptAsync_WhileRunning_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task InterruptAsync_BeforeStart_IsNoOp()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c echo x");
        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);
    }

    // ── G. StopAsync idempotency ──────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c echo x");
        var ex = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(ex);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopAsync_CalledMultipleTimes_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c echo x");
        await session.StartAsync();

        var ex = await Record.ExceptionAsync(async () =>
        {
            await session.StopAsync();
            await session.StopAsync();
            await session.StopAsync();
        });
        Assert.Null(ex);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopAsync_ExitedFiredAtMostOnce()
    {
        if (!IsConPtySupported) return;

        int count = 0;
        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => Interlocked.Increment(ref count);

        await session.StartAsync();
        await session.StopAsync();
        await session.StopAsync();

        Assert.True(count <= 1);
    }

    // ── H. Dispose idempotency ────────────────────────────────────────────────

    [Fact]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        var session = CreateSession("cmd.exe", "/c echo x");
        var ex = Record.Exception(() => { session.Dispose(); session.Dispose(); });
        Assert.Null(ex);
    }

    [Fact]
    public async Task Dispose_AfterStart_DoesNotThrow()
    {
        if (!IsConPtySupported) return;

        var session = CreateSession("cmd.exe", "/c echo x");
        await session.StartAsync();
        var ex = Record.Exception(() => { session.Dispose(); session.Dispose(); });
        Assert.Null(ex);
    }

    [Fact]
    public async Task Dispose_DoesNotFireExited()
    {
        if (!IsConPtySupported) return;

        int count = 0;
        var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => Interlocked.Increment(ref count);

        session.Dispose();
        await Task.Delay(100); // small settle time

        Assert.Equal(0, count);
    }

    // ── I. StartAsync guard: already running ──────────────────────────────────

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Throws()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c pause");
        await session.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());

        await session.StopAsync();
    }

    // ── J. SendInputAsync: no-op when not running ─────────────────────────────

    [Fact]
    public async Task SendInputAsync_BeforeStart_IsNoOp()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("cmd.exe", "/c echo x");
        var ex = await Record.ExceptionAsync(() => session.SendInputAsync("hello\n"));
        Assert.Null(ex);
    }

    // ── K. Start failure: invalid executable ──────────────────────────────────

    [Fact]
    public async Task StartAsync_InvalidExecutable_ThrowsAndIsNotRunning()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("this_exe_does_not_exist_xyzzy123.exe", "");

        // CreateProcess fails → StartAsync throws; all handles must be released.
        var startEx = await Record.ExceptionAsync(() => session.StartAsync());
        Assert.NotNull(startEx);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopAsync_AfterFailedStart_IsNoOp()
    {
        if (!IsConPtySupported) return;

        using var session = CreateSession("this_exe_does_not_exist_xyzzy123.exe", "");
        try { await session.StartAsync(); } catch { }

        var ex = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(ex);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task Dispose_AfterFailedStart_IsNoOp()
    {
        if (!IsConPtySupported) return;

        var session = CreateSession("this_exe_does_not_exist_xyzzy123.exe", "");
        try { await session.StartAsync(); } catch { }

        var ex = Record.Exception(() => { session.Dispose(); session.Dispose(); });
        Assert.Null(ex);
    }

    // ── L. Exited fires exactly once after Stop + Dispose ─────────────────────

    [Fact]
    public async Task Exited_FiresExactlyOnce_AfterStopThenDispose()
    {
        if (!IsConPtySupported) return;

        int count = 0;
        var session = CreateSession("cmd.exe", "/c pause");
        session.Exited += (_, _) => Interlocked.Increment(ref count);

        await session.StartAsync();
        await session.StopAsync();
        await Task.Delay(50);
        session.Dispose();
        await Task.Delay(100);

        Assert.Equal(1, count);
    }

    // ── M. Resize / Interrupt after natural exit are no-ops ───────────────────

    [Fact]
    public async Task ResizeAsync_AfterNaturalExit_IsNoOp()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task InterruptAsync_AfterNaturalExit_IsNoOp()
    {
        if (!IsConPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("cmd.exe", "/c echo x");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);
    }
}
