// Modern .NET lifecycle guard for TApplication.
// Not a port of the DOS TSystemError signal handler.
using System;
using System.Runtime.ExceptionServices;
using SharpVision.Constants;

namespace SharpVision;

/// <summary>
/// Wraps a <see cref="TApplication.Run"/> call with a try/finally that
/// guarantees <see cref="TApplication.ShutDown"/> is always executed, even
/// when an unhandled exception escapes the event loop, and handles Ctrl+C /
/// Ctrl+Break gracefully by posting <c>cmQuit</c> instead of aborting the
/// process.
/// </summary>
public static class AppLifecycleGuard
{
    /// <summary>
    /// Run <paramref name="app"/> and ensure cleanup on every exit path.
    /// </summary>
    /// <param name="app">The application to run. Must not be null.</param>
    /// <param name="onError">
    /// Optional callback invoked with the exception when <c>Run()</c> throws.
    /// When <see langword="null"/>, the exception is re-thrown (via
    /// <see cref="ExceptionDispatchInfo"/> to preserve the original stack) after
    /// cleanup completes.
    /// </param>
    /// <returns>
    /// 0 on a clean exit; 1 when an exception was caught and consumed by
    /// <paramref name="onError"/>.
    /// </returns>
    public static int Run(TApplication app, Action<Exception>? onError = null)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        Exception? caught = null;

        // Post cmQuit on Ctrl+C / Ctrl+Break instead of aborting the process.
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            var ev = new TEvent { What = Events.evCommand };
            ev.message.command = Views.cmQuit;
            TEventQueue.Enqueue(ev);
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            caught = ex;
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;

            // Isolated teardown: each step is guarded so a failure in one does
            // not prevent the others from running.
            try { app.ShutDown(); } catch { }
            try { TDisplay.driver?.Shutdown(); } catch { }

            if (caught != null)
            {
                if (onError != null)
                    onError(caught);
                else
                    ExceptionDispatchInfo.Capture(caught).Throw();
            }
        }
    }
}
