namespace SharpVision.Terminal;

/// <summary>
/// Reports pseudo-terminal availability for the current runtime platform and OS version.
/// </summary>
public static class PtyAvailability
{
    /// <summary>
    /// <see langword="true"/> when Windows ConPTY is supported on the current platform.
    /// Requires Windows 10 version 1809 (build 17763) or later.
    /// </summary>
    public static bool IsConPtySupported
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>
    /// <see langword="true"/> when POSIX PTY is supported on the current platform.
    /// Requires Linux or macOS.
    /// </summary>
    public static bool IsPosixPtySupported
        => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>
    /// <see langword="true"/> when at least one PTY implementation is available
    /// on the current platform.
    /// </summary>
    public static bool IsAnyPtySupported
        => IsConPtySupported || IsPosixPtySupported;
}
