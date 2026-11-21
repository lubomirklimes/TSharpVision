namespace TSharpVision.Drivers;

/// <summary>
/// A driver's <see cref="IDriver.Initialize"/> failed for a reason another registered driver does not share, after
/// the failing driver released everything it had started. Under automatic selection the factory then initializes
/// <see cref="FallbackDriver"/> once instead (TV13s: <c>SDLGpuDriver</c> → <c>SDLDriver</c> when no GPU device or
/// swapchain is available).
/// </summary>
/// <remarks>
/// Thrown only by drivers inside the framework (it is internal); any other exception from
/// <see cref="IDriver.Initialize"/> keeps propagating unchanged. It derives from
/// <see cref="InvalidOperationException"/>, the type driver startup failures have always had.
/// </remarks>
internal sealed class DriverStartupFallbackException : InvalidOperationException
{
    public DriverStartupFallbackException(string message, string fallbackDriver, Exception innerException)
        : base(message, innerException)
    {
        FallbackDriver = fallbackDriver;
    }

    /// <summary>The registered driver name (<see cref="ScreenDriverAttribute.Driver"/>) to try instead.</summary>
    public string FallbackDriver { get; }
}
