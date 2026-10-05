# Changelog

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
