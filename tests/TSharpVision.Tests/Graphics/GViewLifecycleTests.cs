using System.Diagnostics;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Samples;
using Xunit;
using Xunit.Abstractions;
using static TSharpVision.Tests.Graphics.PixelGeometry;

namespace TSharpVision.Tests.Graphics;

/// <summary>
/// TV12o: <see cref="GView"/> keeps its driver layer in line with the view tree at the present point
/// (<see cref="PixelGraphicsSynchronization"/>), not one frame later. The recording driver snapshots every layer
/// clip when <see cref="IDriver.PumpMessages"/> runs, so each test checks the frame that would be presented right
/// after a handler changed the tree.
/// </summary>
[Collection("NonParallel")]
public sealed class GViewLifecycleTests
{
    private readonly ITestOutputHelper _output;

    public GViewLifecycleTests(ITestOutputHelper output) => _output = output;

    private static DemoTestScope PixelScope(out PixelRecordingDriver driver)
    {
        // Static state shared with other tests must be clean before and after every test here.
        Assert.Equal(0, GView.ExposedCount);
        PixelRecordingDriver? created = null;
        var scope = new DemoTestScope(wrapDriver: inner => created = new PixelRecordingDriver(inner));
        driver = created!;
        return scope;
    }

    private static GWindow OpenGraphics(ScriptedDemoApp app, TRect bounds)
    {
        var win = new GWindow(bounds, "g", Views.wnNoNumber);
        win.Insert(new GLine(0, 0, GraphConstants.GraphXMax, GraphConstants.GraphYMax, 0xFFFFFFFF));
        app.DeskTop!.Insert(win);
        return win;
    }

    private static TWindow Cover(ScriptedDemoApp app, TRect bounds)
    {
        var cover = new TWindow(bounds, "cover", Views.wnNoNumber);
        app.DeskTop!.Insert(cover);
        return cover;
    }

    // ── Registration ─────────────────────────────────────────────────────

    [Fact]
    public void The_synchronizer_is_registered_exactly_while_a_graphics_view_is_exposed()
    {
        int baseline = PixelGraphicsSynchronization.Count;
        var scope = PixelScope(out var driver);
        try
        {
            var detached = new GWindow(new TRect(0, 0, 20, 8), "d", Views.wnNoNumber);
            Assert.Equal(baseline, PixelGraphicsSynchronization.Count);

            GWindow a = OpenGraphics(scope.App, new TRect(2, 2, 30, 12));
            Assert.Equal(baseline + 1, PixelGraphicsSynchronization.Count);
            Assert.Equal(1, GView.ExposedCount);
            GWindow b = OpenGraphics(scope.App, new TRect(20, 6, 50, 18));
            Assert.Equal(baseline + 1, PixelGraphicsSynchronization.Count); // one shared synchronizer
            Assert.Equal(2, GView.ExposedCount);

            a.Hide();
            Assert.Equal(1, GView.ExposedCount);
            b.Hide();
            Assert.Equal(baseline, PixelGraphicsSynchronization.Count);
            a.Show();
            Assert.Equal(baseline + 1, PixelGraphicsSynchronization.Count);

            a.Close();
            b.Close();
            Assert.Equal(0, GView.ExposedCount);
            Assert.Equal(baseline, PixelGraphicsSynchronization.Count);
            Assert.Equal(0, driver.LayerCount);

            // Application shutdown releases views that are still open.
            OpenGraphics(scope.App, new TRect(2, 2, 30, 12));
            Assert.Equal(baseline + 1, PixelGraphicsSynchronization.Count);
            detached.View.Dispose();
        }
        finally { scope.Dispose(); }
        Assert.Equal(0, GView.ExposedCount);
        Assert.Equal(baseline, PixelGraphicsSynchronization.Count);
        Assert.Equal(0, driver.LayerCount);
    }

    // ── The present point ────────────────────────────────────────────────

    [Fact]
    public void A_window_inserted_by_a_handler_is_cut_out_of_the_very_next_presented_frame()
    {
        using var scope = PixelScope(out var driver);
        GView view = OpenGraphics(scope.App, new TRect(2, 2, 40, 16)).View;
        Present(scope.App);
        PixelRect full = Of(view);
        Assert.Equal([full], driver.PresentedClip(view));

        TWindow cover = Cover(scope.App, new TRect(12, 5, 30, 10));
        // Inserting the cover redraws only the cover: the layer is stale until the present point.
        Assert.Equal([full], driver.Clip(view));
        int presents = driver.Presents;
        Present(scope.App);
        Assert.True(driver.Presents > presents);
        PixelRect[] clip = driver.PresentedClip(view);
        Assert.True(Excludes(clip, cover), "the presented clip overlaps the cover or its shadow");
        Assert.Equal(Area([full]) - Overlap(full, cover), Area(clip));

        cover.Close();
        Present(scope.App);
        Assert.Equal([full], driver.PresentedClip(view));
    }

    [Fact]
    public void Without_the_registered_synchronizer_the_presented_frame_lags()
    {
        using var scope = PixelScope(out var driver);
        GView view = OpenGraphics(scope.App, new TRect(2, 2, 40, 16)).View;
        PixelRect full = Of(view);
        PixelGraphicsSynchronization.Unregister(GView.Synchronizer);
        TWindow cover;
        try
        {
            cover = Cover(scope.App, new TRect(12, 5, 30, 10));
            Present(scope.App);
            // The pre-TV12o behaviour: graphics drawn over the new window for a frame.
            Assert.Equal([full], driver.PresentedClip(view));
        }
        finally { PixelGraphicsSynchronization.Register(GView.Synchronizer); }
        Present(scope.App);
        Assert.True(Excludes(driver.PresentedClip(view), cover));
    }

    [Fact]
    public void Modal_and_nested_modal_dialogs_are_cut_out_while_their_loops_present()
    {
        using var scope = PixelScope(out var driver);
        GView view = OpenGraphics(scope.App, new TRect(0, 0, 80, 23)).View; // the whole desktop
        Present(scope.App);
        PixelRect full = Of(view);

        TView? outer = null;
        bool nestedChecked = false;
        scope.App.Step(() =>
        {
            outer = scope.App.Modal<TDialog>();
            Assert.True(Intersects(Of(outer), full));
            Assert.True(Excludes(driver.PresentedClip(view), outer), "first presented frame overlaps the dialog");

            scope.App.Step(() =>
            {
                var inner = scope.App.Modal<TDialog>();
                Assert.NotSame(outer, inner);
                PixelRect[] clip = driver.PresentedClip(view);
                Assert.True(Excludes(clip, inner), "nested dialog not cut out");
                Assert.True(Excludes(clip, outer), "outer dialog not cut out");
                nestedChecked = true;
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            MsgBox.MessageBox(scope.App.DeskTop, "Nested over the graphics", MsgBox.mfWarning | MsgBox.mfOKButton);
            // Back in the outer loop: its next present shows the nested box gone again.
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        MsgBox.MessageBox(scope.App.DeskTop, "A dialog over the graphics, with a longer line", MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.True(nestedChecked);
        Present(scope.App);
        Assert.Equal([full], driver.PresentedClip(view));
        Assert.Equal(1, driver.LayerCount);
    }

    [Fact]
    public void Menu_boxes_are_cut_out_while_the_menu_loop_presents()
    {
        using var scope = PixelScope(out var driver);
        var app = scope.App;
        GView view = OpenGraphics(app, new TRect(0, 0, 80, 23)).View;
        Present(app);
        PixelRect full = Of(view);

        bool checkedBox = false;
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            Assert.True(Intersects(Of(box), full));
            Assert.True(Excludes(driver.PresentedClip(view), box), "menu box not cut out of the presented frame");
            checkedBox = true;
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc))); // leave the menu bar
        var altD = ComboInput.Key(Keys.kbAltD);
        app.HandleEvent(ref altD);
        Assert.True(checkedBox);
        Assert.Null(TView.ModalTopView);
        Present(app);
        Assert.Equal([full], driver.PresentedClip(view));
    }

    // ── Geometry and z-order ─────────────────────────────────────────────

    [Fact]
    public void Moving_resizing_and_zooming_are_presented_without_lag()
    {
        using var scope = PixelScope(out var driver);
        GWindow win = OpenGraphics(scope.App, new TRect(2, 2, 40, 16));
        GView view = win.View;
        TWindow cover = Cover(scope.App, new TRect(50, 2, 70, 8)); // beside the graphics
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));

        // A covering window moved over the graphics (Locate) and resized away (ChangeBounds).
        cover.Locate(new TRect(10, 4, 30, 10));
        Present(scope.App);
        Assert.True(Excludes(driver.PresentedClip(view), cover));
        Assert.True(Area(driver.PresentedClip(view)) < Area([Of(view)]));
        cover.ChangeBounds(new TRect(60, 2, 70, 6));
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));

        // The graphics window itself moves and zooms (cmZoom): the layer follows in the same present.
        win.Locate(new TRect(4, 3, 50, 18));
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));
        var zoom = ScriptedDemoApp.CommandEvent(Views.cmZoom);
        win.HandleEvent(ref zoom);
        Assert.Equal(Events.evNothing, zoom.What);
        Present(scope.App);
        PixelRect[] zoomed = driver.PresentedClip(view);
        Assert.True(Excludes(zoomed, cover));
        // The zoomed window covers the desktop, so it is in front of nothing but the cover.
        Assert.True(win.size.x >= 70);
        Assert.Equal(Area([Of(view)]) - Overlap(Of(view), cover), Area(zoomed));
    }

    [Fact]
    public void Bringing_a_window_to_front_swaps_the_cut_out_of_two_overlapping_graphics_views()
    {
        using var scope = PixelScope(out var driver);
        GWindow back = OpenGraphics(scope.App, new TRect(2, 2, 40, 16));
        GWindow front = OpenGraphics(scope.App, new TRect(20, 8, 60, 20));
        Present(scope.App);
        Assert.Equal(2, driver.PresentedLayerCount);
        Assert.Equal([Of(front.View)], driver.PresentedClip(front.View));
        Assert.True(Excludes(driver.PresentedClip(back.View), front));

        back.Select();
        Assert.Same(back, scope.App.DeskTop!.First());
        Present(scope.App);
        Assert.Equal([Of(back.View)], driver.PresentedClip(back.View));
        Assert.True(Excludes(driver.PresentedClip(front.View), back));

        // A plain window brought to front over both.
        TWindow cover = Cover(scope.App, new TRect(15, 6, 45, 14));
        front.Select();
        cover.Select();
        Present(scope.App);
        Assert.True(Excludes(driver.PresentedClip(back.View), cover));
        Assert.True(Excludes(driver.PresentedClip(front.View), cover));
        Assert.True(Excludes(driver.PresentedClip(back.View), front));
    }

    // ── Hidden, covered and zero-size views do no work ────────────────────

    [Fact]
    public void A_fully_covered_view_drops_its_layer_and_pauses_its_timer_until_uncovered()
    {
        using var scope = PixelScope(out var driver);
        GView view = OpenGraphics(scope.App, new TRect(10, 4, 40, 14)).View;
        Assert.True(view.IsAnimating);

        TWindow cover = Cover(scope.App, new TRect(5, 2, 50, 20));
        Present(scope.App);
        Assert.False(driver.PresentedHasLayer(view)); // no stale pixels over the cover
        Assert.False(view.HasGraphicsLayer);
        Assert.False(view.IsAnimating);
        Assert.Equal(0, driver.LayerCount);

        // Paused means silent: no tick is posted however long it stays covered.
        TEventQueue.ClearPosted();
        Thread.Sleep(10 * 1000 / GView.TickHz);
        Assert.Equal(0, TEventQueue.PostedCount);
        int submissions = driver.Submissions;
        Present(scope.App);
        Assert.Equal(submissions, driver.Submissions);

        cover.Close();
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));
        Assert.True(view.IsAnimating);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (TEventQueue.PostedCount == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Assert.True(TEventQueue.PostedCount > 0, "the timer did not resume");
    }

    [Fact]
    public void A_zero_size_view_owns_no_layer_and_does_not_animate()
    {
        using var scope = PixelScope(out var driver);
        GWindow win = OpenGraphics(scope.App, new TRect(10, 4, 40, 14));
        GView view = win.View;
        TRect bounds = view.GetBounds();
        view.ChangeBounds(new TRect(bounds.a.x, bounds.a.y, bounds.a.x, bounds.b.y));
        Present(scope.App);
        Assert.False(view.HasGraphicsLayer);
        Assert.False(view.IsAnimating);
        Assert.False(driver.PresentedHasLayer(view));

        view.ChangeBounds(bounds);
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));
        Assert.True(view.IsAnimating);
    }

    [Fact]
    public void Hide_show_remove_reinsert_dispose_close_and_reopen_leak_no_layers()
    {
        using var scope = PixelScope(out var driver);
        TGroup desk = scope.App.DeskTop!;
        GWindow win = OpenGraphics(scope.App, new TRect(10, 4, 40, 14));
        GView view = win.View;

        win.Hide();
        Present(scope.App);
        Assert.Equal(0, driver.PresentedLayerCount);
        Assert.False(view.IsAnimating);
        win.Show();
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));

        desk.Remove(win);
        Assert.Equal(0, driver.LayerCount);
        Assert.Equal(0, GView.ExposedCount);
        Assert.False(view.IsAnimating);
        desk.Insert(win);
        Present(scope.App);
        Assert.Equal([Of(view)], driver.PresentedClip(view));
        Assert.True(view.IsAnimating);

        // Disposed while still in the tree: the layer goes and a later redraw does not bring it back.
        view.Dispose();
        Assert.Equal(0, driver.LayerCount);
        Assert.Equal(0, GView.ExposedCount);
        win.DrawView();
        view.Refresh();
        Present(scope.App);
        Assert.Equal(0, driver.PresentedLayerCount);
        win.Close();

        GWindow again = OpenGraphics(scope.App, new TRect(10, 4, 40, 14));
        Present(scope.App);
        Assert.Equal(1, driver.PresentedLayerCount);
        again.Close();
        Present(scope.App);
        Assert.Equal(0, driver.PresentedLayerCount);
        Assert.Equal(0, driver.LayerCount);
        Assert.False(again.View.IsAnimating);
    }

    // ── Cost of a synchronization pass ───────────────────────────────────

    [Fact]
    public void An_unchanged_synchronization_pass_calls_no_driver_and_allocates_nothing()
    {
        using var scope = PixelScope(out var driver);
        GWindow a = OpenGraphics(scope.App, new TRect(2, 2, 40, 16));
        GWindow b = OpenGraphics(scope.App, new TRect(20, 8, 60, 20));
        Cover(scope.App, new TRect(30, 4, 50, 12));
        Present(scope.App);
        // Ticks resubmit; keep them out of the measurement.
        TEventQueue.ClearPosted();
        for (int i = 0; i < 1000; i++) GView.Synchronizer(scope.App); // warm-up (lists sized, code jitted)
        int submissions = driver.Submissions, removals = driver.Removals;

        const int passes = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < passes; i++) GView.Synchronizer(scope.App);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(submissions, driver.Submissions);
        Assert.Equal(removals, driver.Removals);
        // No per-pass allocation; a few one-off runtime bytes (tiered JIT) are tolerated.
        Assert.True(allocated < passes, $"{allocated} bytes allocated in {passes} passes");
        Assert.True(a.View.HasGraphicsLayer && b.View.HasGraphicsLayer);
        _output.WriteLine($"synchronization pass, 2 views + 1 cover: {elapsed.TotalMilliseconds * 1000 / passes:F3} µs");
    }

    // Area of the cover (and its shadow) inside the rectangle, in logical pixels.
    private static long Overlap(PixelRect area, TView cover)
    {
        static long Inter(PixelRect a, PixelRect b)
        {
            long w = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
            long h = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
            return w > 0 && h > 0 ? w * h : 0;
        }
        PixelRect body = Of(cover), shadow = ShadowOf(cover);
        long total = Inter(area, body);
        if ((cover.state & Views.sfShadow) != 0)
        {
            // Shadow minus the part that overlaps the body (both inside the area).
            PixelRect both = new(Math.Max(body.X, shadow.X), Math.Max(body.Y, shadow.Y),
                Math.Min(body.X + body.W, shadow.X + shadow.W) - Math.Max(body.X, shadow.X),
                Math.Min(body.Y + body.H, shadow.Y + shadow.H) - Math.Max(body.Y, shadow.Y));
            long common = both.W > 0 && both.H > 0 ? Inter(area, both) : 0;
            total += Inter(area, shadow) - common;
        }
        return total;
    }
}
