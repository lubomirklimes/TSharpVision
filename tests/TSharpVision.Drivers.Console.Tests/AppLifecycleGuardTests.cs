using System;
using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Drivers.Console.Tests;

// ── file-local helpers ────────────────────────────────────────────────────────

/// <summary>Minimal headless TProgram.</summary>
file sealed class TestProgram25b : TProgram
{
    public TestProgram25b() { }
}

/// <summary>TApplication that throws InvalidOperationException in Run().</summary>
file sealed class ThrowOnRunApp : TApplication
{
    public override void Run() =>
        throw new InvalidOperationException("Injected failure");
}

/// <summary>TApplication that throws in Run() and tracks whether ShutDown was called.</summary>
file sealed class TrackShutDownApp : TApplication
{
    public bool ShutDownCalled;

    public override void Run() =>
        throw new InvalidOperationException("Injected failure");

    public override void ShutDown()
    {
        ShutDownCalled = true;
        base.ShutDown();
    }
}

// ── tests ─────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class AppLifecycleGuardTests
{
    // ── null app throws ────────────────────────────────────────────────────

    [Fact]
    public void Run_NullApp_ThrowsArgumentNullException()
    {
        using var driver = new DriverScope();
        Assert.Throws<ArgumentNullException>(() => AppLifecycleGuard.Run(null!));
    }

    // ── normal run returns 0 ──────────────────────────────────────────────

    [Fact]
    public void Run_CleanExit_Returns0()
    {
        using var driver = new DriverScope();
        var app = new TApplication();
        var qev = new TEvent { What = Events.evCommand };
        qev.message.command = Views.cmQuit;
        app.PutEvent(ref qev);
        int rc = AppLifecycleGuard.Run(app);
        Assert.Equal(0, rc);
    }

    // ── exception caught and passed to onError ────────────────────────────────

    [Fact]
    public void Run_ExceptionCaughtByOnError()
    {
        using var driver = new DriverScope();
        var throwApp = new ThrowOnRunApp();
        Exception? captured = null;
        int rc = AppLifecycleGuard.Run(throwApp, ex => captured = ex);
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal(1, rc);
    }

    // ── exception rethrown when onError is null ──────────────────────────────

    [Fact]
    public void Run_ExceptionRethrownWhenOnErrorNull()
    {
        using var driver = new DriverScope();
        var throwApp = new ThrowOnRunApp();
        Assert.Throws<InvalidOperationException>(
            () => AppLifecycleGuard.Run(throwApp));
    }

    // ── ShutDown called even when Run throws ──────────────────────────────────

    [Fact]
    public void Run_ShutDownCalledEvenWhenRunThrows()
    {
        using var driver = new DriverScope();
        var trackApp = new TrackShutDownApp();
        AppLifecycleGuard.Run(trackApp, _ => { });
        Assert.True(trackApp.ShutDownCalled);
    }

    // ── second run also returns 0 ────────────────────────────────────────────

    [Fact]
    public void Run_SecondCallAlsoReturns0()
    {
        using var driver = new DriverScope();
        var app1 = new TApplication();
        var qev1 = new TEvent { What = Events.evCommand };
        qev1.message.command = Views.cmQuit;
        app1.PutEvent(ref qev1);
        AppLifecycleGuard.Run(app1);

        var app2 = new TApplication();
        var qev2 = new TEvent { What = Events.evCommand };
        qev2.message.command = Views.cmQuit;
        app2.PutEvent(ref qev2);
        int rc2 = AppLifecycleGuard.Run(app2);
        Assert.Equal(0, rc2);
    }

    // ── Idle with DoNotReleaseCPU == 0 ──────────────────────────────────────

    [Fact]
    public void Idle_DoNotReleaseCPU_Zero_NoException()
    {
        using var driver = new DriverScope();
        byte saved = TProgram.DoNotReleaseCPU;
        try
        {
            TProgram.DoNotReleaseCPU = 0;
            var app = new TestProgram25b();
            var ex = Record.Exception(() => { app.Idle(); app.ShutDown(); });
            Assert.Null(ex);
        }
        finally { TProgram.DoNotReleaseCPU = saved; }
    }

    // ── Idle with DoNotReleaseCPU == 1 ──────────────────────────────────────

    [Fact]
    public void Idle_DoNotReleaseCPU_One_NoException()
    {
        using var driver = new DriverScope();
        byte saved = TProgram.DoNotReleaseCPU;
        try
        {
            TProgram.DoNotReleaseCPU = 1;
            var app = new TestProgram25b();
            var ex = Record.Exception(() => { app.Idle(); app.ShutDown(); });
            Assert.Null(ex);
        }
        finally { TProgram.DoNotReleaseCPU = saved; }
    }

    // ── Win32ConsoleDriver.Shutdown is idempotent ─────────────────────────────

    [Fact]
    public void Win32ConsoleDriver_ShutdownIdempotent()
    {
        using var driver = new DriverScope();
        var w32 = new Win32ConsoleDriver();
        w32.Initialize();
        var ex = Record.Exception(() => { w32.Shutdown(); w32.Shutdown(); });
        Assert.Null(ex);
    }

    // ── DoNotReleaseCPU defaults to 0 ──────────────────────────────────────

    [Fact]
    public void DoNotReleaseCPU_DefaultIsZero()
    {
        byte saved = TProgram.DoNotReleaseCPU;
        try { Assert.Equal(0, TProgram.DoNotReleaseCPU); }
        finally { TProgram.DoNotReleaseCPU = saved; }
    }
}
