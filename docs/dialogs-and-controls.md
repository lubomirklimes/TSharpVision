# Dialogs and controls

Insert controls into a dialog, then run it on the application's desktop. Inserted views belong to the dialog's view hierarchy. These examples use core `TSharpVision` within an application started as in [Getting started](getting-started.md).

## A simple dialog

Call `NameDialog.Edit(DeskTop, "World")` from an application command handler. It returns the accepted text, or null on cancellation.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class NameDialog
{
    public static string? Edit(TGroup desktop, string initialName)
    {
        var dialog = new TDialog(new TRect(0, 0, 48, 9), "Name");
        dialog.MoveTo((desktop.size.x - 48) / 2, (desktop.size.y - 9) / 2);
        var input = new TInputLine(new TRect(10, 2, 44, 3), 40);
        input.SetData(initialName);
        dialog.Insert(input);
        dialog.Insert(new TLabel(new TRect(2, 2, 10, 3), "~N~ame:", input));
        dialog.Insert(new TButton(new TRect(9, 5, 21, 7), "~O~K",
            Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(new TButton(new TRect(25, 5, 39, 7), "~C~ancel",
            Views.cmCancel, ButtonConstants.bfNormal));
        dialog.SelectNext(false);

        try
        {
            return desktop.ExecView(dialog) == Views.cmOK ? input.Data : null;
        }
        finally
        {
            dialog.ShutDown();
        }
    }
}
```

Bounds are owner-relative character cells with exclusive right and bottom edges. The label's `~N~` mnemonic selects the linked input. Tab and Shift+Tab move between controls; `SelectNext(false)` gives the first one focus.

Buttons dispatch commands. `TDialog` ends modal execution for `Views.cmOK` and `Views.cmCancel`; Enter activates the default button and Escape cancels. Read values only after OK.

`ExecView` temporarily inserts an unowned dialog and detaches it on return. Read its controls before `finally` calls `ShutDown`. For group records, `TProgram.ExecuteDialog` handles execution, accepted data transfer and teardown together; see [lifecycle and ownership](architecture/lifecycle-and-ownership.md).

The next four examples return ready-to-run dialogs. Use `DeskTop.ExecView(dialog)` with the same `try`/`finally` to read controls afterwards, or `ExecuteDialog(dialog, null)` when you need no values. Invoke them from an active application's command handler.

## Check boxes and radio buttons

Check boxes represent independent choices; radio buttons select one option. Each `TSItem` links to the next label, ending with null.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class OutputDialog
{
    public static TDialog Create()
    {
        var dialog = new TDialog(new TRect(2, 2, 56, 12), "Output settings");
        var checks = new TCheckBoxes(new TRect(2, 3, 27, 5),
            new TSItem("~L~ine numbers", new TSItem("~W~rap lines", null)));
        checks.SetData((ushort)0b01);
        var mode = new TRadioButtons(new TRect(30, 3, 52, 6),
            new TSItem("~N~ormal", new TSItem("~S~ilent",
                new TSItem("~D~ebug", null))));
        mode.SetData((ushort)0);
        dialog.Insert(checks);
        dialog.Insert(mode);
        dialog.Insert(new TLabel(new TRect(2, 1, 27, 2), "Display:", checks));
        dialog.Insert(new TLabel(new TRect(30, 1, 52, 2), "Mode:", mode));
        dialog.Insert(new TButton(new TRect(20, 7, 34, 9), "~O~K",
            Views.cmOK, ButtonConstants.bfDefault));
        dialog.SelectNext(false);
        return dialog;
    }
}
```

Check-box bits follow item order; here line numbers start enabled. The radio value is a zero-based index: 0 means Normal. Arrow keys move within a cluster and Space activates an item.

Keep `checks` and `mode` references to read them after OK. Their `value` fields are `uint`; ordinary cluster `GetData(ref object)` returns a boxed `ushort`, which `SetData` accepts. See [group data transfer](architecture/data-transfer.md) for exchanging several values together.

## Multi-state check boxes

Use `TMultiCheckBoxes` when an option needs more than an on/off state. This checklist distinguishes pending, planned and done work.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class ChecklistDialog
{
    public static TDialog Create()
    {
        var dialog = new TDialog(new TRect(2, 2, 46, 12), "Release checklist");
        dialog.Insert(new TStaticText(new TRect(2, 1, 42, 3),
            "[ ] pending, [~] planned, [x] done"));
        var states = new TMultiCheckBoxes(new TRect(2, 4, 30, 7),
            new TSItem("~D~esign", new TSItem("~C~ode",
                new TSItem("~T~est", null))),
            3, TMultiCheckBoxes.cfTwoBits, " ~x");
        states.SetData(0b10_01_00u); // Design pending, Code planned, Test done.
        dialog.Insert(states);
        dialog.Insert(new TButton(new TRect(16, 8, 30, 10), "~O~K",
            Views.cmOK, ButtonConstants.bfDefault));
        dialog.SelectNext(false);
        return dialog;
    }
}
```

The marker string maps space to 0, `~` to 1 and `x` to 2. `cfTwoBits` packs two bits per item, starting at the lowest bits. `GetData` returns the packed `uint`; `SetData` accepts it.

Space cycles each item independently: 0 → 2 → 1 → 0. `MultiMark(index)` reads one item's state.

## Lists and scrolling

`TListBox` references a `TStringCollection`. Insert its scrollbar into the same dialog; the list updates its range.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class LanguageDialog
{
    public static TDialog Create()
    {
        var dialog = new TDialog(new TRect(2, 2, 36, 12), "Language");
        var scroll = new TScrollBar(new TRect(31, 2, 32, 7));
        var list = new TListBox(new TRect(2, 2, 31, 7), 1, scroll);
        var items = new TStringCollection();
        foreach (string name in new[] { "C#", "C++", "Pascal", "Python",
                                       "Delphi", "Rust", "Go", "Java" })
            items.Insert(name);
        list.NewList(items);
        dialog.Insert(scroll);
        dialog.Insert(list);
        dialog.Insert(new TButton(new TRect(10, 8, 24, 10), "~O~K",
            Views.cmOK, ButtonConstants.bfDefault));
        dialog.SelectNext(false);
        return dialog;
    }
}
```

Eight entries share five visible rows. Arrow and page keys navigate; the scrollbar reaches hidden entries. The collection preserves insertion order. Pass null for a list without a scrollbar.

After OK, read `list.focused` and `list.GetText(list.focused, 256)`. Call `NewList` again after changing the collection to refresh the range and reset focus. Direct data transfer uses `TListBoxRec`, pairing the collection with its selected index.

## Outline

Build sibling chains first, then attach them as children. This tree has one Project root, a Source child containing Program.cs, and a Docs sibling.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class ProjectDialog
{
    public static TDialog Create()
    {
        var dialog = new TDialog(new TRect(2, 2, 48, 14), "Project");
        var docs = new TNode("Docs");
        var source = new TNode("Source", new TNode("Program.cs"), docs);
        var root = new TNode("Project", source, null);
        var scroll = new TScrollBar(new TRect(43, 2, 44, 9));
        var outline = new TOutline(new TRect(2, 2, 43, 9), null, scroll, root);
        dialog.Insert(scroll);
        dialog.Insert(outline);
        dialog.Insert(new TButton(new TRect(17, 10, 31, 12), "~C~lose",
            Views.cmCancel, ButtonConstants.bfDefault));
        dialog.SelectNext(false);
        return dialog;
    }
}
```

The second node argument is its first child; the third is its next sibling. Nodes start expanded. Arrow and page keys navigate; `+` and `-` expand or collapse and `*` expands descendants. Optional horizontal and vertical scrollbars support larger trees. Call `outline.Update()` after changing links or expansion yourself. See [outline views](views/outline.md) for more behavior.

## Popup menu

Open a popup from an application command handler, as TVDemo does. It runs for one selection on the desktop, while the main menu bar remains part of the application layout.

```csharp
using TSharpVision;
using TSharpVision.Constants;

static class EditPopup
{
    public static ushort Choose(TGroup desktop)
    {
        var copy = new TMenuItem("~C~opy", Views.cmCopy, Keys.kbNoKey);
        copy.Append(new TMenuItem("~P~aste", Views.cmPaste, Keys.kbNoKey));
        var popup = new TMenuPopup(
            new TRect(4, 4, desktop.size.x, desktop.size.y), new TMenu(copy));
        try
        {
            return desktop.ExecView(popup);
        }
        finally
        {
            popup.ShutDown();
        }
    }
}
```

The popup sizes itself within the supplied bounds. `Choose(DeskTop)` returns a command, or 0 when dismissed; handle `Views.cmCopy` and `Views.cmPaste` in the caller. Entries follow enabled-command state: enable these commands when the focused editor supports them.
