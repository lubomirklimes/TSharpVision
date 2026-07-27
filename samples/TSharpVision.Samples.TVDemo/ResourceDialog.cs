using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private static TDialog BuildResourceSampleDialog()
    {
        var dlg = new TDialog(new TRect(0, 0, 56, 13), "Resource Dialog");

        var st = new TStaticText(new TRect(2, 2, 52, 5),
            "This dialog was loaded from a TResourceFile.\n" +
            "The resource was generated at runtime by\n" +
            "TSharpVision and persisted to disk.");
        dlg.Insert(st);

        var il = new TInputLine(new TRect(10, 6, 50, 7), 40);
        il.Data = "type something here";
        dlg.Insert(il);
        dlg.Insert(new TLabel(new TRect(2, 6, 10, 7), "~I~nput:", il));

        dlg.Insert(new TButton(new TRect(14, 10, 26, 12), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(28, 10, 40, 12), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        return dlg;
    }
}

