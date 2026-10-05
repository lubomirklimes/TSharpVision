# Diagnostics

The `diagnostics/` directory holds two developer tools for working on the drivers. They are built with the solution but are not packaged, and applications never need them.

Run the commands below from the repository root.

## SDL glyphs

Both SDL back-ends draw the CP437 box, block and shading characters (`B0`–`DF`) themselves instead of taking them from the font, so that lines join and shades tile at any cell size. `TSharpVision.Diagnostics.SdlGlyphs` renders those generated glyphs to files and runs the geometry checks on them. Use it after changing glyph generation or cell metrics, or when a particular font shows gaps in frames, uneven shading or misplaced scrollbar parts.

The tool is headless. It opens no window, renderer or GPU device; SDL_ttf is initialised only to measure the fonts and to rasterise the comparison glyphs that still come from the font. The generated pixels come from the production generator in `TSharpVision.Drivers.SDL`, so the output is what both `SDLDriver` and `SDLGpuDriver` would draw. It needs the SDL3 and SDL_ttf native libraries that the SDL driver already brings for the current platform, and each font must exist on the machine.

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.SdlGlyphs -- --out artifacts/sdl-glyphs "Cascadia Mono" Consolas
```

Give one to five fonts, as file paths or as names resolved by the driver's own font locator. Pass `--out`: the default output directory is `./diagnostics` under the current directory, which from the repository root is the source folder.

Each font gets a numbered directory. By default it contains two runs at automatically selected point sizes, `1px` and `2px`, named after the single-stroke thickness the generated geometry has at that size. `--pt <n>` pins one size and produces a single `pt<n>` run instead. `--shading dither|uniform|both` selects the shading variant to render; the drivers always use `dither`, the other two exist for comparison.

A run directory contains `checks.txt` with one PASS or FAIL line per check, `font-info.txt` with the measured metrics and cell size, text previews of the glyphs, and BMP scenes for boxes, blocks, shading, button shadows, scrollbars and the full `B0`–`DF` range. `selection.txt` records why each size was chosen and `summary.txt` in the output root covers all fonts. The exit code is 0 when every run was produced and every check passed, 1 when a check failed or a run could not be produced, and 2 for a usage error.

The checks cover geometry: dimensions, edge connectivity, seams between neighbouring cells, shading coverage and tiling, and determinism. They do not compare against what a driver actually put on screen, so a problem in texture upload, scaling or blending will not show here.

## Keyboard

`TSharpVision.Diagnostics.Keyboard` checks what a driver reports for a key against what the key should produce. A step names a physical key, and the result is judged on four things: the logical (legacy) key identity in `keyCode`, the Unicode text, the modifier and lock state, and the order of press, release and modifier events.

One executable plays two roles. The controller owns the profiles, expectations and verdicts. It starts itself a second time as the target, a minimal TSharpVision application on the driver under test, which records every keyboard event and returns it over a named pipe.

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- list
```

`list` prints the profiles, drivers, sources and suites. A run is:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver <console|sdl|sdl-gpu|terminal> [options]
```

| Option | Meaning |
|---|---|
| `--profile <us\|cz-qwerty>` | Layout profile; default `us`. |
| `--suite <name>` | `main`, `punctuation`, `function`, `navigation`, `keypad`, `modifiers`, `unicode`, `release` or `full`; default `main`. |
| `--source <human\|native\|os\|pty>` | Where the input comes from; default `human`. |
| `--host <auto\|conhost\|default>` | Console host for the console driver on Windows. `auto` uses conhost for `os` and `native` and the system default for `human`. |
| `--locks` | Include the steps that press NumLock. Its state is restored afterwards. |
| `--kitty <none\|full\|flags>` | With `--source pty`: the Kitty flags the simulated terminal confirms, `0`–`31`; default `none`. |
| `--timeout <seconds>` | Per step for the human source; default 30. |
| `--report <path>` | Also write a JSON Lines report. |

`console` selects the Win32 console driver on Windows and the terminal driver elsewhere. `terminal` needs Linux or macOS.

### Sources

The source decides what a result proves, and every report states it.

`human` is real input: the target shows which key to press and a person presses it. The key goes through the active layout, the host and the driver. It works with every driver and on every platform, and it is the only source that covers a tabbed terminal host. In the controller window, `S` skips a step and `Q` ends the run.

`native` writes events straight into the driver's own input queue: `WriteConsoleInputW` for the console driver, `SDL_PushEvent` for the SDL drivers. It is deterministic and needs no focus, but it bypasses the operating system's layout translation entirely. The injected records already contain the characters the profile expects. A PASS here proves the driver's translation of native events. It is not a layout end-to-end PASS.

`os` is the automated end-to-end run and exists on Windows only. It injects physical scan codes with `SendInput`, and the active Windows keyboard layout turns them into virtual keys and characters exactly as it does for a real keyboard. `SendInput` has no target parameter, so the keys go to whichever window has the foreground. The target window must therefore keep the focus for the whole run: do not touch the keyboard or switch windows. The foreground is checked before every key, and if it changes, all held keys are released and the run aborts as INVALID. The tool refuses to run elevated, never sends system chords such as Alt+F4 or Alt+Tab, and accepts only hosts whose foreground window proves where the keys go, which means conhost or an SDL window.

`pty` runs the terminal driver behind a real PTY on Linux or macOS, with the controller playing the terminal: it answers the Kitty negotiation and writes key byte sequences. This validates the byte and protocol path of the driver. No physical key and no layout is involved, so the profile is not used.

### Profiles and layouts

A profile pins the text each key position produces on one layout. Two are built in: `us` (US English, Windows layout `00000409`) and `cz-qwerty` (Czech QWERTY, `00010405`).

The Czech number row is where layout bugs show first:

| Key position | Text |
|---|---|
| `Digit1` | `+` |
| `Digit2` | `ě` |
| `Digit3` | `š` |
| `Digit4` | `č` |
| `Digit5` | `ř` |
| `Digit6` | `ž` |
| `Digit7` | `ý` |
| `Digit8` | `á` |
| `Digit9` | `í` |
| `Digit0` | `é` |
| `Minus` | `=` |
| `Equal` | dead acute |

None of these keys needs a non-zero `keyCode`. A key whose text is not ASCII is expected to arrive as text with no legacy identity.

The tool never installs, loads or switches a layout. For `human` and `os`, select the layout for the target window yourself. Each such run starts with a layout check on three discriminating keys and is INVALID when their text does not match the profile.

### Verdicts

- **PASS**: the events match the profile and the capabilities the driver advertises.
- **FAIL**: wrong text, a false or missing identity, a lost or stuck modifier, or an event nobody asked for.
- **LIMITED**: correct for this transport, which cannot report everything the step describes. A plain ANSI terminal reporting a keypad key as its main-keyboard alias is LIMITED.
- **SKIPPED**: the step does not apply to the capabilities or source, or the operator skipped it.
- **INVALID**: the run itself is unsound: wrong layout, no events, lost focus, target gone or a pipe failure.

`keyCode = 0` with `text = "ě"` is a PASS. Failures look like `ě` arriving as `kbEsc`, `š` arriving as `A`, F5 reported as F4, a release whose identity differs from its press on a driver that advertises `KeyReleaseEvents`, or a keypad key reported as its main-keyboard alias when `DistinctNumericKeypad` is advertised.

The exit code is 0 without a FAIL, 1 with at least one, and 2 for a usage or environment error or an INVALID run. LIMITED and SKIPPED do not affect it.

### Commands

Native injection, layout bypassed:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver console --source native --profile us --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver console --source native --profile cz-qwerty --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver sdl --source native --profile us --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver sdl-gpu --source native --profile us --suite full
```

Windows layout end-to-end. Select the profile's layout first, then leave the keyboard alone:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver console --source os --profile us --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver sdl --source os --profile us --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver sdl-gpu --source os --profile us --suite full
```

Use `--profile cz-qwerty` for the Czech runs. Add `--locks` to include the NumLock steps:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver sdl --source os --profile cz-qwerty --suite keypad --locks
```

A person at the keyboard, here in whatever console host Windows opens:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver console --source human --profile cz-qwerty --suite main --host default
```

Terminal driver behind a PTY (Linux, macOS), as a plain ANSI terminal and with the full Kitty protocol:

```
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver terminal --source pty --suite full
dotnet run --project diagnostics/TSharpVision.Diagnostics.Keyboard -- run --driver terminal --source pty --kitty full --suite full
```

### Windows coverage

The `os` source has been run successfully with both the US English and the Czech QWERTY layout on the console driver (conhost), the SDL renderer driver and the SDL GPU driver. NumLock behaviour was additionally verified by hand on the real SDL input path.

Windows Terminal is not driven by the `os` source. A tabbed host keeps the foreground while another tab or pane has the input focus, so the foreground check cannot prove where injected keys go. With `--source os --host default` the run is therefore refused when the default host is not conhost. Use `--source human` there.
