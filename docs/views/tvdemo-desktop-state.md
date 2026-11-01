# TVDemo desktop state

TVDemo can save its plain demonstration windows and its framework palette to `tvdemo-desktop.tvr` next to the
executable. This is sample code (`samples/TSharpVision.Samples.TVDemo/Persistence.cs` and `DesktopState.cs`); the
framework itself persists nothing.

## When it saves and loads

| Trigger | What happens |
| --- | --- |
| **Window > Save desktop state** | Writes the plain windows (title, bounds, number, static text), the active palette family and all three family tables. Games, accessories and viewers are skipped and counted. The file is written to a temporary name and then moved over the old one, so a failed save keeps the previous snapshot. |
| Startup | If the file exists, restores the palette family and the three tables. Saved windows are not opened. |
| **Window > Load desktop state** | Replaces the open plain windows with the saved ones and restores the family and the tables. Other windows stay open. A missing file is reported. |
| Exit | Nothing is saved. TVDemo has no autosave. |

Before applying anything, Load and startup read and check the whole file: the header and version, every palette table
(size and leading entry-count byte), the family name, the window count and every window. A missing, truncated,
corrupt or newer-version file is reported in a message box. Nothing is applied, and the file is never deleted or
rewritten.

While a modal dialog is open, such as **Options > UI colors** with a pending **Try** preview, the menu cannot reach
Save or Load. If the command arrives by another route, TVDemo refuses it and asks you to close the dialog first. A
preview that is later cancelled is therefore never saved. After Cancel, the restored palette is what gets saved;
after OK, the edit is saved.

## Palette families

The family (Color, Black and white, Monochrome) and each family's table are recorded separately. An edit made in
one family is saved and restored for that family only, and the active family's table is never written into
another family's table. A family whose table equals the framework's built-in table is recorded as `default`, so it
follows the built-in table of the TVDemo that loads it. Loading a snapshot also resets any family recorded as
`default` back to the built-in table. After a restore, the **Options > Framework palette** `(●)` marker shows the
restored family.

The three tables are process-wide statics shared by every `TProgram`. TVDemo copies them when it starts, before
reading the snapshot, and copies them back in `ShutDown`. Its changes therefore end with the program that made them.

## File format

`tvdemo-desktop.tvr` is a `TResourceFile` (`FBPR` header, keyed resources, index at the end):

| Key | Content |
| --- | --- |
| `metadata` | `TStringCollection`: `"TVDemo desktop 1"`, hex of the **Color** table (including its leading size byte), window count. Same layout as before TV12d. |
| `window.00000` … | One streamed `TWindow` per saved window, back to front. |
| `palettes` | `TStringCollection`: `"TVDemo palettes 1"`, `"mode=Color"` / `"mode=BlackWhite"` / `"mode=Monochrome"`, then `"Color=<t>"`, `"BlackWhite=<t>"`, `"Monochrome=<t>"`, where `<t>` is `default` or the table's hex. Added in TV12d. |

Compatibility:

- **Snapshots without `palettes`** (written by TVDemo before TV12d) load as the Color table with the Color family
  selected. The Black and white and Monochrome tables are left as they are, because the file does not record them.
  Trailing or extra data is never read as a family.
- **Older TVDemo builds** ignore the `palettes` resource. They load the windows and the Color table from `metadata`,
  never the table of the family that was active.
- **A newer version** of either header (`TVDemo desktop N` or `TVDemo palettes N` with N other than 1), an unknown
  family name, or a wrong number of entries is rejected with a message, and nothing is applied.
