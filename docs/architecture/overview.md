# Architecture overview

`TProgram` runs the event loop and idle work. `TApplication` supplies the usual menu bar, desktop and status line. The desktop contains windows, dialogs and other views; windows and dialogs are groups that own their child controls.

Every attached `TView` has an `owner`. A `TGroup` keeps children in a circular linked order: `last` identifies the last child, `last.Next` the first, and `current` the selected child. Insert through group operations rather than editing links. Ordering matters for drawing, event routing and data transfer; those operations do not all traverse in the same direction.

## Selection and focus

`current` is the owner's chosen child. `sfSelected` marks the selection chain, while `sfFocused` reflects input focus along that chain. `sfActive` controls active presentation and behavior. A disabled view stays in the hierarchy but cannot accept normal interaction. Activating a window does not select an arbitrary control inside it.

`TView.Focus()` recursively focuses the owner before selecting the requester. If the outgoing current view has `ofValidate`, its `Valid(cmReleasedFocus)` can veto the move. A veto returns false and leaves the requester unselected. `TProgram.CanMoveFocus()` checks the desktop's current validation. A button with `bfGrabFocus` requests focus before activation; other buttons may issue commands without moving focus.

Detachment, closing and resource cleanup have distinct effects; use the [lifecycle rules](lifecycle-and-ownership.md) when deciding who owns cleanup.

## Drawing and colors

Bounds use character-cell coordinates relative to the owner. Drawing translates local positions through the owner hierarchy and clips against the view, its owners and the exposed region. Framework drawing primitives preserve those rules for overlapping views.

`TDrawBuffer` builds a row or run of characters and attributes. `TVWrite` transfers it into the screen buffer with coordinate translation and clipping; the active driver renders changed cells. The same grid can appear in a console, an ANSI terminal or an SDL window.

Views normally draw with semantic indexes in a local palette. `GetPalette()` supplies the mapping, `MapColor()` maps through the owner chain, and `GetColor()` resolves the final screen attribute. An empty palette passes mapping to the owner. Controls can keep stable local color indexes while the application's palette changes their appearance.

Shadows use the shared screen `shadowSize`, updated during screen-mode initialization. A shadow occupies extra cells outside a view's bounds, remains subject to clipping, and does not enlarge its logical bounds.
