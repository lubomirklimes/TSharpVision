# Changelog

## 1.0.0

First stable release of TSharpVision. It contains everything in the 1.0.0 release candidates below, and the public
API baseline of all eight packages is now shipped, with no pending Unshipped entries. Changes since 1.0.0-rc.4:

### Keyboard and input

- Added a cross-platform Meta modifier: `Keys.kbMetaShift` (0x0100) in `controlKeyState` represents Command on macOS,
  the Windows key and Linux Super. It combines with Shift, Ctrl, Alt and the lock states; there is no separate Meta
  key-code family, so Meta+F5 is `kbF5` and Ctrl+Meta+F5 is `kbCtrlF5`, each with the full modifier state. Existing
  key codes, masks and the Alt > Ctrl > Shift key-code precedence are unchanged.
- Added `KeyboardCapabilities.StandaloneMetaTransitions`: a driver that advertises it reports Meta presses and
  releases on their own as `evModifierChanged`.
- SDL: left and right GUI keys (Command / Windows / Super) set `kbMetaShift`, including standalone transitions, on
  both `SDLDriver` and `SDLGpuDriver`.
- ANSI terminal: an xterm/CSI modifier parameter with the Meta bit (8), including SS3 function keys, and Kitty's
  Super modifier set `kbMetaShift`; with the full Kitty protocol, standalone Super transitions are reported too.
  ESC-prefixed text is still Alt, and Kitty's separate Hyper and Meta modifiers are not mapped. The Win32 console
  reports no Meta state.
- The opt-in input trace is enabled by `TSHARPVISION_TRACE_INPUT=1`, as documented (the mixed-case name it read
  before is still accepted), and always writes `tsharpvision-input-trace.log`, in lowercase, in the working directory.

### SDL

- Both SDL drivers render at the display's native pixel density (Retina / HiDPI). Glyphs are rasterised at the
  configured font size times the density, and box drawing, shading, cursor and underline are drawn at drawable
  resolution, so text and frames are sharp instead of being magnified from a 1× surface. The column/row grid, window
  size in points and the meaning of `fontSize` are unchanged.
- When the window's pixel density changes, for example on a display with a different scale, the drivers
  re-rasterise glyphs without a restart; mouse positions are converted from window coordinates to cells at the
  current density.
- Mouse-wheel and trackpad scrolling follow the operating system's scrolling preference, such as macOS natural
  scrolling, vertically and horizontally. Previously, the SDL drivers reversed the direction whenever SDL reported a
  flipped device. Both drivers share the same wheel translation; scrolling speed is unchanged.
- Environment variables are documented in `docs/environment-variables.md`. `TSHARPVISION_SDL_VSYNC` now accepts its
  values in any case, `TSHARPVISION_DRIVER` ignores surrounding whitespace, and the SDL headless switch is spelled
  `TSHARPVISION_NO_SDL` (the mixed-case name is still accepted).

### Driver selection

- Driver selection is the same for `TSHARPVISION_DRIVER` and `[driver] name`: `sdl` selects `SDLGpuDriver`, and
  `console` and `terminal` select the platform's native text driver (`Win32ConsoleDriver` on Windows,
  `AnsiTerminalDriver` on Linux and macOS), so one configuration file works on every OS. Exact driver names select
  exactly that driver. Automatic selection now prefers `SDLGpuDriver` over `SDLDriver`; `SDLDriver` stays available
  by name.

### Samples

- TVDemo: Diagnostics > Function keys shows which of F1–F10 arrive with no modifier, Shift, Ctrl, Alt/Option and
  Cmd/Meta on the active driver, with live event details, so a terminal's keyboard limits are easy to see.

## 1.0.0-rc.4

- TFrame: fix title rendering at very small widths, including bounds safety and Unicode surrogate-safe truncation.
- TMenuView: fix menu command enable/disable synchronization, including broadcasts and initial state refresh when opening menus.
- SDL: share cell-sized glyph masks between the 2D renderer and GPU shader/CPU-composite paths, preserving Czech accents and descenders with consistent baselines, colors and cell geometry.
- SDL: add `[graphics] fontFit=Condense|Uniform`, defaulting to Condense. Uniform measures one common ordinary-text size using the complete Czech uppercase/lowercase repertoire; rare compatibility letters and exceptional Unicode outliers retain individual fitting.
- Configuration: add public `TSharpVision.Config.FontFitMode` and `GraphicsDriverConfiguration.FontFit` get/init, with programmatic configuration, case-insensitive cfg values and user/application precedence. Invalid cfg values report a clear error.
- SDL GPU: declare the glyph fragment shader atlas sampler in shadercross metadata, correcting Vulkan initialization while preserving the D3D12 path and existing shader resources.
- SDL: disable the synthetic quit following the last native window close, retaining genuine OS quit events, application veto/confirmation and graceful cleanup.
- Modified TVDemo editors now display the standard Save/Discard/Cancel confirmation, including during application shutdown.
- Canceling application shutdown after an earlier Don't Save choice no longer leaves unsaved documents incorrectly marked clean.

## 1.0.0-rc.3

- `TButton` draws its shadow cells on the owner's surface background (owner palette entry 1), keeping the shadow
  role's foreground. Buttons on a blue, cyan or custom-palette parent no longer show gray cells beside and below the
  face; standard gray dialogs render identically.
- Dialog controls (buttons, clusters, input lines, history, lists) now resolve their colors inside a plain `TWindow`:
  the blue, cyan and gray window palettes gained entries 9–32 that reuse existing application roles, instead of
  rendering the error attribute. Entries 1–8 and `TDialog` are unchanged; `TWindow.GetPalette()` now has 32 entries.

## 1.0.0-rc.2

- Added optional `IIdleWaitDriver` support and the protected `TProgram.GetIdleWaitTimeout()` extension point:
  idle loops wait for input, posted work or application deadlines, including in modal dialogs and menus.
  Applications with periodic `Idle()` work should override the deadline hook; continuous invocation is no longer promised.
- Fixed quit and shutdown wakeups during modal execution and kept SDL GPU resources alive until the render worker stops.
- Fixed blank screens after unchanged-size resize notifications, redundant console resize handling, and flicker when reordering views.
- Reduced driver discovery overhead by inspecting assembly metadata before loading unrelated libraries.
- Made editor saves replace the destination only after writing succeeds, preserving existing files on encoding/write failures,
  and fixed saving Unicode surrogate pairs split by the editor's gap buffer.
- Fixed standalone Escape timing, bounded malformed terminal mouse input, and prevented control characters in cells from injecting terminal commands.
- Released HexView and TableView read cancellation resources after completion while keeping active request tokens valid during shutdown.

## 1.0.0-rc.1

- First release candidate for TSharpVision 1.0; feature-frozen for 1.0.
- Public API baseline frozen for 1.0, with no pending Unshipped API entries.
- Scoped the POSIX controlling-terminal assertion to Linux and clarified the macOS behavior.

## 0.1.0-preview.4

### Keyboard and input

- Normalize keypad events to their text, Enter or navigation actions while preserving the physical identity in
  `KeyDownEvent.keypadKey`; applications can customize this through `TProgram.NormalizeKeyEvent` and `KeypadKeys`.
- Fixed duplicate keypad `+` and `-` text on SDL and unwanted characters from Ctrl+Shift+Insert / Delete.
- `TInputLine` copies, cuts and pastes through `ClipboardService.Current`: Ctrl+C / Ctrl+Insert, Ctrl+X / Shift+Delete
  and Ctrl+V / Shift+Insert. Pasted text becomes one line and is cut to the line's capacity; a masked line
  (`PasswordChar`) copies nothing.
- Fixed `TInputLine` deleting the selection twice when a typed character of two UTF-16 units replaced it.

### Editor

- Added Shift selection, Ctrl+Home / End navigation and Ctrl+Backspace / Delete word deletion, including selections
  and undo; restored Ctrl+Insert, Shift+Insert and Shift+Delete clipboard shortcuts.
- Fixed word navigation past the end of a full document buffer.

### Code editor and syntax (`TSharpVision.CodeEditor`)

- Added TOML highlighting and expanded filename detection for XML, environment and TOML files.
- Detect JSON, XML and YAML content when the filename does not identify a language.
- Added `ISyntaxService.GetLanguages()` and `SyntaxLanguageInfo` for language selection, including aliases and file names.
- Added registration and discovery of custom syntax languages, using application classifiers or TextMate grammars.

### Views

- Fixed a group that was hidden and shown again being drawn from the image it had before it was hidden. A group
  resized while hidden could throw `ArgumentOutOfRangeException` from `TView.WriteBuf` or show a garbled image; one
  whose content changed while hidden showed the old content.
- `MsgBox.MessageBox` sizes its dialog to the message and buttons instead of always using 40×9 cells, so longer or
  multi-line messages are no longer cut off. A message that fits 40×9 looks as before; a larger one grows up to the
  host's size. CRLF and CR line breaks in message-box text are treated as line breaks. `MessageBoxRect` keeps the
  caller's bounds.

### Diagnostics

- Keep raw keypad identities visible in TVDemo Keyboard Diagnostics and the cross-driver keyboard diagnostics tool.

### Documentation

- Updated input routing and editor documentation for keypad normalization, editing shortcuts and custom languages.

## 0.1.0-preview.3

### Keyboard and input

- The numeric keypad has its own key identities: `kbKeypad0`–`kbKeypad9`, `kbKeypadDecimal`, `kbKeypadDivide`,
  `kbKeypadMultiply` and `kbKeypadEnter`, alongside the existing `kbGrayMinus` and `kbGrayPlus`. `kbNumLock` identifies
  the NumLock key; `kbNumState` still reports the lock state.
- `KeyboardCapabilities.DistinctNumericKeypad` tells whether the active driver can report those identities. The console
  and SDL drivers do; the ANSI terminal driver does once the Kitty protocol reports all keys, and otherwise keeps
  reporting keypad keys as their main-keyboard aliases.
- Unicode text stays separate from the legacy key identity. A key that only types a non-ASCII character arrives with
  its `text` and `keyCode` 0, instead of a code derived from the character. This fixes Czech QWERTY and similar
  layouts, where keys such as `ě` or `č` were reported as Esc, Enter or other unrelated keys.
- SDL: a key release carries the identity of its press, and a text-only key no longer gets an invented one.
- AltGr / Right Alt: a text-producing combination yields its text, not an Alt or Ctrl shortcut.
- Kitty keyboard protocol: text comes only from the associated text the terminal reports; keys without a legacy
  identity, keypad keys and their repeats and releases are decoded consistently.
- Console: releasing a dead key no longer reports its accent character as a key identity.
- ANSI terminal: the Linux console sequences for F1–F5 are decoded.

### Diagnostics

- New developer tool `TSharpVision.Diagnostics.Keyboard` checks key identity, text, modifiers and event order on the
  console, SDL renderer, SDL GPU and terminal drivers. It has US English and Czech QWERTY profiles and four input
  sources: a person at the keyboard, native event injection, Windows scan-code injection through the real layout, and
  a PTY for the terminal driver.
- TVDemo: a Keyboard Diagnostics dialog shows live key events and a numeric keypad that follows the driver's
  capabilities; the Mouse Diagnostics dialog draws the buttons and wheel.

### Documentation

- [Input and event routing](docs/architecture/event-model.md) describes the keyboard semantics: event kinds, identity
  versus text, releases, AltGr, keypad identities, capabilities and terminal limits.
- [Diagnostics](docs/diagnostics.md) covers the keyboard and SDL glyph tools.

## 0.1.0-preview.2

### Breaking API corrections

- Terminal widget, emulator and session APIs now belong to `TSharpVision.Terminal`. The old core terminal API, including its streamable widget and raw key map, is removed without compatibility shims.
- Terminal sessions now send `ReadOnlyMemory<byte>` and deliver output through `TerminalOutputEventArgs.Data` rather than strings. The redesigned API uses the extension package's terminal types; `SendTextAsync` encodes UTF-8 text.

- `Keys.kbF11` changed from `0x5700` to `0x8500`, and `Keys.kbF12` from `0x5800` to `0x8600`. The old values were also
  the codes of Shift+F4 (`kbShiftF4`) and Shift+F5 (`kbShiftF5`), so those keys could not be told apart.
- `Keys.kbSpace` changed from `0x0034` to `0x0020`. The old value was the key code of a typed `4`; every driver reports
  Space as `0x0020`.
- These are `const` fields, which C# copies into the assemblies that use them. Rebuild anything compiled against an
  earlier preview or local package; otherwise its F11, F12 and Space bindings keep the old values.

### Changed behavior

- Keyboard: a function key pressed with several modifiers reports the code of the highest-precedence one — Alt, then
  Ctrl, then Shift — on every driver; `controlKeyState` carries the full combination.
- `TEditor`: the buffer is LF-only. `LineStart`, `LineEnd`, `NextLine` and drawing no longer treat a CR as a line
  break, matching `CountLines` and the LF normalization of `TFileEditor` and clipboard paste. A CR inserted through the
  API is an ordinary character, drawn blank.

### Fixes

- ANSI terminal: modified F1–F4, Shift+Tab, Shift+Insert / Shift+Delete and Alt+Backspace are decoded; 
- SDL: Ctrl+Enter, Ctrl/Alt+Backspace and keypad Enter are reported.
- `KeyText.PrintableText` no longer treats DEL (0x7F, the low byte of `kbCtrlBack`) as text, so Ctrl+Backspace no
  longer inserts a DEL character in `TEditor` or `TInputLine`.
- Resource compiler: `Shift+`, `Ctrl+` and `Alt+` `F11` / `F12` key names.

## 0.1.0-preview.1

First public preview candidate. The core framework provides Turbo Vision-style windows, dialogs, menus, editors, resources, help, and keyboard interaction, with restored compatibility behavior for classic Turbo Vision applications.

The package family includes Windows console, ANSI terminal, and SDL3 graphical drivers; TextMate-based code editing; paged hexadecimal and table views; and ConPTY/POSIX child-process terminal sessions. The packages target .NET 10. APIs and behavior may change during the preview series.
