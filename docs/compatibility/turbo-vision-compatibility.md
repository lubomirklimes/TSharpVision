# Turbo Vision compatibility

Compatibility means retaining useful source concepts and behavior: class topology and names, historical key/command/event identities where implemented, character-cell geometry, owner/group routing, palettes, control-data transfer and subclass extension points. It does not promise that every original program can be translated without changes.

Managed references replace pointers, strings carry Unicode text, and graph streaming uses `System.IO.Stream`. Group control records use logical spans in `TDataRecord`. Framework ownership and teardown remain meaningful even though garbage collection manages object memory; deterministic resource disposal is a separate responsibility.

Some representations are deliberately wider: `MessageEvent.infoLong` is signed 64-bit, and editor positions use `uint`, with `uint.MaxValue` as `sfSearchFailed`. Modern drivers, Unicode text, release/modifier/wheel input, syntax-aware editing and byte-stream terminal sessions are intentional extensions rather than historical Borland features. The color-preview command `cmTryColors` is distinct from the historical `cmSaveColorIndex`.

C++ ABI compatibility, DOS object layout, pointer layout and automatic Borland binary-stream compatibility are explicit non-goals. Logical data offsets and preserved numeric identities do not imply binary interchangeability.
