using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Console;
using TSharpVision.Drivers.SDL;

string driverName = args.ElementAtOrDefault(0) ?? "console";
string output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "idle-wait-result.json");
try
{
    if (driverName == "console")
    {
        Native.FreeConsole();
        if (!Native.AllocConsole()) throw new InvalidOperationException("AllocConsole failed");
        Native.ShowWindow(Native.GetConsoleWindow(), 0);
    }
    IDriver driver = driverName == "console" ? new Win32ConsoleDriver() : new SDLDriver();
    driver.Initialize();
    TDisplay.driver = driver;
    TScreen.SetCrtData();
    TScreen.ScreenBuffer = driver.AllocateScreenBuffer();
    TEventQueue.Resume();
    if (args.ElementAtOrDefault(2) == "validate")
    {
        try { Responsiveness.Run(driverName, output); }
        finally { driver.Shutdown(); }
        return;
    }
    var app = new Probe();
    var worker = Task.Run(async () =>
    {
        await Task.Delay(1000); // Warm-up is outside the measured idle sample.
        using var process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long passes = Interlocked.Read(ref app.Passes);
        var watch = Stopwatch.StartNew();
        await Task.Delay(15000);
        double elapsed = watch.Elapsed.TotalSeconds;
        double cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
        long samplePasses = Interlocked.Read(ref app.Passes) - passes;
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            driver = driverName, elapsedSeconds = elapsed, processCpuSeconds = cpuSeconds,
            oneCorePercent = cpuSeconds / elapsed * 100, eventPasses = samplePasses,
            passesPerSecond = samplePasses / elapsed,
            applicationDeadlineWakes = 0,
            nativeSafetyDeadline = driverName == "sdl" ? "native SDL safety deadline if supported by this build" : "none"
        }, new JsonSerializerOptions { WriteIndented = true }));
        var quit = new TEvent { What = Events.evCommand };
        quit.message.command = Views.cmQuit;
        TEventQueue.Enqueue(quit);
    });
    try { app.Run(); worker.GetAwaiter().GetResult(); }
    finally { app.ShutDown(); driver.Shutdown(); }
}
catch (Exception ex)
{
    File.WriteAllText(output, JsonSerializer.Serialize(new { driver = driverName, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = 1;
}
finally { if (driverName == "console") Native.FreeConsole(); }

sealed class Probe : TProgram
{
    public long Passes;
    public override void GetEvent(ref TEvent ev) { Interlocked.Increment(ref Passes); base.GetEvent(ref ev); }
}
static class Native
{
    [DllImport("kernel32.dll")] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll")] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll")] internal static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
}
