# Platform support

All eight packages target `net10.0`: core, the Console/SDL/Terminal drivers, and CodeEditor, HexView, TableView and Terminal extensions. Build with the .NET 10 SDK. Framework-dependent applications need the matching runtime and architecture.

## Intended support and evidence

The Console driver requires Windows console handles. The ANSI Terminal driver targets interactive Linux/macOS terminals; its Windows initialization does not supply an interactive backend. SDL targets desktop Windows, Linux and macOS. Core's `NullDriver` supports headless use. Native child sessions in the Terminal extension have their own ConPTY/POSIX availability checks.

Historical Windows x64 package-only restore/build evidence covered core and the three driver packages, not the current eight-package set. It also covered headless lifecycle execution and Console key translation. SDL native loading and shadercross compilation ran in build/publish layouts; that was not a clean-machine package-only graphical test.

This evidence does not establish interactive rendering, input, resize and shutdown across hosts. Interactive Console and Linux ANSI sessions need matching-host validation. For 1.0.0, macOS on Apple Silicon was exercised interactively: TVDemo with `SDLDriver` and `SDLGpuDriver` on Retina and external HiDPI displays (rendering, mouse, wheel and trackpad, keyboard including Command) and with `AnsiTerminalDriver` in Apple Terminal. No Windows arm64, macOS Intel or Linux runtime validation is claimed here. Managed build/unit-test results and native-asset availability should be recorded separately from manually exercised UI behavior.

## SDL deployment

SDL dependencies supply assets for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` and `linux-arm64`. SDL3-CS's OS, TTF and Shadercross packages supply native binaries; the driver does not vendor them.

Publish for the application's actual OS and architecture, for example:

```shell
dotnet publish -c Release -r win-x64 --self-contained false
```

Distribute the complete publish directory, preserving subdirectories. A successful build on one OS does not validate another target. Run on a matching host with the required system libraries, display, graphics support and monospace fonts. Do not mix native libraries from different upstream versions or copy them from a developer's global NuGet cache.

Linux system-library baselines, display servers, graphics backends and terminal emulators have not been certified. Mobile/browser targets, NativeAOT, trimming and single-file publishing are not claimed.
