// SharpVision.Samples.TVDemo
//
// Implements: ASCII Table sample + Calculator sample.
//
// Run on Windows:
//   dotnet run --project SharpVision.Samples.TVDemo
//
// Force a specific driver:
//   set SHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:
//   set SHARPVISION_DRIVER=NullDriver
//
// Config file (optional, looked up next to the executable):
//   TVDemo.cfg
using SharpVision;
using SharpVision.Config;
using SharpVision.Constants;
using SharpVision.Drivers;
using SharpVision.Samples.TVDemo;

// StreamableRegistration ensures all streamable types are available if
// any stream/resource code path is exercised (defensive, same pattern as Demo01).
StreamableRegistration.RegisterAll();

// Load configuration before the driver is initialized.
var config = SharpVisionConfigurationLoader.Load();
ScreenDriverFactory.ConfiguredDriverName   = config.DriverName;
ScreenDriverFactory.ConfiguredSdlFontName  = config.SdlFontName;

var app = new TVDemoApp();
return AppLifecycleGuard.Run(app);
