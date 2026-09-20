using System.Reflection;
using System.Runtime.Versioning;
using TSharpVision.Terminal.Posix;
using Xunit;

namespace TSharpVision.Terminal.Tests;

public sealed class PublicSurfaceCleanupTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public void PosixArgumentTokenizerIsInternal()
    {
        MethodInfo method = typeof(PosixPtyTerminalSession).GetMethod(
            "TokenizeArguments", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(method.IsAssembly);
        Assert.Equal(["-c", "echo hello"], PosixPtyTerminalSession.TokenizeArguments("-c \"echo hello\""));
    }
}
