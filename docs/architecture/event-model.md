# Input and event routing

`TEvent.What` selects a keyboard, mouse or message event family. Handlers consume events by clearing them. `evCommand` requests an operation; `evBroadcast` carries a notification to interested views.

## Routing

For keyboard and command input, `TGroup.HandleEvent` first dispatches to eligible preprocessing children, then to the focused/current target, then to eligible postprocessing children if the event remains unconsumed. Broadcasts traverse the circular child order and may reach multiple observers unless a handler clears the event according to the notification's contract.

Mouse routing uses position and visibility, translating coordinates through the owner hierarchy. Capture and mouse auto events retain the target of an ongoing interaction. `evMouseAuto` repeats a held mouse interaction; it is separate from keyboard repeat.

During `ExecView`, the executing modal view becomes the shared routing root. Nested modal execution replaces that root temporarily; return or exception restores the previous boundary.

## Keyboard

There are three keyboard event kinds. `evKeyDown` is the initial press and each native repeat; there is no separate repeat event and no framework repeat timer. `evKeyUp` is the release of an ordinary key, on drivers that advertise `KeyReleaseEvents`. `evModifierChanged` is a Shift, Ctrl or Alt transition reported on its own, on drivers that advertise `StandaloneModifierTransitions`. Modifier releases are reported only as `evModifierChanged`, never as `evKeyUp`. The historical `evKeyboard` mask still covers `evKeyDown` alone, so a view opts in to the other two.

### Identity and text

`keyCode` is the historical Turbo Vision key identity: commands, navigation, function keys, ASCII and the preserved Ctrl/Alt combinations. `text` is the Unicode text that the active layout or input transport produced for the event. The two are independent, and arbitrary Unicode is never encoded into `keyCode`. A key that only types a character outside that legacy set arrives as

```
evKeyDown  keyCode = 0  text = "ě"
```

which is complete and intentional, not an unmapped key. Handle shortcuts by `keyCode` and typed input by `text`. The one legacy exception is the console driver, which may still carry a Latin-1 character (U+0080–U+00FF) in `keyCode` as well as in `text`.

`text` is never null. Events without text, including every release and modifier change, use `string.Empty`.

`charScan.scanCode` is the scan component of the packed historical identity, the high byte of `keyCode`. `raw_scanCode` is a different thing: an optional backend-native scan byte that a driver fills in when its host supplies one. It is not a portable physical-key identifier and is zero when not supplied.

`controlKeyState` holds the logical modifier and lock state when the host supplies it.

### Releases

A release carries the identity its press was published with, and no text. For a text-only key that is

```
evKeyDown  keyCode = 0  text = "ě"
evKeyUp    keyCode = 0  text = ""
```

A driver does not invent a legacy identity for a release that the press did not have, and nothing in the event tells which physical position was pressed.

### Alt and AltGr

A text-producing AltGr combination yields its text and no Alt or Ctrl shortcut identity: AltGr+E on a Czech layout is `€`, not `kbAltE` or `kbCtrlE`. The logical modifier bits in `controlKeyState` remain available. On hosts where Right Alt is ambiguous, a driver may hold back a Right Alt shortcut until it knows whether text follows, so such a shortcut can arrive slightly later than its Left Alt equivalent.

### Numeric keypad

When the transport can tell the keypad from the main keyboard, keypad keys have their own identities: `kbKeypad0`–`kbKeypad9`, `kbKeypadDecimal`, `kbKeypadDivide`, `kbKeypadMultiply`, `kbKeypadEnter`, and the historical `kbGrayMinus` and `kbGrayPlus` for keypad `-` and `+`. The identity says which key was pressed; `text` says what character it generated, if any; `controlKeyState & kbNumState` says whether NumLock is on. `kbNumLock` is the identity of the NumLock key itself.

Drivers report these identities; views never have to know them. `TProgram.GetEvent` gives each keypad press and release its semantic key code before the status line, a view, a modal loop or a menu sees it, and keeps the physical key in `keypadKey`:

```
driver        evKeyDown  keyCode = kbKeypad8                      NumLock off
dispatched    evKeyDown  keyCode = kbUp       keypadKey = kbKeypad8

driver        evKeyDown  keyCode = kbKeypad1  text = "1"          NumLock on
dispatched    evKeyDown  keyCode = '1'        text = "1"  keypadKey = kbKeypad1
```

The step is `KeypadKeys.Normalize`. It decides from the event alone, in this order:

| The driver reported | Dispatched as |
|---|---|
| `kbKeypadEnter` | `kbEnter`; `kbCtrlEnter` with Ctrl |
| A keypad key with `text` | That text. `keyCode` is the character when the text is one ASCII character and 0 otherwise, as for any typed key. The text is the driver's (the decimal key types what the layout says) and is never derived from the identity. |
| `kbKeypad0`–`kbKeypad9` or `kbKeypadDecimal` without text, NumLock off | The cursor key printed on it: 7 Home, 8 Up, 9 PgUp, 4 Left, 6 Right, 1 End, 2 Down, 3 PgDn, 0 Ins, decimal Del, in the modifier variant the main key has (`kbCtrlHome`, `kbShiftIns`, …). `controlKeyState` keeps the whole combination. |
| Anything else | Unchanged, which no view handles: a key without text while NumLock is on, keypad 5 without text, `/` or `*` without text. |

`kbGrayMinus` and `kbGrayPlus` keep their historical key codes, which applications bind. Like every keypad key they type only the text their event carries: the character in their legacy scan pair is cleared when the event has no text.

`keypadKey` is set on every keypad event, changed or not, so `keypadKey != 0` means the key is on the keypad. It is a field of its own because where a key is on the keyboard is not modifier or lock state. A release carries no text, so the release of a key that typed text keeps the keypad identity as its `keyCode`; `keypadKey` pairs it with its press.

A program that must see keys as the driver reported them, such as a keyboard diagnostic, overrides `TProgram.NormalizeKeyEvent`.

What the drivers supply differs:

| | Text of a typing keypad key | NumLock state | Consequence |
|---|---|---|---|
| Console | In the same event | yes | One event per key. |
| SDL | A separate text event after the key event | yes (`SDL_KMOD_NUM`) | The key event is the identity (navigation, or nothing with NumLock on); the text event that follows types, and has no keypad identity. |
| Kitty | In the same event when the terminal reports associated text | yes | Without associated text a typing keypad key types nothing. |
| Plain ANSI | — | no | The keypad sends what the main keys send; there is nothing to normalize. |

With NumLock on, Shift turns the keypad into cursor keys for the host, but the event then still carries NumLock on and no text, like a digit whose text has not arrived. It is not treated as navigation on any driver.

### Capabilities

Drivers differ in what they can report. `IDriver.KeyboardCapabilities` states what the active driver reports reliably, and a driver never fabricates a report its transport does not supply.

| Flag | Meaning |
|---|---|
| `KeyReleaseEvents` | Releases of ordinary keys arrive as `evKeyUp`. |
| `StandaloneModifierTransitions` | Shift, Ctrl and Alt transitions arrive as `evModifierChanged`. |
| `DistinctNumericKeypad` | Keypad keys are reported with identities separate from the corresponding main-keyboard keys. |

`Win32ConsoleDriver`, `SDLDriver` and `SDLGpuDriver` advertise all three. `AnsiTerminalDriver` advertises what the terminal has confirmed, and the value can change after startup once the negotiation reply has been read.

### Terminals

Plain ANSI input is text and escape sequences. It cannot reliably expose key releases, standalone modifiers, the physical origin of a key or a distinct keypad, so the capabilities are `None`, input arrives as key-down events only, and keypad keys are reported as their main-keyboard aliases.

The terminal driver requests the Kitty keyboard protocol at startup and reads the reply asynchronously; capabilities stay `None` until it is confirmed. What is then available depends on the confirmed flags. `KeyReleaseEvents` and `StandaloneModifierTransitions` need both event-type reporting and all-keys reporting, and `DistinctNumericKeypad` needs all-keys reporting, so a partial confirmation can leave some or all of them unavailable. The text of an event is the associated text the terminal reports. A Kitty key identity alone is not turned into generated text. Shutdown disables the negotiated modes and restores the terminal state.

## Mouse

The payload separates character-cell position (`where`), physical buttons (`buttons`), detail (`eventFlags`) and keyboard modifiers (`controlKeyState`). Buttons include left, right, middle and two extra physical buttons. Wheel directions never stand in for buttons.

Down, up, move and auto events belong to the historical `evMouse` mask. `evMouseWheel` is outside that mask, so a view must opt in explicitly. Its direction is carried by `meWheelUp`, `meWheelDown`, `meWheelLeft` or `meWheelRight`; movement and double-click detail use `meMouseMoved` and `meDoubleClick`. Available buttons and wheel axes depend on the host.
