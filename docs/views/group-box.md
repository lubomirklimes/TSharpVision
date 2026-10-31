# Group boxes

`TGroupBox` is a titled container using ordinary group-local child coordinates. With a border, `ContentRect` starts at (1, 1); insert children inside that rectangle. Children are clipped to it rather than repositioned. `ShowBorder = false` gives a logical borderless group. `TitleAlignment` accepts `Left`, `Center` or `Right`; titles are presentation text, without mnemonic processing.

The group inherits its parent's palette. Tab and Shift+Tab visit eligible children in insertion order and leave at the boundary; hidden and disabled children are skipped. Nested groups follow the same policy. Empty groups do not add a focus stop. Size changes preserve normal child grow modes. Small bounds and long titles are clipped safely.

Mnemonics work across groups. A key the focused control and its group leave unhandled is offered to the labels, buttons and clusters of the other group boxes, after the focused group, so the focused group wins a duplicate mnemonic; hidden and disabled controls are skipped, and text typed into a focused input line stays there. A label or cluster mnemonic that selects a control in another group box focuses that group too. Entering a group with Tab starts at its first (Shift+Tab: last) child; a click focuses the child under the mouse; entering it any other way, such as `Focus` or `Select` on the group, restores its current child. Showing or hiding a control keeps the current child while it stays eligible.

A hidden control, or one inside a hidden group, never acts on its mnemonic, in a group box or a flat dialog; neither does a disabled one. A group box has `ofValidate`, so a child marked `ofValidate` is asked `Valid(cmReleasedFocus)` once when focus leaves the group for another one, by Tab, Shift+Tab, a click, a label or cluster mnemonic, or `Focus`. If it refuses, focus, the current views and the other group stay as they were, and the cluster or click does nothing. `Select` remains the unchecked primitive and does not validate.

See the complete [group composition example](../dialogs-and-controls.md#group-boxes) and TVDemo's Controls > Group boxes. This control is constructed in code; object streaming is not supported.
