// TSharpVision.Samples.TVDemo
//
// General controls, classic accessories, games, help and resource persistence.
//
// Run on Windows:
//   dotnet run --project samples/TSharpVision.Samples.TVDemo
//
// Force a specific driver:
//   set TSHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:
//   set TSHARPVISION_DRIVER=NullDriver
//
// Config file (optional, looked up next to the executable):
//   TSharpVision.Samples.TVDemo.cfg
using TSharpVision;
using TSharpVision.Samples.TVDemo;

return TSharpVisionRuntime.Run(() => new TVDemoApp());

