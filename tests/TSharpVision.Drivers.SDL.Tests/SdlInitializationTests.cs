using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

[CollectionDefinition("SDL initialization", DisableParallelization = true)]
public sealed class SdlInitializationCollection { }

[Collection("SDL initialization")]
public sealed class SdlInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStartupFailurePropagatesWithDriverAndCause(bool gpu)
    {
        string? previous = Environment.GetEnvironmentVariable("TSharpVision_NO_SDL");
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", null);
        try
        {
            var cause = new DllNotFoundException("native dependency missing");
            using var driver = gpu ? (IDisposable)new SDLGpuDriver() : new SDLDriver();
            var error = Assert.Throws<InvalidOperationException>(() => Initialize(driver, () => throw cause));
            Assert.Same(cause, error.InnerException);
            Assert.Contains(driver.GetType().Name, error.Message);
            Assert.Contains(cause.Message, error.Message);
            // A failed attempt does not mark the driver attached: a retry executes initialization.
            Assert.Throws<InvalidOperationException>(() => Initialize(driver, () => throw cause));
        }
        finally { Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", previous); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitHeadlessOptOutSkipsNativeInitializationAndReportsIt(bool gpu)
    {
        string? previous = Environment.GetEnvironmentVariable("TSharpVision_NO_SDL");
        var previousError = Console.Error;
        using var output = new StringWriter();
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", "1");
        Console.SetError(output);
        try
        {
            using var driver = gpu ? (IDisposable)new SDLGpuDriver() : new SDLDriver();
            Initialize(driver, () => throw new Exception("must not run"));
            Assert.Contains("remains detached", output.ToString());
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", previous);
        }
    }

    private static void Initialize(IDisposable driver, Action action)
    {
        if (driver is SDLGpuDriver gpu) gpu.Initialize(action);
        else ((SDLDriver)driver).Initialize(action);
    }
}
