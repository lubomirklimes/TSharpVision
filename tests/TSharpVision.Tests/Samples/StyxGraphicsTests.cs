using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Graphics;
using static TSharpVision.Tests.Graphics.PixelGeometry;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>
/// TV12n: the TSharpVision.Graphics views and the TVDemo STYX demo against a recording
/// <see cref="IPixelGraphicsDriver"/> around the scope's NullDriver (the real TVDemo dispatch, menus and modal
/// loops), and the unsupported-driver fallback on the plain NullDriver. Since TV12o the layer follows the view tree
/// at the present point (no Idle hook): assertions read the clip the driver held when the loop presented.
/// </summary>
[Collection("NonParallel")]
public sealed class StyxGraphicsTests
{
    private const int CellW = PixelRecordingDriver.CellW, CellH = PixelRecordingDriver.CellH;

    // ── Graphics primitives (no application) ─────────────────────────────

    [Fact]
    public void Logical_coordinates_map_onto_the_view_pixel_rectangle()
    {
        GraphConstants.GlobalToPhysical(40, 80, 320, 160, 0, 0, out int x, out int y);
        Assert.Equal((40, 80), (x, y));
        GraphConstants.GlobalToPhysical(40, 80, 320, 160, GraphConstants.GraphXMax, GraphConstants.GraphYMax, out x, out y);
        Assert.Equal((360, 240), (x, y));
        GraphConstants.GlobalToPhysical(40, 80, 320, 160, GraphConstants.GraphXMax / 2, GraphConstants.GraphYMax / 4, out x, out y);
        Assert.Equal((200, 120), (x, y));
        Assert.Equal(16, GraphConstants.CgaPalette.Length);
        Assert.Equal(0xFFFFFFFFu, GraphConstants.CgaPalette[15]);
    }

    [Fact]
    public void Lines_and_boxes_draw_their_primitives_into_the_canvas()
    {
        var canvas = new GraphicsCanvas();
        new GLine(0, 0, GraphConstants.GraphXMax, GraphConstants.GraphYMax, 0x112233FF).GraphDraw(canvas, 10, 20, 100, 50);
        Assert.Equal(GraphicsPrimitive.Line(10, 20, 110, 70, 0x112233FF), canvas.Primitives[0]);

        canvas.Clear();
        new GBox(0, 0, 5000, 7500, 0xFFu).GraphDraw(canvas, 0, 0, 100, 60);
        Assert.Equal(
            [GraphicsPrimitive.Line(0, 0, 50, 0, 0xFF), GraphicsPrimitive.Line(50, 0, 50, 60, 0xFF),
             GraphicsPrimitive.Line(50, 60, 0, 60, 0xFF), GraphicsPrimitive.Line(0, 60, 0, 0, 0xFF)],
            canvas.Primitives.ToArray());

        canvas.Clear();
        canvas.FillRect(1, 1, 0, 4, 0xFF);
        canvas.FillRect(1, 1, 2, 3, 0xFF);
        Assert.Equal([GraphicsPrimitive.FillRect(1, 1, 2, 3, 0xFF)], canvas.Primitives.ToArray());
    }

    [Fact]
    public void Styx_keeps_sixteen_fading_lines_and_moves_the_newest_each_tick()
    {
        var styx = new TStyx(new Random(7));
        var canvas = new GraphicsCanvas();
        styx.GraphDraw(canvas, 0, 0, 1000, 750);
        Assert.Equal(TStyx.Count, canvas.Count);
        var colours = canvas.Primitives.ToArray().Select(p => p.Rgba).ToHashSet();
        Assert.Subset(new HashSet<uint> { 0xFFFFFFFF, 0x55FFFFFF, 0x5555FFFF, 0x0000AAFF, 0x000000FF }, colours);
        Assert.Contains(0xFFFFFFFFu, colours); // exactly one newest line is white
        Assert.Single(canvas.Primitives.ToArray(), p => p.Rgba == 0xFFFFFFFF);

        var before = (styx.Newest.Xa, styx.Newest.Ya, styx.Newest.Xb, styx.Newest.Yb);
        styx.GraphProd();
        Assert.NotEqual(before, (styx.Newest.Xa, styx.Newest.Ya, styx.Newest.Xb, styx.Newest.Yb));

        // Many ticks keep the end points near the logical area (bounce, at most one 64-unit step outside).
        for (int i = 0; i < 5000; i++) styx.GraphProd();
        GLine n = styx.Newest;
        Assert.InRange(n.Xa, -64, GraphConstants.GraphXMax + 64);
        Assert.InRange(n.Yb, -64, GraphConstants.GraphYMax + 64);
    }

    // ── TVDemo with a pixel-graphics driver ──────────────────────────────

    private static DemoTestScope PixelScope(out PixelRecordingDriver driver)
    {
        PixelRecordingDriver? created = null;
        var scope = new DemoTestScope(wrapDriver: inner => created = new PixelRecordingDriver(inner));
        driver = created!;
        return scope;
    }

    private static TStyxDemo[] StyxWindows(ScriptedDemoApp app) =>
        ScriptedDemoApp.Descendants(app.DeskTop!).OfType<TStyxDemo>().ToArray();

    [Fact]
    public void Menu_command_opens_the_styx_window_in_the_same_application_with_a_clipped_layer()
    {
        using var scope = PixelScope(out var driver);
        var system = TVDemoMenuNavigationTests.Items(scope.App.MenuBar!.Menu!).First();
        Assert.Contains(TVDemoMenuNavigationTests.Items(system.SubMenu), i => i.Command == ShowcaseCmd.Styx && i.Name == "~S~TYX graphics");

        scope.App.RunCommand(ShowcaseCmd.Styx);
        TStyxDemo win = Assert.Single(StyxWindows(scope.App));
        Assert.Same(scope.App.DeskTop, win.owner);
        Assert.Equal("S T Y X", win.title);
        Assert.Equal((34, 12), (win.size.x, win.size.y));

        GView view = win.View;
        Assert.True(view.HasGraphicsLayer);
        Assert.True(view.IsAnimating);
        Assert.Equal(1, driver.LayerCount);
        Assert.Equal([Of(view)], driver.Clip(view)); // nothing in front of it
        Assert.Equal(TStyx.Count, driver.Primitives(view).Length);
        Assert.All(driver.Primitives(view), p => Assert.Equal(GraphicsPrimitiveKind.Line, p.Kind));
    }

    [Fact]
    public void Windows_and_modal_dialogs_in_front_are_cut_out_of_the_layer_including_their_shadow()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        GView view = Assert.Single(StyxWindows(scope.App)).View;
        PixelRect full = Of(view);

        var cover = new TWindow(new TRect(12, 5, 30, 10), "cover", Views.wnNoNumber);
        scope.App.DeskTop!.Insert(cover);
        Present(scope.App); // the next frame of the application loop, synchronized right before it is presented
        PixelRect[] clip = driver.PresentedClip(view);
        TPoint c = cover.MakeGlobal(new TPoint(0, 0));
        var coverPx = new PixelRect(c.x * CellW, c.y * CellH, cover.size.x * CellW, cover.size.y * CellH);
        TPoint s = TView.shadowSize;
        var shadowPx = coverPx with { X = coverPx.X + s.x * CellW, Y = coverPx.Y + s.y * CellH };
        Assert.All(clip, r => Assert.False(Intersects(r, coverPx) || Intersects(r, shadowPx), $"{r} overlaps the cover"));
        Assert.True(Area(clip) < Area([full]));

        cover.Close();
        Present(scope.App);
        Assert.Equal([full], driver.PresentedClip(view));

        // A modal message box runs its own loop, which presents through the same synchronization point.
        bool checkedInsideModal = false;
        scope.App.Step(() =>
        {
            var box = scope.App.Modal<TDialog>();
            TPoint g = box.MakeGlobal(new TPoint(0, 0));
            var boxPx = new PixelRect(g.x * CellW, g.y * CellH, box.size.x * CellW, box.size.y * CellH);
            if (Intersects(boxPx, full))
                Assert.All(driver.PresentedClip(view), r => Assert.False(Intersects(r, boxPx), $"{r} overlaps the dialog"));
            checkedInsideModal = true;
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        MsgBox.MessageBox(scope.App.DeskTop, "A dialog over the STYX window", MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.True(checkedInsideModal);
        Present(scope.App);
        Assert.Equal([full], driver.PresentedClip(view));
    }

    [Fact]
    public void Closing_removes_the_layer_and_stops_the_timer_and_reopening_works()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        TStyxDemo[] wins = StyxWindows(scope.App);
        Assert.Equal(2, wins.Length);
        Assert.Equal(2, driver.LayerCount);
        Assert.NotEqual(wins[0].origin, wins[1].origin); // staggered

        // The older window is partly behind the newer one in the next presented frame.
        Present(scope.App);
        GView back = wins.Single(w => w.owner!.last!.Next != w).View;
        Assert.True(Area(driver.PresentedClip(back)) < Area([Of(back)]));

        foreach (TStyxDemo w in wins)
        {
            GView view = w.View;
            w.Close();
            Assert.False(view.HasGraphicsLayer);
            Assert.False(view.IsAnimating);
        }
        Assert.Equal(0, driver.LayerCount);
        Present(scope.App);
        Assert.Equal(0, driver.PresentedLayerCount);
        Assert.Equal(0, GView.ExposedCount);

        scope.App.RunCommand(ShowcaseCmd.Styx);
        Assert.Equal(1, driver.LayerCount);
    }

    [Fact]
    public void Resizing_moving_and_hiding_follow_the_window()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        TStyxDemo win = Assert.Single(StyxWindows(scope.App));
        GView view = win.View;

        win.Locate(new TRect(2, 1, 60, 20));
        Assert.Equal((56, 17), (view.size.x, view.size.y)); // client area tracks the frame
        Assert.Equal([Of(view)], driver.Clip(view));
        // Primitives are re-laid out into the new pixel rectangle.
        PixelRect px = Of(view);
        Assert.All(driver.Primitives(view), p =>
        {
            Assert.InRange(p.X1, px.X - CellW, px.X + px.W + CellW);
            Assert.InRange(p.Y2, px.Y - CellH, px.Y + px.H + CellH);
        });

        win.Hide();
        Assert.False(view.HasGraphicsLayer);
        Assert.Equal(0, driver.LayerCount);
        win.Show();
        Assert.True(view.HasGraphicsLayer);
        Assert.Equal([Of(view)], driver.Clip(view));
    }

    [Fact]
    public void Timer_posts_ticks_that_animate_on_the_ui_thread()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        TStyxDemo win = Assert.Single(StyxWindows(scope.App));
        GraphicsPrimitive[] first = driver.Primitives(win.View);
        int submissions = driver.Submissions;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (TEventQueue.PostedCount == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Assert.True(TEventQueue.PostedCount > 0, "no tick was posted");
        Assert.True(TEventQueue.PostedCount <= 1, "ticks must be coalesced"); // one in flight per view
        Assert.True(TEventQueue.DeliverPostedEvent());

        Assert.True(driver.Submissions > submissions);
        Assert.NotEqual(first, driver.Primitives(win.View));
        win.Close();
        TEventQueue.ClearPosted();
    }

    [Fact]
    public void Unchanged_refresh_does_not_resubmit()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.Styx);
        int submissions = driver.Submissions;
        // The public pass (kept for compatibility) and the registered synchronizer are the same work.
        GView.RefreshVisibleRegions();
        GView.Synchronizer(scope.App);
        Assert.Equal(submissions, driver.Submissions);
    }

    // ── Drivers without pixel graphics ───────────────────────────────────

    [Fact]
    public void Text_drivers_explain_instead_of_opening_the_window()
    {
        using var scope = new DemoTestScope();
        scope.App.DismissResult("shows text only");
        scope.App.RunCommand(ShowcaseCmd.Styx);
        Assert.Empty(StyxWindows(scope.App));
    }

    [Fact]
    public void A_graphics_view_on_a_text_driver_shows_a_note_and_owns_no_layer()
    {
        using var scope = new DemoTestScope();
        var win = new GWindow(new TRect(0, 0, 60, 8), "g", Views.wnNoNumber);
        win.Insert(new GLine(0, 0, 100, 100, 0xFFFFFFFF));
        scope.App.DeskTop!.Insert(win);
        Assert.False(win.View.HasGraphicsLayer);
        Assert.False(TDisplay.driver is IPixelGraphicsDriver);

        TPoint g = win.View.MakeGlobal(new TPoint(0, 0));
        int row = g.y + win.View.size.y / 2;
        string text = new(Enumerable.Range(g.x, win.View.size.x).Select(x => TScreen.ScreenBuffer!.GetChar((uint)x, (uint)row).Character).ToArray());
        Assert.Contains(win.View.UnavailableText, text);
        win.Close();
    }

    [Fact]
    public void A_detached_graphics_window_has_no_visible_region()
    {
        using var scope = PixelScope(out var driver);
        var win = new GWindow(new TRect(0, 0, 20, 8), "g", Views.wnNoNumber);
        win.Insert(new GLine(0, 0, 100, 100, 0xFFFFFFFF));
        win.View.Refresh();
        Assert.False(win.View.HasGraphicsLayer);
        Assert.Equal(0, driver.LayerCount);
        win.View.Dispose();
    }
}
