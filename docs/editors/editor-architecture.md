# Editor architecture

`TEditor` owns the editing buffer, selection, cursor movement, commands, search, undo, scrolling and drawing. `TMemo` uses that engine for in-memory multi-line text. `TFileEditor` adds file loading, saving, encoding choices and file dialogs.

LF is the editor line separator. File loading and clipboard paste normalize CRLF and CR line endings to LF. A CR inserted directly into `TEditor` is an ordinary character and draws blank; it does not start a new line.

`TFileEditor` defaults to strict UTF-8 decoding with Latin-1 fallback for invalid UTF-8. Open options can require UTF-8 or select a registered legacy encoding, including CP437, CP852, Windows-1250, ISO-8859-2 and Kamenicky. Saving retains the file encoding and UTF-8 BOM where present, and converts internal LF separators to the selected save line endings. Loading records the detected line-ending style and chooses the dominant style for mixed input; a file with no separators uses the platform default. Unrepresentable legacy characters produce an error rather than silent replacement.

Saving an unnamed document invokes Save As. Successful saving clears the modified state; cancellation or a write/encoding error reports failure. Closing a modified file editor can prompt to save, discard or cancel.

The key map follows current editing conventions alongside the Ctrl+K / Ctrl+Q prefix commands. Shift with a movement key, or a Shift+click, extends the selection from its anchor, and a movement without Shift collapses it. Ctrl+Left / Ctrl+Right move by word, Ctrl+Home / Ctrl+End (and Ctrl+PgUp / Ctrl+PgDn) to the ends of the document, Ctrl+Backspace (`cmDelWordLeft`) and Ctrl+Delete (`cmDelWord`) delete a word, or the selection when there is one. Ctrl+C / Ctrl+X / Ctrl+V and Ctrl+Ins / Shift+Del / Shift+Ins are Copy, Cut and Paste. A key code carries one modifier, so the Shift of Ctrl+Shift+Left is read from `controlKeyState`. Undo is one level.

Command customization belongs in `ConvertEvent`. Buffer allocation can be specialized through `InitBuffer`/`DoneBuffer`, and `InsertFrom` handles insertion of another editor's selected content, including clipboard routing. Overrides must preserve buffer and selection invariants. Positions use `uint`; `sfSearchFailed` is `uint.MaxValue`.

`TSharpVision.CodeEditor` builds on this same document model. `TCodeEditor` derives from `TFileEditor` and renders syntax with TextMate grammars and an active scope; `TCodeWindow` supplies the surrounding edit window. The protected virtual `DrawCodeLines` hook allows rendering customization while retaining selection, scrolling and clipping. The package does not supply a language server. Applications can add languages of their own to the syntax service; see Custom languages below.

## Custom languages

`TSharpVision.CodeEditor` separates what a language *is* from how it is classified. `ISyntaxService` detects a document's `SyntaxLanguage`, lists the languages a person can choose (`GetLanguages()`) and hands out an `ISyntaxClassifier` for a language. The standard service, `TextMateSyntaxService`, also implements `ISyntaxLanguageRegistry`, so an application can add languages to it without replacing the service and without touching TSharpVision. A registered language sits in the same catalog as the built-in ones: it is listed, detected in the same order and classified through the same calls. `TCodeEditor` uses `TextMateSyntaxService.Default`; register there before opening editors for automatic detection and selection through `SetLanguage`. With a separate service instance, pass its classifier to `editor.SyntaxHighlighter.SetClassifier(service.CreateClassifier(language))`, or supply that service when constructing a standalone `EditorSyntaxHighlighter`. Enumerate the same service instance for a language chooser.

### Language metadata

A `SyntaxLanguageDefinition` describes the language. Nothing in it is specific to TextMate. Its collection inputs are validated and copied when registered; do not mutate them during registration. Later changes do not affect the catalog. `SyntaxLanguageInfo` copies its constructor collections into read-only snapshots, treats null collections as empty, and rejects null elements. Returned metadata cannot be modified through collection casts.

| Member | Meaning |
|---|---|
| `Language` | The identity: a stable `Id` and a display name. The `Id` is the key. |
| `Aliases` | Other names, for searching a list. |
| `FileExtensions` | Extensions, most characteristic first. The leading dot is optional; multi-part extensions (`.d.ts`) are allowed. Case-insensitive. |
| `FileNames` | Exact file names such as `Makefile`. Case-insensitive. |
| `FileNamePatterns` | Wildcards over the whole file name: `*` and `?`. |
| `FirstLinePattern` | A .NET regular expression tried against the first line (at most 512 characters, 50 ms) when the name decided nothing. |
| `Priority` | Precedence among equally specific claims. Built-in languages have 0, the default. |

### A language with its own classifier

Implement `ISyntaxClassifier` and register a factory for it. The classifier turns one line and the state the previous line ended with into `SyntaxSpan`s and the state for the next line; it holds no per-document state.

Call `RegisterLanguage(definition, classifierFactory)` on the service. For a complete classifier example, see [the 6502 consumer example](../../tests/TSharpVision.CodeEditor.Consumer.Tests/Mos6502.cs). The factory returns an `ISyntaxClassifier`; the definition supplies its stable language identity and detection metadata.

The factory runs at most once, the first time the language is needed, and its classifier is shared by every document of that language. The service calls it from one thread at a time. A line the classifier fails on (an exception, no result, spans outside the line or out of order) is drawn plain and keeps the state it started with, so a defect in a classifier never reaches a view that is drawing. A factory that throws or returns null leaves the language listed and detected but without highlighting; it is not asked again.

### A language with its own TextMate grammar

When a TextMate grammar exists, no classifier needs writing. Supply the grammar (a `.tmLanguage.json` document) as a stream or as text; it does not have to be a file and is never looked for on disk.

```csharp
using System;
using System.Linq;
using TSharpVision;
using TSharpVision.CodeEditor;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;

var service = TextMateSyntaxService.Default;
var language = new SyntaxLanguage("tiny", "Tiny");
service.RegisterTextMateLanguage(
    new SyntaxLanguageDefinition(language) { FileExtensions = new[] { ".tiny" } },
    """
    { "scopeName": "source.tiny", "patterns": [
      { "name": "keyword.control.tiny", "match": "\\blet\\b" }
    ] }
    """);

SyntaxLanguageInfo selected = service.GetLanguages().Single(info => info.Language.Id == "tiny");
SyntaxLanguage detected = service.DetectLanguage("example.tiny", null);
if (!selected.Language.Equals(detected)) throw new InvalidOperationException("Detection mismatch.");

var editor = new TCodeEditor(new TRect(0, 0, 80, 24), null, null, null, null);
try
{
    editor.SyntaxHighlighter.SetLanguage(selected.Language);
    if (editor.SyntaxHighlighter.Classifier is null) throw new InvalidOperationException("Missing classifier.");
}
finally
{
    editor.ShutDown();
}
```

The stream is read to its end during the call and left open; the caller disposes it. The grammar is identified by its own `scopeName`, which must be new to the service: a grammar cannot take the scope of a bundled one. It may `include` the scopes of bundled grammars. Injection grammars are not supported for registered languages. A grammar that cannot be read, declares no scope name or cannot be loaded is refused with `FormatException`, and nothing is registered.

### Detection

Registered and built-in languages are detected together, in one pass:

1. exact file name;
2. file-name pattern;
3. longest matching extension;
4. first-line pattern;
5. `#!` interpreter (built-in table);
6. a conservative look at the content (JSON, XML and YAML only);
7. otherwise Plain Text.

Detection never asks a question and always gives the same answer for the same input. There is no content-detection callback for registered languages: names, patterns and the first-line pattern cover detection, and an application can always set a language explicitly.

### Collisions

Several languages may claim the same name; `.asm` belongs to more than one assembly language. The winner is decided by:

1. **specificity** - an exact name beats a pattern, a pattern beats an extension, a longer extension beats a shorter one;
2. **`Priority`** - among equally specific claims the higher priority wins;
3. **registration order** - among equal priorities the language registered first wins, and the built-in languages are registered before any other.

So a language that claims `.asm` at the default priority does not displace the bundled x86 assembly; it does at `Priority = 1`. Every claimant stays in `GetLanguages()` and can be chosen explicitly.

### Identifiers

A language identifier is registered once. Registering an identifier that exists already (compared ignoring case), built-in or not, throws `InvalidOperationException` and changes nothing; there is no replace and no unregister. `SyntaxLanguage.PlainText` cannot be registered or redefined: it always means "no classifier". Malformed metadata (an empty name, a wildcard among the extensions, an invalid first-line pattern) throws `ArgumentException`.

### Lifecycle and ownership

Registrations belong to the service instance they were made on and last as long as it. There is no process-wide registry: `new TextMateSyntaxService()` starts with the built-in languages only, and two services never see each other's languages. `TextMateSyntaxService.Default` is the instance `TCodeEditor` uses, so registering there is how an application makes a language known everywhere the shared service is used.

Register languages at startup: create or take the service, register, then open documents. Registering later is allowed, but a document that is already open keeps the language it was given until the application detects or sets it again.

### Thread safety

Registration, detection, enumeration and classifier creation may be called from any thread, also at the same time. A registration replaces the service's catalog as a whole, so a concurrent reader sees it either without or with the new language. A list returned by `GetLanguages()` is immutable: it is the same instance until a language is registered, and a list taken earlier is not changed.
