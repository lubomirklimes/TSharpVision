# Platform support

All nine library packages target `net10.0`: core, the Console/SDL/Terminal drivers, and CodeEditor, Graphics, HexView, TableView and Terminal extensions. `TSharpVision.Graphics` is new in 1.2.0; the other eight were also released as 1.1.0. The three command-line tool packages (`svrc`, `svhc`, `svres`), new in 1.2.0, are framework-dependent .NET tools for `net10.0` and run wherever the .NET 10 runtime does. Build with the .NET 10 SDK. Framework-dependent applications need the matching runtime and architecture.

## Intended support

The Console driver requires Windows console handles. The ANSI Terminal driver targets interactive Linux/macOS terminals; its Windows initialization does not supply an interactive backend. SDL targets desktop Windows, Linux and macOS. Core's `NullDriver` supports headless use. Native child sessions in the Terminal extension have their own ConPTY/POSIX availability checks.

## SDL deployment

SDL dependencies supply assets for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` and `linux-arm64`. SDL3-CS's OS, TTF and Shadercross packages supply native binaries; the driver does not vendor them.

Publish for the application's actual OS and architecture, for example:

```shell
dotnet publish -c Release -r win-x64 --self-contained false
```

Distribute the complete publish directory, preserving subdirectories. A successful build on one OS does not validate another target. Run on a matching host with the required system libraries, display, graphics support and monospace fonts. Do not mix native libraries from different upstream versions or copy them from a developer's global NuGet cache.

Linux system-library baselines, display servers, graphics backends and terminal emulators have not been certified. Mobile/browser targets, NativeAOT, trimming and single-file publishing are not claimed.
