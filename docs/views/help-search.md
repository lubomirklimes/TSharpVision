# Help collections and search

A program can show help from one compiled `.hlp` file (`GetHelpFile`) or from several at once
(`GetHelpCollection`). Either way the help window can search the text of the topics. The V1 and V2 `.hlp` formats and
`svhc` are unchanged; existing help files work as they are.

## Several help files

```csharp
private readonly THelpCollection _help = new();

public MyApp()
{
    _help.Open(Path.Combine(AppContext.BaseDirectory, "Help", "app.hlp"));
    _help.Open(Path.Combine(AppContext.BaseDirectory, "Help", "plugin.hlp"), "Plugin");
}

public override THelpCollection? GetHelpCollection() => _help;

public override void ShutDown()
{
    _help.Dispose();
    base.ShutDown();
}
```

- `Open(path, name)` opens a file read-only and registers it after the others. A missing file throws
  `FileNotFoundException`; a file that is not compiled help throws `InvalidDataException`. The name tells files apart
  in search results and defaults to the file name.
- `Add(file, name)` registers a `THelpFile` you opened yourself, for example a V1 file in a legacy encoding; you keep
  its stream.
- `Remove(file)` unregisters a file and closes it if the collection opened it. `Clear()` and `Dispose()` do that for
  all files.
- **Context collisions:** F1 help, `GetTopic` and `FindContext` use the **first registered** file that defines the
  context. Removing that file lets the next one answer.
- Links inside a topic lead to topics of the same file, as before.
- When `GetHelpCollection` returns null or an empty collection, F1 uses `GetHelpFile` exactly as before.

## Searching

In a help window press **Alt+S** (or send `Views.cmHelpSearch`). The window asks for words, lists the matching topics
as *title [source]*, and **Enter**, **Open** or a double-click opens one. **Backspace** returns to the topic shown
before, as after following a link. The prompt offers the previous words. Sending `cmHelpSearch` to the program opens
the help window first when none is open, so a menu item such as TVDemo's **Help > Search...** works from anywhere.

Matching rules:

- Every word of the query must occur in the topic, in any order. A word also matches inside a longer word
  (`print` finds *printer*).
- Case is ignored with culture-invariant Unicode simple case mapping: `ČTENÍ` finds *čtení*. Diacritics are not
  ignored (`zlutoucky` does not find *žluťoučký*), and `ß` does not match `SS`.
- Characters have no special meaning: there are no operators, quotes, wildcards or regular expressions.
- The whole topic text is searched, not only titles. A topic's title is its first non-blank line.
- Ranking: each occurrence in the title counts 8, each occurrence in the text counts 1, and the whole query found as a
  phrase adds 20. Equal scores keep registration order, then context number, so results are deterministic.

The first search after a file is registered reads its topics once and keeps a lower-cased copy of their text (about
two bytes per character of help text); later searches reuse it. Removing the file drops that copy. A damaged topic is
left out of the results instead of failing the search. Use the collection on the UI thread.

A program that still uses `GetHelpFile` gets the same search over its single file.
