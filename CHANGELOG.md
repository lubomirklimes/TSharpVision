# Changelog

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
