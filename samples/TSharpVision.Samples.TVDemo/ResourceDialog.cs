using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    internal static void CompactLegacyResourceDialog(TDialog dialog)
    {
        // Keep the persisted resource unchanged; only recognize the old sample geometry.
        if (dialog.title != "Resource Dialog" || dialog.size != new TPoint(56, 13)) return;
        dialog.ForEachView(view =>
        {
            if (view is TButton && view.origin.y == 10) view.MoveTo(view.origin.x, 8);
        });
        dialog.ChangeBounds(new TRect(0, 0, 56, 12));
    }

    internal static TDialog BuildResourceSampleDialog()
    {
        var dlg = new TDialog(new TRect(0, 0, 56, 12), "Resource Dialog");

        var st = new TStaticText(new TRect(2, 2, 52, 5),
            "This dialog was loaded from a TResourceFile.\n" +
            "The resource was generated at runtime by\n" +
            "TSharpVision and persisted to disk.");
        dlg.Insert(st);

        var il = new TInputLine(new TRect(10, 6, 50, 7), 40);
        il.Data = "type something here";
        dlg.Insert(il);
        dlg.Insert(new TLabel(new TRect(2, 6, 10, 7), "~I~nput:", il));

        dlg.Insert(new TButton(new TRect(14, 8, 26, 10), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(28, 8, 40, 10), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        DemoButtonLayout.CenterRows(dlg);
        dlg.SelectNext(false);
        return dlg;
    }
}

