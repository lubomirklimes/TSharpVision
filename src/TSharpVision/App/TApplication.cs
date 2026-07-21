namespace TSharpVision;

// TApplication is the canonical entry point. It owns the lifetime
// of the static event queue (singleton-guarded like the upstream `static
// TEventQueue *teq = 0;`, and bridges Suspend/Resume to TEventQueue + TScreen.
public class TApplication : TProgram
{
    // Upstream stores `teq` as a file-static singleton; we mirror that with
    // a private static field guarded by a null-check in the constructor.
    private static TEventQueue _teq;

    // TScreen mirrors a static singleton too (statics live on the type),
    // but we keep an instance field so the GC pins it for the application's
    // lifetime — disposal at TApplication finalize/Dispose tears it down.
    protected TScreen tsc = new TScreen();

    // TSystemError port deferred (signal/abort handlers — driver concern).

    public TApplication() : base()
    {
        if (_teq == null)
            _teq = new TEventQueue();
        // initHistory() — TVHistory persistence layer deferred.
    }

    // No finalizer. It used to call Dispose(false), which reached the shared event queue and
    // the shared screen through _teq and tsc — so finalizing an abandoned application
    // suspended the input queue and the driver belonging to whichever application was
    // actually running. Deterministic disposal owns that teardown now.

    /// <summary>
    /// Releases the shared singletons this application set up.
    ///
    /// Only on a deterministic call: <paramref name="disposing"/> is false only on the
    /// finalizer thread, where touching other managed objects is invalid anyway, and where
    /// suspending process-wide state would race any live application.
    ///
    /// The authoritative teardown remains <see cref="TProgram.ShutDown"/>, which
    /// <see cref="AppLifecycleGuard"/> always calls; this only adds the singleton release for
    /// callers that dispose explicitly. Repeated calls are harmless.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // doneHistory() deferred.
            if (_teq != null)
            {
                _teq.Dispose();
                _teq = null;
            }

            tsc.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Suspend()
    {
        TEventQueue.Suspend();
        TScreen.Suspend();
    }

    public override void Resume()
    {
        TScreen.Resume();
        TEventQueue.Resume();
        ResetIdleTime();
    }
}
