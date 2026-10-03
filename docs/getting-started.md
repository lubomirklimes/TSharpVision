# Getting started

Install the .NET 10 SDK and create an application:

```shell
dotnet new console -n HelloTSharpVision -f net10.0
cd HelloTSharpVision
dotnet add package TSharpVision.Drivers.Console --prerelease
```

Use `TSharpVision.Drivers.Console` for Windows, `TSharpVision.Drivers.Terminal` for Linux/macOS terminals, or `TSharpVision.Drivers.SDL` for a desktop window. Each driver depends on core. Core alone supplies the headless `NullDriver`.

For local nupkg files, register their directory and keep nuget.org enabled for upstream dependencies:

```shell
dotnet nuget add source C:\packages\tsharpvision --name TSharpVisionLocal
```

Use an absolute directory on Linux/macOS too. Select an available preview with `--prerelease`, or use `--version` with the exact version in your feed.

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

Run `dotnet run` in an attached console. Alt+X exits; closing the greeting window leaves the application running. For an ANSI terminal select `AnsiTerminalDriver`; for SDL select `SDLDriver`.

Bounds use character cells relative to the owner. `DeskTop.Insert` attaches the window to the desktop.

Set driver selection before `TSharpVisionRuntime.Run`. It registers built-in streamable types, loads configuration and constructs the application, then runs its event loop and cleans up the application and driver on exit. It returns 0 on clean exit; exceptions propagate after cleanup. Run one application at a time per process.
