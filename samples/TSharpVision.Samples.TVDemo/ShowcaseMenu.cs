using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

internal static class ShowcaseCmd
{
    public const ushort NewWindow = 320, SimpleDialog = 321, Controls = 322,
        History = 323, Resource = 324, Colors = 325, SaveDesktop = 326,
        LoadDesktop = 327, Tetris = 328;
}

public partial class TVDemoApp
{
    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        var menu = new TMenuBar(r,
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~N~ew demonstration window", ShowcaseCmd.NewWindow, Keys.kbF4) +
                new TMenuItem("Open file ~v~iewer...", TVDemoCmd.cmFileViewer, Keys.kbF3) +
                new TMenuItem("E~x~it", Views.cmQuit, Keys.kbAltX) +
            new TSubMenu("~D~emo", Keys.kbAltD) +
                new TMenuItem("~S~imple dialog...", ShowcaseCmd.SimpleDialog, Keys.kbNoKey) +
                new TMenuItem("~C~ontrols showcase...", ShowcaseCmd.Controls, Keys.kbNoKey) +
                new TMenuItem("Input ~h~istory...", ShowcaseCmd.History, Keys.kbNoKey) +
                new TMenuItem("~R~esource dialog...", ShowcaseCmd.Resource, Keys.kbNoKey) +
            new TSubMenu("~A~ccessories", Keys.kbAltA) +
                new TMenuItem("~A~SCII table", TVDemoCmd.cmAsciiTable, Keys.kbNoKey) +
                new TMenuItem("~C~alculator", TVDemoCmd.cmCalculator, Keys.kbNoKey) +
                new TMenuItem("Ca~l~endar", TVDemoCmd.cmCalendar, Keys.kbNoKey) +
                new TMenuItem("Cloc~k~", TVDemoCmd.cmClock, Keys.kbNoKey) +
            new TSubMenu("~G~ames", Keys.kbAltG) +
                new TMenuItem("~P~uzzle", TVDemoCmd.cmPuzzle, Keys.kbNoKey) +
                new TMenuItem("~T~etris", ShowcaseCmd.Tetris, Keys.kbNoKey) +
            new TSubMenu("D~i~agnostics", Keys.kbAltI) +
                new TMenuItem("~M~ouse dialog", TVDemoCmd.cmMouseDlg, Keys.kbNoKey) +
                new TMenuItem("~H~eap / memory", TVDemoCmd.cmHeap, Keys.kbNoKey) +
            new TSubMenu("~O~ptions", Keys.kbAltO) +
                new TMenuItem("UI ~c~olors...", ShowcaseCmd.Colors, Keys.kbNoKey) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext", Views.cmNext, Keys.kbF6) +
                new TMenuItem("~P~revious", Views.cmPrev, Keys.kbShiftF6) +
                new TMenuItem("~Z~oom", Views.cmZoom, Keys.kbF5) +
                new TMenuItem("~R~esize / move", Views.cmResize, Keys.kbCtrlF5) +
                new TMenuItem("~C~ascade", Views.cmCascade, Keys.kbNoKey) +
                new TMenuItem("~T~ile", Views.cmTile, Keys.kbNoKey) +
                new TMenuItem("C~l~ose", Views.cmClose, Keys.kbAltF3) +
                TMenuItem.NewLine() +
                new TMenuItem("~S~ave desktop state", ShowcaseCmd.SaveDesktop, Keys.kbNoKey) +
                new TMenuItem("L~o~ad desktop state", ShowcaseCmd.LoadDesktop, Keys.kbNoKey) +
            new TSubMenu("~H~elp", Keys.kbAltH) +
                new TMenuItem("~I~ndex", Views.cmHelpIndex, Keys.kbNoKey) +
                new TMenuItem("~A~bout", TVDemoCmd.cmAbout, Keys.kbNoKey));
        menu.helpCtx = DemoHelpCtx.MainMenu;
        return menu;
    }

    private bool HandleShowcaseCommand(ushort command)
    {
        switch (command)
        {
            case ShowcaseCmd.NewWindow: OpenNewWindow(); break;
            case ShowcaseCmd.SimpleDialog: ShowSampleDialog(); break;
            case ShowcaseCmd.Controls: OpenControlsShowcaseDialog(); break;
            case ShowcaseCmd.History: OpenHistoryDemoDialog(); break;
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
