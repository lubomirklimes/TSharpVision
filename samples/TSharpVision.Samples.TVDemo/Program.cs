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
using TSharpVision.Samples.TVDemo;

return TSharpVisionRuntime.Run(() => new TVDemoApp());
