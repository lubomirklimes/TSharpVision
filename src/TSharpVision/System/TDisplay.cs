using TSharpVision.Drivers;

namespace TSharpVision;

/// <summary>Shared screen-driver facade providing display dimensions, cursor control, and deterministic disposal hooks.</summary>
public class TDisplay : IDisposable
{
    /// <summary>Process-wide display driver, or null before initialization or after failed startup.</summary>
    public static IDriver driver;

    /// <summary>Legacy screen-mode identifiers and the optional 8-by-8 font mode bit.</summary>
    public enum SM : ushort
    {
        /// <summary>Legacy 80-column black-and-white text mode.</summary>
        BW80 = 0x0002,
        /// <summary>Legacy 80-column color text mode.</summary>
        CO80 = 0x0003,
        /// <summary>Legacy monochrome text mode.</summary>
        Mono = 0x0007,
        /// <summary>Mode bit requesting an 8-by-8-character font where supported.</summary>
        Font8x8 = 0x0100
    }

    /// <summary>Asks the active driver to clear the specified character-cell area; does nothing without a driver.</summary>
    public static void ClearScreen(ushort cols, ushort rows)
    {
        driver?.ClearScreen(cols, rows);
    }

    /// <summary>Passes a cursor-shape value to the active driver; does nothing without a driver.</summary>
    public static void SetCursorType(ushort cursorType)
    {
        driver?.SetCursorType(cursorType);
    }

    /// <summary>Returns the active driver's cursor-shape value, or zero without a driver.</summary>
    public static ushort GetCursorType()
    {
        return driver?.GetCursorType() ?? 0;
    }

    /// <summary>Returns the initialized driver's display height in character cells.</summary>
    public static ushort GetRows()
    {
        return driver.GetRows();
    }

    /// <summary>Returns the initialized driver's display width in character cells.</summary>
    public static ushort GetCols()
    {
        return driver.GetCols();
    }

    /// <summary>Unsupported legacy mode-setting entry point; always throws NotImplementedException.</summary>
    public static void SetCrtMode(SM mode)
    {
        throw new NotImplementedException("TDisplay.SetCrtMode(ushort) není implementováno.");
    }

    /// <summary>Returns the initialized driver's current screen-mode identifier.</summary>
    public static SM GetCrtMode()
    {
        return (SM)driver.GetScreenMode();
    }


    /// <summary>Initializes a newly created shared driver when none exists, otherwise reuses it; a failed initialization clears the shared reference and rethrows.</summary>
    protected TDisplay()
    {
        if (driver == null)
        {
            driver = ScreenDriverFactory.CreateScreenDriver();
            try
            {
                driver.Initialize();
            }
            catch
            {
                // A later application must not reuse a driver whose startup failed.
                driver = null!;
                throw;
            }
        }
        // Driver already initialized by an earlier TDisplay/TScreen
        // construction (or by the host harness); reuse it. Upstream
        // tvision allocates TScreen as part of TApplication's lifetime
        // without re-initializing the global driver state.

        //UpdateIntlChars(); Not needed
    }

    /// <summary>Creates a display facade without initializing or copying driver state; the supplied instance is unused.</summary>
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

    /// <summary>Marks this facade disposed; derived implementations may release state during deterministic disposal.</summary>
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

    // No finalizer: nothing here owns an unmanaged resource, and the derived TScreen's
    // Dispose drives the shared driver, which must not happen on the finalizer thread.

    /// <summary>Invokes deterministic disposal and suppresses finalization for this facade.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
