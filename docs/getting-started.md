# Getting started

Install the .NET 10 SDK and create an application:

```shell
dotnet new console -n HelloTSharpVision -f net10.0
cd HelloTSharpVision
dotnet add package TSharpVision.Drivers.Console
```

Use `TSharpVision.Drivers.Console` for Windows, `TSharpVision.Drivers.Terminal` for Linux/macOS terminals, or `TSharpVision.Drivers.SDL` for a desktop window. Each driver depends on core. Core alone supplies the headless `NullDriver`.

For local nupkg files, register their directory and keep nuget.org enabled for upstream dependencies:

```shell
dotnet nuget add source C:\packages\tsharpvision --name TSharpVisionLocal
```

Use an absolute directory on Linux/macOS too. Use `--version` to select an exact version from your feed, and `--prerelease` only for a prerelease build.

## Run an application

Replace `Program.cs` with this Windows Console example:

```csharp
using System;
using TSharpVision;
using TSharpVision.Constants;

Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "Win32ConsoleDriver");
return TSharpVisionRuntime.Run(() => new HelloApp(), "Hello TSharpVision");

sealed class HelloApp : TApplication
{
    public HelloApp()
    {
        var window = new TWindow(new TRect(2, 2, 52, 10), "Hello", Views.wnNoNumber);
        window.Insert(new TStaticText(new TRect(2, 2, 46, 4),
            "Hello from TSharpVision! Alt+X exits."));
        DeskTop.Insert(window);
    }
}
```

Run `dotnet run` in an attached console. Alt+X exits; closing the greeting window leaves the application running. For an ANSI terminal select `AnsiTerminalDriver`; for an SDL window select `sdl` (the GPU driver, `SDLGpuDriver`) or `SDLDriver`. The aliases `console` and `terminal` pick the native text driver on any OS.

Bounds use character cells relative to the owner. `DeskTop.Insert` attaches the window to the desktop.

Set driver selection before `TSharpVisionRuntime.Run`. It registers built-in streamable types, loads configuration and constructs the application, then runs its event loop and cleans up the application and driver on exit. It returns 0 on clean exit; exceptions propagate after cleanup. Run one application at a time per process.
