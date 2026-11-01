using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private void OpenColorDialog()
    {
        if (DeskTop == null) return;

        // Clone the live TProgram palette so TColorDialog edits a copy.
        // TColorItem indices are 1-based positions in TProgram.cpColor:
        //   1           → TBackground (desktop fill)
        //   2, 4        → TMenuView normal / selected item
        //   8, 9        → TWindow (blue) frame / title
        //   32, 34      → TDialog frame / title
        //   41, 45      → TButton normal / default
        var pal = GetPalette().Clone();

        var groups =
            new TColorGroup("Desktop",
                new TColorItem("Background", 1)) +
            new TColorGroup("Menu",
                new TColorItem("Normal",    2) +
                new TColorItem("Selected",  4)) +
            new TColorGroup("Window",
                new TColorItem("Frame",  8) +
                new TColorItem("Title",  9)) +
            new TColorGroup("Dialog",
                new TColorItem("Frame", 32) +
                new TColorItem("Title", 34)) +
            new TColorGroup("Button",
                new TColorItem("Normal",  41) +
                new TColorItem("Default", 45));

        var dlg = new TColorDialog(pal, groups);
        if (ValidView(dlg) != null)
        {
            dlg.helpCtx = DemoHelpCtx.ColorDialog;
            // Try previews on the live palette of the family active now; OK keeps the edits, and Cancel, Escape or
            // the close box restore the palette as it was when the dialog opened. Redraw also recomposes buffers.
            dlg.BindLivePalette(GetPalette(), Redraw);
            DeskTop.ExecView(dlg);
        }
    }
}

