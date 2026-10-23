# Environment variables

TSharpVision reads a small number of environment variables. One selects the screen driver; the rest switch on diagnostics or tune the SDL drivers, and none is needed for normal use. Everything a variable controls that has a configuration-file equivalent is better set there; the variables exist for one-off runs, CI and investigation.

All TSharpVision variables are spelled in uppercase with the `TSHARPVISION_` prefix. Environment variable names are case-sensitive on Linux and macOS and case-insensitive on Windows, so use the exact spelling shown here.

Unless a section says otherwise, a variable is read when the component that uses it starts, so set it before the process starts or, in code, before `TSharpVisionRuntime.Run`. Changing it later has no effect on a running driver. A value of `1` is the only spelling that switches on the plain on/off variables; `true`, `yes` and `on` do not, except where a section lists them.

## Summary

| Variable | Package | Values | Default | Purpose | Kind |
|---|---|---|---|---|---|
| [`TSHARPVISION_DRIVER`](#tsharpvision_driver) | `TSharpVision` | `sdl`, `console`, `terminal`, `SDLGpuDriver`, `SDLDriver`, `Win32ConsoleDriver`, `AnsiTerminalDriver`, `NullDriver` | `[driver] name`, else the highest-priority installed driver (GPU SDL preferred) | Select the screen driver | Public runtime configuration |
| [`TSHARPVISION_TRACE_INPUT`](#tsharpvision_trace_input) | `TSharpVision` | `1` | off | Write an input-routing trace file | Public diagnostic |
| [`TSHARPVISION_SDL_VSYNC`](#tsharpvision_sdl_vsync) | `TSharpVision.Drivers.SDL` | `0`/`off`/`false` \| `1`/`on`/`true` \| `adaptive` | SDL's default | Vsync for `SDLDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDL_DIAGNOSTICS`](#tsharpvision_sdl_diagnostics) | `TSharpVision.Drivers.SDL` | `1` \| `verbose` | off | Performance summaries for `SDLDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDLGPU_DIAGNOSTICS`](#tsharpvision_sdlgpu_diagnostics) | `TSharpVision.Drivers.SDL` | `1` \| `verbose` | `[sdl] diagnostics`, else off | Performance summaries for `SDLGpuDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDLGPU_BACKEND`](#tsharpvision_sdlgpu_backend) | `TSharpVision.Drivers.SDL` | `metal` \| `vulkan` \| `direct3d12`/`d3d12` \| an SDL backend name | `[sdl] backend`, else SDL chooses | GPU backend for `SDLGpuDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDLGPU_PRESENT_MODE`](#tsharpvision_sdlgpu_present_mode) | `TSharpVision.Drivers.SDL` | `immediate` \| `vsync` \| `mailbox` | `[sdl] presentMode`, else `immediate` | Swapchain present mode for `SDLGpuDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDLGPU_ALLOWED_FRAMES`](#tsharpvision_sdlgpu_allowed_frames) | `TSharpVision.Drivers.SDL` | `1` \| `2` \| `3` | `[sdl] allowedFrames`, else SDL's default | Frames in flight for `SDLGpuDriver` | Advanced diagnostic |
| [`TSHARPVISION_SDLGPU_CONTINUOUS`](#tsharpvision_sdlgpu_continuous) | `TSharpVision.Drivers.SDL` | `1`/`on`/`true` \| `0`/`off`/`false` | on demand | Accepted, currently no effect | Advanced diagnostic |
| [`TSHARPVISION_NO_SDL`](#tsharpvision_no_sdl) | `TSharpVision.Drivers.SDL` | `1` | SDL initializes | Keep SDL drivers from initializing | Internal / test |

System variables that TSharpVision observes but does not define, sample-only variables, and the variables used by the repository's build and release tooling are listed after the runtime variables.

## Driver selection

### `TSHARPVISION_DRIVER`

- **Used by:** `TSharpVision` (`ScreenDriverFactory`), for every installed driver package.
- **Purpose:** chooses which screen driver the application creates.
- **Accepted values:** a portable alias or an exact driver name, case-insensitive, with surrounding whitespace ignored. The same names, with the same meaning, are accepted by `[driver] name` in the configuration file.
- **Default:** when unset, empty or whitespace, the `[driver] name` configuration value is used; when that is also absent, the highest-priority driver installed for the current OS, which prefers the GPU SDL driver.
- **Platforms:** all. A name only matches a driver registered for the current OS.
- **Scope:** runtime. Affects package consumers.
- **Notes:** read each time the application creates its driver (when the first `TApplication` is constructed), so setting it in code before `TSharpVisionRuntime.Run` works.

#### Portable aliases

Aliases mean the same thing on every OS, so one configuration file works unchanged on Windows, Linux and macOS.

| Alias | Windows | Linux and macOS |
|---|---|---|
| `sdl` | `SDLGpuDriver` | `SDLGpuDriver` |
| `console` | `Win32ConsoleDriver` | `AnsiTerminalDriver` |
| `terminal` | `Win32ConsoleDriver` | `AnsiTerminalDriver` |

`console` and `terminal` are the same choice: the platform's native text-mode driver. On Windows they select `Win32ConsoleDriver` even though `AnsiTerminalDriver` is also registered there.

#### Exact driver names

An exact name selects that one implementation and is never reinterpreted for another OS.

| Name | Windows | Linux and macOS |
|---|---|---|
| `SDLGpuDriver` | `SDLGpuDriver` | `SDLGpuDriver` |
| `SDLDriver` | `SDLDriver` (SDL_Renderer compatibility path) | `SDLDriver` |
| `Win32ConsoleDriver` | `Win32ConsoleDriver` | not registered: invalid name |
| `AnsiTerminalDriver` | `AnsiTerminalDriver` (registered for decoder testing) | `AnsiTerminalDriver` |
| `NullDriver` | `NullDriver` | `NullDriver` |

#### Selection order

1. `TSHARPVISION_DRIVER`, if it has a non-blank value.
2. `[driver] name` from the configuration files. The application's `<AssemblyName>.cfg` overrides the user's `~/.tsvision.cfg`.
3. The highest-priority driver installed for the OS:

| Priority | Windows | Linux and macOS |
|---|---|---|
| 300 | `SDLGpuDriver` | `SDLGpuDriver` |
| 200 | `SDLDriver` | `SDLDriver` |
| 50 | `Win32ConsoleDriver` | `AnsiTerminalDriver` |
| 10 | `AnsiTerminalDriver` | |
| 0 | `NullDriver` | `NullDriver` |

Steps 1 and 2 go through the same resolver, so a name means the same thing in either place.

A name that matches no installed driver for the OS writes `Warning: no driver named '<name>' found for platform <OS>; using default.` to standard error and continues with step 3; for an alias the warning also names the driver it stands for, for example `'console' (Win32ConsoleDriver)`. It does not fall back to the configuration file. Selection happens before initialization: if the selected driver then fails to start, the application fails and no other driver is tried.

## Diagnostics

### `TSHARPVISION_TRACE_INPUT`

- **Used by:** `TSharpVision` (`InputTrace`). The core application loop, `TScreen`, `Win32ConsoleDriver` and the SDL mouse translator write to it.
- **Purpose:** writes a line for each stage an input event passes through, from the driver to the view that handles it, for diagnosing keys or clicks that do not arrive.
- **Accepted values:** `1` enables tracing. Every other value, including `true`, disables it.
- **Default:** off. When off, tracing costs nothing beyond a flag check.
- **Output:** `tsharpvision-input-trace.log`, in lowercase, in the process's current working directory. The file is replaced at each start and flushed after every line. If it cannot be created, tracing produces nothing and the application continues.
- **Platforms:** all. Which stages appear depends on the driver.
- **Scope:** public diagnostic. Affects package consumers.
- **Legacy spelling:** `TSharpVision_TRACE_INPUT` is still accepted on Linux and macOS when `TSHARPVISION_TRACE_INPUT` is unset. If the uppercase name is set to anything, including an empty value, the legacy name is ignored. On Windows the two spellings are the same variable.
- **Notes:** read once, the first time any trace code runs; set it before the process starts. The trace contains the text of key events, so treat a trace of typed passwords or other secrets as sensitive.

### `TSHARPVISION_SDL_DIAGNOSTICS`

- **Used by:** `TSharpVision.Drivers.SDL`, `SDLDriver` only.
- **Purpose:** periodic performance summaries of the SDL renderer (frame and present timings, redraw reasons, idle waits, vsync state) and window and display details at startup.
- **Accepted values:** `1` reports every 60 seconds; `verbose` (any case) reports every 5 seconds. Anything else, including `true`, leaves it off. Whitespace is not trimmed.
- **Default:** off.
- **Output:** standard error. Under a terminal the UI may hide it; redirect standard error to a file.
- **Platforms:** Windows, Linux, macOS.
- **Scope:** advanced diagnostic.
- **Notes:** read once per process. The `[sdl] diagnostics` configuration key does not apply to `SDLDriver`.

### `TSHARPVISION_SDLGPU_DIAGNOSTICS`

- **Used by:** `TSharpVision.Drivers.SDL`, `SDLGpuDriver` only.
- **Purpose:** periodic performance summaries of the GPU renderer, with emphasis on swapchain acquire latency, plus backend and present-mode details at startup.
- **Accepted values:** `1` reports every 60 seconds; `verbose` (any case) reports every 5 seconds. Anything else leaves it off.
- **Default:** the `[sdl] diagnostics` configuration value, otherwise off.
- **Output:** summaries go to `sdlgpu_diag.log` in the system temporary directory, replaced at each start; standard error names the file. If the file cannot be opened, summaries go to standard error instead. A few startup lines always go to standard error.
- **Platforms:** Windows, Linux, macOS.
- **Scope:** advanced diagnostic.

## SDL GPU driver options

These variables tune `SDLGpuDriver` and have no effect on `SDLDriver`. Each one overrides a key in the `[sdl]` section of the configuration file:

| Variable | `[sdl]` key |
|---|---|
| `TSHARPVISION_SDLGPU_DIAGNOSTICS` | `diagnostics` |
| `TSHARPVISION_SDLGPU_BACKEND` | `backend` |
| `TSHARPVISION_SDLGPU_PRESENT_MODE` | `presentMode` |
| `TSHARPVISION_SDLGPU_ALLOWED_FRAMES` | `allowedFrames` |
| `TSHARPVISION_SDLGPU_CONTINUOUS` | `continuous` |

They are read once, when the driver initializes. A variable that is set wins over the configuration key even when its value is empty, which is possible on Linux and macOS; an empty value then means the built-in default. Unset the variable to use the configuration value.

### `TSHARPVISION_SDLGPU_BACKEND`

- **Purpose:** asks SDL for a specific GPU API.
- **Accepted values:** `metal`, `vulkan`, `direct3d12` or `d3d12`, case-insensitive, with surrounding whitespace ignored. Other names are passed to SDL unchanged.
- **Default:** SDL chooses the backend for the platform.
- **Notes:** a backend the system does not provide makes driver initialization fail with `SDL_CreateGPUDevice failed`; there is no fallback to automatic selection.

### `TSHARPVISION_SDLGPU_PRESENT_MODE`

- **Purpose:** how frames are presented to the window.
- **Accepted values:** `vsync`, `immediate` or `mailbox`, case-insensitive, with surrounding whitespace ignored.
- **Default:** `immediate`. An unrecognised value also means `immediate`, without a message.
- **Notes:** if the window does not support the mode, standard error says so and SDL's default presentation is kept.

### `TSHARPVISION_SDLGPU_ALLOWED_FRAMES`

- **Purpose:** how many frames the GPU may have in flight.
- **Accepted values:** `1`, `2` or `3`.
- **Default:** SDL's default. Any other value, including `0`, `4` or a non-number, is ignored without a message.

### `TSHARPVISION_SDLGPU_CONTINUOUS`

- **Accepted values:** `1`, `on`, `true`, `0`, `off`, `false`, case-insensitive.
- **Notes:** accepted for compatibility but currently has no effect: the driver always renders on demand. With diagnostics on, the startup line reports the parsed value as `mode=`.

## SDL renderer option

### `TSHARPVISION_SDL_VSYNC`

- **Used by:** `TSharpVision.Drivers.SDL`, `SDLDriver` only.
- **Purpose:** overrides the SDL renderer's vsync setting.
- **Accepted values:** `0`, `off` or `false` disables vsync; `1`, `on` or `true` enables it; `adaptive` requests adaptive vsync. Case-insensitive, with surrounding whitespace ignored.
- **Default:** when unset or blank, SDL's renderer default is kept.
- **Platforms:** Windows, Linux, macOS. Adaptive vsync depends on the renderer backend; with `TSHARPVISION_SDL_DIAGNOSTICS` on, standard error shows whether SDL accepted the request.
- **Scope:** advanced diagnostic.
- **Notes:** any other value writes a warning to standard error and is ignored. Read when the window is created.

## Internal and test switches

### `TSHARPVISION_NO_SDL`

- **Used by:** `TSharpVision.Drivers.SDL`, both `SDLDriver` and `SDLGpuDriver`.
- **Purpose:** headless guard for tests and smoke runs. With `1`, driver initialization skips SDL entirely, writes `[SDL] Initialization explicitly disabled by TSHARPVISION_NO_SDL=1; driver remains detached.` to standard error, and the driver stays detached with no window.
- **Accepted values:** `1`. Every other value leaves SDL enabled.
- **Default:** SDL initializes normally.
- **Scope:** internal / test. An application started this way has no display; use `TSHARPVISION_DRIVER=NullDriver` for a headless application instead.
- **Legacy spelling:** `TSharpVision_NO_SDL`, the name the drivers read before 1.0, is still accepted when the uppercase name is unset. The repository's SDL tests use it.
- **Notes:** read on every initialization attempt.

### Keyboard diagnostic session variables

`TSHARPVISION_KBDIAG_PIPE` and `TSHARPVISION_KBDIAG_TOKEN` are set by the keyboard diagnostic tool (`TSharpVision.Diagnostics.Keyboard`, not packaged) for the target process it starts, together with `TSHARPVISION_DRIVER`. They name the session pipe and its token. Do not set them yourself; running the target without them prints a hint to use `run` instead.

## System variables TSharpVision observes

TSharpVision does not define these variables, but its behaviour depends on them.

| Variable | Used by | Effect |
|---|---|---|
| `PATH` | `TSharpVision.Drivers.Terminal` | Searched for the clipboard tools: `pbcopy`/`pbpaste` on macOS; `wl-copy`/`wl-paste`, then `xclip`, then `xsel` on Linux. Searched at each clipboard operation; without one, the system clipboard is reported as unavailable. |
| `WINDIR` | `TSharpVision.Drivers.SDL` (Windows) | Locates the Windows `Fonts` folder when probing for a monospace font; `C:\Windows` when unset. |
| `HOME` (Linux, macOS) | `TSharpVision`, `TSharpVision.Drivers.SDL` | Through .NET's user-profile folder: the location of `~/.tsvision.cfg` and of per-user font folders. |
| `TMPDIR` (Linux, macOS), `TMP`/`TEMP` (Windows) | `TSharpVision.Drivers.SDL` | Through .NET's temporary path: the folder for `sdlgpu_diag.log`. |
| `SDL_*` | SDL3 itself | SDL reads its own hints from the environment, for example `SDL_VIDEO_DRIVER`. TSharpVision does not interpret them. The drivers set the `SDL_QUIT_ON_LAST_WINDOW_CLOSE` hint to `0`; an environment value for that hint takes precedence, as SDL defines. |

The terminal driver does not read `TERM`, `COLORTERM` or `TERM_PROGRAM`. It negotiates keyboard capabilities, including the Kitty keyboard protocol, by querying the terminal, so a stale or spoofed `TERM` cannot switch input modes on.

### Child sessions in `TSharpVision.Terminal`

A child process started by `PosixPtyTerminalSession` receives this process's environment with the overrides from `PosixPtyTerminalSessionOptions.Environment` applied; a `null` value removes a variable. The session does not set `TERM` on its own. Set it to `TTerminal.TermName` (`xterm-256color`), as the samples do, so programs in the child see what the emulator implements rather than the host terminal. A `ConPtyTerminalSession` child on Windows inherits this process's environment unchanged.

## Sample applications

These are read by the samples in this repository, not by the packages.

| Variable | Sample | Effect |
|---|---|---|
| `SHELL` | `TSharpVision.Samples.TVDemo`, `TSharpVision.Samples.TVTerm` | Shell started in the terminal window on Linux and macOS; `/bin/sh` when unset. In TVTerm a configured default shell takes precedence. |
| `ComSpec` | `TSharpVision.Samples.TVDemo` | Shell started in the terminal demo on Windows; `cmd.exe` when unset. TVTerm always uses `cmd.exe` on Windows unless configured. |
| `TERM_PROGRAM` | `TSharpVision.Samples.TVDemo` | Shown as the host in the function-key diagnostics dialog for text drivers; display only. |

Both samples start child shells with `TERM=xterm-256color`; TVTerm also removes `COLORTERM` from the child's environment.

## Build, release and CI

These affect only building and releasing this repository, never an application that uses the packages.

| Variable | Where | Effect |
|---|---|---|
| `CI`, `GITHUB_ACTIONS`, `TF_BUILD` | `Directory.Build.props` | When any equals `true`, the build sets `ContinuousIntegrationBuild` for deterministic, source-mapped packages. |
| `GITHUB_OUTPUT`, `GITHUB_STEP_SUMMARY` | `scripts/prepare-github-release.ps1`, `scripts/pack-local-packages.ps1` | Written when present, to pass the version and commit between workflow steps and to add a job summary. |
| `GH_REPO`, `GH_TOKEN` | `scripts/complete-github-release.ps1` | Required with `-Publish`; the script stops if either is missing. |
| `NUGET_USER` | `.github/workflows/publish-nuget.yml` | The NuGet profile name, from the `release` environment's variables; the workflow stops if it is empty. |
| `RELEASE_TAG`, `RELEASE_VERSION`, `RELEASE_RID`, `RELEASE_COMMIT`, `EXPECTED_COMMIT`, `NUGET_TEMP_KEY` | `.github/workflows/*.yml` | Step-local values passed to the scripts as parameters; not read by the scripts themselves. |
| `TERM` | `.github/workflows/release.yml` | The release workflow runs the test suite with `TERM=xterm-256color`. |

## Test-only variables

The test projects set and restore these themselves; contributors do not need to set them.

| Variable | Test project | Use |
|---|---|---|
| `TSharpVision_NO_SDL` | `TSharpVision.Drivers.SDL.Tests` | Keeps SDL from initializing in driver tests. |
| `TSHARPVISION_DRIVER` | `TSharpVision.Tests`, `TSharpVision.Drivers.SDL.Tests` | Checks driver selection. |
| `COMSPEC` | `TSharpVision.Terminal.Tests` | Locates `cmd.exe` for ConPTY tests on Windows. |
| `TERM`, `COLORTERM`, `LANG`, `LC_ALL` | `TSharpVision.Terminal.Tests` | Set or removed for child processes in the real-PTY emulator tests. |
| `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `MC_SKIN` | `TSharpVision.Terminal.Tests` | Point the Midnight Commander probe at an empty temporary configuration with the default skin, so a developer's own `~/.config/mc` cannot change its result. |
| `NUGET_PACKAGES` | `tests/native/ShadercrossDeploymentSmoke.ps1` | Temporarily redirected to check native-library loading without the NuGet cache; restored afterwards. |
