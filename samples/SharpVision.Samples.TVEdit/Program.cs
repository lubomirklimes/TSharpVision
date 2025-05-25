// SharpVision.Samples.TVEdit — standalone editor sample using TEditorApp.
//
// Run:
//   dotnet run --project SharpVision.Samples.TVEdit
//
// Open files on startup:
//   dotnet run --project SharpVision.Samples.TVEdit -- file1.txt file2.txt
//
// Force a specific driver:
//   set SHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:
//   set SHARPVISION_DRIVER=NullDriver
//
// Config file (optional, looked up next to the executable):
//   TVEdit.cfg
using SharpVision;
using SharpVision.Config;
using SharpVision.Constants;
using SharpVision.Drivers;

// Load configuration before the driver is initialized.
var config = SharpVisionConfigurationLoader.Load();
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
