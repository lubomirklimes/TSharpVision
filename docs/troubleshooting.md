# Troubleshooting

## Application starts but no UI appears

Check that you installed a UI driver package and selected its exact name before calling `TSharpVisionRuntime.Run`: `Win32ConsoleDriver`, `AnsiTerminalDriver` or `SDLDriver`. Core alone includes NullDriver, which intentionally produces no UI. An unknown driver name prints a warning and uses the default instead; read stderr.

Run Console/Terminal applications in an interactive terminal, not an IDE output panel, service, pipe or redirected session. Console can remain unattached with redirected handles; Terminal can remain unattached without a usable TTY. Installing the Terminal package on Windows does not make it a working interactive Windows driver. Use Console there.

If the application does display but will not quit, use **Alt+X** in the minimal examples. Closing a child window does not quit the application. A NullDriver application needs a programmed exit path because it receives no real user input.

## SDL initialization fails

Capture the full exception and inner exception chain from runtime startup. Errors can identify `SDL_Init`, window creation, font loading, graphics/shader setup or a missing native library. Reflection-based construction can wrap the useful cause, so the outer exception alone may not be enough.

Check the chosen driver name, an active desktop/display session, graphics support and a usable monospace font. The renderer probes common system fonts; a minimal OS image may have none of them. Install an appropriate system monospace font (for example DejaVu Sans Mono on Linux) or use an available configured font. `SDLGpuDriver` and `SDLDriver` are separate selections; a failure does not guarantee an automatic fallback. You can explicitly try `SDLDriver` to distinguish the renderer path from the GPU path.

Check restore and publish output if the error names a native library.

## Terminal rendering/input issues

Use an interactive UTF-8 Linux/macOS terminal with xterm-style ANSI support. Input/output redirection, limited terminal emulators, SSH/multiplexer settings and terminal shortcuts can change escape sequences, mouse reporting and key delivery. Confirm the emulator sends the requested key rather than intercepting it. Compare behavior outside the multiplexer or remote session before attributing it to the application.

Use a font with box-drawing characters and sufficient glyph coverage. Record the OS, architecture, emulator/version, locale, `TERM` value, selected driver and exact key sequence when reporting a problem.

## Wrong target framework

The packages target **net10.0**. Check `dotnet --info` and your project's `TargetFramework`. Create a new project with `dotnet new console -f net10.0`, or retarget an appropriate existing project. Installing an older runtime does not make a net10.0 package compatible with an older target. A framework-dependent deployment needs the matching .NET 10 runtime.

## NuGet package restores but a runtime native library is missing

1. Check the OS and process architecture against the [supported asset RIDs](platform-support.md). Set the correct `RuntimeIdentifier` or pass `-r` to publish.
2. From your application directory, inspect dependencies with `dotnet list package --include-transitive`. An SDL application should resolve SDL3-CS and the OS, TTF and Shadercross packages. A driver-only reference still restores core.
3. Run `dotnet restore`, then publish for the intended RID, for example `dotnet publish -c Release -r win-x64 --self-contained false`. Do not use a stale output from another architecture.
4. Check the publish output and its `.deps.json`, then deploy the complete directory. Native assets may be under runtime subdirectories in some build layouts; do not flatten or selectively copy the output.
5. If the named library exists, the load error may concern one of its native dependencies or an incompatible system/architecture. Keep the inner exception, OS/RID and library name for diagnosis on the target host.

Use NuGet restore/publish to supply matching files. Avoid absolute paths into a developer's global package cache; a deployment must work without that cache. Package asset availability alone does not certify system libraries or graphics operation on a clean machine.

## Package version cannot be found

Preview candidates are not necessarily published on NuGet.org. Confirm the version exists on your configured feed and include `--prerelease` when selecting a preview. If using local nupkg files, register their directory and keep nuget.org available for SDL3-CS dependencies.
