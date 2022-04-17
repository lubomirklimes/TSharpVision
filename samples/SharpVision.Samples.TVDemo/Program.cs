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
using SharpVision;
using SharpVision.Constants;
using SharpVision.Samples.TVDemo;

// StreamableRegistration ensures all streamable types are available if
// any stream/resource code path is exercised (defensive, same pattern as Demo01).
StreamableRegistration.RegisterAll();

// Install the Win32 OS clipboard service on Windows.
if (OperatingSystem.IsWindows())
    ClipboardService.Current = new SharpVision.Drivers.Console.Win32ClipboardService();

var app = new TVDemoApp();
return AppLifecycleGuard.Run(app);
