# Input and event routing

`TEvent.What` selects a keyboard, mouse or message event family. Handlers consume events by clearing them. `evCommand` requests an operation; `evBroadcast` carries a notification to interested views.

## Routing

For keyboard and command input, `TGroup.HandleEvent` first dispatches to eligible preprocessing children, then to the focused/current target, then to eligible postprocessing children if the event remains unconsumed. Broadcasts traverse the circular child order and may reach multiple observers unless a handler clears the event according to the notification's contract.

Mouse routing uses position and visibility, translating coordinates through the owner hierarchy. Capture and mouse auto events retain the target of an ongoing interaction. `evMouseAuto` repeats a held mouse interaction; it is separate from keyboard repeat.

During `ExecView`, the executing modal view becomes the shared routing root. Nested modal execution replaces that root temporarily; return or exception restores the previous boundary.

## Keyboard

`evKeyDown` represents the initial press and each native repeat. There is no `evKeyRepeat` or framework keyboard-repeat timer. `evKeyUp` reports an ordinary release when the host exposes it. `evModifierChanged` reports a logical Shift, Ctrl or Alt transition independently of ordinary releases.

`keyCode` carries command and navigation identity, including preserved historical key combinations. `text` carries Unicode textual content independently of that identity. It is always a non-null string; events without text, including releases and modifier changes, use `string.Empty`. The shared `uint controlKeyState` contains keyboard modifiers and lock state when supplied by the host.

Consumers should use the active driver's `KeyboardCapabilities` to determine whether releases and standalone modifier transitions are available. A backend does not fabricate missing reports.

The outer ANSI terminal driver requests Kitty/CSI-u reporting at startup and reads the negotiation reply asynchronously. Capabilities remain `None` before confirmation. Confirmation alone is insufficient: the confirmed flags must include both event-type reporting and all-keys reporting for `KeyReleaseEvents` and `StandaloneModifierTransitions`. Partial confirmation can leave both unavailable. Associated Unicode text depends on the reports supplied by the terminal. Legacy ANSI input produces key-down events without reliable release or modifier-only reporting. Shutdown disables negotiated modes and restores terminal state.

## Mouse

The payload separates character-cell position (`where`), physical buttons (`buttons`), detail (`eventFlags`) and keyboard modifiers (`controlKeyState`). Buttons include left, right, middle and two extra physical buttons. Wheel directions never stand in for buttons.

Down, up, move and auto events belong to the historical `evMouse` mask. `evMouseWheel` is outside that mask, so a view must opt in explicitly. Its direction is carried by `meWheelUp`, `meWheelDown`, `meWheelLeft` or `meWheelRight`; movement and double-click detail use `meMouseMoved` and `meDoubleClick`. Available buttons and wheel axes depend on the host.
