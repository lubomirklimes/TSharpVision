# Unicode text and screen cells

TSharpVision lays text out by user-perceived character (extended grapheme cluster), not by UTF-16 code unit. Text models, offsets in the public API and stream formats stay UTF-16; only how many screen columns text occupies, and how it is placed in screen cells, follows the rules below.

## Clusters and widths

`TSharpVision.Text.TextLayout` segments text with the runtime's `StringInfo.GetNextTextElementLength` (UAX #29 with Unicode 16.0 data on .NET 10; the runtime does not implement rule GB9c, so an Indic conjunct such as `क्ष` is two clusters) and gives each cluster a width from a Unicode 16.0.0 table:

| Text | Columns |
|---|---|
| Latin, Czech, Greek, Cyrillic, box drawing, most symbols | 1 |
| East Asian Wide and Fullwidth (`漢字`, Hangul syllables) | 2 each |
| Emoji with emoji presentation (`😀`), a variation sequence with VS16 (`❤️`), emoji ZWJ sequences (`👨‍👩‍👧`), emoji modifier sequences (`👍🏽`), flags (`🇨🇿`), a lone regional indicator | 2 |
| A base with combining marks (`e` + U+0301) | the base's width |
| A character with VS15 (`❤︎`) | 1 |
| East Asian Ambiguous (`±`, `§`, `Ω`) | 1, or 2 with `TextLayoutOptions.AmbiguousIsWide` |
| Tab | to the next tab stop |
| Unpaired surrogate | 1, drawn as U+FFFD |

`TextLayout.Measure`, `Fit` (never splits a cluster), `ColumnAtOffset`, `OffsetAtColumn` (with a floor, ceiling or nearest bias inside a wide cluster) and `EnumerateClusters` (one span, or the two halves of a gap buffer) are allocation-free. `TextWidthProfile.CodePoint` sums per-code-point widths instead, as terminals that do not join sequences do.

## Screen cells

A `TScreenChar` is a character, an attribute and, for a cluster, its role:

* **Single**: an ordinary cell holding `Character`.
* **Lead**: the first cell of a cluster that needs more than one UTF-16 unit or more than one cell. `GetText` returns the whole cluster; `CellWidth` is the number of cells it covers; `Character` holds a fallback (the base character, or U+FFFD) for code that reads only `Character`.
* **Trail**: a following cell of the same cluster; `Character` is `'\0'`.

`TScreenChar.CreateCluster` and `CreateTrail` build such cells. Assigning `Character` directly turns a lead back into an ordinary cell. The cluster text of a lead is kept in a process-wide table that only grows (65,536 clusters; beyond that a cell draws U+FFFD), so cells kept in group buffers always resolve to the same text.

`TDrawBuffer.moveStr`, `moveCStr` and `moveBuf` write clusters (`moveBuf`'s count is the number of cells); a cluster that does not fit is replaced by blanks. `moveChar` and `putChar` write single cells. Writing over part of a cluster blanks the rest of it, and a view clipped by its owner or covered by another view never shows half of a wide character. Drivers report how faithfully they draw clusters through `IDriver.TextCapabilities`.

## Drivers

| Driver | Wide characters | Emoji and sequences |
|---|---|---|
| SDL (GPU and renderer) | drawn across their cells | shaped per cluster with one font chosen per cluster; colour emoji keep their colours. Windows fonts have no flag glyphs, so flags show their letters. |
| Console in Windows Terminal | correct | written as VT text: colour emoji, skin tones and ZWJ sequences |
| Console in conhost | correct (East Asian characters) | a single emoji in monochrome; other sequences show their base character |
| ANSI terminal | the cursor is placed after every wide or multi-character cluster, so a terminal that measures differently cannot shift the rest of the row | as the terminal draws them |

See [Drivers](../drivers.md) and [Environment variables](../environment-variables.md) for the font and output overrides.

## Compatibility switch

Setting the AppContext switch `TSharpVision.Text.LegacyCellWidth` to true restores one cell per UTF-16 code unit everywhere (writers, controls, editor columns, drivers), as in TSharpVision 1.2:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="TSharpVision.Text.LegacyCellWidth" Value="true" />
</ItemGroup>
```

or `AppContext.SetSwitch("TSharpVision.Text.LegacyCellWidth", true)` before any text is drawn. Editing still moves by cluster.

The Unicode data is distributed under the Unicode License v3; see [third-party notices](../../resources/licences/THIRD-PARTY-NOTICES.md).
