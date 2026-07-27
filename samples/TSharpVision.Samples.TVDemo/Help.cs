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

    public override void ShutDown()
    {
        _helpStream?.Close();
        _helpStream = null;
        _helpFile = null;
        base.ShutDown();
    }
}
