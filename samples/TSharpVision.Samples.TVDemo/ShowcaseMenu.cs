using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

internal static class ShowcaseCmd
{
    public const ushort NewWindow = 320, SimpleDialog = 321, Controls = 322,
        History = 323, Resource = 324, Colors = 325, SaveDesktop = 326,
        LoadDesktop = 327, Tetris = 328, Selection = 329, Outline = 330,
        Popup = 331, CodeEditor = 332, HexView = 333, TableView = 334,
        Terminal = 335, ComboBox = 336, Progress = 337, PaletteGallery = 338,
        GroupBox = 339, CharacterMap = 350;

    public const ushort FileOpenDialog = 500, FileSaveDialog = 501, ChangeDirectory = 502,
        MessageBoxes = 503, InputBox = 504, ValidatedInput = 505, Memo = 506;
}

public partial class TVDemoApp
{
    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        var menu = new TMenuBar(r,
            new TSubMenu($"~{TSharpVisionGlyphs.SystemMenu}~", Keys.kbAltSpace) +
                new TMenuItem("~A~SCII table", TVDemoCmd.cmAsciiTable, Keys.kbNoKey) +
                new TMenuItem("~C~alculator", TVDemoCmd.cmCalculator, Keys.kbNoKey) +
                new TMenuItem("Ca~l~endar", TVDemoCmd.cmCalendar, Keys.kbNoKey) +
                new TMenuItem("Cloc~k~", TVDemoCmd.cmClock, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~P~uzzle", TVDemoCmd.cmPuzzle, Keys.kbNoKey) +
                new TMenuItem("~T~etris", ShowcaseCmd.Tetris, Keys.kbNoKey) +
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~N~ew demonstration window", ShowcaseCmd.NewWindow, Keys.kbF4, Views.hcNoContext, "F4") +
                new TMenuItem("Open file ~v~iewer...", TVDemoCmd.cmFileViewer, Keys.kbF3, Views.hcNoContext, "F3") +
                new TMenuItem("E~x~it", Views.cmQuit, Keys.kbAltX, Views.hcNoContext, "Alt+X") +
            new TSubMenu("~D~emo", Keys.kbAltD) +
                DemoSubmenu("~D~ialogs",
                    new TMenuItem("~S~imple modal dialog...", ShowcaseCmd.SimpleDialog, Keys.kbNoKey),
                    new TMenuItem("~O~pen file dialog...", ShowcaseCmd.FileOpenDialog, Keys.kbNoKey),
                    new TMenuItem("Sa~v~e file dialog...", ShowcaseCmd.FileSaveDialog, Keys.kbNoKey),
                    new TMenuItem("~C~hange directory...", ShowcaseCmd.ChangeDirectory, Keys.kbNoKey),
                    new TMenuItem("~M~essage boxes...", ShowcaseCmd.MessageBoxes, Keys.kbNoKey),
                    new TMenuItem("~I~nput box...", ShowcaseCmd.InputBox, Keys.kbNoKey),
                    TMenuItem.NewLine(),
                    new TMenuItem("~R~esource-loaded dialog...", ShowcaseCmd.Resource, Keys.kbNoKey)) +
                DemoSubmenu("~C~ontrols",
                    new TMenuItem("~C~ommon controls...", ShowcaseCmd.Controls, Keys.kbNoKey),
                    new TMenuItem("~M~ulti-state checkboxes...", ShowcaseCmd.Selection, Keys.kbNoKey),
                    new TMenuItem("Com~b~o boxes...", ShowcaseCmd.ComboBox, Keys.kbNoKey),
                    new TMenuItem("~P~rogress bars...", ShowcaseCmd.Progress, Keys.kbNoKey),
                    new TMenuItem("~G~roup boxes...", ShowcaseCmd.GroupBox, Keys.kbNoKey),
                    new TMenuItem("Input ~h~istory...", ShowcaseCmd.History, Keys.kbNoKey),
                    TMenuItem.NewLine(),
                    new TMenuItem("~V~alidated input...", ShowcaseCmd.ValidatedInput, Keys.kbNoKey),
                    new TMenuItem("Multi~l~ine memo...", ShowcaseCmd.Memo, Keys.kbNoKey),
                    TMenuItem.NewLine(),
                    new TMenuItem("P~a~lette inheritance gallery", ShowcaseCmd.PaletteGallery, Keys.kbNoKey),
                    new TMenuItem("Pop~u~p menu...", ShowcaseCmd.Popup, Keys.kbNoKey)) +
                DemoSubmenu("~V~iews and tools",
                    new TMenuItem("~O~utline...", ShowcaseCmd.Outline, Keys.kbNoKey),
                    new TMenuItem("Code ~e~ditor...", ShowcaseCmd.CodeEditor, Keys.kbNoKey),
                    new TMenuItem("He~x~ viewer...", ShowcaseCmd.HexView, Keys.kbNoKey),
                    new TMenuItem("~T~able viewer...", ShowcaseCmd.TableView, Keys.kbNoKey),
                    new TMenuItem("Ter~m~inal...", ShowcaseCmd.Terminal, Keys.kbNoKey)) +
            new TSubMenu("D~i~agnostics", Keys.kbAltI) +
                new TMenuItem("~C~haracter Map...", ShowcaseCmd.CharacterMap, Keys.kbNoKey) +
                new TMenuItem("~K~eyboard...", TVDemoCmd.cmKeyboardDlg, Keys.kbNoKey) +
                new TMenuItem("~F~unction keys...", TVDemoCmd.cmFunctionKeysDlg, Keys.kbNoKey) +
                new TMenuItem("~M~ouse...", TVDemoCmd.cmMouseDlg, Keys.kbNoKey) +
                new TMenuItem("~H~eap / memory", TVDemoCmd.cmHeap, Keys.kbNoKey) +
            new TSubMenu("~O~ptions", Keys.kbAltO) +
                new TMenuItem("UI ~c~olors...", ShowcaseCmd.Colors, Keys.kbNoKey) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext", Views.cmNext, Keys.kbF6, Views.hcNoContext, "F6") +
                new TMenuItem("~P~revious", Views.cmPrev, Keys.kbShiftF6, Views.hcNoContext, "Shift+F6") +
                new TMenuItem("~Z~oom", Views.cmZoom, Keys.kbF5, Views.hcNoContext, "F5") +
                new TMenuItem("~R~esize / move", Views.cmResize, Keys.kbCtrlF5, Views.hcNoContext, "Ctrl+F5") +
                new TMenuItem("~C~ascade", Views.cmCascade, Keys.kbNoKey) +
                new TMenuItem("~T~ile", Views.cmTile, Keys.kbNoKey) +
                new TMenuItem("C~l~ose", Views.cmClose, Keys.kbAltF3, Views.hcNoContext, "Alt+F3") +
                TMenuItem.NewLine() +
                new TMenuItem("~S~ave desktop state", ShowcaseCmd.SaveDesktop, Keys.kbNoKey) +
                new TMenuItem("L~o~ad desktop state", ShowcaseCmd.LoadDesktop, Keys.kbNoKey) +
            new TSubMenu("~H~elp", Keys.kbAltH) +
                new TMenuItem("~I~ndex", Views.cmHelpIndex, Keys.kbNoKey) +
                new TMenuItem("~A~bout", TVDemoCmd.cmAbout, Keys.kbNoKey));
        menu.helpCtx = DemoHelpCtx.MainMenu;
        return menu;
    }

    private static TMenuItem DemoSubmenu(string caption, params TMenuItem[] items)
    {
        for (int i = 0; i + 1 < items.Length; i++) items[i].Next = items[i + 1];
        return new TMenuItem(caption, Keys.kbNoKey, new TMenu(items[0]));
    }

    private bool HandleShowcaseCommand(ushort command)
    {
        switch (command)
        {
            case ShowcaseCmd.FileOpenDialog: OpenFileDialogDemo(); break;
            case ShowcaseCmd.FileSaveDialog: OpenFileSaveDialogDemo(); break;
            case ShowcaseCmd.ChangeDirectory: OpenChangeDirectoryDemo(); break;
            case ShowcaseCmd.MessageBoxes: OpenMessageBoxDemo(); break;
            case ShowcaseCmd.InputBox: OpenInputBoxDemo(); break;
            case ShowcaseCmd.ValidatedInput: OpenValidatedInputDemo(); break;
            case ShowcaseCmd.Memo: OpenMemoDemo(); break;
            case ShowcaseCmd.CharacterMap: OpenCharacterMap(); break;
            case ShowcaseCmd.NewWindow: OpenNewWindow(); break;
            case ShowcaseCmd.SimpleDialog: ShowSampleDialog(); break;
            case ShowcaseCmd.Controls: OpenControlsShowcaseDialog(); break;
            case ShowcaseCmd.Selection: OpenSelectionDemo(); break;
            case ShowcaseCmd.Outline: OpenOutlineDemo(); break;
            case ShowcaseCmd.Popup: OpenPopupDemo(); break;
            case ShowcaseCmd.CodeEditor: OpenCodeEditorDemo(); break;
            case ShowcaseCmd.HexView: OpenHexViewDemo(); break;
            case ShowcaseCmd.TableView: OpenTableViewDemo(); break;
            case ShowcaseCmd.Terminal: OpenTerminalDemo(); break;
            case ShowcaseCmd.History: OpenHistoryDemoDialog(); break;
            case ShowcaseCmd.ComboBox: OpenComboBoxDemoDialog(); break;
            case ShowcaseCmd.Progress: OpenProgressDemoDialog(); break;
            case ShowcaseCmd.GroupBox: OpenGroupBoxDemo(); break;
            case ShowcaseCmd.PaletteGallery: OpenPaletteGallery(); break;
            case ShowcaseCmd.Resource: LoadResourceDialog(); break;
            case ShowcaseCmd.Colors: OpenColorDialog(); break;
            case ShowcaseCmd.SaveDesktop: SaveDesktopState(); break;
            case ShowcaseCmd.LoadDesktop: LoadDesktopState(); break;
            case ShowcaseCmd.Tetris: OpenTetris(); break;
            case Views.cmTile when DeskTop is TDeskTop tileDesktop: tileDesktop.Tile(tileDesktop.GetExtent()); break;
            case Views.cmCascade when DeskTop is TDeskTop cascadeDesktop: cascadeDesktop.Cascade(cascadeDesktop.GetExtent()); break;
            default: return false;
        }
        return true;
    }
}
