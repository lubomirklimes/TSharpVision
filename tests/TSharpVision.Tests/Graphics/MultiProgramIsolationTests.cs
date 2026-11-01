using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Graphics;

/// <summary>
/// TV12p: pixel-graphics synchronization is scoped to the program that presents. A graphics view of program A must
/// never submit to the driver of program B while B runs its loop (coexisting programs), and successive programs leave
/// no registrations, layers or timers behind. Deterministic: no wall-clock waits; ticks are driven directly.
/// </summary>
[Collection("NonParallel")]
public sealed class MultiProgramIsolationTests
{
    private static GWindow GraphicsWindow(TApplication app, int x)
    {
        var win = new GWindow(new TRect(x, 1, x + 24, 9), "g", Views.wnNoNumber);
        win.Insert(new GLine(0, 0, GraphConstants.GraphXMax, GraphConstants.GraphYMax, 0xFFFFFFFF));
        app.DeskTop!.Insert(win);
        return win;
    }

    private static void Pass(TProgram app)
    {
        TEvent ev = default;
        app.GetEvent(ref ev);
    }

    [Fact]
    public void A_coexisting_program_never_submits_to_the_driver_of_the_program_that_presents()
    {
        Assert.Equal(0, GView.ExposedCount);
        using var scope = new DriverScope();
        var driverA = new PixelRecordingDriver(scope.Driver);
        var driverB = new PixelRecordingDriver(scope.Driver);
        TApplication? appA = null, appB = null;
        try
        {
            TDisplay.driver = driverA;
            appA = new TApplication();
            GView viewA = GraphicsWindow(appA, 2).View;
            Pass(appA);
            Assert.True(driverA.PresentedHasLayer(viewA));

            // Program B takes over the display while A still exists with exposed graphics.
            TDisplay.driver = driverB;
            appB = new TApplication();
            GView viewB = GraphicsWindow(appB, 40).View;
            Pass(appB);

            Assert.True(driverB.PresentedHasLayer(viewB));
            Assert.False(driverB.HasLayer(viewA), "program A's view submitted to program B's driver during B's present");
            int submissionsB = driverB.Submissions;

            // A tick of A's view delivered while B is the presenting program (posts are process-wide) stays out of B.
            viewA.Tick();
            Pass(appB);
            Assert.False(driverB.HasLayer(viewA));
            Assert.False(viewA.IsAnimating, "A's view must pause while another program presents");
            Assert.True(driverB.Submissions >= submissionsB);

            // Back to A: its view synchronizes into its own driver again on A's next present.
            TDisplay.driver = driverA;
            Pass(appA);
            Assert.True(driverA.PresentedHasLayer(viewA));
            Assert.False(driverA.HasLayer(viewB), "program B's view submitted to program A's driver");
        }
        finally
        {
            TDisplay.driver = driverB;
            appB?.ShutDown();
            TDisplay.driver = driverA;
            appA?.ShutDown();
        }
        Assert.Equal(0, driverA.LayerCount);
        Assert.Equal(0, driverB.LayerCount);
        Assert.Equal(0, GView.ExposedCount);
        Assert.Equal(0, PixelGraphicsSynchronization.Count);
    }

    [Fact]
    public void Programs_sharing_one_driver_show_only_the_presenting_programs_graphics()
    {
        Assert.Equal(0, GView.ExposedCount);
        using var scope = new DriverScope();
        var driver = new PixelRecordingDriver(scope.Driver);
        TDisplay.driver = driver;
        TApplication? appA = null, appB = null;
        try
        {
            appA = new TApplication();
            GView viewA = GraphicsWindow(appA, 2).View;
            Pass(appA);
            Assert.True(driver.PresentedHasLayer(viewA));

            appB = new TApplication();
            GView viewB = GraphicsWindow(appB, 40).View;
            Pass(appB);
            Assert.True(driver.PresentedHasLayer(viewB));
            Assert.False(driver.PresentedHasLayer(viewA), "program A's graphics were presented in program B's frame");
            Assert.False(viewA.IsAnimating);

            Pass(appA);
            Assert.True(driver.PresentedHasLayer(viewA));
            Assert.False(driver.PresentedHasLayer(viewB), "program B's graphics were presented in program A's frame");
        }
        finally
        {
            appB?.ShutDown();
            appA?.ShutDown();
        }
        Assert.Equal(0, driver.LayerCount);
        Assert.Equal(0, PixelGraphicsSynchronization.Count);
    }

    [Fact]
    public void A_failing_synchronizer_propagates_and_the_next_present_runs_normally()
    {
        using var scope = new DriverScope();
        var app = new TApplication();
        int calls = 0;
        Action<TProgram> failing = _ => { if (++calls == 1) throw new InvalidOperationException("sync failed"); };
        PixelGraphicsSynchronization.Register(failing);
        try
        {
            TEvent ev = default;
            Assert.Equal("sync failed", Assert.Throws<InvalidOperationException>(() => app.GetEvent(ref ev)).Message);
            app.GetEvent(ref ev);
            Assert.True(calls >= 2);
        }
        finally
        {
            PixelGraphicsSynchronization.Unregister(failing);
            app.ShutDown();
        }
    }

    [Fact]
    public void Successive_programs_leave_no_registration_layer_or_timer_behind()
    {
        Assert.Equal(0, GView.ExposedCount);
        using var scope = new DriverScope();
        for (int round = 0; round < 3; round++)
        {
            var driver = new PixelRecordingDriver(scope.Driver);
            TDisplay.driver = driver;
            var app = new TApplication();
            GView view;
            try
            {
                view = GraphicsWindow(app, 4).View;
                Pass(app);
                Assert.True(driver.PresentedHasLayer(view));
                Assert.True(view.IsAnimating);
                Assert.Equal(1, PixelGraphicsSynchronization.Count);
            }
            finally { app.ShutDown(); }
            Assert.Equal(0, driver.LayerCount);
            Assert.False(view.IsAnimating);
            Assert.Equal(0, GView.ExposedCount);
            Assert.Equal(0, PixelGraphicsSynchronization.Count);
            // A late tick on a shut-down view never resubmits.
            view.Tick();
            Assert.Equal(0, driver.LayerCount);
        }
        TEventQueue.ClearPosted();
    }
}
