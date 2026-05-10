using System;
using System.IO;
using System.Text;
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
            new TSubMenu(Loc("Menu_File", "~F~ile"), Keys.kbAltF) +
                new TMenuItem(Loc("Menu_New", "~N~ew"),         Views.cmNew,    Keys.kbNoKey) +
                new TMenuItem(Loc("Menu_Open", "~O~pen..."),     Views.cmOpen,   Keys.kbF3, Views.hcNoContext, "F3") +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Save", "~S~ave"),        Views.cmSave,   Keys.kbF2,    Views.hcNoContext, "F2") +
                new TMenuItem(Loc("Menu_SaveAs", "S~a~ve As..."),  Views.cmSaveAs, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Exit", "E~x~it"),        Views.cmQuit,   Keys.kbAltX, Views.hcNoContext, "Alt+X") +

            new TSubMenu(Loc("Menu_Edit", "~E~dit"), Keys.kbAltE) +
                new TMenuItem(Loc("Menu_Undo", "~U~ndo"),   Views.cmUndo,  Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Cut", "Cu~t~"),    Views.cmCut,   Keys.kbShiftDel, Views.hcNoContext, "Shift+Del") +
                new TMenuItem(Loc("Menu_Copy", "~C~opy"),   Views.cmCopy,  Keys.kbCtrlIns,  Views.hcNoContext, "Ctrl+Ins") +
                new TMenuItem(Loc("Menu_Paste", "~P~aste"),  Views.cmPaste, Keys.kbShiftIns, Views.hcNoContext, "Shift+Ins") +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Clear", "C~l~ear"),  Views.cmClear, Keys.kbCtrlDel,  Views.hcNoContext, "Ctrl+Del") +

            new TSubMenu(Loc("Menu_Search", "~S~earch"), Keys.kbAltS) +
                new TMenuItem(Loc("Menu_Find", "~F~ind..."),      Views.cmFind,        Keys.kbNoKey) +
                new TMenuItem(Loc("Menu_Replace", "~R~eplace..."),   Views.cmReplace,     Keys.kbNoKey) +
                new TMenuItem(Loc("Menu_Again", "~A~gain"),        Views.cmSearchAgain, Keys.kbNoKey) +

            new TSubMenu(Loc("Menu_Window", "~W~indow"), Keys.kbAltW) +
                new TMenuItem(Loc("Menu_SizeMove", "~S~ize/Move"),  Views.cmResize,  Keys.kbCtrlF5, Views.hcNoContext, "Ctrl+F5") +
                new TMenuItem(Loc("Menu_Zoom", "~Z~oom"),       Views.cmZoom,    Keys.kbF5,     Views.hcNoContext, "F5") +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Tile", "~T~ile"),       Views.cmTile,    Keys.kbNoKey) +
                new TMenuItem(Loc("Menu_Cascade", "C~a~scade"),    Views.cmCascade, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Next", "~N~ext"),       Views.cmNext,    Keys.kbF6,      Views.hcNoContext, "F6") +
                new TMenuItem(Loc("Menu_Previous", "~P~revious"),   Views.cmPrev,    Keys.kbShiftF6, Views.hcNoContext, "Shift+F6") +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("Menu_Close", "~C~lose"),      Views.cmClose,   Keys.kbAltF3,   Views.hcNoContext, "Alt+F3")
        );
    }

    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem(Loc("Status_F10_Menu", "~F10~ Menu"),   Keys.kbF10,   Views.cmMenu) +
            new TStatusItem(Loc("Status_F2_Save", "~F2~ Save"),    Keys.kbF2,    Views.cmSave) +
            new TStatusItem(Loc("Status_F3_Open", "~F3~ Open"),    Keys.kbF3,    Views.cmOpen) +
            new TStatusItem(Loc("Status_AltX_Exit", "~Alt+X~ Exit"), Keys.kbAltX,  Views.cmQuit) +
            new TStatusItem(Loc("Status_F5_Zoom", "~F5~ Zoom"),    Keys.kbF5,    Views.cmZoom) +
            new TStatusItem(Loc("Status_F6_Next", "~F6~ Next"),    Keys.kbF6,    Views.cmNext) +
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
        => OpenEditor(fileName, visible, null);

    public virtual TEditWindow OpenEditor(
        string fileName,
        bool visible,
        TFileEditorOpenOptions openOptions)
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

        TEditWindow ew;
        try
        {
            ew = new TEditWindow(r, fileName ?? string.Empty, n, openOptions);
        }
        catch (DecoderFallbackException)
        {
            MsgBox.MessageBox(
                DeskTop,
                Loc("File_Err_EncodingDecode", "The file could not be decoded using the selected encoding."),
                MsgBox.mfError | MsgBox.mfOKButton);
            return null;
        }

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
            Loc("File_Title_Open", "Open File"),
            Loc("File_Label_Name", "~N~ame"),
            (ushort)(FileDialogOptions.fdOpenButton
                | FileDialogOptions.fdEncodingSelector),
            100);

        if (ValidView(dlg) == null) return;

        ushort result = DeskTop.ExecView(dlg);
        if (result == Views.cmOK)
        {
            dlg.GetData(out string chosen);
            if (!string.IsNullOrEmpty(chosen))
            {
                OpenEditor(
                    chosen,
                    true,
                    new TFileEditorOpenOptions
                    {
                        Encoding = dlg.SelectedEncoding,
                    });
            }
        }
    }

    public virtual void Tile() => DeskTop?.Tile(DeskTop.GetExtent());

    public virtual void Cascade() => DeskTop?.Cascade(DeskTop.GetExtent());
}
