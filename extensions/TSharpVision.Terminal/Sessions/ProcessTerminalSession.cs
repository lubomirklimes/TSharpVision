using System.Diagnostics;

namespace TSharpVision.Terminal;

/// <summary>
/// A child process connected through redirected pipes rather than a pseudo-terminal. Its output bytes are delivered
/// unchanged, so escape sequences it writes are interpreted by the emulator; being no terminal, it cannot be resized or
/// signalled, and programs that need a TTY will not behave as in one.
/// </summary>
/// <remarks>
/// A pipe carries bare line feeds (no terminal translates "\n" to "\r\n"), so a view showing it sets
/// <see cref="TTerminal.NewLineMode"/>. Interrupting stops the process: without a terminal there is no Ctrl+C to deliver.
/// </remarks>
public sealed class ProcessTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession,
    IExitCodeTerminalSession
{
    private readonly string _fileName;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private Process? _process;
    private volatile bool _isRunning;
    private volatile bool _disposed;
    private int _exitedFired;

    /// <summary>Stores what to start; nothing runs until <see cref="StartAsync"/>.</summary>
    public ProcessTerminalSession(string fileName, string arguments = "", string? workingDirectory = null)
    {
        _fileName = fileName;
        _arguments = arguments ?? string.Empty;
        _workingDirectory = workingDirectory;
    }

    /// <inheritdoc />
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc />
    public event EventHandler? Exited;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <summary>The exit code once the process has exited; null before, or when it has none.</summary>
    public int? ExitCode { get; private set; }

    /// <inheritdoc /><remarks>Starts the process and one reader per output pipe.</remarks>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var start = new ProcessStartInfo
        {
            FileName = _fileName,
            Arguments = _arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        if (_workingDirectory != null) start.WorkingDirectory = _workingDirectory;

        var process = new Process { StartInfo = start };
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

        Task output = ReadAsync(process.StandardOutput.BaseStream, false, cancellationToken);
        Task error = ReadAsync(process.StandardError.BaseStream, true, cancellationToken);

        // Exited only after both pipes are drained, so no output is lost.
        _ = Task.WhenAll(output, error).ContinueWith(_ =>
        {
            try { process.WaitForExit(); } catch { }
            try { ExitCode = process.HasExited ? process.ExitCode : null; } catch { }
            _isRunning = false;
            FireExited();
        }, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    private async Task ReadAsync(Stream stream, bool isError, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (_disposed) return;
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(buffer.AsMemory(0, read), isError));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <inheritdoc /><remarks>Writes to a live process's standard input; does nothing otherwise.</remarks>
    public async Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        Process? process = _process;
        if (process is null || _disposed) return;
        try
        {
            if (process.HasExited) return;
            Stream stdin = process.StandardInput.BaseStream;
            await stdin.WriteAsync(input.ToArray(), cancellationToken).ConfigureAwait(false);
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }   // includes ObjectDisposedException
    }

    /// <inheritdoc /><remarks>Kills a running process and waits for it to exit.</remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process = _process;
        if (process is null)
        {
            _isRunning = false;
            return;
        }

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception) { }

        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) { }

        try { ExitCode = process.HasExited ? process.ExitCode : null; } catch { }
        _isRunning = false;
    }

    /// <summary>Does nothing: a pipe has no terminal size.</summary>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Stops the process: without a terminal there is no Ctrl+C to deliver, so stopping is the honest fallback.</summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    /// <inheritdoc /><remarks>Releases the process handle and raises no further events; call StopAsync first to end it.</remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _process?.Dispose();
    }

    private void FireExited()
    {
        if (!_disposed && Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0) Exited?.Invoke(this, EventArgs.Empty);
    }
}
