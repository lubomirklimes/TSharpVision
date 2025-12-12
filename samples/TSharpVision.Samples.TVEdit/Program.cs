// TSharpVision.Samples.TVEdit — standalone editor sample using TEditorApp.
//
// Run:
//   dotnet run --project TSharpVision.Samples.TVEdit
//
// Open files on startup:
//   dotnet run --project TSharpVision.Samples.TVEdit -- file1.txt file2.txt
//
// Force a specific driver:
//   set TSHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:
//   set TSHARPVISION_DRIVER=NullDriver
//
// Config file (optional, looked up next to the executable):
//   TVEdit.cfg
using TSharpVision;
using TSharpVision.Config;
using TSharpVision.Constants;
using TSharpVision.Drivers;

// Load configuration before the driver is initialized.
var config = TSharpVisionConfigurationLoader.Load();
ScreenDriverFactory.ConfiguredDriverName   = config.DriverName;
ScreenDriverFactory.ConfiguredSdlFontName  = config.SdlFontName;

var app = new TVEditApp(args);
return AppLifecycleGuard.Run(app);

// ---------------------------------------------------------------------------
// TVEditApp — thin TEditorApp subclass.
// Adds startup file-open and an About message box.
// ---------------------------------------------------------------------------
internal sealed class TVEditApp : TEditorApp
{
    // Keep the command-line args available for post-construction use.
    private readonly string[] _startupFiles;

    public TVEditApp(string[] args) : base()
    {
        _startupFiles = args ?? Array.Empty<string>();

        // Open files passed on the command line.
        foreach (string path in _startupFiles)
        {
            if (!string.IsNullOrWhiteSpace(path))
                OpenEditor(path, true);
        }

        // If no files were given, start with one empty editor window so the
        // user has something to type in right away.
        if (_startupFiles.Length == 0)
            FileNew();
    }
}
