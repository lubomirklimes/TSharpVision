// TSharpVision.Samples.TVDemo
//
// Implements: ASCII Table sample + Calculator sample.
//
// Run on Windows:
//   dotnet run --project TSharpVision.Samples.TVDemo
//
// Force a specific driver:
//   set TSHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:
//   set TSHARPVISION_DRIVER=NullDriver
//
// Config file (optional, looked up next to the executable):
//   TVDemo.cfg
using TSharpVision;
using TSharpVision.Config;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Samples.TVDemo;

// StreamableRegistration ensures all streamable types are available if
// any stream/resource code path is exercised (defensive, same pattern as Demo01).
StreamableRegistration.RegisterAll();

// Load configuration before the driver is initialized.
var config = TSharpVisionConfigurationLoader.Load();
ScreenDriverFactory.ConfiguredDriverName   = config.DriverName;
ScreenDriverFactory.ConfiguredSdlFontName  = config.SdlFontName;

var app = new TVDemoApp();
return AppLifecycleGuard.Run(app);
