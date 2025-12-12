using System;
using System.IO;
using TSharpVision.Constants;

namespace TSharpVision;

// TEditorApp — reusable Turbo Vision-style application class for editor
// workflows.  Inherits TApplication and provides an editor-oriented menu
// bar, status line, and event handler.
public class TEditorApp : TApplication
{
    // Running editor-window counter (offset so windows do not stack exactly).
    private int _nextWinNum = 1;

    public TEditorApp() : base()
    {
        // Disable editor-specific commands at startup; they are re-enabled
        // by TEditor.UpdateCommands() when an editor window becomes active.
        DisableCommand(Views.cmSave);
        DisableCommand(Views.cmSaveAs);
        DisableCommand(Views.cmCut);
        DisableCommand(Views.cmCopy);
        DisableCommand(Views.cmPaste);
        DisableCommand(Views.cmClear);
        DisableCommand(Views.cmUndo);
        DisableCommand(Views.cmFind);
        DisableCommand(Views.cmReplace);
        DisableCommand(Views.cmSearchAgain);

        // Install the standard editor-dialog dispatcher.
        TEditorDialogHelper.Install(DeskTop);
    }


    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        return new TMenuBar(r,
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~N~ew",         Views.cmNew,    Keys.kbNoKey) +
                new TMenuItem("~O~pen...",     Views.cmOpen,   Keys.kbF3, Views.hcNoContext, "F3") +
                TMenuItem.NewLine() +
                new TMenuItem("~S~ave",        Views.cmSave,   Keys.kbF2,    Views.hcNoContext, "F2") +
                new TMenuItem("S~a~ve As...",  Views.cmSaveAs, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("E~x~it",        Views.cmQuit,   Keys.kbAltX, Views.hcNoContext, "Alt+X") +

            new TSubMenu("~E~dit", Keys.kbAltE) +
                new TMenuItem("~U~ndo",   Views.cmUndo,  Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("Cu~t~",    Views.cmCut,   Keys.kbShiftDel, Views.hcNoContext, "Shift+Del") +
                new TMenuItem("~C~opy",   Views.cmCopy,  Keys.kbCtrlIns,  Views.hcNoContext, "Ctrl+Ins") +
                new TMenuItem("~P~aste",  Views.cmPaste, Keys.kbShiftIns, Views.hcNoContext, "Shift+Ins") +
                TMenuItem.NewLine() +
                new TMenuItem("C~l~ear",  Views.cmClear, Keys.kbCtrlDel,  Views.hcNoContext, "Ctrl+Del") +

            new TSubMenu("~S~earch", Keys.kbAltS) +
                new TMenuItem("~F~ind...",      Views.cmFind,        Keys.kbNoKey) +
                new TMenuItem("~R~eplace...",   Views.cmReplace,     Keys.kbNoKey) +
                new TMenuItem("~A~gain",        Views.cmSearchAgain, Keys.kbNoKey) +

            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~S~ize/Move",  Views.cmResize,  Keys.kbCtrlF5, Views.hcNoContext, "Ctrl+F5") +
                new TMenuItem("~Z~oom",       Views.cmZoom,    Keys.kbF5,     Views.hcNoContext, "F5") +
                TMenuItem.NewLine() +
                new TMenuItem("~T~ile",       Views.cmTile,    Keys.kbNoKey) +
                new TMenuItem("C~a~scade",    Views.cmCascade, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~N~ext",       Views.cmNext,    Keys.kbF6,      Views.hcNoContext, "F6") +
                new TMenuItem("~P~revious",   Views.cmPrev,    Keys.kbShiftF6, Views.hcNoContext, "Shift+F6") +
                TMenuItem.NewLine() +
                new TMenuItem("~C~lose",      Views.cmClose,   Keys.kbAltF3,   Views.hcNoContext, "Alt+F3")
        );
    }

    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F10~ Menu",   Keys.kbF10,   Views.cmMenu) +
            new TStatusItem("~F2~ Save",    Keys.kbF2,    Views.cmSave) +
            new TStatusItem("~F3~ Open",    Keys.kbF3,    Views.cmOpen) +
            new TStatusItem("~Alt+X~ Exit", Keys.kbAltX,  Views.cmQuit) +
            new TStatusItem("~F5~ Zoom",    Keys.kbF5,    Views.cmZoom) +
            new TStatusItem("~F6~ Next",    Keys.kbF6,    Views.cmNext) +
            new TStatusItem(null, Keys.kbAltF3,   Views.cmClose) +
            new TStatusItem(null, Keys.kbCtrlF5,  Views.cmResize) +
            new TStatusItem(null, Keys.kbShiftF6, Views.cmPrev)
        );
    }


    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        if (ev.What != Events.evCommand) return;

        switch (ev.message.command)
        {
            case Views.cmNew:
                FileNew();
                ClearEvent(ref ev);
                break;

            case Views.cmOpen:
                FileOpen();
                ClearEvent(ref ev);
                break;

            case Views.cmTile:
                Tile();
                ClearEvent(ref ev);
                break;

            case Views.cmCascade:
                Cascade();
                ClearEvent(ref ev);
                break;
        }
    }


    public virtual TEditWindow OpenEditor(string fileName, bool visible)
    {
        if (DeskTop == null) return null;

        // Stagger windows so they do not stack exactly.
        int n = _nextWinNum++;
        int ox = (n % 6) * 2 + 2;
        int oy = (n % 4) * 2 + 1;
        var r = new TRect(ox, oy,
            Math.Min(ox + 72, DeskTop.size.x - 2),
            Math.Min(oy + 18, DeskTop.size.y - 2));

        // Enforce minimum TEditWindow size (24×6).
        if (r.b.x - r.a.x < TEditWindow.MinEditWinSize.x)
            r.b.x = r.a.x + TEditWindow.MinEditWinSize.x;
        if (r.b.y - r.a.y < TEditWindow.MinEditWinSize.y)
            r.b.y = r.a.y + TEditWindow.MinEditWinSize.y;

        var ew = new TEditWindow(r, fileName ?? string.Empty, n);
        var validated = (TEditWindow)ValidView(ew);
        if (validated == null) return null;

        if (!visible) validated.Hide();
        DeskTop.Insert(validated);
        return validated;
    }

    public virtual void FileNew() => OpenEditor(null, true);

    public virtual void FileOpen()
    {
        if (DeskTop == null) return;

        var dlg = new TFileDialog("*.*",
            TSharpVisionIntl.Get("File_Title_Open", "Open File"),
            "~N~ame", FileDialogOptions.fdOpenButton, 100);

        if (ValidView(dlg) == null) return;

        ushort result = DeskTop.ExecView(dlg);
        if (result == Views.cmOK)
        {
            dlg.GetData(out string chosen);
            if (!string.IsNullOrEmpty(chosen))
                OpenEditor(chosen, true);
        }
    }

    public virtual void Tile() => DeskTop?.Tile(DeskTop.GetExtent());

    public virtual void Cascade() => DeskTop?.Cascade(DeskTop.GetExtent());
}
