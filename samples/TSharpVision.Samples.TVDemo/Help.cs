namespace TSharpVision.Samples.TVDemo;

internal static class DemoHelpCtx
{
    public const ushort MainMenu = 2, WelcomeWindow = 3, WindowManagement = 5,
        ControlsShowcase = 6, ColorDialog = 7, ResourceDialog = 8;
}

public partial class TVDemoApp
{
    private THelpFile? _helpFile;
    private Fpstream? _helpStream;

    public override THelpFile GetHelpFile()
    {
        if (_helpFile != null) return _helpFile;
        THelpFile.RegisterStreamableTypes();
        _helpStream = new Fpstream(Path.Combine(AppContext.BaseDirectory, "Help", "tvdemo.hlp"));
        _helpFile = new THelpFile(_helpStream);
        return _helpFile;
    }

    // -----------------------------------------------------------------------
    // OpenHelpIndex — Help|Index menu command. cmHelpIndex is only handled by
    // THelpViewer, so without this the menu item does nothing when no help
    // window is open. Reuses an already open help window when present.
    // -----------------------------------------------------------------------
    private void OpenHelpIndex()
    {
        if (DeskTop == null) return;
        var hf = GetHelpFile();
        if (hf == null) return;

        THelpWindow? existing = null;
        DeskTop.ForEachView(v => { if (v is THelpWindow hw) existing ??= hw; });
        if (existing != null)
        {
            THelpViewer? viewer = null;
            existing.ForEachView(v => { if (v is THelpViewer hv) viewer ??= hv; });
            existing.Select();
            if (viewer != null)
            {
                viewer.GoToIndex();
                viewer.DrawView();
            }
            return;
        }

        // THelpWindow's centering constructor places the window on the desktop centre.
        var window = new THelpWindow(hf, THelpViewer.IndexContext);
        if (ValidView(window) != null)
            ExecuteHelp(window);
    }

    public override void ShutDown()
    {
        _helpStream?.Close();
        _helpStream = null;
        _helpFile = null;
        base.ShutDown();
    }
}
