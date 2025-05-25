using SharpVision.Config;
using SharpVision.Constants;
using SharpVision.Drivers;

namespace SharpVision.Demo01;

public class TVDemo : TApplication
{
    public TVDemo()
        : base()
    {
        TWindow window = new TWindow(new TRect(1, 1, 60, 24), "SharpVision Demo", 0);
        //{
        //    Options = Views.ofCentered | Views.ofPreProcess,
        //    HelpCtx = Views.hcNoContext
        //};
        DeskTop.Insert(window);
    }

    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;

        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F1~ Help", Keys.kbF1, Views.cmHelp) +
            new TStatusItem("~F2~ Menu", Keys.kbF2, Views.cmMenu) +
            new TStatusItem("~F3~ View", Keys.kbF3, Views.cmMenu) +
            new TStatusItem("~F4~ Edit", Keys.kbF4, Views.cmMenu) +
            new TStatusItem("~F5~ Copy", Keys.kbF5, Views.cmMenu) +
            new TStatusItem("~F6~ Move", Keys.kbF6, Views.cmMenu) +
            new TStatusItem("~F7~ New Folder", Keys.kbF7, Views.cmMenu) +
            new TStatusItem("~F8~ Delete", Keys.kbF8, Views.cmMenu) +
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, Views.cmQuit) +
            new TStatusItem(null, Keys.kbF10, Views.cmMenu) +
            new TStatusItem(null, Keys.kbAltF3, Views.cmClose) +
            new TStatusItem(null, Keys.kbF5, Views.cmZoom) +
            new TStatusItem(null, Keys.kbCtrlF5, Views.cmResize)
            );
    }

    public static class MyCommands 
    {
        public static ushort cmMyFileOpen = 1000;
        public static ushort cmMyNewWin = 1001;
    }

    public override TMenuBar InitMenuBar(TRect r)
    {
        /*
    r.b.y = r.a.y + 1;    // set bottom line 1 line below top line
    return new TMenuBar( r,
        *new TSubMenu( "~F~ile", kbAltF )+
            *new TMenuItem( "~O~pen", cmMyFileOpen, kbF3, hcNoContext, "F3" )+
            *new TMenuItem( "~N~ew",  cmMyNewWin,   kbF4, hcNoContext, "F4" )+
            newLine()+
            *new TMenuItem( "E~x~it", cmQuit, cmQuit, hcNoContext, "Alt-X" )+
        *new TSubMenu( "~W~indow", kbAltW )+
            *new TMenuItem( "~N~ext", cmNext,     kbF6, hcNoContext, "F6" )+
            *new TMenuItem( "~Z~oom", cmZoom,     kbF5, hcNoContext, "F5" )
        );         
         */

        r.b.y = r.a.y + 1;

        // \360
        //TSubMenu sub1 = new TSubMenu("~≡~", 0, 0 /*Views.hcSystem*/) +
        //    new TMenuItem("~A~bout...", Views.cmCancel, Keys.kbNoKey, 0 /*hcSAbout*/);

        //return (new TMenuBar(r, sub1));

        //return new TMenuBar(r,
        //    new TSubMenu("~≡~", Keys.kbAltF) +
        //        new TMenuItem("~O~pen", MyCommands.cmMyFileOpen, Keys.kbF3, Views.hcNoContext, "F3") +
        //        new TMenuItem("~N~ew", MyCommands.cmMyNewWin, Keys.kbF4, Views.hcNoContext, "F4") +
        //        //TMenuItem.NewLine() +
        //        new TMenuItem("E~x~it", Views.cmQuit, Views.cmQuit, Views.hcNoContext, "Alt-X")
        //    );

        return new TMenuBar(r,
            new TSubMenu("~≡~", Keys.kbAltF) +
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~O~pen", MyCommands.cmMyFileOpen, Keys.kbF3, Views.hcNoContext, "F3") +
                new TMenuItem("~N~ew", MyCommands.cmMyNewWin, Keys.kbF4, Views.hcNoContext, "F4") +
                TMenuItem.NewLine() +
                new TMenuItem("E~x~it", Views.cmQuit, Views.cmQuit, Views.hcNoContext, "Alt-X") +
            new TSubMenu("~E~dit", Keys.kbAltF) +
            new TSubMenu("~S~earch", Keys.kbAltF) +
            new TSubMenu("~R~un", Keys.kbAltF) +
            new TSubMenu("~C~ompile", Keys.kbAltF) +
            new TSubMenu("~D~ebug", Keys.kbAltF) +
            new TSubMenu("~P~roject", Keys.kbAltF) +
            new TSubMenu("~O~ptions", Keys.kbAltF) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext", Views.cmNext, Keys.kbF6, Views.hcNoContext, "F6") +
                new TMenuItem("~Z~oom", Views.cmZoom, Keys.kbF5, Views.hcNoContext, "F5") +
            new TSubMenu("~H~elp", Keys.kbAltW)
            );
    }

    public override TDeskTop InitDesktop(TRect r)
    {
        return base.InitDesktop(r);
    }

    //    static TStatusLine* initStatusLine(TRect r);
    //    static TMenuBar* initMenuBar(TRect r);
    //    virtual void handleEvent(TEvent& Event);
    //    virtual void getEvent(TEvent& event);
    ////    virtual TPalette& getPalette() const;
    //    virtual void idle();              // Updates heap and clock views
}

internal class Program
{
    static void Main(string[] args)
    {
        // StreamableRegistration ensures all streamable types are available if
        // any stream/resource code path is exercised (defensive, same pattern as Demo01).
        StreamableRegistration.RegisterAll();

        // Load configuration before the driver is initialized.
        var config = SharpVisionConfigurationLoader.Load();
        ScreenDriverFactory.ConfiguredDriverName = config.DriverName;
        ScreenDriverFactory.ConfiguredSdlFontName = config.SdlFontName;

        TVDemo demo = new TVDemo();
        demo.Run();
    }
}
