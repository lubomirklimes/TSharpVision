namespace TSharpVision.Terminal;

/// <summary>
/// The ordered, single-writer path from <see cref="ITerminalSession.SendInputAsync"/> to a PTY.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A write to a PTY blocks while the child is not reading and the kernel or
/// conhost input buffer is full — a large paste into a busy program is enough. Callers are UI
/// threads, so the bytes are queued here and written by one pool thread, in order, instead of on
/// the caller's stack.
/// </para>
/// <para>
/// <b>Ordering.</b> Items are written strictly in enqueue order by at most one drain task at a time.
/// </para>
/// <para>
/// <b>Failure and shutdown.</b> When the writer reports that the PTY is gone, or <see cref="Close"/>
/// is called, everything still queued is discarded and its task completes: input to a finished
/// session is dropped silently, which is the <see cref="ITerminalSession"/> contract for a session
/// that is not running. The returned tasks never fault.
/// </para>
/// <para>
/// <b>Bound.</b> At most <see cref="MaximumPendingBytes"/> may wait. Input beyond that is dropped
/// rather than letting a program that never reads grow the heap without limit.
/// </para>
/// </remarks>
internal sealed class TerminalInputPump
{
    /// <summary>The most input that may be waiting for the child to read it.</summary>
    internal const int MaximumPendingBytes = 4 * 1024 * 1024;

    // Writes every byte, or returns false when the PTY can no longer accept input.
    private readonly Func<byte[], bool> _writeAll;
    private readonly object _gate = new();
    private readonly Queue<(byte[] Data, TaskCompletionSource Written)> _pending = new();
    private int _pendingBytes;
    private bool _draining;
    private bool _closed;

    internal TerminalInputPump(Func<byte[], bool> writeAll)
        => _writeAll = writeAll ?? throw new ArgumentNullException(nameof(writeAll));

    /// <summary>True once <see cref="Close"/> ran or the writer reported the PTY gone.</summary>
    internal bool IsClosed
    {
        get { lock (_gate) return _closed; }
    }

    /// <summary>Bytes queued and not yet handed to the writer.</summary>
    internal int PendingBytes
    {
        get { lock (_gate) return _pendingBytes; }
    }

    /// <summary>
    /// Queues <paramref name="data"/>. The task completes when the bytes were written, or when they
    /// were discarded because the session ended or the bound was exceeded.
    /// </summary>
    internal Task EnqueueAsync(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return Task.CompletedTask;

        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool startDrain;
        lock (_gate)
        {
            if (_closed || _pendingBytes > MaximumPendingBytes - data.Length) return Task.CompletedTask;

            _pending.Enqueue((data, written));
            _pendingBytes += data.Length;
            startDrain = !_draining;
            _draining = true;
        }

        if (startDrain) _ = Task.Run(Drain);
        return written.Task;
    }

    /// <summary>Stops accepting input and discards whatever is still queued. Idempotent.</summary>
    internal void Close()
    {
        List<TaskCompletionSource> discarded;
        lock (_gate)
        {
            _closed = true;
            discarded = new List<TaskCompletionSource>(_pending.Count);
            foreach ((byte[] _, TaskCompletionSource written) in _pending) discarded.Add(written);
            _pending.Clear();
            _pendingBytes = 0;
        }
        foreach (TaskCompletionSource written in discarded) written.TrySetResult();
    }

    private void Drain()
    {
        while (true)
        {
            (byte[] Data, TaskCompletionSource Written) item;
            lock (_gate)
            {
                if (_closed || _pending.Count == 0)
                {
                    _draining = false;
                    return;
                }
                item = _pending.Dequeue();
                _pendingBytes -= item.Data.Length;
            }

            bool ok;
            try { ok = _writeAll(item.Data); }
            catch (Exception) { ok = false; }

            item.Written.TrySetResult();
            if (!ok) Close();
        }
    }
}
