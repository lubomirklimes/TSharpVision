# Editor architecture

`TEditor` owns the editing buffer, selection, cursor movement, commands, search, undo, scrolling and drawing. `TMemo` uses that engine for in-memory multi-line text. `TFileEditor` adds file loading, saving, encoding choices and file dialogs.

LF is the editor line separator. File loading and clipboard paste normalize CRLF and CR line endings to LF. A CR inserted directly into `TEditor` is an ordinary character and draws blank; it does not start a new line.

`TFileEditor` defaults to strict UTF-8 decoding with Latin-1 fallback for invalid UTF-8. Open options can require UTF-8 or select a registered legacy encoding, including CP437, CP852, Windows-1250, ISO-8859-2 and Kamenicky. Saving retains the file encoding and UTF-8 BOM where present, and converts internal LF separators to the selected save line endings. Loading records the detected line-ending style and chooses the dominant style for mixed input; a file with no separators uses the platform default. Unrepresentable legacy characters produce an error rather than silent replacement.

Saving an unnamed document invokes Save As. Successful saving clears the modified state; cancellation or a write/encoding error reports failure. Closing a modified file editor can prompt to save, discard or cancel.

Command customization belongs in `ConvertEvent`. Buffer allocation can be specialized through `InitBuffer`/`DoneBuffer`, and `InsertFrom` handles insertion of another editor's selected content, including clipboard routing. Overrides must preserve buffer and selection invariants. Positions use `uint`; `sfSearchFailed` is `uint.MaxValue`.

`TSharpVision.CodeEditor` builds on this same document model. `TCodeEditor` derives from `TFileEditor` and renders syntax with TextMate grammars and an active scope; `TCodeWindow` supplies the surrounding edit window. The protected virtual `DrawCodeLines` hook allows rendering customization while retaining selection, scrolling and clipping. The package does not supply a language server or a general language-service/plugin framework.
