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
| `TSharpVision.Graphics` | Optional pixel graphics views and image preview |

## Optional pixel graphics

`TSharpVision.Graphics` is optional and depends only on the core package. `GWindow` hosts a `GView` display list of lines, shapes and images; `GImage` shows a `PixelImage` fitted or stretched, with nearest or linear sampling. It accepts decoded RGBA8888 pixels, not PNG, JPEG or WebP files: the application decodes them.

- `TSharpVision.Graphics` alone, or on the Console, Terminal and Null drivers: the views show a text fallback message instead of pixels.
- `TSharpVision.Graphics` + `TSharpVision.Drivers.SDL`: `SDLGpuDriver` and `SDLDriver` draw the pixels. The application references the SDL package itself; neither package depends on the other.

See [showing an image in a window](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/graphics/image-preview.md).

## Command-line tools

The resource compiler, help compiler and resource inspector are .NET tool packages:

| Package | Command | Purpose |
|---|---|---|
| `TSharpVision.ResourceCompiler` | `svrc` | Compiles `.trc` resource scripts into `.tvr` files |
| `TSharpVision.HelpCompiler` | `svhc` | Compiles help source text into `.hlp` files |
| `TSharpVision.ResourceTools` | `svres` | Lists, shows, dumps and validates `.tvr` resources |

```shell
dotnet tool install --global TSharpVision.ResourceCompiler
svrc compile app_en.trc app_en.tvr
```

See [command-line tools](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/tools.md).

## Documentation

- [Documentation](https://github.com/lubomirklimes/TSharpVision/tree/main/docs)
- [Getting started](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/getting-started.md)
- [Driver guide](https://github.com/lubomirklimes/TSharpVision/blob/main/docs/drivers.md)
- [Samples](https://github.com/lubomirklimes/TSharpVision/tree/main/samples)
- [Source and issues](https://github.com/lubomirklimes/TSharpVision)
- [Releases](https://github.com/lubomirklimes/TSharpVision/releases)

## New in 1.2.0

- `TProgram.SetAppPalette` switches a running program between the Color, BlackWhite and Monochrome palette families.
- `SDLGpuDriver` adds physical palettes (Default, Green, Amber, Grayscale) and one display effect at a time (Scanlines, Glow, Curvature, ShadowMask), set with `[graphics] palette=` / `effect=` or at run time. Palettes recolour text colours only, never pixel graphics; other drivers are unaffected.
- The new optional `TSharpVision.Graphics` package adds pixel graphics views, shapes and RGBA images (above).
- Built-in framework translations (cs, de, es, fr, it, pl, sk beside English) with `[localization] language=` and runtime switching via `TSharpVisionIntl.UseBuiltInLanguage`.
- Help search across several help files (`THelpCollection`), and file dialogs that start in the directory of the last file accepted in an Open dialog, for the rest of the session.
- The command-line tools `svrc`, `svhc` and `svres` are available as .NET tool packages (above).

All TSharpVision packages, the nine libraries and the three tools, share one version. See the [changelog](https://github.com/lubomirklimes/TSharpVision/blob/main/CHANGELOG.md).
