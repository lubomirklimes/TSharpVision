using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TSharpVision.Terminal.Posix;

/// <summary>
/// Sole owner of a PTY master file descriptor.
/// </summary>
/// <remarks>
/// A descriptor is a small integer the kernel hands out again as soon as it is closed. Keeping the
/// number in a plain field after <c>close</c> let a later read, write or ioctl reach whatever file
/// or socket the process opened next. The <see cref="SafeHandle"/> reference count prevents that:
/// every use runs between <see cref="SafeHandle.DangerousAddRef"/> and
/// <see cref="SafeHandle.DangerousRelease"/>, <see cref="SafeHandle.Dispose()"/> refuses new
/// references at once, and the real <c>close</c> runs exactly once, when the last reference is
/// released.
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PtyMasterHandle : SafeHandle
{
    internal PtyMasterHandle(int fd) : base(new IntPtr(-1), ownsHandle: true)
        => SetHandle(new IntPtr(fd));

    /// <inheritdoc/>
    public override bool IsInvalid => handle.ToInt64() < 0;

    /// <summary>The descriptor. Meaningful only while the caller holds a reference.</summary>
    internal int Fd => (int)handle.ToInt64();

    /// <summary>
    /// Takes a reference, or returns false once the handle has been disposed. A true result must be
    /// paired with <see cref="SafeHandle.DangerousRelease"/>.
    /// </summary>
    internal bool TryAcquire()
    {
        bool added = false;
        try
        {
            DangerousAddRef(ref added);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        return added;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle() => NativeMethods.Close(Fd) == 0;
}
