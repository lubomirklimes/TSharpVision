# Palette modes

`TProgram` resolves the standard application colors through one of three logical palette families,
`TProgram.AP.Color`, `TProgram.AP.BlackWhite` and `TProgram.AP.Monochrome`. BlackWhite and Monochrome use only
black, light gray, dark gray and white. The family is a logical palette: it is not a grayscale filter, and it does not
change the screen mode or the driver's colors. The driver's output colors are a separate layer that composes with
the family; see [physical palettes and display effects](display-presets.md).

## Switching a running program

```csharp
app.SetAppPalette(TProgram.AP.Monochrome);
```

One call switches the family and repaints the whole program: the menu bar, status line, desktop, every window,
including buffered and overlapping ones, and an open modal dialog or dropdown. Selecting the family that is already
active does nothing. An undefined value throws `ArgumentOutOfRangeException`, and a program that has been shut down
throws `ObjectDisposedException`. Call it on the UI thread.

Assigning `AppPalette` directly still works but does not repaint; follow it with `Redraw()`.

## Starting in a family

Set `AppPalette` from an `InitScreen` override. `InitScreen` runs inside the `TProgram` constructor, so the first frame
is already drawn in that family:

```csharp
sealed class MonoApp : TApplication
{
    public override void InitScreen()
    {
        base.InitScreen();
        AppPalette = AP.Monochrome;
    }
}
```

## Legacy screen mode

`SetScreenMode(mode)` keeps its Turbo Vision meaning. A nonzero `mode` goes through `TScreen.SetVideoMode`, which
normalizes it with `TScreen.FixCrtMode` (an unknown low byte becomes `SM.CO80`; `SM.Font8x8` is kept), passes it to
the driver with `TDisplay.SetCrtMode` and records it as the logical `TScreen.ScreenMode`. Then `InitScreen` derives
the family from `TScreen.ScreenMode` (`SM.Mono` selects Monochrome, removes shadows and sets `TView.showMarkers`;
`SM.BW80` selects BlackWhite; anything else selects Color) and the program repaints. `SetScreenMode(0)` skips the
mode change and only re-derives and repaints from the current `TScreen.ScreenMode`, as every call did before 1.2.

Every shipped driver keeps its physical display and goes on reporting `SM.CO80`: no resolution, font or row count
changes, and `SM.Font8x8` only narrows shadows. `TScreen.Suspend` and `TScreen.Resume` ask the driver for the
startup mode and back, and `Resume` keeps the logical mode. `SetAppPalette` changes only the palette and leaves the
mode, shadows and markers alone, so `SetScreenMode(0)` after `SetAppPalette` returns to the mode's family.

## What does not change

Only views that resolve their colors through the application palette follow the family. These keep their own colors:

- views that draw fixed attributes or override `MapColor` with literal attributes, such as the TVDemo palette
  galleries and the accessories not named below;
- code editor syntax colors, which come from `SyntaxColorScheme` (use `SyntaxColorScheme.Monochrome` yourself);
- terminal widget output, which shows the hosted program's colors.

The TVDemo Puzzle, Calendar, Calculator, Clock, Tetris and ASCII chart keep their classic colors in Color and follow
BlackWhite and Monochrome. In those two families Tetris draws each piece type with its own glyph pattern, because the
piece colors are not available.

Edits made with the color dialog change the table of the active family only. The framework saves nothing: the family
and the three tables live for the process. TVDemo's **Window > Save desktop state** records the active family and each
family's table separately (an unedited family as "default"), and startup or **Window > Load desktop state** restores
them without ever copying one family's table into another; see [TVDemo desktop state](tvdemo-desktop-state.md).

## TVDemo

**Options > Framework palette** offers Color, Black and white and Monochrome, with `(●)` marking the active family.
The choice lasts for the session unless you save it with **Window > Save desktop state**; startup restores it.
With the SDL GPU driver, **Options > Physical palette** and **Options > Display effect** additionally change the
driver's output; see [physical palettes and display effects](display-presets.md#tvdemo).
