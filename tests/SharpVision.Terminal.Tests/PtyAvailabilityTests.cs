using SharpVision.Terminal;
using Xunit;

namespace SharpVision.Terminal.Tests;

/// <summary>
/// Tests for <see cref="PtyAvailability"/>.
/// All tests are purely logic-based and do not start any process or UI.
/// </summary>
public sealed class PtyAvailabilityTests
{
    [Fact]
    public void IsConPtySupported_ReturnsTrueOnSupportedWindowsVersion()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        Assert.True(PtyAvailability.IsConPtySupported);
    }

    [Fact]
    public void IsConPtySupported_ReturnsFalseOnNonWindowsOrOlderWindows()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        Assert.False(PtyAvailability.IsConPtySupported);
    }

    [Fact]
    public void IsPosixPtySupported_ReturnsTrueOnLinuxOrMacOS()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        Assert.True(PtyAvailability.IsPosixPtySupported);
    }

    [Fact]
    public void IsPosixPtySupported_ReturnsFalseOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(PtyAvailability.IsPosixPtySupported);
    }

    [Fact]
    public void IsAnyPtySupported_EqualsConPtyOrPosixPty()
    {
        Assert.Equal(
            PtyAvailability.IsConPtySupported || PtyAvailability.IsPosixPtySupported,
            PtyAvailability.IsAnyPtySupported);
    }
}
