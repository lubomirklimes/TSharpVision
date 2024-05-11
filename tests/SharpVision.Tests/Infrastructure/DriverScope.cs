using SharpVision;
using SharpVision.Drivers;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Initializes a headless NullDriver and installs it as TDisplay.driver.
/// Restores TDisplay.driver to null on Dispose.
/// </summary>
public sealed class DriverScope : IDisposable
{
    private readonly IDriver _previous;
    public NullDriver Driver { get; }

    public DriverScope() : this(80, 25) { }

    public DriverScope(ushort cols, ushort rows)
    {
        _previous = TDisplay.driver;
        Driver = new NullDriver(cols, rows);
        Driver.Initialize();
        TDisplay.driver = Driver;
        // Allocate a minimal screen buffer so views that probe TScreen survive.
        TScreen.ScreenBuffer = Driver.AllocateScreenBuffer();
    }

    public void Dispose()
    {
        TDisplay.driver = _previous;
    }
}
