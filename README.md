# T# Vision

T# Vision is a text-mode UI framework for C# and .NET, with Turbo Vision-style windows, dialogs, menus, editors and keyboard-driven interaction. Applications use the core framework with a console, terminal or SDL graphical driver.

## Packages

| Package | Purpose |
|---|---|
| `TSharpVision` | Core framework; includes a headless NullDriver |
| `TSharpVision.Drivers.Console` | Native Windows Console driver |
| `TSharpVision.Drivers.Terminal` | ANSI/POSIX terminal driver |
| `TSharpVision.Drivers.SDL` | SDL3 graphical drivers |
| `TSharpVision.CodeEditor` | Syntax-aware code editor components using TextMate grammars |
| `TSharpVision.HexView` | Paged read-only hexadecimal view over a byte source |
| `TSharpVision.TableView` | Paged read-only table view over a row source |
| `TSharpVision.Terminal` | ConPTY and POSIX pseudo-terminal session implementations |

Install one driver package; it brings in `TSharpVision` transitively, including the public core APIs used below. A separate core reference is only needed for a core-only/headless application or when intentionally managing its version directly.

The first planned release is `0.1.0-preview.1`. These are preview packages; a local candidate is not a NuGet.org publication. Use an available prerelease from your configured feed; see [Getting Started](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/getting-started.md) for local-feed setup.

## Requirements

Use the **.NET 10 SDK** to build (`net10.0`) and a matching .NET 10 runtime to run framework-dependent applications. Console needs an attached Windows console; Terminal targets interactive Linux/macOS terminals; SDL needs a desktop display, native runtime assets and a usable font. See [Platform Support](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/platform-support.md) for the distinction between available packages and tested runtime behavior.

## Quick Start

In a Windows console, create a project and install the Console driver from your configured package feed:

```shell
dotnet new console -n HelloTSharpVision -f net10.0
cd HelloTSharpVision
dotnet add package TSharpVision.Drivers.Console --prerelease
```

Replace `Program.cs` with:

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

Run `dotnet run`. You should see a window with a greeting; press **Alt+X** to exit. Use an interactive console, not an IDE output pane or redirected input/output. For Linux/macOS or a graphical window, choose the package and driver shown in [Drivers](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/drivers.md).

## Drivers

Select a driver before starting the runtime. The examples choose an exact driver name to avoid depending on automatic selection. See [Drivers](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/drivers.md) for Console, Terminal, SDL and headless examples.

## License

TSharpVision's original project work uses the [MIT License](https://github.com/lubomirklimes/TSharpVision/blob/main/LICENSE).
