# TColorDialog: Try, OK and Cancel

`TColorDialog` edits a palette through grouped items, foreground/background selectors and a preview. It always
edits the palette passed to its constructor (`Pal`) in place. What **Try**, **OK** and **Cancel** do to the running
application depends on whether the caller opts in to live preview.

## Opting in

```csharp
var dlg = new TColorDialog(GetPalette().Clone(), groups); // edit an independent copy
if (ValidView(dlg) != null)
{
    dlg.BindLivePalette(GetPalette(), Redraw); // live table of the current family + full repaint
    DeskTop.ExecView(dlg);                     // no copy-back needed
}
```

`BindLivePalette(TPalette livePalette, Action repaint)` must be called once, before the dialog runs. It captures a
snapshot of `livePalette`'s bytes. After that:

| Action | Effect on `livePalette` | Repaint |
| --- | --- | --- |
| **Try** (`cmTryColors`) | edited entries copied in; dialog stays open; repeatable | yes, when a byte changed |
| **OK** (`cmOK`) | edited entries copied in, with or without a previous Try | yes, when a byte changed |
| **Cancel**, **Esc**, close box, any other end of the modal run | exact snapshot restored, however many Trys happened | yes, when a byte changed |
| Modeless use: `Close()` | snapshot restored | yes |
| Modeless use: shut down without `Close()` (program teardown) | snapshot restored | no |

`TProgram.Redraw` (inherited from `TGroup.Redraw`) is the right repaint: it invalidates every buffered group, so
nested buffered views and overlapping windows are recomposed and no stale cells remain. Try still broadcasts
`cmUpdateColorsChanged` to the dialog's owner before the repaint, exactly as before.

States: `Open` --Try--> `Tried` --Try--> `Tried`; `Open`/`Tried` --OK--> `Committed`;
`Open`/`Tried` --Cancel/Esc/close box/other--> `Restored`. `Committed` and `Restored` are final: a later shutdown
of the dialog does nothing.

## Palette family changes while the dialog is open

The dialog is bound to the palette **object** passed to `BindLivePalette` — for `TProgram.GetPalette()` that is the
table of the family (`TProgram.AppPalette`: Color, BlackWhite or Monochrome) active when the dialog opened. If the
family changes while the dialog is open (for example through `TProgram.SetAppPalette`):

- Try and OK still write only into the captured table, never into the newly selected one; the screen keeps showing
  the newly selected family, so a Try of a table that is not on screen changes nothing visible.
- Cancel restores only the captured table.
- The family itself is never changed by the dialog.

## Callers that do not opt in

Without `BindLivePalette` the dialog behaves exactly as in 1.1: it only edits `Pal`, Try only broadcasts
`cmUpdateColorsChanged`, and copying the result back after `cmOK` is the caller's job.

TVDemo (**Options > UI colors**) and TVTerm (**Options > UI Colors**) both opt in.
