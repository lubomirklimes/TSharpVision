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

### Fonts and font fitting

The `[graphics]` section selects the font for both SDL drivers. The font and size determine the cell size, and therefore the window size for a given grid:

```ini
[graphics]
fontName=JetBrainsMono-Regular.ttf
fontSize=18
fontFit=Condense
```

Some fonts draw glyphs that are larger than their cell. For example, Consolas draws the accents of Czech capitals above its ascent. `fontFit` chooses how such text is fitted into the cell:

| Value | Behaviour |
|---|---|
| `Condense` (default) | Text keeps the configured size. Only a glyph that overflows the cell is redrawn by FreeType, first condensing just the overflowing axis (at most 15 %), then reducing the point size if needed. |
| `Uniform` | All ordinary text is drawn at one common, proportionally smaller size: the largest whole point size at which the font's printable ASCII, Latin-1 letters and Latin Extended-A letters fit the cell. Plain and accented letters share the size and one baseline. A font whose text already fits keeps its configured size. |

Both modes keep the cell size, grid, window size, borders, box drawing, block elements and generated CP437 graphics. They also behave the same on `SDLDriver` and `SDLGpuDriver`, including the GPU CPU-composite fallback.

- The `Uniform` repertoire covers the whole Czech alphabet in both cases, so carons, acutes and the ring set the common size. It is not a guarantee for every Unicode code point.
- In `Uniform`, a symbol or a character outside that repertoire that still does not fit is fitted as in `Condense`, starting from the common size. This also applies to the rare compatibility letters Ĳ ĳ Ŀ ŀ ŉ ſ, which still render but never shrink the common size.
- If common-size measurement cannot fit a font down to half its configured size, or an auxiliary font cannot be opened, even repertoire characters can take the individual fitting path and ultimately clip. Supported fonts require common-size validation.
- Values are matched case-insensitively and surrounding whitespace is ignored. A missing or empty value means "not set", which is `Condense`.
- Any other value makes loading the .cfg file fail with an `InvalidOperationException` naming `[graphics] fontFit`, the value, the file and the supported values. It never falls back silently.
- `<AssemblyName>.cfg` overrides `~/.tsvision.cfg` per key: an application file that does not set `fontFit` keeps the user's value.
- The setting is read when the driver initializes; changing it requires a restart.
- Only the SDL drivers render differently per mode. The Console and Terminal drivers ignore it.

#### Typed configuration

`fontFit` is the typed `GraphicsDriverConfiguration.FontFit` property (enum `TSharpVision.Config.FontFitMode`), next to `FontName` and `FontSize`. Both SDL drivers read that property; the .cfg file and code produce the same value.

```csharp
using TSharpVision;
using TSharpVision.Config;
using TSharpVision.Drivers;

// Read the configured mode (after TSharpVisionRuntime.Configure or Run has loaded the .cfg files).
GraphicsDriverConfiguration graphics = ScreenDriverFactory.Configuration!.Graphics;
FontFitMode mode = graphics.FontFit;   // Condense unless configured

// Select a mode in code, without a .cfg file, before the driver is created.
ScreenDriverFactory.Configuration = new TSharpVisionConfiguration
{
    Graphics = new GraphicsDriverConfiguration
    {
        FontName = "consola.ttf",
        FontSize = 20,
        FontFit  = FontFitMode.Uniform,   // or FontFitMode.Condense
    },
};
```

The configuration objects are immutable (`init`). To keep the loaded .cfg values and override only the mode, call `TSharpVisionRuntime.Configure()` and assign a new configuration built from the result before creating the application:

```csharp
TSharpVisionConfiguration loaded = TSharpVisionRuntime.Configure();
ScreenDriverFactory.Configuration = new TSharpVisionConfiguration
{
    Driver       = loaded.Driver,
    Localization = loaded.Localization,
    RawSections  = loaded.RawSections,
    Graphics     = new GraphicsDriverConfiguration
    {
        FontName = loaded.Graphics.FontName,
        FontSize = loaded.Graphics.FontSize,
        FontFit  = FontFitMode.Uniform,
    },
};
```

A configuration assigned in code is used as-is; it replaces the loaded one and is not merged with the .cfg files. `TSharpVisionRuntime.Run` loads the .cfg files itself, so an application that sets the configuration in code calls `StreamableRegistration.RegisterAll()` and `Configure`, then runs its application directly with `AppLifecycleGuard.Run`.

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

## Idle wait and application deadlines

`TProgram.GetEvent` remains a nonblocking probe when called outside a framework event loop.
Inside `TGroup.Execute` and menu loops it performs one `Idle()` pass after empty input,
then waits when no queued event, targeted post, pending event, modal termination or rendering
work remains. `PumpMessages` is nonblocking for every built-in driver. Posts and input are
delivered on the UI thread after waking, including during nested modal execution.

`IDriver` is unchanged. Drivers may additionally implement `IIdleWaitDriver.WaitForEvent(int,
WaitHandle)`. The timeout is a maximum in milliseconds (`-1` means no application deadline).
The supplied framework signal is borrowed: never reset, dispose or retain it after returning.
Wait on native input and that signal together, shorten the wait for driver deadlines, and return
immediately for native or rendering work already pending. Throw on native wait failure; interrupt
an active wait before releasing driver resources during shutdown. Framework queue details remain
internal. Legacy drivers use a wakeable framework wait capped at 100 ms before probing native input.

An application that needs periodic Idle work overrides `protected virtual int GetIdleWaitTimeout()`.
Return the remaining time until the earliest required work, computed from a monotonic clock;
perform expired work in `Idle`, then advance its deadline. Return `-1` when no deadline exists.
If a background producer adds an earlier deadline, queue an ordinary framework event or use
`TView.Post` so the UI can recompute it promptly. Existing Idle overrides still run on each empty
pass, but continuous Idle invocation is no longer promised. `DoNotReleaseCPU != 0` deliberately
bypasses waiting and retains continuous execution.

Win32 Console waits on the borrowed console input handle and framework signal together. ANSI
Terminal polls stdin plus a managed-wake pipe, using the earlier of the application deadline,
the decoder's remaining 50 ms standalone Esc deadline, and a 100 ms resize-probe deadline.
SDL renderer and GPU bridge managed wakes into registered SDL user events. Their native wait
has a 1000 ms safety deadline to detect a failed/filtered user-event wake; normal posts and
native input interrupt it immediately. The safety deadline is a failure-detection bound, not
an input-latency bound. Dirty rendering bypasses waiting.

Use queued `cmQuit` for cross-thread termination (the lifecycle Ctrl+C handler already does this).
While a modal is active, queued quit is routed to the application's virtual handler on the UI thread;
once accepted, nested groups and menus unwind through `cmCancel` before root quit validation.
Driver disposal interrupts native waiting and propagates through normal lifecycle cleanup.
View-tree mutation and application teardown otherwise retain the framework's UI-thread ownership rules.

The nonshipping `diagnostics/TSharpVision.Diagnostics.IdleWait` project measures 15 seconds of
idle process CPU and loop passes. Run its executable with `console` or `sdl` and an output JSON
path. Add `validate` as the third argument for native key/mouse/resize, queue/Post and quit latency.
The console diagnostic allocates and hides its own console. SDL measurement uses an actual window.
