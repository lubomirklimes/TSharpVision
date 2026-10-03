# Drivers

Drivers render the core cell grid and translate host input. Select `TSHARPVISION_DRIVER` before runtime startup, or configure `[driver] name`. The environment takes precedence; otherwise the highest-priority installed driver for the OS is selected. An unavailable name prints a warning and falls back to the default, possibly `NullDriver`.

The runtime reads the user's `.tsvision.cfg`, then overlays `<AssemblyName>.cfg` beside the executable. Driver discovery expects ordinary assembly files in the application output.

| Driver | Host/output | Input limits |
|---|---|---|
| `Win32ConsoleDriver` | Windows console cells | Release and modifier reports; repeat counts are not expanded |
| `SDLDriver` / `SDLGpuDriver` | Desktop window, renderer / GPU | Native repeat and releases; separate Unicode/IME text input |
| `AnsiTerminalDriver` | Linux/macOS ANSI terminal | Release/modifier capabilities depend on negotiated flags |
| `NullDriver` | Headless | No interactive input |

The [event model](architecture/event-model.md) explains payloads, routing and terminal capability negotiation.

## Console

Install `TSharpVision.Drivers.Console` and run in an attached Windows console, including Windows Terminal. Redirected handles and IDE output panes can leave the driver unattached with no UI. Native key records are preserved, but `wRepeatCount` is not expanded into multiple framework events.

```csharp
using System;
using TSharpVision;

Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "Win32ConsoleDriver");
return TSharpVisionRuntime.Run(() => new TApplication(), "TSharpVision");
```

## ANSI terminal

Install `TSharpVision.Drivers.Terminal` for an interactive UTF-8 Linux/macOS TTY with xterm-style ANSI support. Rendering, mouse reports and key delivery depend on the emulator. The driver enters raw mode and the alternate screen, and restores terminal modes and screen state on shutdown. Its Windows initialization is a no-op; use Console there.

```csharp
using System;
using TSharpVision;

Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "AnsiTerminalDriver");
return TSharpVisionRuntime.Run(() => new TApplication(), "TSharpVision");
```

## SDL

Install `TSharpVision.Drivers.SDL`. Restore/publish must supply SDL3-CS native dependencies for the target OS and architecture. Both driver choices require a desktop display and a usable monospace font; [platform support](platform-support.md) covers assets and deployment limits.

```csharp
using System;
using TSharpVision;

Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "SDLDriver");
return TSharpVisionRuntime.Run(() => new TApplication(), "TSharpVision");
```

`SDLDriver` uses SDL's renderer. `SDLGpuDriver` uses the GPU/shadercross path and needs suitable graphics support; select it by changing the name above. A failed GPU initialization does not guarantee fallback to another driver. SDL key events carry identity and repeat/release information, while TextInput supplies Unicode and IME text separately.

## Headless

`NullDriver` is included in core. Without a programmed exit, its event loop would wait for input indefinitely. This example exits on its first idle pass:

```csharp
using System;
using TSharpVision;
using TSharpVision.Constants;

Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "NullDriver");
return TSharpVisionRuntime.Run(() => new HeadlessApp(), "Headless check");

sealed class HeadlessApp : TApplication
{
    public override void Idle()
    {
        base.Idle();
        EndModal(Views.cmQuit);
    }
}
```

The interactive examples show an empty desktop; Alt+X exits.
