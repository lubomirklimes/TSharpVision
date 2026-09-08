using System.Reflection;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class PlatformDetectionTests
{
    [Theory]
    [InlineData(true, false, false, Platform.Windows)]
    [InlineData(false, true, false, Platform.MacOS)]
    [InlineData(false, false, true, Platform.Linux)]
    [InlineData(false, true, true, Platform.MacOS)]
    public void ClassifiesPlatformsIndependentlyOfRunner(bool windows, bool macOS, bool linux, Platform expected)
    {
        var method = typeof(ScreenDriverFactory).GetMethod("ClassifyPlatform", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [windows, macOS, linux]));
    }

    [Fact]
    public void UnsupportedPlatformFailsExplicitly()
    {
        var method = typeof(ScreenDriverFactory).GetMethod("ClassifyPlatform", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [false, false, false]));
        Assert.IsType<PlatformNotSupportedException>(error.InnerException);
    }
}
