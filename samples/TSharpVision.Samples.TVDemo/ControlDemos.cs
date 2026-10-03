using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private TRect DemoBounds(int width, int height)
    {
        int w = Math.Min(width, Math.Max(24, DeskTop!.size.x - 2));
        int h = Math.Min(height, Math.Max(8, DeskTop.size.y - 2));
        return new TRect(Math.Max(0, (DeskTop.size.x - w) / 2),
            Math.Max(0, (DeskTop.size.y - h) / 2),
            Math.Max(0, (DeskTop.size.x - w) / 2) + w,
            Math.Max(0, (DeskTop.size.y - h) / 2) + h);
    }

    private void InsertDemoWindow(TWindow window)
    {
        var valid = ValidView(window);
        if (valid != null) DeskTop!.Insert(valid);
    }

    private void OpenSelectionDemo()
    {
        if (DeskTop == null) return;
        var dlg = new TDialog(DemoBounds(54, 17), "Selection Controls");
        dlg.Insert(new TStaticText(new TRect(2, 1, 50, 3),
            "Cycle each item: [ ] off, [~] planned, [x] done."));
        var states = new TMultiCheckBoxes(new TRect(2, 4, 27, 8),
            new TSItem("~D~esign", new TSItem("~C~ode", new TSItem("~T~est", null))),
            3, TMultiCheckBoxes.cfTwoBits, " ~x");
        states.value = 0b10_01_00;
        dlg.Insert(states);
        dlg.Insert(new TStaticText(new TRect(30, 4, 50, 8),
            "Space cycles the focused\nitem through three states.\nArrow keys move focus."));
        dlg.Insert(new TButton(new TRect(20, 12, 32, 14), "~C~lose", Views.cmCancel,
            ButtonConstants.bfDefault));
        DemoButtonLayout.CenterRows(dlg);
        dlg.SelectNext(false);
        if (ValidView(dlg) != null) DeskTop.ExecView(dlg);
    }

    private void OpenOutlineDemo()
    {
        if (DeskTop == null) return;
        var win = new TWindow(DemoBounds(58, 18), "Outline", _nextWinNum++);
        int w = win.size.x, h = win.size.y;
        win.Insert(new TStaticText(new TRect(2, 1, w - 2, 2),
            "Arrows navigate; +, -, and * expand or collapse."));
        var scroll = new TScrollBar(new TRect(w - 2, 3, w - 1, h - 2));
        win.Insert(scroll);
        var samples = new TNode("Samples", new TNode("TVDemo", null, new TNode("TVTerm")), null);
        var extensions = new TNode("Extensions",
            new TNode("Code Editor", null, new TNode("Hex Viewer", null,
                new TNode("Table Viewer", null, new TNode("Terminal")))), samples);
        var windows = new TNode("Windows", new TNode("Frames", null, new TNode("Desktop")), null);
        var dialogs = new TNode("Dialogs", new TNode("Input", null, new TNode("Lists")), windows);
        var root = new TNode("TSharpVision", new TNode("Core", dialogs, extensions), null);
        var outline = new TOutline(new TRect(2, 3, w - 2, h - 2), null, scroll, root);
        win.Insert(outline);
        win.SelectNext(false);
        InsertDemoWindow(win);
    }

    private void OpenPopupDemo()
    {
        if (DeskTop == null) return;
        var info = new TMenuItem("~I~nfo", 336, Keys.kbNoKey);
        info.Append(new TMenuItem("~W~arning", 337, Keys.kbNoKey,
            Views.hcNoContext, null, new TMenuItem("~S~uccess", 338, Keys.kbNoKey)));
        var menu = new TMenu(info);
        var popup = new TMenuPopup(new TRect(4, 4, DeskTop.size.x, DeskTop.size.y), menu);
        ushort result = DeskTop.ExecView(popup);
        string? choice = result switch { 336 => "Info", 337 => "Warning", 338 => "Success", _ => null };
        if (choice != null)
            MsgBox.MessageBox(DeskTop, $"Popup choice: {choice}",
                MsgBox.mfInformation | MsgBox.mfOKButton);
    }
}
