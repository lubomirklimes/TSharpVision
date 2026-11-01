# Physical palettes and display effects

A driver can offer two output-stage display settings on top of whatever the application draws:

- a **physical palette**, which recolours the 16 attribute colours the driver presents (for example an amber
  monochrome monitor), and
- a **display effect**, which post-processes the finished frame (for example scanlines or a CRT curvature).

Both only change how the driver shows the screen. Logical attributes, `TColorAttr` values, the application palette
families (Color, BlackWhite, Monochrome, see [palette modes](palette-modes.md)), the color dialog, blink and intensity
are unchanged. The two layers compose: an application in the Monochrome family can be shown through the Amber
physical palette with scanlines, and switching either layer leaves the other alone. One display effect is active at a
time; selecting another effect replaces the current one.

## Support

Only `SDLGpuDriver` (package `TSharpVision.Drivers.SDL`) implements them. `SDLDriver`, the Console and Terminal
drivers and `NullDriver` do not: they ignore the configuration keys, and the runtime interfaces below are absent.
A driver decorator (`TSHARPVISION_DRIVER_DECORATOR`) hides both capabilities unless the decorator itself implements
and forwards the interfaces.

## Configuration

The `[graphics]` section, next to `fontName`, `fontSize` and `fontFit` (see [drivers](../drivers.md#fonts-and-font-fitting)),
selects the startup values:

```ini
[graphics]
palette=Amber
effect=Scanlines
```

- Ids are matched case-insensitively and surrounding whitespace is ignored (`palette= amber ` selects Amber).
  A missing or empty value means "not set", which is `Default`.
- An unknown id does not stop the application: the GPU driver writes a warning to standard error naming the value and
  the supported ids, and uses `Default`. This keeps a configuration written for a newer driver usable with an older one.
  (`fontFit`, by contrast, rejects unknown values.) `palette=VGA` and `palette=CGA`, accepted by earlier previews, are
  now unknown ids: the driver warns and uses `Default`, which is the classic VGA mapping.
- `effect=` takes a single id. Effects cannot be stacked (`effect=Glow,Scanlines` is an unknown id).
- `<AssemblyName>.cfg` overrides `~/.tsvision.cfg` per key, as for the other `[graphics]` keys.
- The values are read when the driver initializes. Selecting a palette or effect at run time (below) takes precedence
  and is kept across suspend and resume.
- In code, the values are the typed `GraphicsDriverConfiguration.Palette` and `GraphicsDriverConfiguration.Effect`
  properties (`string?`, trimmed, null when not set). Core does not validate them; the driver owns the set of ids.

## Physical palettes

Each palette is a fixed 16-entry table indexed by the attribute colour (0 = black ... 15 = white). The tables are
precomputed, so a palette costs nothing per frame.

| Id | Output |
|---|---|
| `Default` | The driver's unmodified output: the classic VGA 16-colour mapping, with brown at index 6. |
| `Green` | Green phosphor ramp, phosphor colour `#33FF33`; red, bright red, yellow and white are pale phosphor-green highlights (`#98FF98`, `#A0FFA0`, `#A8FFA8`, `#C8FFC8`) so accelerators stay visible beside black text. |
| `Amber` | Amber phosphor ramp, phosphor colour `#FFB000`; red, bright red, yellow and white are luminous cream highlights (`#FFCE6E`, `#FFD478`, `#FFDA84`, `#FFE49C`) so accelerators stay visible beside black text. |
| `Grayscale` | Neutral ramp, phosphor colour `#FFFFFF`. |

The three ramps compute each `Default` (classic VGA) entry's Rec.601 luma `Y = 0.299 R + 0.587 G + 0.114 B` (normalised to 0..1) and
draw it at `t = 0.12 + 0.88 * Y` of the phosphor colour. Index 0 stays exactly black; the 0.12 floor keeps dark blue
distinguishable from black. Brightness therefore follows the `Default` luma order, and different colours of similar luma look
alike, as on a real monochrome monitor. Use the BlackWhite or Monochrome family when the user interface must stay
readable without colour; the ramps are a presentation choice.

## Display effects

| Id | Output |
|---|---|
| `Default` | No post-processing: the frame is drawn exactly as without effect support. |
| `Scanlines` | Darkens every other horizontal band of device pixels across the terminal grid with 35 % black. The band thickness is `max(1, round(density))` device pixels, where density is the window's pixel density (2 on a 2x display), and the period is twice the thickness; the first band is never darkened. The bands are recomputed from the drawable height and density, so they stay pixel exact on HiDPI displays and after resizing. |
| `Glow` | Phosphor glow: a soft, bounded halo around bright text. |
| `Curvature` | CRT curvature: a bounded barrel warp of the grid image; the area outside the warped image is black. Mouse positions are remapped through the same warp, so clicks land on the cell drawn under the pointer. |
| `ShadowMask` | CRT shadow mask: a fine, neutral (luminance-only) mask pattern anchored to device pixels, so the Green, Amber and Grayscale palettes stay single-hue. |

Exactly one effect is active at a time; `effect=` and `SetDisplayEffect` take a single id.

`Scanlines` is drawn over the grid in the same pass, both on the GPU path and in the driver's CPU compositing fallback.
`Glow`, `Curvature` and `ShadowMask` are post-process effects of the SDL GPU shader path: the grid is drawn to an
offscreen texture and the effect's shader draws it to the window. When they cannot run, the driver shows the frame as
`Default` and writes one warning to standard error: on the CPU compositing fallback (used when the shader pipeline is
unavailable), and when the effect's GPU resources (shader, pipeline or offscreen texture) cannot be created. The active
id stays the selected one. `Default` creates no extra GPU resources.

Effect parameters are fixed by the driver:

| Effect | Parameters | GPU cost when selected |
|---|---|---|
| Scanlines | 35% black strips, thickness `max(1, round(density))` device px, period 2x thickness | one extra draw in the same render pass |
| Glow | brightness threshold smoothstep 0.5..0.9 (Rec.601 luma), strength 0.5, 33-tap single-pass blur, radius `clamp(2.5 x density, 1, 8)` px; pixels farther than the radius from a bright pixel are unchanged, nothing gets darker | grid drawn to an offscreen texture, plus one full-screen pass |
| Curvature | barrel warp `src = c * (1 + 0.05 * dot(c * aspect, c * aspect))` in centred coordinates; corners and a thin edge band (about 2% of the width) are black, every cell stays visible, 1 px antialiased edge, bilinear sampling | offscreen texture plus one full-screen pass |
| ShadowMask | neutral 3x2 staggered slot mask, mask cell `round(density)` device px, dark cells x0.8 (average 6.7% dimmer), no coloured subpixels, no resampling | offscreen texture plus one full-screen pass |

Default and absent `effect` add no pass, texture or per-frame allocation. With Curvature the mouse follows the warp: the
driver maps the pointer through the same formula the shader samples with, so a click lands on the cell drawn under it.

## Switching at run time

The capabilities are optional interfaces in `TSharpVision.Drivers`, discovered on the active driver, `TDisplay.driver`
(also reachable as `TScreen.driver`):

```csharp
using TSharpVision;
using TSharpVision.Drivers;

if (TDisplay.driver is IPhysicalPaletteDriver p)
    p.SetPhysicalPalette("Amber");               // or DisplayPresetNames.Amber

if (TDisplay.driver is IDisplayEffectDriver e)
{
    foreach (string id in e.DisplayEffects)      // canonical ids, "Default" first
        Console.Error.WriteLine(id + (id == e.DisplayEffect ? " (active)" : ""));
    e.SetDisplayEffect(DisplayPresetNames.Scanlines);
}
```

- `PhysicalPalettes` / `DisplayEffects` list the driver's canonical ids, with `DisplayPresetNames.Default` first;
  `PhysicalPalette` / `DisplayEffect` return the active canonical id.
- `Set*` matches case-insensitively and ignores surrounding whitespace. Null throws `ArgumentNullException`; an
  unknown id throws `ArgumentException` and leaves the active choice unchanged.
- The change is presented with the next frame; the application does not need to redraw. Call it on the UI thread.
- `DisplayPresetNames` holds the common ids: palettes `Default`, `Green`, `Amber`, `Grayscale`; effects `Default`,
  `Scanlines`, `Glow`, `Curvature`, `ShadowMask`.
  Ids are driver-defined, so list them from the driver instead of assuming the set.

## TVDemo

With `SDLGpuDriver`, **Options > Physical palette** and **Options > Display effect** list the driver's ids, with `(●)`
marking the active one; choosing an item applies it immediately and **Default** returns to the unmodified output.
The palette submenu lists Default, Green, Amber and Grayscale; the effect submenu lists Default, Scanlines, Glow,
Curvature and ShadowMask, of which one is active at a time.
They sit below **Options > Framework palette**, which keeps selecting the logical family. With any other driver, or a
decorator that does not forward the capabilities, the two submenus are not shown.

TVDemo does not persist these choices: **Window > Save desktop state** records only the framework palette family and
tables. Use `[graphics] palette=` and `effect=` in `TSharpVision.Samples.TVDemo.cfg` (or `~/.tsvision.cfg`) to start
with a preset.
