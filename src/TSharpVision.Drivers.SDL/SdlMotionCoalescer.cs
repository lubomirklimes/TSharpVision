namespace TSharpVision.Drivers.SDL;

/// <summary>
/// Coalesces consecutive SDL mouse-motion events into a single latest position, so a drag jumps
/// straight to the cursor's current position instead of replaying every intermediate step.
/// <para>
/// <see cref="TryFlush(out int, out int, out byte, out uint)"/> must also be called before any non-motion event that can interleave
/// with motion (button down/up), otherwise the emitted events end up out of order.
/// </para>
/// </summary>
internal sealed class SdlMotionCoalescer
{
    private bool _hasPending;
    private int  _pixelX;
    private int  _pixelY;
    private byte _heldButtons;
    private uint _controlKeyState;

    /// <summary>True if at least one motion has been accumulated since the last flush.</summary>
    public bool HasPending => _hasPending;

    /// <summary>
    /// Records the latest mouse position and its event-order modifier snapshot,
    /// discarding any prior unflushed position.
    /// Returns true if an earlier pending position was overwritten (i.e. coalesced away).
    /// </summary>
    public bool Accumulate(int pixelX, int pixelY, byte heldButtons, uint controlKeyState = 0)
    {
        bool coalesced = _hasPending;
        _pixelX      = pixelX;
        _pixelY      = pixelY;
        _heldButtons = heldButtons;
        _controlKeyState = controlKeyState;
        _hasPending  = true;
        return coalesced;
    }

    /// <summary>
    /// Retrieves and clears the pending position.
    /// Returns false if nothing was accumulated since the last flush.
    /// </summary>
    public bool TryFlush(
        out int pixelX, out int pixelY, out byte heldButtons, out uint controlKeyState)
    {
        if (!_hasPending)
        {
            pixelX = pixelY = 0;
            heldButtons = 0;
            controlKeyState = 0;
            return false;
        }
        pixelX      = _pixelX;
        pixelY      = _pixelY;
        heldButtons = _heldButtons;
        controlKeyState = _controlKeyState;
        _hasPending = false;
        return true;
    }

    /// <summary>Compatibility overload for callers that do not consume the modifier snapshot.</summary>
    public bool TryFlush(out int pixelX, out int pixelY, out byte heldButtons)
        => TryFlush(out pixelX, out pixelY, out heldButtons, out _);
}
