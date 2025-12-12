#pragma warning disable CA1416 // Runtime OS guards in each test body make these calls safe.

using System.Text;
using System.Threading.Tasks;
using TSharpVision.Terminal.Posix;
using Xunit;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// Integration tests for <see cref="PosixPtyTerminalSession"/>.
/// Each test returns early on platforms where POSIX PTY is not available
/// (Windows and any unsupported OS). Tests appear as "Passed" on those
/// platforms rather than "Failed", matching the convention used by
/// <see cref="ConPtyTerminalSessionTests"/>.
/// </summary>
public sealed class PosixPtyTerminalSessionTests
{
    private static bool IsPosixPtySupported
        => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static PosixPtyTerminalSession CreateSession(string fileName, string arguments)
        => new(new PosixPtyTerminalSessionOptions
        {
            FileName    = fileName,
            Arguments   = arguments,
            InitialSize = new TerminalSize(80, 24)
        });

    // ── A. Unsupported platform ───────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_OnUnsupportedPlatform_ThrowsPlatformNotSupported()
    {
        if (IsPosixPtySupported) return; // only runs on non-POSIX platforms

        using var session = CreateSession("/bin/sh", "-c \"echo test\"");
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => session.StartAsync());
    }

    // ── B. Simple command produces output ─────────────────────────────────────

    [Fact]
    public async Task StartAsync_SimpleCommand_OutputContainsExpectedText()
    {
        if (!IsPosixPtySupported) return;

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"echo posix_pty_smoke_12345\"");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("posix_pty_smoke_12345", output.ToString());
    }

    [Fact]
    public async Task StartAsync_SimpleCommand_OutputIsNonEmpty()
    {
        if (!IsPosixPtySupported) return;

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"echo hello\"");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEmpty(output.ToString());
    }

    // ── C. Exited event and IsRunning ─────────────────────────────────────────

    [Fact]
    public async Task Exited_FiresExactlyOnce_AfterNaturalExit()
    {
        if (!IsPosixPtySupported) return;

        int exitedCount = 0;
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"echo x\"");
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
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"echo x\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(session.IsRunning);
    }

    // ── D. ExitCode ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExitCode_IsZeroAfterSuccessfulCommand()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"exit 0\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public async Task ExitCode_IsNonZeroAfterFailedCommand()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"exit 42\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(42, session.ExitCode);
    }

    // ── E. Resize smoke test ──────────────────────────────────────────────────

    [Fact]
    public async Task ResizeAsync_WhileRunning_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);

        await session.StopAsync();
    }

    [Fact]
    public async Task ResizeAsync_BeforeStart_IsNoOp()
    {
        if (!IsPosixPtySupported) return;

        using var session = CreateSession("sh", "-c \"echo x\"");
        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);
    }

    // ── F. Interrupt smoke test ───────────────────────────────────────────────

    [Fact]
    public async Task InterruptAsync_WhileRunning_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);

        await session.StopAsync();
    }

    [Fact]
    public async Task InterruptAsync_BeforeStart_IsNoOp()
    {
        if (!IsPosixPtySupported) return;

        using var session = CreateSession("sh", "-c \"echo x\"");
        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);
    }

    // ── G. StopAsync idempotency ──────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        using var session = CreateSession("sh", "-c \"echo x\"");
        var ex = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(ex);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopAsync_CalledMultipleTimes_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await session.StopAsync();
        var ex = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task StopAsync_ExitedFiredAtMostOnce()
    {
        if (!IsPosixPtySupported) return;

        int count = 0;
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) =>
        {
            Interlocked.Increment(ref count);
            exited.TrySetResult(true);
        };

        await session.StartAsync();
        await session.StopAsync();
        await session.StopAsync();

        Assert.Equal(1, count);
    }

    // ── H. Dispose idempotency ────────────────────────────────────────────────

    [Fact]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        var session = CreateSession("sh", "-c \"echo x\"");
        session.Dispose();
        session.Dispose(); // idempotent
    }

    [Fact]
    public async Task Dispose_AfterStart_DoesNotThrow()
    {
        if (!IsPosixPtySupported) return;

        var session = CreateSession("sh", "-c \"sleep 2\"");
        await session.StartAsync();

        var ex = Record.Exception(() =>
        {
            session.Dispose();
            session.Dispose(); // idempotent
        });
        Assert.Null(ex);
    }

    [Fact]
    public async Task Dispose_DoesNotFireExited()
    {
        if (!IsPosixPtySupported) return;

        int count = 0;
        var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) => Interlocked.Increment(ref count);

        await session.StartAsync();

        // Give background tasks a moment to start.
        await Task.Delay(100);

        session.Dispose();

        // Brief wait to allow any spurious Exited event to arrive.
        await Task.Delay(200);

        Assert.Equal(0, count);
    }

    // ── I. StartAsync when already running ────────────────────────────────────

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Throws()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"sleep 2\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());

        await session.StopAsync();
    }

    // ── J. SendInputAsync before start is a no-op ────────────────────────────

    [Fact]
    public async Task SendInputAsync_BeforeStart_IsNoOp()
    {
        if (!IsPosixPtySupported) return;

        using var session = CreateSession("sh", "-c \"echo x\"");
        var ex = await Record.ExceptionAsync(() => session.SendInputAsync("hello\n"));
        Assert.Null(ex);
    }

    // ── K. Invalid executable: forkpty succeeds, child exits quickly ──────────

    [Fact]
    public async Task StartAsync_InvalidExecutable_ExitsQuicklyIsNotRunning()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("this_exe_does_not_exist_xyzzy123", "");
        session.Exited += (_, _) => exited.TrySetResult(true);

        // StartAsync itself should not throw (forkpty succeeds; execvp fails in
        // the child which then calls _exit(127)).
        var startEx = await Record.ExceptionAsync(() => session.StartAsync());
        Assert.Null(startEx);

        // The child exits almost immediately with code 127.
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopAsync_AfterInvalidExecutableExit_IsNoOp()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("this_exe_does_not_exist_xyzzy123", "");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var ex = await Record.ExceptionAsync(() => session.StopAsync());
        Assert.Null(ex);
    }

    // ── L. Exited fires exactly once after Stop + Dispose ─────────────────────

    [Fact]
    public async Task Exited_FiresExactlyOnce_AfterStopThenDispose()
    {
        if (!IsPosixPtySupported) return;

        int count = 0;
        var session = CreateSession("sh", "-c \"sleep 30\"");
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
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("sh", "-c \"echo x\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var ex = await Record.ExceptionAsync(() => session.ResizeAsync(new TerminalSize(120, 40)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task InterruptAsync_AfterNaturalExit_IsNoOp()
    {
        if (!IsPosixPtySupported) return;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession("sh", "-c \"echo x\"");
        session.Exited += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var ex = await Record.ExceptionAsync(() => session.InterruptAsync());
        Assert.Null(ex);
    }

    // ── N. UTF-8 output decoding ──────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_AsciiOutput_ContainsExpectedTextWithoutReplacementChars()
    {
        if (!IsPosixPtySupported) return;

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var session = CreateSession("sh", "-c \"echo utf8_ok_12345\"");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("utf8_ok_12345", output.ToString());
        Assert.DoesNotContain("\uFFFD", output.ToString()); // no UTF-8 replacement character
    }

    // ── O. Argument tokenizer: quoted arguments ────────────────────────────────

    [Fact]
    public async Task StartAsync_QuotedArgument_PassedAsOneToken()
    {
        if (!IsPosixPtySupported) return;

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // sh -c "echo hello world" — the quoted arg must arrive as a single token.
        using var session = CreateSession("sh", "-c \"echo hello world\"");
        session.OutputReceived += (_, e) => output.Append(e.Text);
        session.Exited         += (_, _) => exited.TrySetResult(true);

        await session.StartAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("hello world", output.ToString());
    }

    // ── P. TokenizeArguments unit tests ───────────────────────────────────────

    [Fact]
    public void TokenizeArguments_SimpleWords_SplitOnWhitespace()
    {
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("-c echo");
        Assert.Equal(new[] { "-c", "echo" }, tokens);
    }

    [Fact]
    public void TokenizeArguments_DoubleQuotedArg_PreservesInteriorWhitespace()
    {
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("-c \"echo hello world\"");
        Assert.Equal(new[] { "-c", "echo hello world" }, tokens);
    }

    [Fact]
    public void TokenizeArguments_SingleQuotedArg_TreatedLiterally()
    {
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("-c 'echo hello'");
        Assert.Equal(new[] { "-c", "echo hello" }, tokens);
    }

    [Fact]
    public void TokenizeArguments_BackslashInsideDoubleQuotes_EscapesNextChar()
    {
        // -c "echo \"hi\"" → ["-c", "echo \"hi\""]
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("-c \"echo \\\"hi\\\"\"");
        Assert.Equal(new[] { "-c", "echo \"hi\"" }, tokens);
    }

    [Fact]
    public void TokenizeArguments_EmptyString_ReturnsEmpty()
    {
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeArguments_OnlyWhitespace_ReturnsEmpty()
    {
        string[] tokens = PosixPtyTerminalSession.TokenizeArguments("   ");
        Assert.Empty(tokens);
    }
}
