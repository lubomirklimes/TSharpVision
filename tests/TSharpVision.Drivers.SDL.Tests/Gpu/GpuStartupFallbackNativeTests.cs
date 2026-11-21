using System.Reflection;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// TV13s on a real SDL runtime (opt-in, <c>TSV_NATIVE_GPU_TESTS=1</c>): every startup stage releases SDL and SDL_ttf
/// when it fails, only a missing GPU device or swapchain asks for the SDL renderer fallback, and the factory's
/// automatic selection really starts <see cref="SDLDriver"/> after such a failure.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuStartupFallbackNativeTests : IDisposable
{
    private const string Backend = "TSHARPVISION_SDLGPU_BACKEND";
    private const string DriverVariable = "TSHARPVISION_DRIVER";
    private const string DecoratorVariable = "TSHARPVISION_DRIVER_DECORATOR";

    // The fallback signal is internal to the core assembly: recognise it by name and read its target by reflection.
    private static bool IsFallback(Exception ex) => ex.GetType().Name == "DriverStartupFallbackException";
    private static string? FallbackTarget(Exception ex) => (string?)ex.GetType().GetProperty("FallbackDriver")?.GetValue(ex);
    private readonly ITestOutputHelper _output;
    private readonly string? _savedDriver = Environment.GetEnvironmentVariable(DriverVariable);
    private readonly string? _savedBackend = Environment.GetEnvironmentVariable(Backend);
    private readonly string? _savedDecorator = Environment.GetEnvironmentVariable(DecoratorVariable);

    public GpuStartupFallbackNativeTests(ITestOutputHelper output)
    {
        _output = output;
        Environment.SetEnvironmentVariable(DriverVariable, null);
        Environment.SetEnvironmentVariable(DecoratorVariable, null);
        Environment.SetEnvironmentVariable(Backend, null);
    }

    public void Dispose()
    {
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.None;
        SDLDriver.FailStartupForAllInstancesForTesting = false;
        Environment.SetEnvironmentVariable(DriverVariable, _savedDriver);
        Environment.SetEnvironmentVariable(DecoratorVariable, _savedDecorator);
        Environment.SetEnvironmentVariable(Backend, _savedBackend);
    }

    private static void AssertSdlReleased(string context)
    {
        Assert.True(SDL3.SDL.WasInit(SDL3.SDL.InitFlags.Video) == 0, $"{context}: SDL video still initialized");
        Assert.True(SDL3.TTF.WasInit() == 0, $"{context}: SDL_ttf still initialized");
    }

    private static IDriver StartThroughFactory()
    {
        MethodInfo start = typeof(ScreenDriverFactory).GetMethod("CreateAndInitializeScreenDriver",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (IDriver)start.Invoke(null, null)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // Case 7: each stage, on its own; then a fresh driver starts normally (a retry in a fresh invocation).
    [NativeGpuTheory]
    [InlineData((int)SdlGpuStartupFault.Window, false)]
    [InlineData((int)SdlGpuStartupFault.Device, true)]
    [InlineData((int)SdlGpuStartupFault.ClaimWindow, true)]
    [InlineData((int)SdlGpuStartupFault.Font, false)]
    public void EachFailedStageReleasesSdlAndOnlyGpuStagesAskForTheFallback(int stage, bool fallback)
    {
        var fault = (SdlGpuStartupFault)stage;
        AssertSdlReleased("before");
        var driver = new SDLGpuDriver { StartupFaultForTesting = fault };
        var thrown = Assert.ThrowsAny<InvalidOperationException>(driver.Initialize);
        _output.WriteLine($"{fault}: {thrown.GetType().Name}: {thrown.Message}");
        if (fallback)
            Assert.Equal("SDLDriver", IsFallback(thrown) ? FallbackTarget(thrown) : null);
        else
            Assert.False(IsFallback(thrown));
        AssertSdlReleased($"after {fault}");
        driver.Dispose();

        using var fresh = new SDLGpuDriver();
        fresh.Initialize();
        Assert.True(SDL3.SDL.WasInit(SDL3.SDL.InitFlags.Video) != 0);
        fresh.Shutdown();
        AssertSdlReleased($"after a fresh start following {fault}");
    }

    [NativeGpuFact]
    public void AForcedGpuBackendIsNeverAFallbackReason()
    {
        Environment.SetEnvironmentVariable(Backend, OperatingSystem.IsMacOS() ? "metal" : "vulkan");
        var driver = new SDLGpuDriver { StartupFaultForTesting = SdlGpuStartupFault.Device };
        var thrown = Assert.ThrowsAny<InvalidOperationException>(driver.Initialize);
        Assert.False(IsFallback(thrown));
        AssertSdlReleased("forced backend");
    }

    // Case 1
    [NativeGpuFact]
    public void AutomaticSelectionKeepsAWorkingGpuDriver()
    {
        IDriver driver = StartThroughFactory();
        try
        {
            Assert.IsType<SDLGpuDriver>(driver);
        }
        finally
        {
            driver.Shutdown();
            (driver as IDisposable)?.Dispose();
        }
        AssertSdlReleased("after shutdown");
    }

    // Case 2, natively: no GPU device \u2192 the SDL renderer driver starts, works and shuts down cleanly.
    [NativeGpuFact]
    public void AutomaticSelectionStartsTheSdlRendererWhenNoGpuDeviceIsAvailable()
    {
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.Device;
        IDriver driver = StartThroughFactory();
        try
        {
            var sdl = Assert.IsType<SDLDriver>(driver);
            Assert.True(SDL3.SDL.WasInit(SDL3.SDL.InitFlags.Video) != 0);
            ScreenBuffer screen = sdl.AllocateScreenBuffer();
            screen.SetChar(0, 0, TScreenChar.CreateCluster("\U0001F600", 2, new TColorAttr(0x1F)));
            screen.SetChar(1, 0, TScreenChar.CreateTrail(new TColorAttr(0x1F)));
            sdl.WriteBuf(0, 0, (int)screen.Width, 1, screen.Data[..(int)screen.Width]);
            sdl.PumpMessages();
            Assert.True(sdl.TextCapabilities.HasFlag(TextCapabilities.ClusterText));
        }
        finally
        {
            driver.Shutdown();
            (driver as IDisposable)?.Dispose();
        }
        AssertSdlReleased("after the fallback driver shut down");
    }

    // Case 3, natively: the exact GPU driver name fails fast; the SDL renderer is not started.
    [NativeGpuFact]
    public void AnExplicitGpuRequestFailsFast()
    {
        Environment.SetEnvironmentVariable(DriverVariable, "SDLGpuDriver");
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.Device;
        var thrown = Assert.Throws<InvalidOperationException>(StartThroughFactory);
        Assert.False(IsFallback(thrown));
        Assert.Contains("SDL_CreateGPUDevice failed", thrown.Message);
        AssertSdlReleased("explicit GPU");
    }

    // The portable alias asks for SDL in general and may fall back.
    [NativeGpuFact]
    public void TheSdlAliasMayFallBack()
    {
        Environment.SetEnvironmentVariable(DriverVariable, "sdl");
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.ClaimWindow;
        IDriver driver = StartThroughFactory();
        try { Assert.IsType<SDLDriver>(driver); }
        finally { driver.Shutdown(); (driver as IDisposable)?.Dispose(); }
        AssertSdlReleased("alias");
    }

    // Case 5, natively: both fail \u2192 one aggregated error, nothing left initialized, no further retry.
    [NativeGpuFact]
    public void WhenBothSdlDriversFailBothErrorsAreReported()
    {
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.Device;
        SDLDriver.FailStartupForAllInstancesForTesting = true;
        var thrown = Assert.Throws<AggregateException>(StartThroughFactory);
        _output.WriteLine(thrown.Message);
        Assert.Contains("SDL_CreateGPUDevice failed", thrown.Message);
        Assert.Contains("SDL renderer startup failed", thrown.Message);
        AssertSdlReleased("both failed");
    }

    // Case 4, natively: an explicit SDL renderer request never touches the GPU driver (its fault would throw).
    [NativeGpuFact]
    public void AnExplicitSdlRendererRequestNeverAttemptsTheGpu()
    {
        Environment.SetEnvironmentVariable(DriverVariable, "SDLDriver");
        SDLGpuDriver.StartupFaultForAllInstancesForTesting = SdlGpuStartupFault.Window;
        IDriver driver = StartThroughFactory();
        try { Assert.IsType<SDLDriver>(driver); }
        finally { driver.Shutdown(); (driver as IDisposable)?.Dispose(); }
        AssertSdlReleased("explicit SDLDriver");
    }
}

/// <summary>A theory that runs only with <c>TSV_NATIVE_GPU_TESTS=1</c>, like <see cref="NativeGpuFactAttribute"/>.</summary>
public sealed class NativeGpuTheoryAttribute : TheoryAttribute
{
    public NativeGpuTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable(NativeGpuFactAttribute.EnvironmentVariable) != "1")
            Skip = $"Native SDL_GPU test: set {NativeGpuFactAttribute.EnvironmentVariable}=1 on a machine with a GPU.";
    }
}
