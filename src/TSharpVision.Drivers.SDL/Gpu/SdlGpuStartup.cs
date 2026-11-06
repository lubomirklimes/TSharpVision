namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// No GPU device or swapchain could be created for the window (TV13s). The SDL renderer driver does not need either,
/// so under automatic selection <see cref="SDLGpuDriver"/> reports this as a startup fallback to SDLDriver.
/// Thrown only when no GPU backend was forced (<c>TSHARPVISION_SDLGPU_BACKEND</c> / <c>[sdl] backend</c>).
/// </summary>
internal sealed class SdlGpuUnavailableException : InvalidOperationException
{
    public SdlGpuUnavailableException(string message) : base(message) { }
}

/// <summary>Startup stages at which tests can make <see cref="SDLGpuDriver"/> fail (TV13s); None in normal use.</summary>
internal enum SdlGpuStartupFault
{
    None,
    /// <summary>After <c>SDL_Init</c>, as if window creation failed (common mode: not a fallback reason).</summary>
    Window,
    /// <summary>Before <c>SDL_CreateGPUDevice</c>, as if no device were available (fallback reason).</summary>
    Device,
    /// <summary>After the device was created, as if the window could not be claimed (fallback reason).</summary>
    ClaimWindow,
    /// <summary>After <c>TTF_Init</c>, as if the font failed to load (common mode: not a fallback reason).</summary>
    Font,
}
