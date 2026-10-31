# Combo boxes

`TComboBox` is a single-line control with a dropdown list of strings. It has two modes, chosen at construction:

- **Non-editable** — the control shows the selected item; the user can only choose one of the items.
- **Editable** — the control is an input line with a list of suggested values; the text may be anything.

It is part of the core `TSharpVision` package and works the same on every driver: it draws only through the cell
buffer and reads only normalized events.

## Construction

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class ConvertDialog
{
    public static TDialog Create(out TComboBox caseBox, out TComboBox encoding)
    {
        var dialog = new TDialog(new TRect(0, 0, 46, 10), "Convert");

        caseBox = new TComboBox(new TRect(13, 2, 31, 3), new[] { "Keep", "Lower", "Upper" });
        caseBox.SelectedIndex = 0;
        dialog.Insert(new TLabel(new TRect(2, 2, 12, 3), "~C~ase:", caseBox));
        dialog.Insert(caseBox);

        encoding = new TComboBox(new TRect(13, 4, 35, 5),
            new[] { "UTF-8", "UTF-16 LE", "Windows-1250", "ISO-8859-2" },
            editable: true, maxLength: 40);
        encoding.Text = "UTF-8";
        dialog.Insert(new TLabel(new TRect(2, 4, 12, 5), "~E~ncoding:", encoding));
        dialog.Insert(encoding);

        dialog.Insert(new TButton(new TRect(10, 7, 21, 9), "~O~K", Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(new TButton(new TRect(23, 7, 34, 9), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));
        caseBox.Select();
        return dialog;
    }
}
```

```text
 Case:       Keep               ▼
 Encoding:   UTF-8                  ▼
```

The bounds are one row high; the last three cells hold the arrow (one cell when the control is narrower than six).
The items are copied: later changes to the source collection do not affect the control. Null items are rejected.
`maxLength` limits the editable text in UTF-16 code units (default 255); longer text, including a longer item chosen
from the list, is truncated without splitting a surrogate pair.

Read the values after `ExecView` returns and before `ShutDown`, as with any other control:

```csharp
var dialog = ConvertDialog.Create(out var caseBox, out var encoding);
try
{
    if (DeskTop.ExecView(dialog) == Views.cmOK)
        Convert(caseBox.SelectedIndex, encoding.Text);
}
finally
{
    dialog.ShutDown();
}
```

## Selection and text

| Member | Meaning |
| --- | --- |
| `Items` | Read-only view of the current items. |
| `SelectedIndex` | Index of the selected item, or **-1 for no selection**. A new combo box has no selection. |
| `SelectedItem` | The selected item, or null. |
| `Text` | Editable: the edit text. Non-editable: the selected item, or `""`. |
| `IsEditable`, `MaxLength`, `MaxDropDownRows` | Mode, text capacity, rows shown before the list scrolls (default 8). |
| `IsDropDownOpen` | Whether the dropdown is open. |
| `SetItems(items)` | Replaces the items (copied). |
| `OpenDropDown()`, `CloseDropDown()` | Open the dropdown modally (returns whether an item was committed); request it to close without committing. |

Rules:

- Setting `SelectedIndex` outside `-1 … Items.Count - 1` throws `ArgumentOutOfRangeException`.
- Editable mode: setting `SelectedIndex` replaces the text with the item; setting -1 clears the text (unless there was
  no selection already, in which case custom text stays). The index *follows the text*: it stays on the selected item
  while the text equals it, otherwise it is the first item equal to the text (ordinal, case-sensitive), otherwise -1.
  Typing a custom value never forces a selection.
- Non-editable mode: `Text = "Lower"` selects that item; `Text = ""` clears the selection; any other value throws
  `ArgumentException`.
- `SetItems` closes an open dropdown without committing it. A non-editable combo box keeps the previously selected
  item if the new items contain it (first match), otherwise it has no selection. An editable one keeps its text and
  re-matches it.

## Keyboard

Closed, non-editable:

| Key | Effect |
| --- | --- |
| Up / Down | Previous / next item (from no selection: the first). Committed immediately. |
| PgUp / PgDn | `MaxDropDownRows` items back / forward. |
| Home / End, Ctrl+PgUp / Ctrl+PgDn | First / last item. |
| A letter | Next item starting with it (case-insensitive), wrapping. |
| Alt+Down, F4 | Open the dropdown. |
| Enter, Esc, Tab, Shift+Tab, Alt+letter | Left to the dialog: default button, cancel, focus movement, label hot keys. |

Closed, editable: every key is the input line's (editing, selection, Ctrl+C/X/V, Unicode input), except
**Down, Alt+Down and F4, which open the dropdown** (as Down opens a `THistory` list). Enter and Esc reach the dialog.

Open (both modes):

| Key | Effect |
| --- | --- |
| Up / Down / PgUp / PgDn / Home / End, letters | Move the highlight. Nothing is committed and no event is raised. |
| Enter, Space, F4, Alt+Up, Alt+Down | Commit the highlighted item and close. |
| Esc | Close without any change. The editable draft is exactly what it was before opening. |
| Tab / Shift+Tab | Commit, close, then move the focus as the key would have from the closed control. |

Because the dropdown runs its own modal loop, keys it consumes never reach the dialog: Enter that commits an item does
not press the default button, and the first Esc only closes the dropdown — a second Esc cancels the dialog.

## Mouse

- A click on a non-editable combo box, or on the arrow of an editable one, focuses it and opens the dropdown.
  A click in the text of an editable combo box positions the caret and selects text as in `TInputLine`.
- In the dropdown, pressing over the list highlights the item under the pointer (dragging past the top or bottom
  scrolls); releasing over an item commits it. Releasing elsewhere commits nothing. The wheel and scrollbar scroll.
- A click outside the dropdown closes it without a change and is consumed, so it does not press a button or move the
  focus underneath. This includes a click on the combo box itself.
- A disabled combo box receives no clicks or keys; `OpenDropDown()` returns false. Code may still set its selection.

## Change notifications

```csharp
caseBox.SelectionChanged += (_, _) => preview.Update(caseBox.SelectedIndex);
encoding.TextChanged += (_, _) => okButton.SetState(Views.sfDisabled, encoding.Text.Length == 0);
```

- `SelectionChanged` — `SelectedIndex` changed. `TextChanged` — the edit text changed (editable mode only).
- Both are raised **after** the state changed, for user actions and code alike, and only when the value actually
  changed. One action raises each at most once, `TextChanged` first: choosing "UTF-16 LE" from the list raises one
  `TextChanged` and one `SelectionChanged`.
- Moving the dropdown highlight raises nothing; only the commit does.
- Derived classes can override `OnSelectionChanged` / `OnTextChanged`.

## Appearance

The field uses the dialog's input-line roles. The arrow is one three-cell button surface (one cell under six columns)
with `▼` in its middle cell, drawn with the dialog's **button** roles so that all three cells share one background:

| State | Surface and padding cells | Arrow | Dialog role (default color palette) |
| --- | --- | --- | --- |
| Normal | green | black | button normal `0x0A` (black on green) |
| Focused | green | yellow | arrow: button shortcut `0x0E` (yellow on green); surface: `0x0A` |
| Dropdown open | green | yellow | as focused — the arrow stays highlighted although the dropdown holds the focus |
| Disabled | disabled background | muted | disabled cluster `0x1F` on all three cells (dark gray on cyan) |

The button has no shadow, sits directly after the field with no gap, and is redrawn on focus changes, on opening and
closing the dropdown, on enabling/disabling and on every resize. Colors are palette roles, not driver colors, so every
driver receives the same cell attributes and the black-and-white and monochrome palettes map them as they map
`TButton`.

## Dropdown placement

The dropdown is a framed window with the colors of the `THistory` window of the dialog that holds the control. It is
inserted into the **desktop** that holds the control (or the root of the view tree when there is no desktop), so it may
extend beyond a small dialog but never covers the menu bar or status line and never writes outside the screen.

- Rows: the item count, at least one (`<empty>` for no items), at most `MaxDropDownRows`; a vertical scrollbar
  appears when the items do not fit.
- Width: at least the control's width; wider when the longest item needs it; never wider than the desktop. A
  dropdown that would cross the right edge moves left.
- Position: directly below the control; above it when there is no room below; otherwise on the larger side, shrunk,
  with the list scrolling; as a last resort inside the desktop over the control. Too small a desktop (under three
  rows or columns) opens nothing.
- While open, the dropdown re-checks its geometry before every event: if the dialog moves or resizes, or the control
  changes bounds, it moves and resizes with them, keeping the highlight and the edit text. If the control is hidden,
  disabled, removed or shut down, its dialog ends, or the program quits, the dropdown closes without committing.

## Ownership and disposal

The combo box creates and owns its child views (the text field or input line and the arrow); `ShutDown` shuts them
down, closes an open dropdown and drops the event handlers. The dropdown exists only while `OpenDropDown` runs: it is
inserted into the desktop by `ExecView`, removed when its loop ends and shut down before `OpenDropDown` returns. No
view outlives the call.

## Limitations

- String items only; there is no item-value association or data binding.
- `DataSize()` is zero: the control takes no part in `TDialog.GetData` / `SetData` records. Use its properties.
- It cannot be written to a stream (`Write` throws `NotSupportedException`) and is not registered for loading.
- Text is laid out as one UTF-16 code unit per cell, as everywhere in the framework; wide (double-cell) characters
  are not measured specially.
- Long items are clipped in the field (with a `>` marker) and in the list; there is no horizontal scrolling of the
  list.
- No autocomplete beyond highlighting the first item that starts with the draft when the dropdown opens.
- A screen resize while the dropdown is open closes it; the resize then proceeds as it does for the dialog itself.
