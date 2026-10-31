# T# Vision

T# Vision is a text-mode UI framework for .NET inspired by Borland Turbo Vision. Build keyboard-driven applications with windows, dialogs, menus and controls, using a Windows console, Linux/macOS terminal, or SDL desktop driver.

## Install

Add the core package:

```shell
dotnet add package TSharpVision
```

For an interactive application, add one driver package as well. Each driver brings in the core package:

- `TSharpVision.Drivers.Console` — Windows console
- `TSharpVision.Drivers.Terminal` — interactive ANSI terminal on Linux or macOS
- `TSharpVision.Drivers.SDL` — SDL desktop window on Windows, Linux or macOS

## Quick start

This minimal Windows Console application opens a window; **Alt+X** exits.

```shell
dotnet new console -n HelloTSharpVision -f net10.0
cd HelloTSharpVision
dotnet add package TSharpVision.Drivers.Console
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
        window.Insert(new TStaticText(new TRect(2, 2, 46, 4), "Hello from T# Vision! Alt+X exits."));
        DeskTop.Insert(window);
    }
}
```

Run `dotnet run` in an attached Windows console.

## Packages

| Package | Purpose |
|---|---|
| `TSharpVision` | Core UI framework; includes a headless driver |
| `TSharpVision.Drivers.Console` | Windows console driver |
| `TSharpVision.Drivers.Terminal` | Linux/macOS ANSI terminal driver |
| `TSharpVision.Drivers.SDL` | SDL desktop window drivers |
| `TSharpVision.CodeEditor` | Syntax-aware code editor components |
| `TSharpVision.HexView` | Read-only hexadecimal viewer |
| `TSharpVision.TableView` | Paged table viewer |
| `TSharpVision.Terminal` | Terminal widget, emulator and sessions |

## Documentation

- [Documentation](https://github.com/lubomirklimes/TSharpVision/tree/main/docs)
- [Getting started](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/getting-started.md)
- [Driver guide](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/drivers.md)
- [Samples](https://github.com/lubomirklimes/TSharpVision/tree/main/samples)
- [Source and issues](https://github.com/lubomirklimes/TSharpVision)
- [Releases](https://github.com/lubomirklimes/TSharpVision/releases)

## New in 1.1.0

Core includes `TComboBox` for editable or list-only selection, `TProgressBar` for determinate and activity feedback, and `TGroupBox` for titled, clipped containers. They use existing dialog/window palettes and are constructed in code; streaming is not supported. Worker progress reaches the UI through `TView.Post`. See [controls](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/dialogs-and-controls.md) for examples.
