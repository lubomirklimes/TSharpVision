// TScreen/TDisplay finalizer crash stabilization tests.
//
// Regression tests covering the dispose/finalizer path that previously caused:
//   NullReferenceException in TDisplay.SetCursorType → TScreen.Suspend → TScreen.Dispose/Finalize
//
// Root cause: TDisplay.SetCursorType dereferenced `driver` without null check.
//             TScreen.Suspend() called it unconditionally from Dispose(bool).
//             During test shutdown, DriverScope.Dispose() set driver=null before
//             GC finalized TScreen, causing the NRE.
//
// Fix:
//   1. TDisplay.SetCursorType/GetCursorType are now null-safe (driver?.)
//   2. TScreen.Suspend() guards on TDisplay.driver == null.
//   3. TScreen.Dispose(bool) is idempotent via _suspendedOnDispose flag.
using SharpVision;
using SharpVision.Drivers;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Drivers;

[Collection("NonParallel")]
public sealed class TScreenDisposeTests : IDisposable
{
    private readonly DriverScope _ds;

    public TScreenDisposeTests()
    {
        _ds = new DriverScope();
    }

    public void Dispose() => _ds.Dispose();

    // ── TDisplay null-safety ───────────────────────────────────────────────

    [Fact]
    public void TDisplay_SetCursorType_WhenDriverNull_DoesNotThrow()
    {
        IDriver saved = TDisplay.driver;
        try
        {
            TDisplay.driver = null;
            var ex = Record.Exception(() => TDisplay.SetCursorType(0));
            Assert.Null(ex);
        }
        finally { TDisplay.driver = saved; }
    }

    [Fact]
    public void TDisplay_GetCursorType_WhenDriverNull_ReturnsZero()
    {
        IDriver saved = TDisplay.driver;
        try
        {
            TDisplay.driver = null;
            ushort result = TDisplay.GetCursorType();
            Assert.Equal((ushort)0, result);
        }
        finally { TDisplay.driver = saved; }
    }

    // ── TScreen.Suspend null-safety ────────────────────────────────────────

    [Fact]
    public void TScreen_Suspend_WhenDriverNull_DoesNotThrow()
    {
        IDriver saved = TDisplay.driver;
        try
        {
            TDisplay.driver = null;
            var ex = Record.Exception(TScreen.Suspend);
            Assert.Null(ex);
        }
        finally { TDisplay.driver = saved; }
    }

    // ── TScreen.Dispose idempotency and null-safety ───────────────────────

    [Fact]
    public void TScreen_Dispose_WhenDriverNull_DoesNotThrow()
    {
        // Arrange: initialize a TScreen while driver is live.
        var tsc = new TScreen();
        IDriver saved = TDisplay.driver;
        try
        {
            // Simulate post-DriverScope teardown: driver is reset to null.
            TDisplay.driver = null;

            // Act: Dispose must not throw even with null driver.
            var ex = Record.Exception(() => tsc.Dispose());
            Assert.Null(ex);
        }
        finally { TDisplay.driver = saved; }
    }

    [Fact]
    public void TScreen_Dispose_IsIdempotent()
    {
        // Arrange: create with driver live.
        var tsc = new TScreen();

        // Act: dispose twice — second call must be a no-op, not a crash.
        tsc.Dispose();
        var ex = Record.Exception(() => tsc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void TScreen_Dispose_AfterDriverReset_DoesNotThrow()
    {
        // This directly simulates the finalizer-crash scenario:
        //   1. TScreen created (driver was live at construction time).
        //   2. Driver reset to null (DriverScope torn down).
        //   3. Dispose/finalizer runs — must not crash.
        var tsc = new TScreen();
        IDriver saved = TDisplay.driver;
        try
        {
            TDisplay.driver = null;

            // Both the explicit Dispose and the (simulated) finalizer path:
            var ex1 = Record.Exception(() => tsc.Dispose());
            Assert.Null(ex1);

            // Second call (idempotency after driver reset):
            var ex2 = Record.Exception(() => tsc.Dispose());
            Assert.Null(ex2);
        }
        finally { TDisplay.driver = saved; }
    }
}
