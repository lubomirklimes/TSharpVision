using TSharpVision.Drivers;

namespace TSharpVision;

public class TDisplay : IDisposable
{
    public static IDriver driver;

    public enum SM : ushort
    {
        BW80 = 0x0002,
        CO80 = 0x0003,
        Mono = 0x0007,
        Font8x8 = 0x0100
    }

    public static void ClearScreen(ushort cols, ushort rows)
    {
        driver?.ClearScreen(cols, rows);
    }

    public static void SetCursorType(ushort cursorType)
    {
        driver?.SetCursorType(cursorType);
    }

    public static ushort GetCursorType()
    {
        return driver?.GetCursorType() ?? 0;
    }

    public static ushort GetRows()
    {
        return driver.GetRows();
    }

    public static ushort GetCols()
    {
        return driver.GetCols();
    }

    public static void SetCrtMode(SM mode)
    {
        throw new NotImplementedException("TDisplay.SetCrtMode(ushort) není implementováno.");
    }

    public static SM GetCrtMode()
    {
        return (SM)driver.GetScreenMode();
    }


    protected TDisplay()
    {
        if (driver == null)
        {
            driver = ScreenDriverFactory.CreateScreenDriver();
            //ScreenBuffer screenBuffer = new ScreenBuffer(800, 600);
            //screenBuffer.
            driver.Initialize();
        }
        // Driver already initialized by an earlier TDisplay/TScreen
        // construction (or by the host harness); reuse it. Upstream
        // tvision allocates TScreen as part of TApplication's lifetime
        // without re-initializing the global driver state.

        //UpdateIntlChars(); Not needed
    }

    protected TDisplay(TDisplay other)
    {
        //UpdateIntlChars(); Not needed
    }

    private static void VideoInt()
    {
        throw new NotImplementedException("TDisplay.VideoInt() není implementováno.");
    }

    //private static void UpdateIntlChars()
    //{
    //    // Aktualizace znaků specifických pro dané mezinárodní nastavení
    //    if (getCodePage() != 437)
    //        TFrame::frameChars[30] = '\xCD';
    //}

    //private static ushort[] equipment;
    //private static byte[] crtInfo;
    //private static byte[] crtRows;
    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~TDisplay()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
