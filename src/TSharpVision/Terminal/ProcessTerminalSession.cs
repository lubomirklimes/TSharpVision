using System.Diagnostics;

namespace TSharpVision;

/// <summary>
/// Pipe-based process session MVP. Starts a child process with redirected
/// stdin/stdout/stderr and surfaces output as <see cref="ITerminalSession.OutputReceived"/>
/// events.
///
/// Limitations (intentional for this MVP):
/// - No PTY/ConPTY — the child process runs with redirected pipes, not a real
///   terminal. Interactive programs that require a TTY will not behave correctly.
/// - No ANSI escape sequence parsing — raw bytes are forwarded as text.
/// - No terminal-size negotiation (no-op implementation of
///   <see cref="IResizableTerminalSession"/>; will be wired to ConPTY in a
///   future milestone).
/// - Interrupt support (<see cref="IInterruptibleTerminalSession"/>) is a
///   safe fallback that kills the process. Because the process runs behind
///   redirected pipes (no PTY/ConPTY), there is no way to deliver a true
///   SIGINT/Ctrl+C to the child; killing is the safest honest alternative.
/// </summary>
public sealed class ProcessTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private readonly string _fileName;
    private readonly string _arguments;
    private readonly string _workingDirectory;
    private Process _process;
    private volatile bool _isRunning;
    private int _exitedFired;   // 0 = not fired, 1 = fired; compare-exchange guards single fire
    private bool _disposed;

    public ProcessTerminalSession(string fileName, string arguments = "", string workingDirectory = null)
    {
        _fileName = fileName;
        _arguments = arguments ?? string.Empty;
        _workingDirectory = workingDirectory;
    }

    public event EventHandler<TerminalOutputEventArgs> OutputReceived;
    public event EventHandler Exited;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Exit code of the process, populated after <see cref="Exited"/> fires.
    /// Null if the process has not yet exited or exited abnormally without a
    /// code.
    /// </summary>
    public int? ExitCode { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _fileName,
            Arguments = _arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        if (_workingDirectory != null)
            psi.WorkingDirectory = _workingDirectory;

        var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }
        _process = process;
        _isRunning = true;

        var stdoutDone = ReadStreamAsync(_process.StandardOutput, false, cancellationToken);
        var stderrDone = ReadStreamAsync(_process.StandardError, true, cancellationToken);

        // Fire Exited after both streams are fully consumed so no output is lost.
        _ = Task.WhenAll(stdoutDone, stderrDone).ContinueWith(_ =>
        {
            try { _process.WaitForExit(); } catch { }
            ExitCode = _process.HasExited ? _process.ExitCode : (int?)null;
            _isRunning = false;
            FireExited();
        }, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    private void FireExited()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReadStreamAsync(StreamReader reader, bool isError, CancellationToken ct)
    {
        try
        {
            var buffer = new char[256];
            int n;
            while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                if (ct.IsCancellationRequested) break;
                string text = new string(buffer, 0, n);
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text, isError));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    public async Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_process != null && !_process.HasExited)
            await _process.StandardInput.WriteAsync(input).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_process == null)
        {
            _isRunning = false;
            return;
        }

        if (!_process.HasExited)
        {
            try { _process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Exception) { }
        }

        try { await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) { }

        ExitCode = _process.HasExited ? _process.ExitCode : (int?)null;
        _isRunning = false;
    }

    /// <summary>
    /// No-op. Redirected-pipe processes do not support terminal size
    /// negotiation. A future ConPTY implementation will forward the size here.
    /// </summary>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Requests cancellation of the running process. Because this session uses
    /// redirected pipes rather than a PTY/ConPTY, a true SIGINT/Ctrl+C cannot
    /// be delivered. <see cref="StopAsync"/> (which calls <see cref="Process.Kill"/>
    /// ) is used as the safest fallback. Safe to call when the process is not
    /// running or has already exited.
    /// </summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _process?.Dispose();
    }
}
