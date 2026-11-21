# Third-party notices

TSharpVision itself is licensed under the MIT License (see `LICENSE`). This file lists third-party data that is
compiled into, or embedded in, the TSharpVision libraries and samples. NuGet package dependencies (for example
SDL3-CS and TextMateSharp) are not redistributed by this repository and are covered by their own package licences.

## Unicode Character Database 16.0.0 and Unicode CLDR 46

Source data from Unicode, Inc., used under the Unicode License v3 (SPDX: `Unicode-3.0`,
https://www.unicode.org/license.txt):

| Data | Version and source | Used for | Repository copy |
|---|---|---|---|
| `UnicodeData.txt` | UCD 16.0.0, https://www.unicode.org/Public/16.0.0/ucd/ | zero-width general categories (Mn, Me, Cf), character names | `tests/TSharpVision.Tests/Text/UnicodeData/16.0.0/` |
| `EastAsianWidth.txt` | UCD 16.0.0 | cell widths | same |
| `emoji-data.txt`, `emoji-variation-sequences.txt` | UCD 16.0.0, `ucd/emoji/` | emoji presentation and widths | same |
| `GraphemeBreakTest.txt` | UCD 16.0.0, `ucd/auxiliary/` | grapheme segmentation tests (test data only) | same |
| `NameAliases.txt`, `Blocks.txt` | UCD 16.0.0 | Character Browser names and block names | `tests/TSharpVision.Tests/Samples/UnicodeNames/` |
| `common/annotations/en.xml` | CLDR 46 (`release-46` tag of unicode-org/cldr) | Character Browser short names and keywords | `tests/TSharpVision.Tests/Samples/UnicodeNames/cldr-46-annotations-en.xml` |

The data is pinned by SHA-256 in `UnicodeWidthTableGenerator` and `CharacterNameDataGenerator` (tests project), and
compiled into:

* `src/TSharpVision/Text/UnicodeWidthData.g.cs` (cell-width property table in the `TSharpVision` library);
* `samples/TSharpVision.Samples.TVDemo/Unicode/CharacterNames.txt.gz` (TVDemo's embedded character-name database).

The licence text below is copied from `tests/TSharpVision.Tests/Text/UnicodeData/16.0.0/LICENSE-UNICODE.txt`.
The CLDR copy (`tests/TSharpVision.Tests/Samples/UnicodeNames/LICENSE-CLDR.txt`) carries the same terms with the
notice "Copyright © 2004-2024 Unicode, Inc." for CLDR.

```text
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```
