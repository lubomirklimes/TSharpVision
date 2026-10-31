using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.SDL;

// Owned pixel copy; no driver, renderer, device or window handle escapes this contract.
internal sealed record SdlDiagnosticCapture(int Width, int Height, byte[] Rgba,
    string Layer, Dictionary<string, string> Details);

public partial class SDLDriver
{
    // Called on the existing driver's UI thread at an idle boundary.
    internal SdlDiagnosticCapture CaptureDiagnosticPixels()
    {
        var active = sdlRenderer ?? throw new InvalidOperationException("SDL renderer detached.");
        if (!_attached || MessageLoop is null) throw new InvalidOperationException("SDL driver is not active.");
        if (active.BeforePresentForTesting is not null) throw new InvalidOperationException("Capture already active.");
        SdlDiagnosticCapture? captured = null;
        active.BeforePresentForTesting = handle =>
        {
            if (!SDL3.SDL.GetCurrentRenderOutputSize(handle, out int outputWidth, out int outputHeight))
                throw new InvalidOperationException("SDL renderer output geometry unavailable.");
            DiagnosticCaptureLimits.Validate(outputWidth, outputHeight);
            IntPtr surface = SDL3.SDL.RenderReadPixels(handle, null);
            if (surface == IntPtr.Zero) throw new InvalidOperationException("SDL_RenderReadPixels failed: " + SDL3.SDL.GetError());
            try
            {
                IntPtr converted = SDL3.SDL.ConvertSurface(surface, SDL3.SDL.PixelFormat.ABGR8888);
                if (converted == IntPtr.Zero) throw new InvalidOperationException("SDL pixel conversion failed.");
                try
                {
                    var s = Marshal.PtrToStructure<SDL3.SDL.Surface>(converted);
                    DiagnosticCaptureLimits.Validate(s.Width, s.Height);
                    if (s.Width != outputWidth || s.Height != outputHeight) throw new InvalidOperationException("SDL readback geometry changed.");
                    var rgba = new byte[checked(s.Width * s.Height * 4)];
                    for (int y = 0; y < s.Height; y++) Marshal.Copy(s.Pixels + y * s.Pitch, rgba, y * s.Width * 4, s.Width * 4);
                    captured = new(s.Width, s.Height, rgba, "renderer-pixels", new()
                    {
                        ["provider"] = "SDL_RenderReadPixels",
                        ["completion"] = "synchronous readback before SDL_RenderPresent",
                        ["renderingPath"] = SDL3.SDL.GetRendererName(handle) ?? "unknown",
                        ["cellWidth"] = active.CellWidth.ToString(), ["cellHeight"] = active.CellHeight.ToString(),
                        ["cursor"] = $"{_caretX},{_caretY}/{_cursorType}",
                        ["presentation"] = "not measured"
                    });
                }
                finally { SDL3.SDL.DestroySurface(converted); }
            }
            finally { SDL3.SDL.DestroySurface(surface); }
        };
        try { MessageLoop(active); }
        finally { active.BeforePresentForTesting = null; }
        return captured ?? throw new InvalidOperationException("Application renderer callback did not render.");
    }
}

internal static class DiagnosticCaptureLimits
{
    internal const int MaximumRgbaBytes = 16 * 1024 * 1024;
    internal static void Validate(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new NotSupportedException("Native capture geometry must be positive.");
        if ((long)width * height * 4 > MaximumRgbaBytes)
            throw new NotSupportedException("Native capture exceeds the 16 MiB RGBA image limit.");
    }
}
